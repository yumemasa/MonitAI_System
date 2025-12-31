using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MonitAI.Core
{
    public class GeminiAnalysisResult
    {
        public string RawText { get; set; } = "";
        public bool IsViolation { get; set; }
        public string Reason { get; set; } = "";
        public string Source { get; set; } = "Unknown"; // "CLI", "API", or "ACP"
    }

    public class GeminiService : IDisposable
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        // CLIコマンドのパス（デフォルトはPATH上のgemini、必要に応じて設定可能）
        public string GeminiCliCommand { get; set; } = "gemini";
        public bool UseGeminiCli { get; set; } = true;

        // --- ACP (常駐) モード用フィールド ---
        private Process? _process;
        private StreamWriter? _stdin;
        private Action<string>? _logger;
        private int _requestId = 1;
        private readonly Dictionary<int, TaskCompletionSource<JsonElement>> _pendingRequests = new();
        private string? _sessionId;
        private const int RPC_TIMEOUT = 120000; // 120秒
        private StringBuilder _responseBuffer = new StringBuilder();
        
        // 応答待ち用
        private TaskCompletionSource<string>? _currentResponseTcs;

        public bool IsRunning => _process != null && !_process.HasExited;

        public GeminiService(Action<string>? logger = null)
        {
            _logger = logger;
        }

        /// <summary>
        /// 解析のメインエントリポイント。常駐モードが有効ならそちらを優先します。
        /// </summary>
        public async Task<GeminiAnalysisResult> AnalyzeAsync(List<string> imagePaths, string userRules, string apiKey, string modelName)
        {
            // 1. 常駐プロセス(ACP)が起動していればそちらを使用
            if (IsRunning)
            {
                return await AnalyzeWithSessionAsync(imagePaths, userRules);
            }

            var result = new GeminiAnalysisResult();

            if (imagePaths == null || imagePaths.Count == 0)
            {
                result.RawText = "画像がありません。";
                return result;
            }

            // 2. CLIでの実行を試みる (One-shot)
            if (UseGeminiCli)
            {
                string? cliOutput = await AnalyzeWithCliAsync(imagePaths, userRules);
                if (!string.IsNullOrWhiteSpace(cliOutput) && !IsCliFileReadFailure(cliOutput))
                {
                    result.RawText = cliOutput;
                    result.IsViolation = IsViolationDetected(cliOutput);
                    result.Reason = ExtractReason(cliOutput);
                    result.Source = "CLI";
                    return result;
                }
            }

            // 3. APIへのフォールバック
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                result.RawText = "APIキーが設定されておらず、CLI実行も失敗しました。";
                return result;
            }

            string apiOutput = await AnalyzeWithApiAsync(imagePaths, userRules, apiKey, modelName);
            result.RawText = apiOutput;
            result.IsViolation = IsViolationDetected(apiOutput);
            result.Reason = ExtractReason(apiOutput);
            result.Source = "API";

            return result;
        }

        // --- ACP (常駐) モードの実装 ---

        public async Task<bool> StartAsync(string workingDir, string nodePath = "", string scriptPath = "")
        {
            try
            {
                // パスの自動解決
                if (string.IsNullOrEmpty(nodePath)) nodePath = "node"; // PATH依存
                if (string.IsNullOrEmpty(scriptPath))
                {
                    // デフォルト: AppData/npm/...
                    scriptPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                                               @"npm\node_modules\@google\gemini-cli\dist\index.js");
                }

                if (!File.Exists(scriptPath))
                {
                     _logger?.Invoke($"❌ [Gemini] スクリプトが見つかりません: {scriptPath}");
                     // 続行不能だが、nodePathだけで動く環境もあるかもしれないのでトライはしない
                     return false;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = nodePath,
                    Arguments = $"\"{scriptPath}\" --experimental-acp",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    WorkingDirectory = workingDir
                };
                psi.EnvironmentVariables["NO_COLOR"] = "true";

                _process = new Process { StartInfo = psi };

                _process.OutputDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) HandleOutput(e.Data);
                };
                _process.ErrorDataReceived += (s, e) => {
                    if (!string.IsNullOrEmpty(e.Data)) _logger?.Invoke($"[STDERR]: {e.Data}");
                };

                _process.Start();
                _stdin = _process.StandardInput;
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();

                _logger?.Invoke("🚀 [Gemini] プロセス起動... 初期化中...");

                // Initialize
                var initParams = new {
                    protocolVersion = 1,
                    clientCapabilities = new {
                        terminal = true,
                        fs = new { readTextFile = true, writeTextFile = false } 
                    }
                };
                
                await SendRpcRequestAsync("initialize", initParams, RPC_TIMEOUT);
                _logger?.Invoke("✅ [Gemini] Initialize 完了");

                // Session Start
                await ResetSessionAsync(workingDir);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"❌ [Gemini] 起動エラー: {ex.Message}");
                return false;
            }
        }

        public async Task ResetSessionAsync(string workingDir)
        {
            if (!IsRunning) return;

            var sessionParams = new {
                cwd = workingDir,
                mcpServers = new object[] { }
            };
            var sessionRes = await SendRpcRequestAsync("session/new", sessionParams, RPC_TIMEOUT);
            
            if (sessionRes.TryGetProperty("sessionId", out var sid))
            {
                _sessionId = sid.GetString();
                _logger?.Invoke($"🔄 [Gemini] セッション切替完了 (ID: {_sessionId?.Substring(0, Math.Min(8, _sessionId.Length))}...)");
            }
        }

        private async Task<GeminiAnalysisResult> AnalyzeWithSessionAsync(List<string> imagePaths, string userRules)
        {
            var result = new GeminiAnalysisResult { Source = "ACP" };
            
            // バッファと完了通知をリセット
            _responseBuffer.Clear();
            _currentResponseTcs = new TaskCompletionSource<string>();

            // プロンプト構築
            string imgPath = imagePaths.FirstOrDefault() ?? "";
            string prompt = $@"
あなたは監視エージェントです。以下のルールに基づいて画像を分析し、ユーザーがルールに違反しているか判定してください。
ルール: {userRules}

回答は必ず以下のJSON形式のみで出力してください。Markdownのコードブロックは不要です。余計な解説も不要です。
{{
  ""IsViolation"": true または false,
  ""Reason"": ""判定理由（違反している場合は具体的に、していない場合は'作業中'など）""
}}

画像パス: ""{imgPath}"" 
Note: Use the read_file tool to read the image data from the path provided.";

            try
            {
                // 送信
                await SendMessageAsync(prompt);

                // 完了待ち (タイムアウト付き)
                var completedTask = await Task.WhenAny(_currentResponseTcs.Task, Task.Delay(30000));
                if (completedTask != _currentResponseTcs.Task)
                {
                    result.RawText = "タイムアウト: 応答が完了しませんでした。";
                    return result;
                }

                string rawJson = _responseBuffer.ToString();
                result.RawText = rawJson;

                // JSONパース
                result = ParseJsonResult(rawJson, result);

                // 次回のためにセッションリセット（記憶消去）
                // ※画像パスのディレクトリを作業ディレクトリとする
                string dir = Path.GetDirectoryName(imgPath) ?? Environment.CurrentDirectory;
                await ResetSessionAsync(dir);
            }
            catch (Exception ex)
            {
                result.RawText = $"エラー: {ex.Message}";
                _logger?.Invoke($"解析例外: {ex.Message}");
            }

            return result;
        }

        private async Task SendMessageAsync(string message)
        {
            if (!IsRunning) return;

            var promptParams = new {
                sessionId = _sessionId,
                prompt = new object[] {
                    new { type = "text", text = message }
                }
            };
            await SendRpcRequestAsync("session/prompt", promptParams, RPC_TIMEOUT);
        }

        private Task<JsonElement> SendRpcRequestAsync(string method, object parameters, int timeoutMs)
        {
            var tcs = new TaskCompletionSource<JsonElement>();
            int id = _requestId++;
            _pendingRequests[id] = tcs;

            var request = new {
                jsonrpc = "2.0",
                method = method,
                @params = parameters,
                id = id
            };

            string json = JsonSerializer.Serialize(request);
            _stdin?.WriteLine(json);
            _stdin?.Flush();

            Task.Delay(timeoutMs).ContinueWith(_ => {
                if (_pendingRequests.ContainsKey(id)) {
                    _pendingRequests.Remove(id);
                    tcs.TrySetException(new TimeoutException($"RPC '{method}' timed out."));
                }
            });

            return tcs.Task;
        }

        private void HandleOutput(string line)
        {
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                // 1. RPCレスポンス
                if (root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
                {
                    int id = idProp.GetInt32();
                    if (_pendingRequests.TryGetValue(id, out var tcs))
                    {
                        if (root.TryGetProperty("result", out var result)) tcs.TrySetResult(result.Clone());
                        else if (root.TryGetProperty("error", out var error)) tcs.TrySetException(new Exception($"RPC Error: {error.GetRawText()}"));
                        _pendingRequests.Remove(id);
                    }
                }
                // 2. 通知 (session/update)
                else if (root.TryGetProperty("method", out var methodProp) && methodProp.GetString() == "session/update")
                {
                    if (root.TryGetProperty("params", out var p) && p.TryGetProperty("update", out var update))
                    {
                        if (update.TryGetProperty("sessionUpdate", out var type) && type.GetString() == "agent_message_chunk")
                        {
                            if (update.TryGetProperty("content", out var content) && content.TryGetProperty("text", out var text))
                            {
                                string chunk = text.GetString() ?? "";
                                _logger?.Invoke($"[🤖]: {chunk}");
                                _responseBuffer.Append(chunk);

                                // 簡易的な完了判定: JSONの閉じ括弧が含まれ、かつ必須キーがある場合
                                // ※本来は turnComplete イベント等を待つべきだが、現状のGemini CLIの挙動に合わせて簡易実装
                                string current = _responseBuffer.ToString();
                                if (current.Contains("}") && (current.Contains("IsViolation") || current.Contains("Reason")))
                                {
                                    _currentResponseTcs?.TrySetResult("Done");
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private GeminiAnalysisResult ParseJsonResult(string rawText, GeminiAnalysisResult result)
        {
            try
            {
                string jsonString = rawText;
                var match = Regex.Match(rawText, @"\{.*\}", RegexOptions.Singleline);
                if (match.Success) jsonString = match.Value;

                using var doc = JsonDocument.Parse(jsonString);
                if (doc.RootElement.TryGetProperty("IsViolation", out var isV)) result.IsViolation = isV.GetBoolean();
                if (doc.RootElement.TryGetProperty("Reason", out var r)) result.Reason = r.GetString() ?? "";
            }
            catch
            {
                result.IsViolation = false;
                result.Reason = "応答の解析に失敗しました";
            }
            return result;
        }

        public void Dispose()
        {
            try { 
                if (_process != null && !_process.HasExited) { 
                    _process.Kill(); 
                    _process.Dispose(); 
                } 
            } catch { }
        }

        // --- 既存のメソッド (CLI One-shot / API) ---

        public async Task<bool> CheckCliConnectionAsync()
        {
            try
            {
                string output = await RunCommandAsync(GeminiCliCommand, "--version", Environment.CurrentDirectory);
                return !string.IsNullOrWhiteSpace(output) &&
                       !output.Contains("error", StringComparison.OrdinalIgnoreCase) &&
                       !output.Contains("not found", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public async Task<string?> AnalyzeWithCliAsync(List<string> imagePaths, string userRules)
        {
            try
            {
                string basePrompt = BuildAnalysisPrompt(userRules).Trim();
                var sb = new StringBuilder();
                sb.AppendLine("【重要：システム指示】");
                sb.AppendLine("あなたは読み込んだ画像データを**視覚的に解析できる**高度なマルチモーダルAIです。");
                sb.AppendLine("「テキストしか読めない」「画像が見えない」といった誤った判断をせず、必ず以下の手順を実行してください。");
                sb.AppendLine("1. ツール `read_file` を使用して、以下の絶対パスにある画像ファイルをすべて読み込む。");
                sb.AppendLine("2. 画像内のウィンドウタイトル、アイコン、テキスト、アクティブな状況を詳細に視覚認識する。");
                sb.AppendLine("3. 後述する【ユーザーのルール】に基づいて判定を行う。");
                sb.AppendLine(basePrompt);
                foreach (var path in imagePaths)
                {
                    sb.AppendLine($"\"{path}\"");
                }

                string prompt = sb.ToString();
                string workingDir = Path.GetDirectoryName(imagePaths[0]) ?? Environment.CurrentDirectory;
                string safePrompt = prompt.Replace("\"", "\\\"").Replace("\r\n", "\\n").Replace("\n", "\\n");
                string args = $"--allowed-tools read_file --yolo -p \"{safePrompt}\"";

                string raw = await RunCommandAsync(GeminiCliCommand, args, workingDir);
                return CleanCliOutput(raw);
            }
            catch
            {
                return null;
            }
        }

        private async Task<string> AnalyzeWithApiAsync(List<string> imagePaths, string userRules, string apiKey, string modelName)
        {
            try
            {
                string apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";
                string prompt = BuildAnalysisPrompt(userRules);

                var jsonBuilder = new StringBuilder();
                jsonBuilder.Append("{\"contents\":[{\"parts\":[");
                jsonBuilder.Append("{\"text\":\"").Append(EscapeJsonString(prompt)).Append("\"}");

                foreach (string imagePath in imagePaths)
                {
                    byte[] imageBytes = File.ReadAllBytes(imagePath);
                    string base64Image = Convert.ToBase64String(imageBytes);
                    jsonBuilder.Append(",{\"inline_data\":{\"mime_type\":\"image/png\",\"data\":\"").Append(base64Image).Append("\"}}");
                }

                jsonBuilder.Append("]}]");
                jsonBuilder.Append(", \"generationConfig\": {\"maxOutputTokens\": 4000}");
                jsonBuilder.Append("}");

                var content = new StringContent(jsonBuilder.ToString(), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(apiUrl, content);
                string responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return ParseGeminiResponse(responseBody);
                }
                else
                {
                    return $"API Error: {response.StatusCode} - {responseBody}";
                }
            }
            catch (Exception ex)
            {
                return $"Exception: {ex.Message}";
            }
        }

        private Task<string> RunCommandAsync(string command, string arguments, string workingDirectory)
        {
            return Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = command,
                        Arguments = arguments,
                        WorkingDirectory = workingDirectory,
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8
                    };

                    using (var process = new Process())
                    {
                        process.StartInfo = psi;
                        process.Start();
                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        process.WaitForExit(90000);
                        if (!process.HasExited) { process.Kill(); return "Timeout"; }

                        string raw = output + (string.IsNullOrEmpty(error) ? "" : "\n" + error);
                        var lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                                       .Where(l => !l.StartsWith("[STARTUP]"));
                        return string.Join("\n", lines).Trim();
                    }
                }
                catch (Exception ex) { return $"Error: {ex.Message}"; }
            });
        }

        private string BuildAnalysisPrompt(string userRules)
        {
            return $@"
あなたはユーザーのPC画面を監視し、生産性を管理する厳格なAIアシスタントです。
以下の【ユーザーのルール】と【判定ガイドライン】に基づいて、厳密に判定を行ってください。

【ユーザーのルール】
{userRules}

【重要：判定ガイドライン】
1. **メインアクティビティの特定**:
   - 画面上の「小さなアイコン」「背景」「脇にある広告」「ブラウザのタブ」は無視してください。
   - 画面の中央、または最も大きく表示されている「アクティブなウィンドウ」の内容だけで判断してください。

2. **誤検知の防止**:
   - 動画サイト（YouTubeなど）のロゴやリンクが画面の隅に映っているだけでは「違反」にしないでください。
   - 勉強や業務のサイトに表示されている「広告バナー」は違反の対象外です。ユーザーがそれをクリックして視聴していない限り、無視してください。

3. **否定条件の解釈**:
   - 「～以外は禁止」というルールの場合、許可された行動（～）のみが「○」です。それ以外は全て「×」です。
   - 「～以外は許可」というルールの場合、禁止された行動（～）のみが「×」です。それ以外は全て「○」です。

【回答フォーマット】
まず、画面の状況を分析し、その後に判定結果を出力してください。
重要: 分析は簡略化せず、以下のステップで思考プロセス（Chain of Thought）を展開してください。回答速度より、回答の精度を優先してください。
重要: 出力にはMarkdown記法（太字、イタリック、見出し記号など）を一切使用せず、プレーンテキストのみを使用してください。

[分析]
1. 状況の客観的記述:
   - 複数の画像がある場合は、(画像1)... (画像2)... のように画像を区別し、ウィンドウタイトル、実行中のコマンド、AIとのチャット内容、操作中の設定項目などを可能な限り詳細に言語化してください。
   - 単に「作業中」とせず、「何を使って」「何をしているか」を具体的に記述してください。
2. ルールとの照合プロセス:
   - 記述した状況をユーザーのルールと照らし合わせ、許可される行動か、禁止される行動かを段階的に検討してください。
   - 違反の疑いがある要素（YouTubeやSNSなど）について、それが「アクティブなウィンドウ」か「単なる映り込み（無視対象）」かを論理的に推論し、判定の根拠を固めてください。

[判定]
（以下のいずれかのみ出力）
○
内容: [ユーザーの行動]
（または）
×
理由: [具体的な違反理由]
";


        }

        private string ExtractReason(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var match = Regex.Match(text, @"(?:理由|Reason)\s*[:：]\s*(.*)", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }
            return "";
        }

        private bool IsViolationDetected(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var match = Regex.Match(text, @"(?:\[?判定\]?|Verdict)\s*[:：]?\s*[\r\n]+\s*([○×xX])", RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string v = match.Groups[1].Value;
                return v == "×" || v.Equals("x", StringComparison.OrdinalIgnoreCase);
            }

            // フォールバック検索
            var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            bool inSection = false;
            foreach (var line in lines)
            {
                if (line.Contains("判定") || line.Contains("Verdict")) { inSection = true; continue; }
                if (inSection)
                {
                    if (line.Trim().StartsWith("×") || line.Trim().StartsWith("x", StringComparison.OrdinalIgnoreCase)) return true;
                    if (line.Trim().StartsWith("○")) return false;
                }
            }
            return false;
        }

        private string CleanCliOutput(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string text = Regex.Replace(raw, @"\x1B\[[^@-~]*[@-~]", ""); // ANSIカラー除去
            text = Regex.Replace(text, @"[╭─╮│╰╯✓]", ""); // 枠線除去
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                            .Where(l => !l.StartsWith("ReadFile") && !l.Contains("Tool Calls") && !l.Contains("YOLO mode"));
            return string.Join(Environment.NewLine, lines).Trim();
        }

        private bool IsCliFileReadFailure(string text)
        {
            string t = text.ToLowerInvariant();
            return t.Contains("画像ファイルが読み込まれなかった") || t.Contains("file not found") || t.Contains("cannot open");
        }

        private string EscapeJsonString(string str)
        {
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r\n", "\\n").Replace("\n", "\\n").Replace("\t", "\\t");
        }

        private string ParseGeminiResponse(string json)
        {
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    return doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString() ?? "";
                }
            }
            catch { return "Parse Error"; }
        }
    }
}

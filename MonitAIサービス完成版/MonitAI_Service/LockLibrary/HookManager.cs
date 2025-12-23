using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LockLibrary
{
    public static class HookManager
    {
        // 外部購読用イベント（Form1 などがこれを購読）
        public static event Action<Keys> OnKeyPressed;

        // --- フラグ・状態 ---
        private static bool _isHooked = false;
        private static bool _delayEnabled = false;
        private static bool _invertMouse = false;
        private static IntPtr _keyboardHookID = IntPtr.Zero;
        private static IntPtr _mouseHookID = IntPtr.Zero;

        // マウス反転用
        private static System.Threading.Timer _mouseTimer;
        private static int _mouseIntervalMs = 20;
        private static POINT _lastMousePos;

        // Windows フック用
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        private static LowLevelKeyboardProc _kbProc = KeyboardHookCallback;
        private static LowLevelMouseProc _msProc = MouseHookCallback;

        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;

        // P/Invoke
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, Delegate lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        // For cursor
        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        // For synthetic keyboard
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public INPUTUNION u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUTUNION
        {
            [FieldOffset(0)] public KEYBDINPUT ki;
            // mouse/input structs omitted (not used)
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        // ========== Public API ==========
        /// <summary>フック開始（必要なら mouse inversion の初期値を指定）</summary>
        public static void StartHooks(bool invertMouse = false)
        {
            if (_isHooked) return;
            _invertMouse = invertMouse;

            _keyboardHookID = SetHook(_kbProc, WH_KEYBOARD_LL);
            _mouseHookID = SetHook(_msProc, WH_MOUSE_LL);

            if (_invertMouse)
            {
                GetCursorPos(out _lastMousePos);
                _mouseTimer = new System.Threading.Timer(MouseTimerCallback, null, 0, _mouseIntervalMs);
            }

            _isHooked = true;
            SystemHelper.Log("HookManager: Hooks started");
        }

        /// <summary>フック停止</summary>
        public static void StopHooks()
        {
            try
            {
                if (_keyboardHookID != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_keyboardHookID);
                    _keyboardHookID = IntPtr.Zero;
                }

                if (_mouseHookID != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(_mouseHookID);
                    _mouseHookID = IntPtr.Zero;
                }

                if (_mouseTimer != null)
                {
                    _mouseTimer.Dispose();
                    _mouseTimer = null;
                }

                _isHooked = false;
                SystemHelper.Log("HookManager: Hooks stopped");
            }
            catch (Exception ex)
            {
                SystemHelper.Log("HookManager Stop Error: " + ex.Message);
            }
        }

        /// <summary>入力遅延を有効/無効にする（DLL外から呼べる）</summary>
        public static void EnableDelay(bool enable)
        {
            _delayEnabled = enable;
            SystemHelper.Log($"HookManager: Delay set to {enable}");
        }

        /// <summary>マウス反転トグル（DLL外から呼べる）</summary>
        public static void ToggleMouseInversion()
        {
            _invertMouse = !_invertMouse;
            if (_invertMouse)
            {
                GetCursorPos(out _lastMousePos);
                if (_mouseTimer == null) _mouseTimer = new System.Threading.Timer(MouseTimerCallback, null, 0, _mouseIntervalMs);
            }
            else
            {
                _mouseTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            }
            SystemHelper.Log($"HookManager: Mouse inversion toggled to {_invertMouse}");
        }

        // ========== 内部ヘルパ ==========
        private static IntPtr SetHook(Delegate proc, int hookType)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(hookType, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        // Keyboard callback - low level
        private static IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN)
                {
                    int vkCode = Marshal.ReadInt32(lParam);
                    Keys key = (Keys)vkCode;

                    // 外部購読者へ通知（非同期で呼ぶと安全）
                    OnKeyPressed?.Invoke(key);

                    // 入力遅延処理: アルファ/数字ならブロックして遅延送信
                    if (_delayEnabled && IsAlphaOrNumber(vkCode))
                    {
                        // 抑止：Do not pass to next hook (consume)
                        // スケジューリングで再送
                        Task.Run(() =>
                        {
                            Thread.Sleep(1000); // 1秒遅延
                            SendVirtualKey((ushort)vkCode);
                        });

                        return (IntPtr)1; // suppress original
                    }
                }
            }
            catch (Exception ex)
            {
                SystemHelper.Log("KeyboardHook Error: " + ex.Message);
            }

            return CallNextHookEx(_keyboardHookID, nCode, wParam, lParam);
        }

        // Mouse callback - currently not altering low-level mouse events (we invert via timer)
        private static IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            // Could inspect mouse movement events here if needed
            return CallNextHookEx(_mouseHookID, nCode, wParam, lParam);
        }

        // Timer callback for mouse inversion (uses Get/SetCursorPos)
        private static void MouseTimerCallback(object state)
        {
            try
            {
                if (!_invertMouse) return;

                if (!GetCursorPos(out POINT cur)) return;

                int dx = cur.X - _lastMousePos.X;
                int dy = cur.Y - _lastMousePos.Y;

                // invert movement: move opposite direction stronger factor (1.5)
                int newX = cur.X - (int)(dx * 1.5);
                int newY = cur.Y - (int)(dy * 1.5);

                SetCursorPos(newX, newY);

                _lastMousePos.X = newX;
                _lastMousePos.Y = newY;
            }
            catch (Exception ex)
            {
                SystemHelper.Log("MouseTimer Error: " + ex.Message);
            }
        }

        private static bool IsAlphaOrNumber(int vk)
        {
            return (vk >= (int)Keys.A && vk <= (int)Keys.Z) || (vk >= (int)Keys.D0 && vk <= (int)Keys.D9);
        }

        // Send a virtual key (keydown + keyup) using SendInput
        private static void SendVirtualKey(ushort vk)
        {
            try
            {
                INPUT[] inputs = new INPUT[2];

                inputs[0].type = INPUT_KEYBOARD;
                inputs[0].u.ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = 0,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                };

                inputs[1].type = INPUT_KEYBOARD;
                inputs[1].u.ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = KEYEVENTF_KEYUP,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                };

                uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
                if (sent != inputs.Length)
                {
                    SystemHelper.Log("SendInput sent count mismatch: " + sent);
                }
            }
            catch (Exception ex)
            {
                SystemHelper.Log("SendVirtualKey Error: " + ex.Message);
            }
        }
    }
}

using System;

namespace MonitAI.Core
{
    public class MonitoringState
    {
        public int CurrentPoints { get; set; }
        public int CurrentPenaltyLevel { get; set; }
        public bool IsViolation { get; set; }
        public DateTime LastUpdate { get; set; }
    }

    public class MonitoringCommand
    {
        public string Command { get; set; } = string.Empty; // "AddPoints", "Stop"
        public int Value { get; set; }
        public DateTime Timestamp { get; set; }
    }
}

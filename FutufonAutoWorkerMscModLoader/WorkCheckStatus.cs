namespace FutufonAutoWorkerMscModLoader
{
    internal enum WorkCheckState { Unavailable, NotClockedIn, Working, Slacking, Waiting, Paused, Checking }

    internal static class WorkCheckStatus
    {
        internal static WorkCheckState Read(bool active, bool atWork, string nativeState, bool counterIncreasing)
        {
            if (!active) return WorkCheckState.Unavailable;
            if (counterIncreasing || nativeState == "Is slacking") return WorkCheckState.Slacking;
            if (!atWork) return WorkCheckState.NotClockedIn;
            if (nativeState == "Is working" || nativeState == "Reset state") return WorkCheckState.Working;
            if (nativeState == "Boss in?") return WorkCheckState.Waiting;
            if (nativeState == "Delay") return WorkCheckState.Paused;
            return WorkCheckState.Checking;
        }

        internal static string Text(WorkCheckState state)
        {
            switch (state)
            {
                case WorkCheckState.Working: return "Supervisor: game recognizes work";
                case WorkCheckState.Slacking: return "Supervisor: WARNING - idle time detected";
                case WorkCheckState.NotClockedIn: return "Supervisor: not clocked in";
                case WorkCheckState.Waiting: return "Supervisor: waiting for inspection";
                case WorkCheckState.Paused: return "Supervisor: inspection on hold";
                case WorkCheckState.Checking: return "Supervisor: checking";
                default: return "Supervisor: no active check";
            }
        }
    }
}

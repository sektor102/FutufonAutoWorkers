using System;
using FutufonAutoWorkerMscModLoader;

public static class WorkCheckStatusTests
{
    public static string Run()
    {
        Check(WorkCheckStatus.Read(false, true, "Is working", false), WorkCheckState.Unavailable, "inactive FSM cannot certify work");
        Check(WorkCheckStatus.Read(true, false, "Boss in?", false), WorkCheckState.NotClockedIn, "attendance comes from live PlayerData");
        Check(WorkCheckStatus.Read(true, true, "Boss in?", false), WorkCheckState.Waiting, "before inspection, do not claim a pass");
        Check(WorkCheckStatus.Read(true, true, "Is working", false), WorkCheckState.Working, "native accepted state survives the activity pulse reset");
        Check(WorkCheckStatus.Read(true, true, "Reset state", false), WorkCheckState.Working, "resetting the consumed activity pulse still represents accepted work");
        Check(WorkCheckStatus.Read(true, true, "Is slacking", false), WorkCheckState.Slacking, "native idle state warns before the next counter sample");
        Check(WorkCheckStatus.Read(true, false, "Is slacking", false), WorkCheckState.Slacking, "a live counting state is not hidden by stale attendance");
        Check(WorkCheckStatus.Read(true, true, "Is working", true), WorkCheckState.Slacking, "actual counter increase overrides an apparently safe state");
        Check(WorkCheckStatus.Read(true, true, "Delay", false), WorkCheckState.Paused, "skipped inspection does not claim accepted work");
        Check(WorkCheckStatus.Read(true, true, "new game state", false), WorkCheckState.Checking, "unknown game states do not claim accepted work");
        return "PASS: supervisor waiting/acceptance, pulse reset, live idle state, counter increase, attendance and unknown-state handling";
    }

    private static void Check(WorkCheckState actual, WorkCheckState expected, string description)
    {
        if (actual != expected) throw new Exception("FAIL: " + description);
    }
}

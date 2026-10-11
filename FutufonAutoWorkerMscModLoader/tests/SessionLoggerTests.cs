using System;
using System.Collections.Generic;
using FutufonAutoWorkerMscModLoader;

public static class SessionLoggerTests
{
    public static string Run()
    {
        var file = new List<string>();
        var console = new List<string>();
        var logger = new SessionLogger(file.Add, console.Add);
        Require(!logger.FileEnabled && !logger.Detailed, "new sessions start with all file logging off");
        logger.Write("cycle started", LogKind.Event);
        for (int package = 1; package <= 44; package++)
        {
            for (int step = 0; step < 12; step++) logger.Write("component step", LogKind.Detail);
            logger.Write("complete package " + package, LogKind.Progress);
        }
        logger.Write("cycle complete", LogKind.Event);
        logger.Write("automatic error snapshot", LogKind.Snapshot);
        Require(console.Count == 2 && file.Count == 0, "normal 44-package cycle and snapshots must never write a file while opted out");
        Require(!logger.Detailed, "event/progress messages must not enable recording");

        logger.FileEnabled = true;
        logger.Detailed = true;
        for (int tick = 0; tick < 2000; tick++)
            if (SessionLogger.ShouldSample("FACTORY", "Clock", false)) logger.Write("clock minute changed", LogKind.Detail);
        Require(file.Count == 0 && console.Count == 2, "continuous factory clock changes must not be logged even when enabled");
        logger.Write("assembly detail", LogKind.Detail);
        Require(file.Count == 1 && console.Count == 2, "opt-in diagnostics go only to the session file");
        logger.Detailed = false;
        logger.Write("ignored detail", LogKind.Detail);
        Require(file.Count == 1, "turning detailed diagnostics off stops detail writes");
        Require(SessionLogger.ShouldSample("FACTORY", "Clock", true), "F9 still includes complete clock data");
        for (int line = 0; line < 1000; line++) logger.Write("snapshot line", LogKind.Snapshot);
        Require(file.Count == 1001 && console.Count == 2 && !logger.Detailed, "snapshots require file opt-in but do not flood the console");
        logger.FileEnabled = false;
        logger.Detailed = true;
        foreach (LogKind kind in Enum.GetValues(typeof(LogKind))) logger.Write("after opt-out", kind);
        Require(file.Count == 1001 && console.Count == 3, "opting out blocks every file category, even if detail remains enabled");
        Require(SessionLogger.ShouldSample("FACTORY", "PlayerData", false) && SessionLogger.ShouldSample("package(Clone)", "Use", false),
            "debug recording must retain factory attendance and assembly data");
        return "PASS: no file writes by default or after opt-out, gated snapshots, opt-in diagnostics, 2000 ignored clock ticks and quiet console";
    }
    private static void Require(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); }
}

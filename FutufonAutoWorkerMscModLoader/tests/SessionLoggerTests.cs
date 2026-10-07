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
        Require(!logger.Detailed, "new game sessions must start with detailed recording off");
        logger.Write("cycle started", LogKind.Event);
        for (int package = 1; package <= 44; package++)
        {
            for (int step = 0; step < 12; step++) logger.Write("component step", LogKind.Detail);
            logger.Write("complete package " + package, LogKind.Progress);
        }
        logger.Write("cycle complete", LogKind.Event);
        Require(console.Count == 2 && file.Count == 46, "normal 44-package cycle keeps only start/finish in the console and per-package progress in file");
        Require(!logger.Detailed, "event/progress messages must not enable recording");

        logger.Detailed = true;
        for (int tick = 0; tick < 2000; tick++)
            if (SessionLogger.ShouldSample("FACTORY", "Clock", false)) logger.Write("clock minute changed", LogKind.Detail);
        Require(file.Count == 46 && console.Count == 2, "continuous factory clock changes must not be logged even in F7 mode");
        logger.Write("assembly detail", LogKind.Detail);
        Require(file.Count == 47 && console.Count == 2, "F7 diagnostics must go only to the session file");
        logger.Detailed = false;
        logger.Write("ignored detail", LogKind.Detail);
        Require(file.Count == 47, "turning diagnostics off stops detail writes");
        Require(SessionLogger.ShouldSample("FACTORY", "Clock", true), "F9 still includes complete clock data");
        for (int line = 0; line < 1000; line++) logger.Write("snapshot line", LogKind.Snapshot);
        Require(file.Count == 1047 && console.Count == 2 && !logger.Detailed, "F9 works while F7 is off without flooding console or enabling recording");
        Require(SessionLogger.ShouldSample("FACTORY", "PlayerData", false) && SessionLogger.ShouldSample("package(Clone)", "Use", false),
            "debug recording must retain factory attendance and assembly data");
        return "PASS: quiet default, 44-package progress in file, file-only diagnostics, 2000 ignored clock ticks and full snapshots without console spam";
    }
    private static void Require(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); }
}

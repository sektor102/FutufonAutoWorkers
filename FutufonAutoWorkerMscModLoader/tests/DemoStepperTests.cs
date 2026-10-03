using System;
using System.Collections;
using System.Collections.Generic;
using FutufonAutoWorkerMscModLoader;

public static class DemoStepperTests
{
    public static string Run()
    {
        var marks = new List<string>();
        using (var stepper = new DemoStepper(Outer(marks, false)))
        {
            Assert(stepper.Tick(), "nested routine yields one frame");
            Assert(string.Join(",", marks.ToArray()) == "outer:start,inner:start", "no action past first frame");
            Assert(!stepper.Tick(), "completed routine stops");
            Assert(string.Join(",", marks.ToArray()) == "outer:start,inner:start,inner:after,inner:cleanup,outer:after,outer:cleanup", "nested order and cleanup");
            Assert(!stepper.Tick(), "completed runner cannot consume another item");
        }
        marks.Clear();
        var cancelled = new DemoStepper(Outer(marks, false));
        cancelled.Tick();
        cancelled.Dispose();
        Assert(string.Join(",", marks.ToArray()) == "outer:start,inner:start,inner:cleanup,outer:cleanup", "stop unwinds both iterators before another action");
        Assert(!cancelled.Tick(), "stop prevents all further actions");
        cancelled.Dispose();
        marks.Clear();
        var failing = new DemoStepper(Outer(marks, true));
        failing.Tick();
        bool threw = false;
        try { failing.Tick(); }
        catch (InvalidOperationException) { threw = true; }
        finally { failing.Dispose(); }
        Assert(threw, "operation failure reaches caller");
        Assert(!marks.Contains("outer:after") && marks.Contains("outer:cleanup") && marks.Contains("inner:cleanup"), "failure releases nested resources and blocks next operation");
        var unstarted = new DemoStepper(Outer(marks, false));
        int before = marks.Count;
        unstarted.Dispose();
        Assert(!unstarted.Tick() && marks.Count == before, "stop before first frame has no side effects");
        return "PASS: completion, frame boundaries, cancellation, exception cleanup, and cancellation before start";
    }

    private static IEnumerator Outer(List<string> marks, bool fail)
    {
        try
        {
            marks.Add("outer:start");
            yield return Inner(marks, fail);
            marks.Add("outer:after");
        }
        finally { marks.Add("outer:cleanup"); }
    }

    private static IEnumerator Inner(List<string> marks, bool fail)
    {
        try
        {
            marks.Add("inner:start");
            yield return null;
            if (fail) throw new InvalidOperationException("Simulated operation failure");
            marks.Add("inner:after");
        }
        finally { marks.Add("inner:cleanup"); }
    }

    private static void Assert(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
    }
}

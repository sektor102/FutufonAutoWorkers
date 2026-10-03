using System;
using FutufonAutoWorkerMscModLoader;

public static class AutomationTests
{
    public static string Run()
    {
        var full = new AutomationOptions(AutomationMode.FullCycle, BatchVolume.OneCarton);
        var carry = new AutomationOptions(AutomationMode.ManualDelivery, BatchVolume.OneCarton);
        var stacks = new AutomationOptions(AutomationMode.LoosePackages, BatchVolume.FillPallet);
        var supplies = new AutomationOptions(AutomationMode.ManualSupplies, BatchVolume.OneCarton);
        Require(full.FetchSupplies && full.PackCarton && full.DeliverCarton, "default full cycle");
        Require(carry.FetchSupplies && carry.PackCarton && !carry.DeliverCarton, "manual delivery must not teleport cartons");
        Require(stacks.FetchSupplies && !stacks.PackCarton && !stacks.DeliverCarton, "loose packages must not be inserted into cartons");
        Require(!supplies.FetchSupplies && !supplies.PackCarton && !supplies.DeliverCarton, "manual supplies must not be moved or fetched");
        Require(!full.Matches(carry) && !full.Matches(new AutomationOptions(AutomationMode.FullCycle, BatchVolume.FillPallet)),
            "changing mode or volume must not silently resume an old run");

        var one = new BatchProgress(1);
        one.AdoptPackedCount(17);
        Require(one.TotalPackages == 17 && one.TargetPackages == 44, "resume retained native carton contents");
        one.RequestPause();
        one.RecordPackage(); // An already-started package can complete after pause is requested.
        Require(one.CurrentPackages == 18 && one.PauseRequested, "soft pause does not cancel current package completion");
        one.Resume();
        Require(one.CurrentPackages == 18 && !one.PauseRequested, "resume keeps completed work");
        Throws(() => one.AdoptPackedCount(3), "removing carton contents must not hide lost progress");
        Throws(() => one.FinishCarton(), "a partial carton cannot advance the job");
        while (one.CurrentPackages < 44) one.RecordPackage();
        Throws(() => one.RecordPackage(), "no 45th package before carton completion");
        one.FinishCarton();
        Require(one.Complete && one.TotalPackages == 44, "one carton stops at exactly 44");
        Throws(() => one.RecordPackage(), "no automatic next cycle");

        foreach (int freeSlots in new[] { 1, 2, 3, 4 })
        {
            var pallet = new BatchProgress(freeSlots);
            for (int carton = 0; carton < freeSlots; carton++)
            {
                for (int package = 0; package < 44; package++) pallet.RecordPackage();
                pallet.FinishCarton();
            }
            Require(pallet.Complete && pallet.TotalPackages == freeSlots * 44, "fill only the initially free pallet slots");
        }
        Throws(() => new BatchProgress(0), "full pallet blocks a run before consuming materials");
        Throws(() => new BatchProgress(5), "one run cannot overfill a four-carton pallet");

        foreach (WorkerPhase phase in Enum.GetValues(typeof(WorkerPhase)))
        {
            Require(!string.IsNullOrEmpty(WorkerText.Phase(true, phase, 0, AutomationMode.FullCycle)), "Russian phase text");
            Require(!string.IsNullOrEmpty(WorkerText.Phase(false, phase, 0, AutomationMode.FullCycle)), "English phase text");
        }
        Require(WorkerText.Mode(true, AutomationMode.ManualSupplies) != WorkerText.Mode(false, AutomationMode.ManualSupplies), "live language selection");
        return "PASS: four automation modes, retained package progress, soft-pause completion, 44-package limits, 1-4 pallet slots and RU/EN texts";
    }
    private static void Require(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); }
    private static void Throws(Action action, string label)
    {
        try { action(); } catch (InvalidOperationException) { return; } catch (ArgumentOutOfRangeException) { return; }
        throw new Exception("FAIL: " + label);
    }
}

public static class ShiftReminderTests
{
    public static string Run()
    {
        var reminders = new ShiftReminders();
        Check(reminders.Observe(1, 10, false, false, true), ShiftNotice.None, "no early lunch");
        Check(reminders.Observe(1, 11, true, false, true), ShiftNotice.Lunch, "native lunch");
        Check(reminders.Observe(1, 11, true, false, true), ShiftNotice.None, "no lunch spam");
        Check(reminders.Observe(1, 16, false, true, true), ShiftNotice.EndOfShift, "native shift finish");
        Check(reminders.Observe(1, 16, false, true, true), ShiftNotice.None, "no finish spam");
        Check(reminders.Observe(2, 11, true, false, true), ShiftNotice.Lunch, "next work day rearms notifications");
        Check(reminders.Observe(2, 16, false, true, false), ShiftNotice.None, "disabled/outside factory notifications stay quiet");
        Check(reminders.Observe(2, 16, false, true, true), ShiftNotice.None, "enabling notifications does not replay muted events");
        Check(reminders.Observe(2, 9, false, false, true), ShiftNotice.None, "clock rollback rearms notifications");
        Check(reminders.Observe(2, 11, true, false, true), ShiftNotice.Lunch, "lunch after rollback");
        Check(reminders.Observe(2, float.NaN, true, false, true), ShiftNotice.None, "invalid clock does not notify");
        var late = new ShiftReminders();
        Check(late.Observe(3, 17, true, true, true), ShiftNotice.EndOfShift, "late load prioritizes finish over lunch");
        return "PASS: lunch/shift reminders, once per day, mute, clock rollback, invalid time and late-load priority";
    }
    private static void Check(ShiftNotice actual, ShiftNotice expected, string label)
    {
        if (actual != expected) throw new Exception("FAIL: " + label);
    }
}

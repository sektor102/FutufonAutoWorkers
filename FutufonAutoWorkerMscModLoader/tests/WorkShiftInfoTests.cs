using System;
using FutufonAutoWorkerMscModLoader;

public static class WorkShiftInfoTests
{
    public static string Run()
    {
        var info = new WorkShiftInfo();
        info.Observe(false, 0, 0, -480);
        Require(info.StartMinutes == null && info.EndMinutes == null, "no invented stamps before first shift");
        info.Observe(true, 482, 510, -480);
        Require(WorkShiftInfo.Clock(info.StartMinutes) == "08:02" && info.EndMinutes == null,
            "clock-in ignores previous day's saved duration");
        info.Observe(false, 482, 498, 18);
        Require(WorkShiftInfo.Clock(info.EndMinutes) == "16:20", "clock-out comes from native recorded duration");
        var reloaded = new WorkShiftInfo();
        reloaded.Observe(false, 482, 498, 18);
        Require(reloaded.StartMinutes == info.StartMinutes && reloaded.EndMinutes == info.EndMinutes,
            "last shift survives reload without relying on unsaved PunchOutMinutes");
        Require(WorkShiftInfo.Balance(info.OvertimeMinutes) == "+00:18", "positive native balance");
        Require(WorkShiftInfo.Balance(-90) == "-01:30", "negative balance is not clamped away");
        Require(WorkShiftInfo.Balance(1510) == "+25:10", "balance does not wrap at 24 hours");
        Require(WorkShiftInfo.Clock(1439.8f) == "00:00", "clock round-over at midnight");
        Require(!WorkShiftInfo.MeetingDay(1) && WorkShiftInfo.MeetingDay(2) && !WorkShiftInfo.MeetingDay(3) &&
            !WorkShiftInfo.MeetingDay(null), "Tuesday-only meeting line");
        info.Observe(true, 470, 498, 18);
        Require(info.StartMinutes == 470 && info.EndMinutes == null, "next card-in clears previous end time");
        info.Observe(false, float.NaN, float.NaN, float.PositiveInfinity);
        Require(info.StartMinutes == null && info.EndMinutes == null && info.OvertimeMinutes == null, "invalid native data stays unknown");
        for (int total = 0; total <= 44; total++)
        {
            int completeRows = total / 4;
            int nativeTopRow = completeRows > 0 ? 1 : 0;
            Require(CartonLayout.LowerRows(total) + nativeTopRow == completeRows,
                "native top row plus added lower rows represent all filled layers at " + total);
        }
        return "PASS: native card times/reload, next shift, signed overtime, Tuesday-only meeting and lower rows at all 0-44 counts";
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); }
}

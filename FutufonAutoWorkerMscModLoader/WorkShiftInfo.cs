using System;
using System.Globalization;

namespace FutufonAutoWorkerMscModLoader
{
    // The game's own saved punch-in and daily duration also recover the last
    // punch-out after loading: PunchOutMinutes itself is not saved by the game.
    internal sealed class WorkShiftInfo
    {
        internal float? StartMinutes { get; private set; }
        internal float? EndMinutes { get; private set; }
        internal float? OvertimeMinutes { get; private set; }
        internal bool Active { get; private set; }
        internal void Observe(bool dayActive, float punchIn, float workedMinutes, float overtime)
        {
            Active = dayActive;
            OvertimeMinutes = Finite(overtime) ? (float?)overtime : null;
            bool validStart = Finite(punchIn) && punchIn > 0f && punchIn < 1440f;
            bool validEnd = Finite(workedMinutes) && workedMinutes > 0f && workedMinutes <= 1440f;
            StartMinutes = validStart && (dayActive || validEnd) ? (float?)punchIn : null;
            EndMinutes = StartMinutes.HasValue && !dayActive && validEnd ? (float?)(punchIn + workedMinutes) : null;
        }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        internal static string Clock(float? minutes)
        {
            if (!minutes.HasValue || !Finite(minutes.Value)) return "--:--";
            int total = ((int)Math.Round(minutes.Value, MidpointRounding.AwayFromZero) % 1440 + 1440) % 1440;
            return (total / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (total % 60).ToString("00", CultureInfo.InvariantCulture);
        }
        internal static string Balance(float? minutes)
        {
            if (!minutes.HasValue || !Finite(minutes.Value)) return "--:--";
            long total = (long)Math.Round(Math.Abs((double)minutes.Value), MidpointRounding.AwayFromZero);
            return (minutes.Value < 0 ? "-" : "+") + (total / 60).ToString("00", CultureInfo.InvariantCulture) + ":" +
                (total % 60).ToString("00", CultureInfo.InvariantCulture);
        }
        internal static bool MeetingDay(int? day) { return day == 2; }
    }
}

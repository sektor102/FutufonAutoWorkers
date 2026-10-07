using System;

namespace FutufonAutoWorkerMscModLoader
{
    internal enum AutomationMode { FullCycle, ManualDelivery, LoosePackages, ManualSupplies }
    internal enum BatchVolume { OneCarton, FillPallet }
    internal enum WorkerPhase
    {
        Ready, Preparing, Collecting, Assembling, Folding, Closing, Packing, Delivering,
        WaitingStock, WaitingOpenStock, WaitingDelivery, WaitingStacks, Returning, Paused, Done, Error
    }

    // Volume stays fixed for a run. Mode changes at a complete-package boundary.
    internal sealed class AutomationOptions
    {
        internal readonly AutomationMode Mode;
        internal readonly BatchVolume Volume;
        internal bool FetchSupplies { get { return Mode != AutomationMode.ManualSupplies; } }
        internal bool PackCarton { get { return Mode <= AutomationMode.ManualDelivery; } }
        internal bool DeliverCarton { get { return Mode == AutomationMode.FullCycle; } }

        internal AutomationOptions(AutomationMode mode, BatchVolume volume) { Mode = mode; Volume = volume; }
        internal bool Matches(AutomationOptions other) { return other != null && Mode == other.Mode && Volume == other.Volume; }
        internal static AutomationMode NextMode(AutomationMode mode) { return (AutomationMode)(((int)mode + 1) % 4); }
    }

    internal sealed class BatchProgress
    {
        internal const int PackagesPerCarton = 44;
        internal readonly int TargetCartons;
        internal int CompletedCartons { get; private set; }
        internal int CurrentPackages { get; private set; }
        internal bool PauseRequested { get; private set; }
        internal bool Complete { get { return CompletedCartons == TargetCartons; } }
        internal int TotalPackages { get { return CompletedCartons * PackagesPerCarton + CurrentPackages; } }
        internal int TargetPackages { get { return TargetCartons * PackagesPerCarton; } }

        internal BatchProgress(int targetCartons)
        {
            if (targetCartons < 1 || targetCartons > 4) throw new ArgumentOutOfRangeException("targetCartons");
            TargetCartons = targetCartons;
        }
        internal void AdoptPackedCount(int count)
        {
            if (count < CurrentPackages || count > PackagesPerCarton || Complete)
                throw new InvalidOperationException("Shipping contents changed unexpectedly.");
            CurrentPackages = count;
        }
        internal void RecordPackage()
        {
            if (Complete || CurrentPackages == PackagesPerCarton) throw new InvalidOperationException("Batch already full.");
            CurrentPackages++;
        }
        internal void FinishCarton()
        {
            if (Complete || CurrentPackages != PackagesPerCarton) throw new InvalidOperationException("Batch is incomplete.");
            CompletedCartons++;
            CurrentPackages = 0;
        }
        internal void RequestPause() { PauseRequested = true; }
        internal void Resume() { PauseRequested = false; }
    }

    internal enum ShiftNotice { None, Lunch, EndOfShift }

    internal static class CartonLayout
    {
        internal static int LowerRows(int total) { return Math.Max(0, total / 4 - 1); }
    }

    // Read-only reminders. A clock rollback/new day rearms them; muted events stay muted.
    internal sealed class ShiftReminders
    {
        private int? _day;
        private float? _hour;
        private bool _lunchSeen, _endSeen;
        internal ShiftNotice Observe(int day, float hour, bool lunch, bool home, bool relevant)
        {
            if (float.IsNaN(hour) || float.IsInfinity(hour)) return ShiftNotice.None;
            if (!_day.HasValue || _day.Value != day || (_hour.HasValue && hour < _hour.Value - 1f))
                _lunchSeen = _endSeen = false;
            _day = day;
            _hour = hour;
            bool endNow = home || hour >= 16f;
            bool lunchNow = !endNow && (lunch || (hour >= 11f && hour < 12f));
            if (endNow && !_endSeen)
            {
                _endSeen = true;
                _lunchSeen = true;
                return relevant ? ShiftNotice.EndOfShift : ShiftNotice.None;
            }
            if (lunchNow && !_lunchSeen)
            {
                _lunchSeen = true;
                return relevant ? ShiftNotice.Lunch : ShiftNotice.None;
            }
            return ShiftNotice.None;
        }
    }
}

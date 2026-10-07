using System;
using System.Collections;
using System.Collections.Generic;
using FutufonAutoWorkerMscModLoader;

namespace FutufonAutoWorkerMscModLoader
{
    internal sealed class GameObject
    {
        internal bool Destroyed;
        public static bool operator ==(GameObject left, GameObject right)
        {
            if (Object.ReferenceEquals(left, right)) return true;
            bool leftNull = Object.ReferenceEquals(left, null) || left.Destroyed;
            bool rightNull = Object.ReferenceEquals(right, null) || right.Destroyed;
            return leftNull && rightNull;
        }
        public static bool operator !=(GameObject left, GameObject right) { return !(left == right); }
        public override bool Equals(object other) { return Object.ReferenceEquals(this, other); }
        public override int GetHashCode() { return base.GetHashCode(); }
    }
    // run-tests.ps1 injects the ACTUAL Run, content synchronization and carton iterators
    // from FactoryDemo.cs into this partial class. Only physical/native actions
    // are replaced, to exercise production scheduling without a running Unity.
    internal partial class FactoryDemo
    {
        internal const int BatchSize = 44;
        private sealed class Box { internal bool activeInHierarchy; }
        private sealed class Counter { internal int Value; }
        private sealed class ObjectVariable { internal GameObject Value; }
        private sealed class Shipping
        {
            internal readonly Counter Total = new Counter();
            internal readonly ObjectVariable Part = new ObjectVariable();
            internal int Empty;
        }
        private readonly Box _shippingBox = new Box();
        private readonly Shipping _shipping = new Shipping();
        private readonly List<int> _shippingBodies = new List<int>();
        private bool _adoptInitialContents = true;
        private int _expectedPackedCount;
        private AutomationMode? _requestedMode;
        private bool ModeChangeRequested { get { return _requestedMode.HasValue && _requestedMode.Value != Options.Mode; } }
        internal AutomationOptions Options;
        internal BatchProgress Progress;
        internal bool WaitingForPlayer;
        internal int Made, Delivered, Stacked, CartonsHeld;
        internal Action<FactoryDemo> BeforePackageFinished, DuringWait;
        internal WorkerPhase Phase;
        internal bool CanResume { get { return !Progress.Complete; } }
        private readonly List<GameObject> _unpackedOutput = new List<GameObject>();
        internal Action<FactoryDemo> BeforeCartonReady;
        private Action<WorkerPhase, int> _status;
        private readonly Action<string> _progress = message => { };
        private readonly Action<string> _report = message => { };
        internal FactoryDemo(AutomationMode mode, int target = 1, int existing = 0)
        {
            Options = new AutomationOptions(mode, target == 1 ? BatchVolume.OneCarton : BatchVolume.FillPallet);
            Progress = new BatchProgress(target);
            _shippingBox.activeInHierarchy = existing > 0;
            _shipping.Total.Value = existing;
            _status = (phase, detail) => Phase = phase;
        }
        internal void RequestMode(AutomationMode mode) { _requestedMode = mode; }
        internal void RequestPause() { Progress.RequestPause(); }
        private void ApplyRequestedMode()
        {
            if (ModeChangeRequested)
            {
                Options = new AutomationOptions(_requestedMode.Value, Options.Volume);
                ReleaseBodies(_shippingBodies);
            }
            _requestedMode = null;
        }
        private static Counter RequiredInt(Shipping shipping, string name) { return shipping.Total; }
        private void ValidateShippingCounts()
        {
            if (_shipping.Total.Value < 0 || _shipping.Total.Value > BatchSize || _shipping.Empty != 0)
                throw new InvalidOperationException("Invalid shipping contents.");
        }
        private static ObjectVariable RequiredGameObject(Shipping shipping, string name) { return shipping.Part; }
        internal void InsertManually(int count, int ownStacks = 0)
        {
            _shippingBox.activeInHierarchy = true;
            _shipping.Total.Value += count;
            int transferred = 0;
            foreach (var package in _unpackedOutput)
            {
                if (transferred == ownStacks) break;
                if (package == null) continue;
                transferred++;
                package.Destroyed = true;
                _shipping.Part.Value = package;
            }
            if (transferred != ownStacks) throw new Exception("Not enough own packages to transfer");
            if (ownStacks > 0 && ownStacks == count)
                _shipping.Part.Value.Destroyed = false; // Native count precedes Unity's deferred Destroy.
            else _shipping.Part.Value = new GameObject();
        }
        internal void MarkIncomplete() { _shipping.Empty = 1; }
        private bool PlayerLeft() { return false; }
        private IEnumerator PrepareShippingBox()
        {
            if (!_shippingBox.activeInHierarchy) { _expectedPackedCount = 0; _shippingBox.activeInHierarchy = true; _shipping.Total.Value = 0; }
            _shippingBodies.Add(1);
            CartonsHeld = Math.Max(CartonsHeld, _shippingBodies.Count);
            yield return null;
            if (BeforeCartonReady != null) { var action = BeforeCartonReady; BeforeCartonReady = null; action(this); }
        }
        private IEnumerator DeliverShippingBox()
        {
            if (_shipping.Total.Value != 44) throw new Exception("Delivered a partial carton");
            _shippingBox.activeInHierarchy = false;
            Delivered++;
            _expectedPackedCount = 0;
            ReleaseBodies(_shippingBodies);
            yield return null;
        }
        private IEnumerator WaitForStackSpace() { yield break; }
        private IEnumerator EnsureSupplies()
        {
            if (DuringWait != null) { var action = DuringWait; DuringWait = null; action(this); }
            yield return null;
        }
        private IEnumerator MakePackage()
        {
            var modeAtStart = Options.Mode;
            yield return null;
            if (BeforePackageFinished != null) BeforePackageFinished(this);
            if (modeAtStart != Options.Mode) throw new Exception("Mode changed inside unfinished package");
            if (Options.PackCarton)
            {
                SynchronizeShippingContents();
                if (_shipping.Total.Value >= 44) throw new Exception("Packed a 45th package into carton");
                _shipping.Total.Value++;
                _expectedPackedCount = _shipping.Total.Value;
            }
            else { Stacked++; _unpackedOutput.Add(new GameObject()); }
            Made++;
        }
        private IEnumerator Pace() { yield return null; }
        private void ReleaseBodies() { }
        private void ReleaseBodies(List<int> bodies) { bodies.Clear(); }
        internal void CarryManually() { _shippingBox.activeInHierarchy = false; }
    }
}

public static class FactoryRunTests
{
    public static string Run()
    {
        foreach (AutomationMode from in Enum.GetValues(typeof(AutomationMode)))
            foreach (AutomationMode to in Enum.GetValues(typeof(AutomationMode)))
            {
                var factory = new FactoryDemo(from);
                factory.BeforePackageFinished = f => { if (f.Made == 16) f.RequestMode(to); };
                Finish(factory);
                Require(factory.Progress.Complete && factory.Made == 44 && factory.Progress.TotalPackages == 44,
                    "all 16 mode transitions retain progress: " + from + " -> " + to);
                Require(factory.Options.Mode == to && factory.CartonsHeld <= 1, "deferred switch and no duplicate body holds");
            }
        foreach (AutomationMode packing in new[] { AutomationMode.FullCycle, AutomationMode.ManualDelivery })
            foreach (int existing in new[] { 0, 1, 17, 43, 44 })
            {
                var initial = new FactoryDemo(packing, 1, existing);
                Finish(initial);
                Require(initial.Made == 44 - existing && initial.Progress.Complete,
                    "manual/saved contents produce only the remaining packages: " + packing + " " + existing);
                Require(initial.Delivered == (packing == AutomationMode.FullCycle ? 1 : 0), "delivery stays mode-specific");
            }
        var afterPickup = new FactoryDemo(AutomationMode.FullCycle);
        afterPickup.BeforeCartonReady = f => f.InsertManually(17);
        Finish(afterPickup);
        Require(afterPickup.Made == 27 && afterPickup.Delivered == 1, "contents are read after acquiring the carton too");
        var manualPause = new FactoryDemo(AutomationMode.FullCycle);
        manualPause.BeforePackageFinished = f => { if (f.Made == 4) f.RequestPause(); };
        Finish(manualPause);
        manualPause.InsertManually(12);
        manualPause.BeforePackageFinished = null;
        Finish(manualPause);
        Require(manualPause.Made == 32 && manualPause.Delivered == 1 && manualPause.Progress.TotalPackages == 44,
            "manual additions while paused reduce resumed work, without another carton");
        var filledOnPause = new FactoryDemo(AutomationMode.FullCycle);
        filledOnPause.BeforePackageFinished = f => { if (f.Made == 4) f.RequestPause(); };
        Finish(filledOnPause);
        filledOnPause.InsertManually(39);
        filledOnPause.BeforePackageFinished = null;
        Finish(filledOnPause);
        Require(filledOnPause.Made == 5 && filledOnPause.Delivered == 1, "manually completed paused carton needs no new parts");
        var whileWaiting = new FactoryDemo(AutomationMode.FullCycle);
        whileWaiting.DuringWait = f => f.InsertManually(17);
        Finish(whileWaiting);
        Require(whileWaiting.Made == 27 && whileWaiting.Delivered == 1, "manual additions during supply wait are read before issuing parts");
        var duringPackage = new FactoryDemo(AutomationMode.FullCycle);
        duringPackage.BeforePackageFinished = f => { if (f.Made == 0) f.InsertManually(17); };
        Finish(duringPackage);
        Require(duringPackage.Made == 27 && duringPackage.Delivered == 1, "manual additions during assembly are included before packing");
        var fullDuringWait = new FactoryDemo(AutomationMode.FullCycle);
        fullDuringWait.DuringWait = f => f.InsertManually(44);
        Finish(fullDuringWait);
        Require(fullDuringWait.Made == 0 && fullDuringWait.Delivered == 1, "manual completion during wait never makes a 45th package");
        var existingPallet = new FactoryDemo(AutomationMode.FullCycle, 4, 17);
        Finish(existingPallet);
        Require(existingPallet.Made == 159 && existingPallet.Delivered == 4, "partial first carton counts toward a four-slot pallet");
        var ownStacks = new FactoryDemo(AutomationMode.LoosePackages);
        ownStacks.BeforePackageFinished = f => { if (f.Made == 9) f.RequestPause(); };
        Finish(ownStacks);
        ownStacks.InsertManually(10, 10);
        ownStacks.RequestMode(AutomationMode.FullCycle);
        ownStacks.BeforePackageFinished = null;
        Finish(ownStacks);
        Require(ownStacks.Made == 44 && ownStacks.Delivered == 1, "manual own-stack transfers, including deferred Destroy, never count twice");
        var invalid = new FactoryDemo(AutomationMode.FullCycle, 1, 17);
        invalid.MarkIncomplete();
        bool rejected = false;
        try { Finish(invalid); } catch (InvalidOperationException) { rejected = true; }
        Require(rejected && invalid.Made == 0, "incomplete manual contents are rejected before consuming parts");
        var paused = new FactoryDemo(AutomationMode.FullCycle);
        paused.BeforePackageFinished = f => { if (f.Made == 43) f.RequestPause(); };
        Finish(paused);
        Require(paused.Progress.CurrentPackages == 44 && !paused.Progress.Complete && paused.Delivered == 0,
            "pause during last package leaves a full carton for resume");
        paused.BeforePackageFinished = null;
        Finish(paused);
        Require(paused.Progress.Complete && paused.Made == 44 && paused.Delivered == 1, "resume delivers once without a 45th package");
        foreach (AutomationMode mode in Enum.GetValues(typeof(AutomationMode)))
        {
            var pallet = new FactoryDemo(mode, 4);
            Finish(pallet, true);
            Require(pallet.Progress.Complete && pallet.Made == 176, "all modes finish exactly 176: " + mode);
        }
        var mixed = new FactoryDemo(AutomationMode.LoosePackages, 2);
        mixed.BeforePackageFinished = f => { if (f.Made == 9) f.RequestMode(AutomationMode.FullCycle); };
        Finish(mixed);
        Require(mixed.Made == 88 && mixed.Stacked == 10 && mixed.Delivered == 1,
            "mixed output preserves stacks, finishes full cartons mid-batch, never adopts existing count twice");
        var waiting = new FactoryDemo(AutomationMode.ManualSupplies);
        waiting.DuringWait = f => f.RequestMode(AutomationMode.FullCycle);
        Finish(waiting);
        Require(waiting.Made == 44 && waiting.Stacked == 0, "mode request while waiting applies before issuing parts");
        return "PASS: actual scheduler and count synchronization, idle/paused manual additions, full cartons without new parts, own-stack transfers, all 16 transitions and 44/176 limits";
    }
    private static void Finish(FactoryDemo factory, bool manualCarry = false)
    {
        using (var worker = new DemoStepper(factory.Run()))
        {
            int frames = 0;
            while (worker.Tick())
            {
                if (manualCarry && factory.WaitingForPlayer) factory.CarryManually();
                if (++frames > 5000) throw new Exception("FAIL: factory did not settle: " + factory.Options.Mode);
            }
        }
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); }
}

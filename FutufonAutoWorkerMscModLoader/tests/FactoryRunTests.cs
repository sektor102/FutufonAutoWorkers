using System;
using System.Collections;
using System.Collections.Generic;
using FutufonAutoWorkerMscModLoader;

namespace FutufonAutoWorkerMscModLoader
{
    // run-tests.ps1 injects the ACTUAL Run and FinishFullShippingBox iterators
    // from FactoryDemo.cs into this partial class. Only physical/native actions
    // are replaced, to exercise production scheduling without a running Unity.
    internal partial class FactoryDemo
    {
        internal const int BatchSize = 44;
        private sealed class Box { internal bool activeInHierarchy; }
        private sealed class Counter { internal int Value; }
        private sealed class Shipping { internal readonly Counter Total = new Counter(); }
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
        private bool PlayerLeft() { return false; }
        private IEnumerator PrepareShippingBox()
        {
            if (!_shippingBox.activeInHierarchy) { _shippingBox.activeInHierarchy = true; _shipping.Total.Value = 0; }
            _shippingBodies.Add(1);
            CartonsHeld = Math.Max(CartonsHeld, _shippingBodies.Count);
            yield return null;
        }
        private IEnumerator DeliverShippingBox()
        {
            if (_shipping.Total.Value != 44) throw new Exception("Delivered a partial carton");
            _shippingBox.activeInHierarchy = false;
            Delivered++;
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
                if (_shipping.Total.Value >= 44) throw new Exception("Packed a 45th package into carton");
                _shipping.Total.Value++;
                _expectedPackedCount = _shipping.Total.Value;
            }
            else Stacked++;
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
        var initial = new FactoryDemo(AutomationMode.FullCycle, 1, 17);
        Finish(initial);
        Require(initial.Made == 27 && initial.Delivered == 1, "saved partial carton is retained, not fabricated or reset");
        var full = new FactoryDemo(AutomationMode.FullCycle, 1, 44);
        Finish(full);
        Require(full.Made == 0 && full.Delivered == 1, "existing full carton delivers without another 44 packages");
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
        return "PASS: actual factory scheduler, all 16 mode transitions, mixed outputs, saved 17/44 and 44/44 cartons, last-package pause/resume and 176 limits";
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

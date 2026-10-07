using System;
using FutufonAutoWorkerMscModLoader;

namespace FutufonAutoWorkerMscModLoader
{
    // The real hotkey handler and mode callback are injected by run-tests.ps1.
    // Only MSCLoader input/settings are replaced, so no Unity session is needed.
    internal partial class FutufonAutoWorkerMscModLoader
    {
        private sealed class Key
        {
            internal bool Pressed;
            internal bool GetKeybindDown() { bool result = Pressed; Pressed = false; return result; }
        }
        private sealed class Slider
        {
            internal int Value;
            internal void SetValue(int value) { Value = value; }
        }
        private readonly Key _modeKey = new Key(), _volumeKey = new Key();
        private readonly Slider _mode = new Slider(), _volume = new Slider();
        internal FactoryDemo _demo;
        private void UpdateSettingsHelp() { }
        internal AutomationOptions CurrentOptions() { return new AutomationOptions((AutomationMode)_mode.Value, (BatchVolume)_volume.Value); }
        internal void Press(bool mode, bool volume) { _modeKey.Pressed = mode; _volumeKey.Pressed = volume; UpdateSelectionHotkeys(); }
    }
}

public static class SelectionTests
{
    public static string Run()
    {
        var controls = new FutufonAutoWorkerMscModLoader.FutufonAutoWorkerMscModLoader();
        for (int i = 1; i <= 4; i++)
        {
            controls.Press(true, false);
            Require((int)controls.CurrentOptions().Mode == i % 4, "mode key cycles before the first F8");
            Require(controls._demo == null, "selection does not create/start a worker");
            Require(WorkerText.SelectedMode(true, controls.CurrentOptions().Mode, null) == WorkerText.Mode(true, controls.CurrentOptions().Mode),
                "idle HUD shows the selected mode immediately");
        }
        controls.Press(false, true);
        Require(controls.CurrentOptions().Volume == BatchVolume.FillPallet, "volume key selects pallet before F8");
        controls.Press(false, true);
        Require(controls.CurrentOptions().Volume == BatchVolume.OneCarton, "volume key toggles back to one carton");
        controls._demo = new FactoryDemo(AutomationMode.FullCycle);
        controls.Press(true, true);
        Require(controls.CurrentOptions().Mode == AutomationMode.ManualDelivery && controls.CurrentOptions().Volume == BatchVolume.FillPallet,
            "paused selections are independent of running state");
        Require(controls._demo.Options.Mode == AutomationMode.FullCycle && controls._demo.Options.Volume == BatchVolume.OneCarton && controls._demo.Made == 0,
            "selection alone never changes an unfinished package or the current batch quota");
        Require(WorkerText.SelectedMode(true, controls.CurrentOptions().Mode, null) == "Переноска вручную",
            "paused HUD uses selected mode rather than the previous run");
        Require(WorkerText.SelectedMode(false, controls.CurrentOptions().Mode, controls._demo.Options.Mode).StartsWith("Changing to: "),
            "running HUD identifies a deferred mode change");
        Require(WorkerText.Volume(true, BatchVolume.OneCarton).Contains("44") && WorkerText.Volume(false, BatchVolume.FillPallet).Contains("176"),
            "HUD volume includes capacity in either language");
        Require(WorkerText.IdleCounter(true, 157.6f).StartsWith("Простой: ") && WorkerText.IdleCounter(true, 157.6f).EndsWith(" мин"),
            "idle counter is labelled and has minutes");
        return "PASS: actual selection hotkeys before F8 and while paused, volume toggle, immediate HUD mode/volume text and labelled idle minutes";
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); }
}

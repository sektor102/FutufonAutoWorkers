using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using FutufonAutoWorkerMscModLoader;
using MSCLoader;

// UI handles stand in for MSCLoader's internal label API (verified against 1.4.2).
namespace MSCLoader
{
    public class TextHandle : UnityEngine.Object { public string text { get; set; } }
    public class ToggleHandle : UnityEngine.Object { public bool isOn { get; set; } }
    public class DropdownHandle : UnityEngine.Object { public IList Items = new List<DropdownItem> { new DropdownItem(), new DropdownItem() }; }
    public class DropdownItem { public string Caption { get; set; } }
    public class Element : UnityEngine.Object
    {
        internal TextHandle settingName = new TextHandle(), value = new TextHandle();
        internal ToggleHandle checkBox = new ToggleHandle();
        internal DropdownHandle dropDownList = new DropdownHandle();
    }
    public class ModSetting
    {
        internal string Name;
        internal Element SettingsElement = new Element();
        private void UpdateName(string value) { Name = value; SettingsElement.settingName.text = value; }
    }
    public class ModKeybind { internal string Name = ""; }
    public class SettingsKeybind : ModKeybind { internal int SavedKey = 42; }
    public class SettingsSliderInt : ModSetting
    {
        internal int Value;
        internal string[] TextValues;
        public int GetValue() { return Value; }
    }
    public class SettingsDropDownList : ModSetting
    {
        internal int Value;
        internal string[] ArrayOfItems = { "Русский", "Английский" };
        public int GetSelectedItemIndex() { return Value; }
    }
    public class SettingsCheckBox : ModSetting { internal bool Value; public bool GetValue() { return Value; } }
    public static class ModConsole { public static void Warning(string message) { throw new Exception(message); } }
}

namespace FutufonAutoWorkerMscModLoader
{
    internal static class MenuText { internal static string Pick(bool ru, string russian, string english) { return ru ? russian : english; } }
    // The production language callback is injected here. No worker or save values are replaced by it.
    internal partial class SettingsUiHarness
    {
        internal SettingsDropDownList _language = new SettingsDropDownList();
        internal SettingsSliderInt _mode = new SettingsSliderInt { Value = 3, TextValues = new string[4] };
        internal SettingsSliderInt _volume = new SettingsSliderInt { Value = 1, TextValues = new string[2] };
        internal SettingsSliderInt _workSpeed = new SettingsSliderInt { Value = 65 };
        internal SettingsCheckBox _showPanel = new SettingsCheckBox { Value = false }, _shiftNotifications = new SettingsCheckBox { Value = true },
            _fileLogging = new SettingsCheckBox { Value = true };
        internal SettingsKeybind _toggleKey = new SettingsKeybind(), _panelKey = new SettingsKeybind(), _modeKey = new SettingsKeybind(),
            _volumeKey = new SettingsKeybind(), _snapshotKey = new SettingsKeybind(), _recordKey = new SettingsKeybind();
        private bool? _menuRussian;
        internal bool? MenuRussian { get { return _menuRussian; } }
        private bool Russian { get { return _language.GetSelectedItemIndex() == 0; } }
        private void UpdateSettingsHelp() { SettingsLocalization.SliderCaption(_mode); SettingsLocalization.SliderCaption(_volume); }
        internal void Language(int index) { _language.Value = index; UpdateSettingsLanguage(); }
    }

    // File methods and the production settings callback are injected here; only the output root is redirected.
    internal partial class LoggingUiHarness
    {
        private readonly string _testDirectory;
        private bool _loaded = true;
        private SettingsCheckBox _fileLogging = new SettingsCheckBox();
        private readonly UiSessionLogger _logger;
        private readonly Dictionary<int, string> _lastSnapshots = new Dictionary<int, string>();
        private float _nextSample;
        private StreamWriter _sessionLog;
        private string _sessionPath;
        internal LoggingUiHarness(string directory)
        {
            _testDirectory = directory;
            _logger = new UiSessionLogger(WriteSessionLog, message => { });
        }
        internal string SessionPath { get { return _sessionPath; } }
        internal bool Enabled { get { return _logger.FileEnabled && _logger.Detailed; } }
        internal bool CacheReset { get { return _nextSample == 0 && _lastSnapshots.Count == 0; } }
        internal void Enable(bool value) { _fileLogging.Value = value; LoggingSettingChanged(); }
        internal void WriteAll() { foreach (UiLogKind kind in Enum.GetValues(typeof(UiLogKind))) _logger.Write("test " + kind, kind); }
        internal void OpenWhileOff() { OpenSessionLog(); }
        internal void Unload() { _loaded = false; LoggingSettingChanged(); }
        internal void Cleanup() { CloseSessionLog(); }
    }
}

public static class SettingsUiTests
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    public static string Run()
    {
        var ui = new SettingsUiHarness();
        var capturedModeLabels = ui._mode.TextValues;
        ui.Language(1); // Saved English must localize the first render too.
        Require(ui.MenuRussian == false && ui._language.Name == "Language" && ui._mode.Name == "Automation mode" && ui._fileLogging.Name == "Write logs (diagnostics)",
            "English setting names are applied to existing controls");
        Require(ui._mode.SettingsElement.value.text == "4: Manual supplies" && ui._volume.SettingsElement.value.text == "Fill one pallet",
            "visible slider values use the selected language");
        Require(ui._language.ArrayOfItems[0] == "Russian" && ((DropdownItem)ui._language.SettingsElement.dropDownList.Items[1]).Caption == "English",
            "live dropdown captions are localized");
        Require(ui._toggleKey.Name == "Start / soft pause" && ui._snapshotKey.Name == "Factory snapshot", "keybinding names use the same language");
        ui.Language(0);
        Require(ui._mode.SettingsElement.settingName.text == "Режим" && ui._mode.SettingsElement.value.text == "4: Компоненты вручную" &&
            ui._fileLogging.Name == "Записывать логи (диагностика)", "switching back updates visible Russian names and values");
        Require(Object.ReferenceEquals(capturedModeLabels, ui._mode.TextValues) && capturedModeLabels[0] == "1: Полный цикл",
            "existing slider callback arrays are retained and translated");
        Require(ui._mode.Value == 3 && ui._volume.Value == 1 && ui._workSpeed.Value == 65 && !ui._showPanel.Value &&
            ui._shiftNotifications.Value && ui._fileLogging.Value && ui._toggleKey.SavedKey == 42,
            "language changes preserve values, flags and assigned keys");
        ui._mode.Value = 2;
        SettingsLocalization.SliderCaption(ui._mode);
        Require(ui._mode.SettingsElement.value.text == "3: Стопки коробок", "hotkey-driven values retain localized slider captions");
        SettingsLocalization.Checkbox(ui._fileLogging);
        Require(ui._fileLogging.SettingsElement.checkBox.isOn, "logging hotkey can synchronize the visible checkbox");

        string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "autoworker-logging-tests-" + Guid.NewGuid().ToString("N"));
        LoggingUiHarness logs = null;
        try
        {
            logs = new LoggingUiHarness(root);
            logs.Enable(false); logs.OpenWhileOff(); logs.WriteAll();
            Require(!Directory.Exists(root), "default use and snapshots create no log directory or file");
            logs.Enable(true);
            string first = logs.SessionPath;
            Require(logs.Enabled && logs.CacheReset && File.Exists(first), "GUI opt-in opens a diagnostic session immediately");
            logs.WriteAll();
            string contents;
            using (var reader = new StreamReader(new FileStream(first, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)))
                contents = reader.ReadToEnd();
            Require(contents.Contains("test Detail") && contents.Contains("test Snapshot") && contents.Contains("test Progress"), "enabled logs include diagnostics and snapshots");
            logs.Enable(false);
            Require(!logs.Enabled && logs.SessionPath == null, "opt-out closes the session and clears its path");
            File.AppendAllText(first, "file unlocked\n");
            long size = new FileInfo(first).Length;
            logs.WriteAll();
            Require(new FileInfo(first).Length == size && Directory.GetFiles(System.IO.Path.Combine(root, "AutoWorkerLogs")).Length == 1,
                "all file categories stop immediately after opt-out");
            logs.Enable(true);
            string second = logs.SessionPath;
            Require(second != first && File.Exists(second), "reenabling uses a new session without overwriting old logs");
            logs.Unload();
            File.AppendAllText(second, "file unlocked\n");
            Require(!logs.Enabled && logs.SessionPath == null, "menu return closes logging even when the saved toggle is on");
        }
        finally
        {
            if (logs != null) logs.Cleanup();
            string absolute = System.IO.Path.GetFullPath(root);
            Require(absolute.StartsWith(System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) &&
                System.IO.Path.GetFileName(absolute).StartsWith("autoworker-logging-tests-"), "cleanup must stay in the owned temporary directory");
            if (Directory.Exists(absolute)) Directory.Delete(absolute, true);
        }
        return "PASS: actual RU/EN menu callback, retained values/keys/callback arrays, localized live captions, default no-directory logging, opt-in/out, session reopen and file disposal";
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using HutongGames.PlayMaker;
using MSCLoader;
using UnityEngine;

namespace FutufonAutoWorkerMscModLoader
{
    public class FutufonAutoWorkerMscModLoader : Mod
    {
        public override string ID => "FutufonAutoWorker";
        public override string Name => "Futufon AutoWorker";
        public override string Author => "2Baikal";
        public override string Version => "1.2.1";
        public override string Description => WorkerText.Pick(Russian,
            "Автосборка на заводе: четыре режима, 44 коробки или палета, мягкая пауза и компактная панель.",
            "Factory assembly: four modes, 44 packages or one pallet, soft pause and a compact HUD.");
        public override Game SupportedGames => Game.MyWinterCar;

        private static readonly string[] FactoryObjectNames =
        {
            "PickChargers", "PickSheets", "PickManuals", "PickTrays", "PickBoxes",
            "chargers box(Clone)", "packaging sheets(Clone)",
            "manuals box(Clone)", "plastic trays(Clone)",
            "charger(Clone)", "package(Clone)", "manual(Clone)",
            "plastic tray(Clone)", "PalletPackagesPlayer", "packages box(Clone)",
            "TriggerPackage", "JOB", "CheckSlacking", "FACTORY"
        };

        private SettingsKeybind _toggleKey;
        private SettingsKeybind _snapshotKey;
        private SettingsKeybind _recordKey;
        private SettingsSliderInt _workSpeed;
        private SettingsSliderInt _mode, _volume;
        private SettingsDropDownList _language;
        private SettingsCheckBox _showPanel, _shiftNotifications;
        private SettingsKeybind _panelKey, _modeKey, _volumeKey;
        private SettingsText _settingsHelp;
        private bool _loaded;
        private SessionLogger _logger;
        private bool _recording
        {
            get { return _logger != null && _logger.Detailed; }
            set { if (_logger != null) _logger.Detailed = value; }
        }
        private float _nextSample;
        private DemoStepper _worker;
        private FactoryDemo _demo;
        private FactoryWorkMonitor _workMonitor;
        private FactoryTelemetry _telemetry;
        private WorkerPhase _phase = WorkerPhase.Ready;
        private int _phaseDetail;
        private ShiftNotice _notice;
        private float _noticeUntil;
        private string _lastError;
        private GUIStyle _hudStyle, _titleStyle;
        private StreamWriter _sessionLog;
        private string _sessionPath;
        private readonly Dictionary<int, string> _lastSnapshots = new Dictionary<int, string>();

        public override void ModSetup()
        {
            SetupFunction(Setup.OnLoad, Mod_OnLoad);
            SetupFunction(Setup.OnMenuLoad, Mod_OnMenuLoad);
            SetupFunction(Setup.Update, Mod_Update);
            SetupFunction(Setup.OnGUI, Mod_OnGUI);
            SetupFunction(Setup.ModSettings, Mod_Settings);
        }

        private void Mod_Settings()
        {
            _language = Settings.AddDropDownList("aw_language", "Язык / Language", new[] { "Русский", "English" }, 0, UpdateSettingsHelp);
            _mode = Settings.AddSlider("aw_mode", "Режим / Automation mode", 0, 3, 0, ModeSettingChanged, new[]
            {
                "1: Полный цикл / Full cycle", "2: Переноска вручную / Manual delivery",
                "3: Стопки коробок / Loose packages", "4: Компоненты вручную / Manual supplies"
            });
            _volume = Settings.AddSlider("aw_volume", "Объём / Volume", 0, 1, 0, UpdateSettingsHelp,
                new[] { "44: одна коробка / One carton", "Палета / Fill one pallet" });
            _workSpeed = Settings.AddSlider("aw_speed", "Скорость (%) / Speed (%)", 10, 100, 100);
            _showPanel = Settings.AddCheckBox("aw_hud", "Панель / Compact HUD", true);
            _shiftNotifications = Settings.AddCheckBox("aw_shift_notices", "Обед и конец смены / Shift notifications", true);
            _toggleKey = Keybind.Add("aw_toggle", "Старт / мягкая пауза — Start / soft pause", KeyCode.F8);
            _panelKey = Keybind.Add("aw_panel", "Показать / скрыть панель — Toggle HUD", KeyCode.F6);
            _modeKey = Keybind.Add("aw_next_mode", "Следующий режим / Next automation mode", KeyCode.None);
            _volumeKey = Keybind.Add("aw_next_volume", "Одна коробка / палета — Toggle batch volume", KeyCode.None);
            // New IDs also remove the previous version's saved F7/F9 defaults.
            _snapshotKey = Keybind.Add("aw_snapshot_optional", "Снимок игровых состояний / Factory snapshot", KeyCode.None);
            _recordKey = Keybind.Add("aw_record_optional", "Запись диагностики / Diagnostic recording", KeyCode.None);
            _settingsHelp = Settings.AddText("");
            UpdateSettingsHelp();
        }

        private bool Russian { get { return _language == null || _language.GetSelectedItemIndex() == 0; } }
        private void ModeSettingChanged()
        {
            UpdateSettingsHelp();
            if (_demo != null && _demo.CanResume) _demo.RequestMode(CurrentOptions().Mode);
        }
        private AutomationOptions CurrentOptions()
        {
            return new AutomationOptions((AutomationMode)(_mode == null ? 0 : Mathf.Clamp(_mode.GetValue(), 0, 3)),
                (BatchVolume)(_volume == null ? 0 : Mathf.Clamp(_volume.GetValue(), 0, 1)));
        }
        private void UpdateSettingsHelp()
        {
            if (_settingsHelp == null) return;
            var options = CurrentOptions();
            string description;
            switch (options.Mode)
            {
                case AutomationMode.ManualDelivery:
                    description = WorkerText.Pick(Russian, "Компоненты и упаковка автоматически. Полную коробку отнеси сам.",
                        "Automatic supplies and packing. Carry the full carton yourself."); break;
                case AutomationMode.LoosePackages:
                    description = WorkerText.Pick(Russian, "Автопополнение. Готовые коробки — четыре стопки по 11. Упаковка вручную.",
                        "Automatic restocking. Four stacks of 11 finished packages. Pack them manually."); break;
                case AutomationMode.ManualSupplies:
                    description = WorkerText.Pick(Russian, "Принеси и открой компоненты на столе. Готовые коробки — стопками; пополнение и упаковка вручную.",
                        "Bring and open supplies on the table. Finished packages go into stacks; restocking and packing are manual."); break;
                default:
                    description = WorkerText.Pick(Russian, "Автопополнение, сборка, упаковка по 44 и переноска на палету.",
                        "Automatic restocking, assembly, packing 44 packages and pallet delivery."); break;
            }
            _settingsHelp.SetValue(description + "\n" + WorkerText.Pick(Russian,
                "Объём «Палета»: до 4 × 44, по свободным местам ближайшей палеты.\nРежим можно выбрать до F8; во время работы смена ждёт текущую маленькую коробку.\nКлавиши режима и объёма назначаются в разделе клавиш; выбор сразу виден в панели.\nОбъём применяется к следующему запуску; язык и скорость меняются сразу. Диагностика без клавиш.",
                "Pallet volume: up to 4 × 44, based on free slots on the nearest pallet.\nChoose the mode before F8; changes while working wait for the current small package.\nAssign mode and volume keys in keybindings; selections appear in the HUD immediately.\nVolume applies to the next run; language and speed change immediately. Diagnostics are unassigned."));
        }

        private void Mod_OnLoad()
        {
            ResetDiagnostics();
            _logger = new SessionLogger(WriteSessionLog, message => ModConsole.Print("[AutoWorker] " + message));
            _loaded = true;
            _workMonitor = new FactoryWorkMonitor(message => _logger.Write(message, LogKind.Progress));
            _telemetry = new FactoryTelemetry(notice => { _notice = notice; _noticeUntil = Time.realtimeSinceStartup + 15f; }, Log);
            OpenSessionLog();
            Log("Loaded v" + Version + " for My Winter Car.");
            Log("DLL: " + GetType().Assembly.Location);
            Log("Start/resume/soft pause and HUD keys are configurable. Mode, volume and diagnostic keys are unassigned by default. Detailed recording OFF.");
            Log("Work speed: " + WorkSpeedPercent() + "% (100% = original pace).");
            Log("Session log: " + (_sessionPath ?? "output_log.txt only"));
        }

        private void Mod_OnMenuLoad()
        {
            _loaded = false;
            ResetDiagnostics();
            CloseSessionLog();
        }

        private void ResetDiagnostics()
        {
            _recording = false;
            _nextSample = 0f;
            _lastSnapshots.Clear();
            StopWorker(true);
            _workMonitor = null;
            if (_telemetry != null) _telemetry.Dispose();
            _telemetry = null;
            _phase = WorkerPhase.Ready;
            _noticeUntil = 0f;
            _lastError = null;
        }

        private void Mod_Update()
        {
            if (!_loaded) return;
            if (_workMonitor != null) _workMonitor.Poll();
            if (_telemetry != null)
                _telemetry.Poll(_shiftNotifications == null || _shiftNotifications.GetValue(),
                    _workMonitor != null && _workMonitor.AtWork, _worker != null);

            // Choose the next run before F8, including when both keys are pressed this frame.
            if (Time.timeScale > 0f) UpdateSelectionHotkeys();

            if (_toggleKey != null && _toggleKey.GetKeybindDown())
            {
                if (_worker != null)
                {
                    if (!_demo.Progress.PauseRequested)
                    {
                        _demo.RequestPause();
                        Log("CYCLE soft pause requested by F8. Finish the current small box before stopping.");
                    }
                }
                else if (Time.timeScale > 0f) StartCycle();
            }

            if (_panelKey != null && _panelKey.GetKeybindDown() && _showPanel != null)
                _showPanel.SetValue(!_showPanel.GetValue());

            if (_recordKey != null && _recordKey.GetKeybindDown())
            {
                _recording = !_recording;
                _lastSnapshots.Clear();
                _nextSample = 0f;
                Log("Recording " + (_recording ? "ON" : "OFF"));
            }

            if (_snapshotKey != null && _snapshotKey.GetKeybindDown())
                SampleFactory(true);

            if (_worker != null && Time.timeScale > 0f)
            {
                try
                {
                    if (_demo.PlayerLeft() && !_demo.WaitingForPlayer && !_demo.Progress.PauseRequested)
                    {
                        _demo.RequestPause();
                        Log("CYCLE soft pause: player left the workstation. Finish the current small box.");
                    }
                    if (!_worker.Tick()) StopWorker(false);
                }
                catch (Exception exception)
                {
                    _demo.MarkFailed();
                    StopWorker(false);
                    _phase = WorkerPhase.Error;
                    _lastError = exception.Message;
                    Log("CYCLE ERROR: " + exception);
                    SampleFactory(true);
                }
            }

            if (_recording && Time.realtimeSinceStartup >= _nextSample)
            {
                _nextSample = Time.realtimeSinceStartup + 1f;
                SampleFactory(false);
            }
        }

        private void UpdateSelectionHotkeys()
        {
            if (_modeKey != null && _modeKey.GetKeybindDown() && _mode != null)
            {
                _mode.SetValue((int)AutomationOptions.NextMode(CurrentOptions().Mode));
                ModeSettingChanged();
            }
            if (_volumeKey != null && _volumeKey.GetKeybindDown() && _volume != null)
            {
                _volume.SetValue((int)AutomationOptions.NextVolume(CurrentOptions().Volume));
                UpdateSettingsHelp();
            }
        }

        private void SampleFactory(bool includeGraph)
        {
            Action<string> write = message => _logger.Write(message, includeGraph ? LogKind.Snapshot : LogKind.Detail);
            var fsms = UnityEngine.Object.FindObjectsOfType(typeof(PlayMakerFSM));
            int count = 0;
            var liveIds = new HashSet<int>();

            foreach (var item in fsms)
            {
                var fsm = item as PlayMakerFSM;
                if (fsm == null || fsm.Fsm == null || !IsFactoryObject(fsm.transform)) continue;
                if (!SessionLogger.ShouldSample(fsm.gameObject.name, fsm.FsmName, includeGraph)) continue;

                count++;
                int id = fsm.GetInstanceID();
                if (!includeGraph) liveIds.Add(id);
                try
                {
                    string snapshot = DescribeFsm(fsm);
                    string previous;
                    if (includeGraph || !_lastSnapshots.TryGetValue(id, out previous) || previous != snapshot)
                    {
                        write(snapshot);
                        if (!includeGraph) _lastSnapshots[id] = snapshot;
                    }

                    if (includeGraph)
                        DumpGraph(fsm, write);
                }
                catch (Exception exception)
                {
                    write("Cannot inspect " + fsm.gameObject.name + ": " + exception.Message);
                }
            }

            var removedIds = new List<int>();
            if (!includeGraph)
                foreach (var id in _lastSnapshots.Keys)
                    if (!liveIds.Contains(id)) removedIds.Add(id);
            foreach (var id in removedIds)
            {
                write("FSM disappeared or became inactive: " + _lastSnapshots[id]);
                _lastSnapshots.Remove(id);
            }

            if (includeGraph)
            {
                write("Factory FSMs found: " + count + ". Visit the factory if none are loaded.");
                Log(_sessionLog != null ? "Factory snapshot saved to " + _sessionPath : "Factory snapshot could not be saved: log file unavailable.");
            }
        }

        private static bool IsFactoryObject(Transform target)
        {
            for (var node = target; node != null; node = node.parent)
                foreach (var name in FactoryObjectNames)
                    if (string.Equals(node.name, name, StringComparison.OrdinalIgnoreCase) &&
                        ((name != "JOB" && name != "CheckSlacking" && name != "FACTORY") || node == target)) return true;
            return false;
        }

        private static string DescribeFsm(PlayMakerFSM fsm)
        {
            var text = new StringBuilder();
            text.Append(fsm.gameObject.name).Append('#').Append(fsm.gameObject.GetInstanceID());
            text.Append(" / ").Append(fsm.FsmName).Append(" state=").Append(fsm.ActiveStateName);
            text.Append(" enabled=").Append(fsm.enabled);
            foreach (var value in fsm.FsmVariables.BoolVariables)
                text.Append(" | ").Append(value.Name).Append('=').Append(value.Value);
            foreach (var value in fsm.FsmVariables.IntVariables)
                text.Append(" | ").Append(value.Name).Append('=').Append(value.Value);
            foreach (var value in fsm.FsmVariables.FloatVariables)
                text.Append(" | ").Append(value.Name).Append('=').Append(value.Value.ToString("R", CultureInfo.InvariantCulture));
            foreach (var value in fsm.FsmVariables.StringVariables)
                text.Append(" | ").Append(value.Name).Append('=').Append(value.Value);
            foreach (var value in fsm.FsmVariables.GameObjectVariables)
                text.Append(" | ").Append(value.Name).Append('=').Append(value.Value == null ? "null" : value.Value.name + "#" + value.Value.GetInstanceID());
            return text.ToString();
        }

        private void DumpGraph(PlayMakerFSM fsm, Action<string> write)
        {
            foreach (var state in fsm.FsmStates)
            {
                var text = new StringBuilder("[AutoWorker]   State ");
                text.Append(state.Name);
                foreach (var action in state.Actions)
                    if (action != null) text.Append(" | action:").Append(action.GetType().Name);
                foreach (var transition in state.Transitions)
                    text.Append(" | ").Append(transition.EventName).Append(" -> ").Append(transition.ToState);
                write(text.ToString().Replace("[AutoWorker] ", ""));
                foreach (var action in state.Actions)
                {
                    if (action == null) continue;
                    var parameters = new StringBuilder("    " + action.GetType().Name);
                    foreach (var field in action.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                        parameters.Append(" | ").Append(field.Name).Append('=').Append(DescribeValue(field.GetValue(action)));
                    write(parameters.ToString());
                }
            }
            foreach (var transition in fsm.Fsm.GlobalTransitions)
                write("  Global " + transition.EventName + " -> " + transition.ToState);
        }

        private static string DescribeValue(object value)
        {
            if (value == null) return "null";
            var owner = value as FsmOwnerDefault;
            if (owner != null) return owner.OwnerOption + ":" + DescribeValue(owner.GameObject);
            var gameObject = value as FsmGameObject;
            if (gameObject != null) return "$" + gameObject.Name + ":" + DescribeValue(gameObject.Value);
            var flag = value as FsmBool;
            if (flag != null) return "$" + flag.Name + ":" + flag.Value;
            var integer = value as FsmInt;
            if (integer != null) return "$" + integer.Name + ":" + integer.Value;
            var number = value as FsmFloat;
            if (number != null) return "$" + number.Name + ":" + number.Value.ToString("R", CultureInfo.InvariantCulture);
            var word = value as FsmString;
            if (word != null) return "$" + word.Name + ":" + word.Value;
            var fsmEvent = value as FsmEvent;
            if (fsmEvent != null) return fsmEvent.Name;
            var unityObject = value as UnityEngine.Object;
            if (unityObject != null) return unityObject.name + "#" + unityObject.GetInstanceID();
            var array = value as Array;
            if (array != null)
            {
                var text = new StringBuilder("[");
                for (int i = 0; i < Math.Min(array.Length, 16); i++)
                    text.Append(i == 0 ? "" : ", ").Append(DescribeValue(array.GetValue(i)));
                if (array.Length > 16) text.Append(" ... total=").Append(array.Length);
                return text.Append(']').ToString();
            }
            return value.ToString();
        }

        private void StartCycle()
        {
            try
            {
                var options = CurrentOptions();
                bool resume = _demo != null && _demo.CanResume && _demo.Options.Volume == options.Volume && !_demo.PlayerLeft();
                if (resume) _demo.RequestMode(options.Mode);
                if (!resume)
                {
                    _demo = new FactoryDemo(_logger.Write, (phase, detail) => { _phase = phase; _phaseDetail = detail; },
                        () => 0.15f * 100f / WorkSpeedPercent(), options);
                    _demo.Prepare();
                }
                _lastSnapshots.Clear();
                _nextSample = 0f;
                _worker = new DemoStepper(_demo.Run());
                _phase = WorkerPhase.Preparing;
                _lastError = null;
                Log("CYCLE " + (resume ? "resumed" : "started") + " by F8. Mode=" + options.Mode +
                    " TargetCartons=" + _demo.Progress.TargetCartons + "; speed=" + WorkSpeedPercent() + "%. Detailed recording=" + _recording + ".");
            }
            catch (Exception exception)
            {
                if (_demo != null) _demo.MarkFailed();
                StopWorker(false);
                _phase = WorkerPhase.Error;
                _lastError = exception.Message;
                Log("CYCLE cannot start: " + exception.Message);
            }
        }

        private void StopWorker(bool discard)
        {
            if (_worker != null) _worker.Dispose();
            _worker = null;
            if (discard) _demo = null;
        }

        private int WorkSpeedPercent()
        {
            return _workSpeed == null ? 100 : Mathf.Clamp(_workSpeed.GetValue(), 10, 100);
        }

        private void OpenSessionLog()
        {
            CloseSessionLog();
            try
            {
                string directory = Path.Combine(Path.GetDirectoryName(GetType().Assembly.Location), "AutoWorkerLogs");
                Directory.CreateDirectory(directory);
                _sessionPath = Path.Combine(directory, "autoworker-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") +
                    "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".log");
                _sessionLog = new StreamWriter(_sessionPath, false, Encoding.UTF8) { AutoFlush = true };
            }
            catch (Exception exception)
            {
                _sessionPath = null;
                ModConsole.Warning("[AutoWorker] Cannot create session log: " + exception.Message);
            }
        }

        private void Log(string message)
        {
            _logger.Write(message, LogKind.Event);
        }

        private void WriteSessionLog(string message)
        {
            if (_sessionLog == null) return;
            try { _sessionLog.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " [AutoWorker] " + message); }
            catch (Exception exception)
            {
                CloseSessionLog();
                ModConsole.Warning("[AutoWorker] Session log write failed: " + exception.Message);
            }
        }

        private void CloseSessionLog()
        {
            if (_sessionLog != null)
            {
                try { _sessionLog.Dispose(); }
                catch (IOException) { }
                _sessionLog = null;
            }
        }

        private void Mod_OnGUI()
        {
            if (!_loaded) return;
            if (_hudStyle == null)
            {
                _hudStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = false };
                _hudStyle.normal.textColor = Color.white;
                _titleStyle = new GUIStyle(_hudStyle) { fontSize = 15, fontStyle = FontStyle.Bold };
            }
            Color previous = GUI.color;
            Color background = GUI.backgroundColor;
            try
            {
                bool ru = Russian;
                if ((_showPanel == null || _showPanel.GetValue()) &&
                    (_worker != null || (_telemetry != null && _telemetry.NearFactory)))
                {
                    float width = Mathf.Min(530f, Screen.width - 20f);
                    bool meeting = _telemetry != null && WorkShiftInfo.MeetingDay(_telemetry.Day);
                    float footerY = meeting ? 264f : 240f;
                    GUI.backgroundColor = new Color(0.04f, 0.04f, 0.04f, 0.92f);
                    GUI.Box(new Rect(10, 10, width, footerY + 24f), "");
                    var selected = CurrentOptions();
                    var mode = _worker == null ? selected.Mode : _demo.Options.Mode;
                    string modeText = WorkerText.SelectedMode(ru, selected.Mode, _worker == null ? (AutomationMode?)null : mode);
                    GUI.Label(new Rect(20, 16, width - 20, 22), "AutoWorker " + Version + " | " + modeText, _titleStyle);
                    bool nextVolume = _demo != null && _demo.Options.Volume != selected.Volume;
                    GUI.Label(new Rect(20, 40, width - 20, 22), WorkerText.Pick(ru, "Объём: ", "Volume: ") +
                        WorkerText.Volume(ru, selected.Volume) + (nextVolume ? WorkerText.Pick(ru, " | Следующий запуск", " | Next run") : ""), _hudStyle);
                    var progress = _demo == null || (_worker == null && nextVolume) ? null : _demo.Progress;
                    string counts = progress == null ? (selected.Volume == BatchVolume.OneCarton ? "0/44" :
                        WorkerText.Pick(ru, "0/до 176", "0/up to 176")) : progress.TotalPackages + "/" + progress.TargetPackages;
                    if (progress != null && progress.TargetCartons > 1)
                        counts += " | " + WorkerText.Pick(ru, "Партии: ", "Batches: ") + progress.CompletedCartons + "/" + progress.TargetCartons;
                    GUI.Label(new Rect(20, 64, width - 20, 22), WorkerText.Pick(ru, "Прогресс: ", "Progress: ") + counts +
                        " | " + WorkerText.Pick(ru, "Темп: ", "Speed: ") + WorkSpeedPercent() + "%", _hudStyle);
                    bool pausing = _worker != null && progress != null && progress.PauseRequested;
                    string phase = pausing ? WorkerText.Pick(ru, "Заканчиваю коробку и ставлю на паузу", "Finishing current package, then pausing") :
                        _phase == WorkerPhase.Error ? WorkerText.Error(ru, _lastError) : WorkerText.Phase(ru, _phase, _phaseDetail, mode);
                    if (_phase == WorkerPhase.Done && _demo != null && _demo.ModeChanged)
                        phase = WorkerText.Pick(ru, "Готово: выбранный объём выполнен", "Done: selected volume completed");
                    GUI.Label(new Rect(20, 88, width - 20, 22), phase, _hudStyle);
                    if (_telemetry != null)
                    {
                        string[] names = ru ? new[] { "Зар", "Инстр", "Лотки", "Упак" } : new[] { "Chargers", "Manuals", "Trays", "Sheets" };
                        var stock = new StringBuilder();
                        for (int i = 0; i < names.Length; i++) stock.Append(i == 0 ? "" : " | ").Append(names[i]).Append(": ").Append(_telemetry.Stock[i]);
                        GUI.Label(new Rect(20, 112, width - 20, 22), stock.ToString(), _hudStyle);
                        GUI.Label(new Rect(20, 136, width - 20, 22), WorkerText.Pick(ru, "Свободно на палетах: ", "Free pallet slots: ") +
                            _telemetry.FreePalletSlots + "/" + _telemetry.PalletCapacity, _hudStyle);
                    }
                    if (_workMonitor != null)
                    {
                        if (_workMonitor.State == WorkCheckState.Slacking) GUI.color = new Color(1f, 0.4f, 0.3f);
                        else if (_workMonitor.State == WorkCheckState.Working) GUI.color = new Color(0.5f, 1f, 0.5f);
                        GUI.Label(new Rect(20, 160, width - 20, 22), WorkerText.Supervisor(ru, _workMonitor.State) +
                            (_workMonitor.IdleMinutes.HasValue ? " | " + WorkerText.IdleCounter(ru, _workMonitor.IdleMinutes.Value) : ""), _hudStyle);
                        GUI.color = previous;
                    }
                    var shift = _telemetry == null ? null : _telemetry.Shift;
                    GUI.Label(new Rect(20, 188, width - 20, 22), WorkerText.Pick(ru, "Приход: ", "Clock in: ") +
                        WorkShiftInfo.Clock(shift == null ? null : shift.StartMinutes) + WorkerText.Pick(ru, " | Уход: ", " | Clock out: ") +
                        WorkShiftInfo.Clock(shift == null ? null : shift.EndMinutes) +
                        (shift != null && shift.Active ? WorkerText.Pick(ru, " | Смена идёт", " | On shift") : WorkerText.Pick(ru, " | Последняя смена", " | Last shift")), _hudStyle);
                    GUI.Label(new Rect(20, 212, width - 20, 22), WorkerText.Pick(ru, "Баланс переработок: ", "Overtime balance: ") +
                        WorkShiftInfo.Balance(shift == null ? null : shift.OvertimeMinutes), _hudStyle);
                    if (meeting) GUI.Label(new Rect(20, 236, width - 20, 22),
                        WorkerText.Pick(ru, "Собрание во вторник в 13:10", "Tuesday meeting at 13:10"), _hudStyle);
                    string keys = KeyHint(_toggleKey, WorkerText.Pick(ru, "старт/пауза", "start/pause")) + " | " +
                        KeyHint(_panelKey, WorkerText.Pick(ru, "панель", "HUD"));
                    if (_modeKey != null && _modeKey.GetKeyValue != KeyCode.None)
                        keys += " | " + KeyHint(_modeKey, WorkerText.Pick(ru, "режим", "mode"));
                    if (_volumeKey != null && _volumeKey.GetKeyValue != KeyCode.None)
                        keys += " | " + KeyHint(_volumeKey, WorkerText.Pick(ru, "объём", "volume"));
                    if (_recording) keys += WorkerText.Pick(ru, " | Диагн: вкл", " | Diag: ON");
                    GUI.Label(new Rect(20, footerY, width - 20, 22), keys, _hudStyle);
                }
                // Notices remain visible when the user hides the HUD.
                if (Time.realtimeSinceStartup < _noticeUntil && (_shiftNotifications == null || _shiftNotifications.GetValue()))
                {
                    float width = Mathf.Min(600f, Screen.width - 20f);
                    float left = (Screen.width - width) * 0.5f;
                    GUI.backgroundColor = new Color(0.12f, 0.12f, 0.04f, 0.95f);
                    GUI.Box(new Rect(left, 20, width, 40), "");
                    GUI.Label(new Rect(left + 12, 28, width - 24, 26), WorkerText.Notice(ru, _notice), _titleStyle);
                }
            }
            finally
            {
                GUI.color = previous;
                GUI.backgroundColor = background;
            }
        }
        private static string KeyHint(SettingsKeybind key, string action)
        {
            return key == null || key.GetKeyValue == KeyCode.None ? action : key.GetKeybindValue + " " + action;
        }
    }
}

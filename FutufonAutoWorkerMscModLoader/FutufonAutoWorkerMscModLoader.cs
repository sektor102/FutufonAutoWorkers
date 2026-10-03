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
        public override string Version => "1.0.4";
        public override string Description => "One F8 cycle assembles 44 complete packages, fills a shipping box and delivers it to a player pallet. AI-assisted code by Codex.";
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
        private bool _loaded;
        private bool _recording;
        private float _nextSample;
        private DemoStepper _worker;
        private FactoryDemo _demo;
        private FactoryWorkMonitor _workMonitor;
        private string _status = "READY: look at a clear factory table, then F8";
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
            _toggleKey = Keybind.Add("aw_toggle", "Start / stop one shipping cycle (44 packages)", KeyCode.F8);
            _snapshotKey = Keybind.Add("aw_snapshot", "Dump factory states and variables", KeyCode.F9);
            _recordKey = Keybind.Add("aw_record", "Toggle factory recording", KeyCode.F7);
            _workSpeed = Settings.AddSlider("aw_speed", "Work speed (%) - 100 is default, lower is slower", 10, 100, 100);
        }

        private void Mod_OnLoad()
        {
            ResetDiagnostics();
            _loaded = true;
            _workMonitor = new FactoryWorkMonitor(Log);
            OpenSessionLog();
            _recording = true;
            Log("Loaded v" + Version + " for My Winter Car.");
            Log("DLL: " + GetType().Assembly.Location);
            Log("F8 start/stop one 44-package shipping cycle; F7 recording; F9 full snapshot. Recording ON.");
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
            StopWorker();
            _workMonitor = null;
            _status = "READY: look at a clear factory table, then F8";
        }

        private void Mod_Update()
        {
            if (!_loaded) return;
            if (_workMonitor != null) _workMonitor.Poll();

            if (_toggleKey != null && _toggleKey.GetKeybindDown())
            {
                if (_worker != null)
                {
                    StopWorker();
                    _status = "STOPPED: packed progress retained; clear unfinished parts before F8";
                    Log("CYCLE stopped by F8. Packed contents retained; no further parts will be issued.");
                }
                else StartCycle();
            }

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
                    if (_demo.PlayerLeft())
                    {
                        StopWorker();
                        _status = "STOPPED: player left the workstation";
                        Log("CYCLE stopped: player left the workstation.");
                    }
                    else if (!_worker.Tick()) StopWorker();
                }
                catch (Exception exception)
                {
                    StopWorker();
                    _status = "ERROR: " + exception.Message;
                    Log("CYCLE ERROR: " + exception);
                    SampleFactory(true);
                }
            }

            if (_recording && Time.realtimeSinceStartup >= _nextSample)
            {
                _nextSample = Time.realtimeSinceStartup + 0.5f;
                SampleFactory(false);
            }
        }

        private void SampleFactory(bool includeGraph)
        {
            var fsms = UnityEngine.Object.FindObjectsOfType(typeof(PlayMakerFSM));
            int count = 0;
            var liveIds = new HashSet<int>();

            foreach (var item in fsms)
            {
                var fsm = item as PlayMakerFSM;
                if (fsm == null || fsm.Fsm == null || !IsFactoryObject(fsm.transform)) continue;

                count++;
                int id = fsm.GetInstanceID();
                liveIds.Add(id);
                try
                {
                    string snapshot = DescribeFsm(fsm);
                    string previous;
                    if (includeGraph || !_lastSnapshots.TryGetValue(id, out previous) || previous != snapshot)
                    {
                        Log(snapshot);
                        _lastSnapshots[id] = snapshot;
                    }

                    if (includeGraph)
                        DumpGraph(fsm);
                }
                catch (Exception exception)
                {
                    Log("Cannot inspect " + fsm.gameObject.name + ": " + exception.Message);
                }
            }

            var removedIds = new List<int>();
            foreach (var id in _lastSnapshots.Keys)
                if (!liveIds.Contains(id)) removedIds.Add(id);
            foreach (var id in removedIds)
            {
                Log("FSM disappeared or became inactive: " + _lastSnapshots[id]);
                _lastSnapshots.Remove(id);
            }

            if (includeGraph)
                Log("Factory FSMs found: " + count + ". Visit the factory if none are loaded.");
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

        private void DumpGraph(PlayMakerFSM fsm)
        {
            foreach (var state in fsm.FsmStates)
            {
                var text = new StringBuilder("[AutoWorker]   State ");
                text.Append(state.Name);
                foreach (var action in state.Actions)
                    if (action != null) text.Append(" | action:").Append(action.GetType().Name);
                foreach (var transition in state.Transitions)
                    text.Append(" | ").Append(transition.EventName).Append(" -> ").Append(transition.ToState);
                Log(text.ToString().Replace("[AutoWorker] ", ""));
                foreach (var action in state.Actions)
                {
                    if (action == null) continue;
                    var parameters = new StringBuilder("    " + action.GetType().Name);
                    foreach (var field in action.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                        parameters.Append(" | ").Append(field.Name).Append('=').Append(DescribeValue(field.GetValue(action)));
                    Log(parameters.ToString());
                }
            }
            foreach (var transition in fsm.Fsm.GlobalTransitions)
                Log("  Global " + transition.EventName + " -> " + transition.ToState);
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
                _demo = new FactoryDemo(Log, value => _status = value, () => 0.15f * 100f / WorkSpeedPercent());
                _demo.Prepare();
                _recording = true;
                _lastSnapshots.Clear();
                _nextSample = 0f;
                _worker = new DemoStepper(_demo.Run());
                _status = "STARTING: one shipping box, 44 complete packages";
                Log("CYCLE started by F8. One shipping box only. Recording ON. Speed=" + WorkSpeedPercent() + "%.");
            }
            catch (Exception exception)
            {
                StopWorker();
                _status = exception.Message;
                Log("CYCLE cannot start: " + exception.Message);
            }
        }

        private void StopWorker()
        {
            if (_worker != null) _worker.Dispose();
            _worker = null;
            _demo = null;
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
            ModConsole.Print("[AutoWorker] " + message);
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
            GUI.Label(new Rect(10, 10, 1000, 24), "AutoWorker " + Version + " | F8 one box (44) / stop | F7 record | F9 snapshot");
            GUI.Label(new Rect(10, 34, 1200, 24), _status);
            GUI.Label(new Rect(10, 58, 1200, 24), "Speed: " + WorkSpeedPercent() + "% | Recording: " +
                (_recording ? "ON" : "OFF") + " | Logs: Mods/AutoWorkerLogs/");
            if (_workMonitor != null)
            {
                Color previous = GUI.color;
                if (_workMonitor.State == WorkCheckState.Slacking) GUI.color = Color.red;
                else if (_workMonitor.State == WorkCheckState.Working) GUI.color = Color.green;
                GUI.Label(new Rect(10, 82, 1200, 24), _workMonitor.Status);
                GUI.color = previous;
            }
        }
    }
}

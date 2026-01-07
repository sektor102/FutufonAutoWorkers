using BepInEx;
using BepInEx.Configuration;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using HutongGames.PlayMaker;

namespace FutufonAutoWorker
{
    [BepInPlugin("com.futufon.autoworker", "Futufon AutoWorker (autofocus10)", "0.4.6")]
    public class Plugin : BaseUnityPlugin
    {
        private bool _busy;
        private bool _running;
        private Coroutine _autoCo;

        private ConfigEntry<KeyCode> KeyToggleAuto;
        private ConfigEntry<KeyCode> KeyProceedSpawner;
        private ConfigEntry<KeyCode> KeyUsePackage;
        private ConfigEntry<KeyCode> KeyDumpLookedFsms;

        private ConfigEntry<float> AimRayDist;
        private ConfigEntry<float> UseRayDist;
        private ConfigEntry<float> PostEventWaitSec;

        private ConfigEntry<float> SpawnWaitSec;
        private ConfigEntry<float> StepWaitSec;
        private ConfigEntry<int> TargetCount;
        private ConfigEntry<float> FindRadius;
        private ConfigEntry<float> TeleportYOffset;
        private ConfigEntry<bool> DebugLog;

        // Cached refs (best-effort)
        private GameObject _pickTrays, _pickChargers, _pickManuals, _pickSheets;
        private GameObject _palletPlayer; // PalletPackagesPlayer
        private Transform _palletTrigger; // TriggerBox

        private void Awake()
        {
            KeyToggleAuto = Config.Bind("Keys", "ToggleAuto", KeyCode.F8, "Toggle full factory automation");
            KeyProceedSpawner = Config.Bind("Keys", "ProceedSpawner", KeyCode.F5, "PROCEED looked spawner (debug)");
            KeyUsePackage = Config.Bind("Keys", "UsePackage", KeyCode.F7, "USE/FOLD looked package (debug)");
            KeyDumpLookedFsms = Config.Bind("Keys", "DumpLookedFsms", KeyCode.F9, "Dump FSMs for looked object (debug)");

            AimRayDist = Config.Bind("Debug", "AimRayDist", 3.0f, "Raycast distance to pick looked object");
            UseRayDist = Config.Bind("Debug", "UseRayDist", 3.0f, "Raycast distance required to interact");
            PostEventWaitSec = Config.Bind("Timing", "PostEventWaitSec", 0.25f, "Wait after sending event");

            SpawnWaitSec = Config.Bind("Timing", "SpawnWaitSec", 0.35f, "Wait after spawning an item from spawner");
            StepWaitSec = Config.Bind("Timing", "StepWaitSec", 0.25f, "Wait between automation cycles/steps");
            FindRadius = Config.Bind("Auto", "FindRadius", 3.0f, "Radius to find spawned parts around player");

            TargetCount = Config.Bind("General", "TargetCount", 0, "How many packages to produce before auto-stopping (0 = infinite).");
            TeleportYOffset = Config.Bind("Auto", "TeleportYOffset", 0.02f, "Small Y offset when teleporting to triggers");
            DebugLog = Config.Bind("Debug", "DebugLog", true, "Verbose logs");

            Log("Loaded autofocus10 v0.4.6 with reflection for SetState.");
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyToggleAuto.Value))
            {
                ToggleAuto();
                return;
            }

            if (_busy) return;

            if (Input.GetKeyDown(KeyProceedSpawner.Value))
                StartCoroutine(RunGuarded(ProceedSpawnerLooked(), "ProceedSpawnerLooked"));

            if (Input.GetKeyDown(KeyUsePackage.Value))
                StartCoroutine(RunGuarded(UsePackageLooked(), "UsePackageLooked"));

            if (Input.GetKeyDown(KeyDumpLookedFsms.Value))
                StartCoroutine(RunGuarded(DumpLookedFsms(), "DumpLookedFsms"));
        }

        private void ToggleAuto()
        {
            _running = !_running;

            if (_running)
            {
                CacheFactoryRefs();
                Log("AutoWork: ON");

                if (_autoCo != null) StopCoroutine(_autoCo);
                _autoCo = StartCoroutine(AutoWork());
            }
            else
            {
                Log("AutoWork: OFF");

                if (_autoCo != null)
                {
                    StopCoroutine(_autoCo);
                    _autoCo = null;
                }

                _busy = false;
            }
        }

        private IEnumerator RunGuarded(IEnumerator inner, string label)
        {
            if (inner == null) yield break;

            object current = null;
            while (true)
            {
                bool moved = false;
                try
                {
                    moved = inner.MoveNext();
                    if (moved) current = inner.Current;
                }
                catch (Exception e)
                {
                    Logger.LogError(string.Format("[{0}] {1}: exception: {2}", "Futufon AutoWorker", label, e));
                    break;
                }

                if (!moved) break;
                yield return current;
            }
        }

        // =========================
        // AUTO WORK (FACTORY)
        // =========================

        private IEnumerator AutoWork()
        {
            Log("AutoWork: ON");
            _busy = true;

            try
            {
                int done = 0;

                while (_running && (TargetCount.Value <= 0 || done < TargetCount.Value))
                {
                    CacheFactoryRefs();

                    // Step 1: Spawn boxes from warehouse crates (Pick*)
                    GameObject boxTrays = null;
                    GameObject boxSheets = null;
                    GameObject boxChargers = null;
                    GameObject boxManuals = null;

                    yield return SpawnBoxFromCrate("plastic trays(Clone)", _pickTrays, o => boxTrays = o);
                    yield return SpawnBoxFromCrate("packaging sheets(Clone)", _pickSheets, o => boxSheets = o);
                    yield return SpawnBoxFromCrate("chargers box(Clone)", _pickChargers, o => boxChargers = o);
                    yield return SpawnBoxFromCrate("manuals box(Clone)", _pickManuals, o => boxManuals = o);

                    Log(string.Format("Auto: spawned boxes trays={0}, sheets={1}, chargers={2}, manuals={3}",
                        Short(boxTrays), Short(boxSheets), Short(boxChargers), Short(boxManuals)));

                    if (boxTrays == null || boxSheets == null || boxChargers == null || boxManuals == null)
                    {
                        Log("Auto: missing boxes after spawn, retrying");
                        yield return new WaitForSeconds(SpawnWaitSec.Value);
                        continue;
                    }

                    // Step 2: Move boxes near player
                    yield return MoveNearPlayer(boxTrays);
                    yield return MoveNearPlayer(boxSheets);
                    yield return MoveNearPlayer(boxChargers);
                    yield return MoveNearPlayer(boxManuals);

                    // Step 3: Open boxes that need opening (chargers and manuals)
                    yield return OpenBoxIfNeeded(boxChargers, "chargers box(Clone)");
                    yield return OpenBoxIfNeeded(boxManuals, "manuals box(Clone)");

                    // Step 4: Spawn individual items from boxes
                    GameObject tray = null;
                    GameObject pack = null;
                    GameObject charger = null;
                    GameObject manual = null;

                    yield return SpawnItemFromBox("plastic tray(Clone)", boxTrays, o => tray = o);
                    yield return SpawnItemFromBox("package(Clone)", boxSheets, o => pack = o);
                    yield return SpawnItemFromBox("charger(Clone)", boxChargers, o => charger = o);
                    yield return SpawnItemFromBox("manual(Clone)", boxManuals, o => manual = o);

                    Log(string.Format("Auto: spawned items tray={0}, pack={1}, charger={2}, manual={3}",
                        Short(tray), Short(pack), Short(charger), Short(manual)));

                    if (tray == null || pack == null || charger == null || manual == null)
                    {
                        Log("Auto: missing items after spawn, retrying");
                        yield return new WaitForSeconds(SpawnWaitSec.Value);
                        continue;
                    }

                    // Step 5: Assemble
                    // Prepare package to stage 5 by sending USE 5 times
                    yield return PreparePackageToStage(pack, 5);

                    // Assemble tray: insert charger and manual
                    yield return AssembleIntoTray(tray, charger, "plastic_tray/TriggerCharger", "charger");
                    yield return AssembleIntoTray(tray, manual, "plastic_tray/TriggerManual", "manual");

                    // Check tray variables
                    if (!CheckTrayAssembled(tray))
                    {
                        Log("Auto: tray not assembled properly, retrying");
                        continue;
                    }

                    // Insert assembled tray into package (stage 5)
                    yield return AssembleTrayIntoPackage(pack, tray);

                    // Send USE once more to finalize (to stage 4?)
                    yield return SendEventThenWait(pack, "Use", "USE", PostEventWaitSec.Value);

                    // Check final package variables
                    if (!CheckPackageFinal(pack))
                    {
                        Log("Auto: final package not ready, retrying");
                        continue;
                    }

                    // Deliver to pallet
                    yield return DeliverToPallet(pack);

                    done++;
                    Log(string.Format("Auto: done {0}/{1}", done, TargetCount.Value > 0 ? TargetCount.Value.ToString() : "∞"));

                    yield return new WaitForSeconds(StepWaitSec.Value);
                }
            }
            finally
            {
                _busy = false;
                Log("AutoWork: OFF");
            }
        }

        // New: Spawn box from crate (Step 1)
        private IEnumerator SpawnBoxFromCrate(string boxName, GameObject crate, Action<GameObject> setFound, int tries = 6)
        {
            if (crate == null)
            {
                Log($"SpawnBoxFromCrate: {boxName} crate=null");
                setFound(null);
                yield break;
            }

            GameObject found = FindSpawnedNear(boxName, crate);
            if (found != null)
            {
                setFound(found);
                yield break;
            }

            PlayMakerFSM fsm = GetFsm(crate, "Use");
            if (fsm == null)
            {
                Log($"SpawnBoxFromCrate: {boxName} no Use FSM");
                setFound(null);
                yield break;
            }

            for (int attempt = 1; attempt <= tries; attempt++)
            {
                string state = fsm.ActiveStateName;
                if (!string.Equals(state, "Wait button", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"SpawnBoxFromCrate: {boxName} not in Wait button (state={state}), forcing state via reflection");
                    var setStateMethod = typeof(Fsm).GetMethod("SetState", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (setStateMethod != null)
                    {
                        setStateMethod.Invoke(fsm.Fsm, new object[] { "Wait button" });
                        yield return new WaitForEndOfFrame();
                        yield return new WaitForEndOfFrame();
                    }
                    else
                    {
                        Log($"SpawnBoxFromCrate: {boxName} failed to find SetState method");
                        continue;
                    }

                    state = fsm.ActiveStateName;
                    if (!string.Equals(state, "Wait button", StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"SpawnBoxFromCrate: {boxName} failed to force Wait button (now {state}), skipping attempt {attempt}");
                        continue;
                    }
                }

                Log($"SpawnBoxFromCrate: {boxName} state OK, sending PROCEED");
                Proceed(crate);
                yield return new WaitForSeconds(SpawnWaitSec.Value);

                found = FindSpawnedNear(boxName, crate);
                if (found != null)
                {
                    setFound(found);
                    yield break;
                }

                Log($"SpawnBoxFromCrate: {boxName} attempt {attempt}/{tries} not found");
            }

            setFound(null);
        }

        // New: Move object near player (Step 2)
        private IEnumerator MoveNearPlayer(GameObject obj)
        {
            if (obj == null) yield break;

            Vector3 playerPos = PlayerPos();
            Vector3 nearPos = playerPos + new Vector3(0f, 0.5f, 1f); // Slightly in front and above
            yield return GlideTo(obj, nearPos, obj.transform.rotation);
        }

        // New: Open box if needed (Step 3)
        private IEnumerator OpenBoxIfNeeded(GameObject box, string boxName)
        {
            if (box == null) yield break;

            bool isOpen;
            if (TryGetBoolVar(box, "open", out isOpen) && !isOpen)
            {
                PlayMakerFSM fsm = GetFsm(box, "Use");
                if (fsm == null)
                {
                    Log($"OpenBoxIfNeeded: {boxName} no Use FSM");
                    yield break;
                }

                string state = fsm.ActiveStateName;
                if (!string.Equals(state, "Wait button", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"OpenBoxIfNeeded: {boxName} not in Wait button (state={state}), forcing state via reflection");
                    var setStateMethod = typeof(Fsm).GetMethod("SetState", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (setStateMethod != null)
                    {
                        setStateMethod.Invoke(fsm.Fsm, new object[] { "Wait button" });
                        yield return new WaitForEndOfFrame();
                        yield return new WaitForEndOfFrame();
                    }
                    else
                    {
                        Log($"OpenBoxIfNeeded: {boxName} failed to find SetState method");
                        yield break;
                    }

                    state = fsm.ActiveStateName;
                    if (!string.Equals(state, "Wait button", StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"OpenBoxIfNeeded: {boxName} failed to force Wait button (now {state})");
                        yield break;
                    }
                }

                Log($"OpenBoxIfNeeded: {boxName} state OK, sending PROCEED");
                Proceed(box);
                yield return WaitForBool(box, "open", true, SpawnWaitSec.Value * 2);
            }
        }

        // New: Spawn item from box (Step 4)
        private IEnumerator SpawnItemFromBox(string itemName, GameObject box, Action<GameObject> setFound, int tries = 6)
        {
            if (box == null)
            {
                Log($"SpawnItemFromBox: {itemName} box=null");
                setFound(null);
                yield break;
            }

            // For openable boxes, check open=true
            bool isOpen;
            if (TryGetBoolVar(box, "open", out isOpen) && !isOpen)
            {
                Log($"SpawnItemFromBox: {itemName} box not open");
                setFound(null);
                yield break;
            }

            GameObject found = FindSpawnedNear(itemName, box);
            if (found != null)
            {
                setFound(found);
                yield break;
            }

            PlayMakerFSM fsm = GetFsm(box, "Use");
            if (fsm == null)
            {
                Log($"SpawnItemFromBox: {itemName} no Use FSM");
                setFound(null);
                yield break;
            }

            for (int attempt = 1; attempt <= tries; attempt++)
            {
                string state = fsm.ActiveStateName;
                if (!string.Equals(state, "Wait button", StringComparison.OrdinalIgnoreCase))
                {
                    Log($"SpawnItemFromBox: {itemName} not in Wait button (state={state}), forcing state via reflection");
                    var setStateMethod = typeof(Fsm).GetMethod("SetState", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (setStateMethod != null)
                    {
                        setStateMethod.Invoke(fsm.Fsm, new object[] { "Wait button" });
                        yield return new WaitForEndOfFrame();
                        yield return new WaitForEndOfFrame();
                    }
                    else
                    {
                        Log($"SpawnItemFromBox: {itemName} failed to find SetState method");
                        continue;
                    }

                    state = fsm.ActiveStateName;
                    if (!string.Equals(state, "Wait button", StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"SpawnItemFromBox: {itemName} failed to force Wait button (now {state}), skipping attempt {attempt}");
                        continue;
                    }
                }

                Log($"SpawnItemFromBox: {itemName} state OK, sending PROCEED");
                Proceed(box);
                yield return new WaitForSeconds(SpawnWaitSec.Value);

                found = FindSpawnedNear(itemName, box);
                if (found != null)
                {
                    setFound(found);
                    yield break;
                }

                Log($"SpawnItemFromBox: {itemName} attempt {attempt}/{tries} not found");
            }

            setFound(null);
        }

        // New: Prepare package to stage 5 (send USE 5 times)
        private IEnumerator PreparePackageToStage(GameObject pack, int targetStage)
        {
            if (pack == null) yield break;

            int currentStage = GetStage(pack);
            if (currentStage >= targetStage) yield break;

            for (int i = currentStage; i < targetStage; i++)
            {
                yield return SendEventThenWait(pack, "Use", "USE", PostEventWaitSec.Value);
            }

            if (GetStage(pack) != targetStage)
            {
                Log($"PreparePackageToStage: failed to reach stage {targetStage}, current={GetStage(pack)}");
            }
        }

        // New: Check tray assembled (Charger=ON, Manual=ON)
        private bool CheckTrayAssembled(GameObject tray)
        {
            if (tray == null) return false;

            bool chargerOn = false;
            bool manualOn = false;

            TryGetBoolVar(tray, "Charger", out chargerOn); // Note: you had "Chager" as typo, assuming "Charger"
            TryGetBoolVar(tray, "Manual", out manualOn);

            return chargerOn && manualOn;
        }

        // New: Check final package (Stage=4, Charger=ON, Manual=ON, Mould=ON)
        private bool CheckPackageFinal(GameObject pack)
        {
            if (pack == null) return false;

            int stage = GetStage(pack);
            bool chargerOn = false;
            bool manualOn = false;
            bool mouldOn = false;

            TryGetBoolVar(pack, "Charger", out chargerOn); // Assuming typo fix
            TryGetBoolVar(pack, "Manual", out manualOn);
            TryGetBoolVar(pack, "Mould", out mouldOn);

            return stage == 4 && chargerOn && manualOn && mouldOn;
        }

        private void CacheFactoryRefs()
        {
            Vector3 near = PlayerPos();
            const float rPick = 150f;
            const float rPallet = 250f;

            _pickTrays = FindNearestActiveInScene("PickTrays", near, rPick, go => go.GetComponent<PlayMakerFSM>() != null || GetFsm(go, "Use") != null) ?? FindGODeep("PickTrays");
            _pickSheets = FindNearestActiveInScene("PickSheets", near, rPick, go => go.GetComponent<PlayMakerFSM>() != null || GetFsm(go, "Use") != null) ?? FindGODeep("PickSheets");
            _pickChargers = FindNearestActiveInScene("PickChargers", near, rPick, go => go.GetComponent<PlayMakerFSM>() != null || GetFsm(go, "Use") != null) ?? FindGODeep("PickChargers");
            _pickManuals = FindNearestActiveInScene("PickManuals", near, rPick, go => go.GetComponent<PlayMakerFSM>() != null || GetFsm(go, "Use") != null) ?? FindGODeep("PickManuals");

            _palletPlayer = FindNearestActiveInScene("PalletPackagesPlayer", near, rPallet, go => go.GetComponent<PlayMakerFSM>() != null) ?? FindGODeep("PalletPackagesPlayer");
            _palletTrigger = null;
            if (_palletPlayer != null)
            {
                Transform t = _palletPlayer.transform.Find("TriggerBox");
                if (t == null) t = FindChildDeep(_palletPlayer.transform, "TriggerBox");
                if (t == null) t = FindChildDeep(_palletPlayer.transform, "TriggerPallet");
                _palletTrigger = t;
            }

            if (DebugLog.Value)
            {
                Log(string.Format(
                    "Cache: PickTrays={0} PickSheets={1} PickChargers={2} PickManuals={3} Pallet={4}/{5}",
                    _pickTrays ? "ok" : "null",
                    _pickSheets ? "ok" : "null",
                    _pickChargers ? "ok" : "null",
                    _pickManuals ? "ok" : "null",
                    _palletPlayer ? "ok" : "null",
                    _palletTrigger ? "ok" : "null"));
            }
        }

        private bool IsPackage(GameObject go)
        {
            return go != null && go.name == "package(Clone)";
        }

        private IEnumerator SendEventThenWait(GameObject go, string fsmName, string ev, float waitSec)
        {
            SendEventToFsm(go, fsmName, ev);
            yield return new WaitForSeconds(Mathf.Max(0.05f, waitSec));
        }

        private IEnumerator AssembleIntoTray(GameObject tray, GameObject part, string triggerPath, string partName)
        {
            if (tray == null || part == null) { Log("Auto: missing " + partName + " or tray"); yield break; }

            string triggerName = triggerPath;
            int slash = triggerName.LastIndexOf('/');
            if (slash >= 0) triggerName = triggerName.Substring(slash + 1);

            Transform trg = tray.transform.Find(triggerPath);
            if (trg == null) trg = FindChildDeep(tray.transform, triggerName);
            if (trg == null) { Log("Auto: tray trigger not found: " + triggerName); yield break; }

            Vector3 pos = trg.position + new Vector3(0f, TeleportYOffset.Value, 0f);
            yield return GlideTo(part, pos, trg.rotation, 0.20f, 10);
            yield return new WaitForSeconds(PostEventWaitSec.Value);

            TrySendEventToAnyFsmWithEvent(part, "ASSEMBLE", "Contents", "Use", "Assembly");
            TrySendEventToAnyFsmWithEvent(part, "PROCEED", "Contents", "Use", "Assembly");
            TrySendEventToAnyFsmWithEvent(part, "FINISHED", "Contents", "Use", "Assembly");

            TrySendEventToAnyFsmWithEvent(trg.gameObject, "TRIGGER ENTER", "Assembly");
            TrySendEventToAnyFsmWithEvent(trg.gameObject, "LOOP", "Assembly");
            TrySendEventToAnyFsmWithEvent(trg.gameObject, "PROCEED", "Contents", "Use", "Assembly");
            TrySendEventToAnyFsmWithEvent(trg.gameObject, "ASSEMBLE", "Contents", "Use", "Assembly");
            TrySendEventToAnyFsmWithEvent(trg.gameObject, "FINISHED", "Contents", "Use", "Assembly");

            TrySendEventToAnyFsmWithEvent(tray, "PROCEED", "Contents", "Use", "Assembly");
            TrySendEventToAnyFsmWithEvent(tray, "ASSEMBLE", "Contents", "Use", "Assembly");
            TrySendEventToAnyFsmWithEvent(tray, "FINISHED", "Contents", "Use", "Assembly");

            yield return new WaitForSeconds(PostEventWaitSec.Value);
        }

        private IEnumerator AssembleTrayIntoPackage(GameObject pack, GameObject tray)
        {
            if (pack == null || tray == null) yield break;

            Transform trg = pack.transform.Find("package_stage6/TriggerTray");
            if (trg == null) trg = FindChildDeep(pack.transform, "TriggerTray");
            if (trg == null)
            {
                Log("Auto: package trigger not found: TriggerTray");
                yield break;
            }

            Vector3 pos = trg.position + new Vector3(0f, TeleportYOffset.Value, 0f);
            yield return GlideTo(tray, pos, trg.rotation, 0.25f, 12);
            yield return new WaitForSeconds(PostEventWaitSec.Value);

            TrySendEventToAnyFsmWithEvent(trg.gameObject, "TRIGGER ENTER", "Contents", "Use", "Assembly");
            TrySendEventToAnyFsmWithEvent(trg.gameObject, "LOOP", "Contents", "Use", "Assembly");
            TrySendEventToAnyFsmWithEvent(trg.gameObject, "PROCEED", "Contents", "Use", "Assembly");
            TrySendEventToAnyFsmWithEvent(trg.gameObject, "ASSEMBLE", "Contents", "Use", "Assembly");
            TrySendEventToAnyFsmWithEvent(trg.gameObject, "FINISHED", "Contents", "Use", "Assembly");

            TrySendEventToAnyFsmWithEvent(tray, "PROCEED", "Contents", "Use", "Assembly");
            TrySendEventToAnyFsmWithEvent(tray, "ASSEMBLE", "Contents", "Use", "Assembly");
            TrySendEventToAnyFsmWithEvent(tray, "FINISHED", "Contents", "Use", "Assembly");

            TrySendEventToAnyFsmWithEvent(pack, "PROCEED", "Contents", "Use", "Assembly");
            TrySendEventToAnyFsmWithEvent(pack, "ASSEMBLE", "Contents", "Use", "Assembly");
            TrySendEventToAnyFsmWithEvent(pack, "FINISHED", "Contents", "Use", "Assembly");

            yield return new WaitForSeconds(PostEventWaitSec.Value);
        }

        private IEnumerator FoldPackage(GameObject pack)
        {
            if (pack == null) yield break;

            int stageBefore = GetStage(pack);

            SendEventToFsm(pack, "Use", "USE");
            yield return new WaitForSeconds(PostEventWaitSec.Value);

            for (int i = 1; i <= 6; i++)
            {
                SendEventToFsm(pack, "Use", "FOLD" + i);
                yield return new WaitForSeconds(0.08f);
            }

            SendEventToFsm(pack, "Use", "FINISHED");
            yield return new WaitForSeconds(PostEventWaitSec.Value);

            int stageAfter = GetStage(pack);
            Log(string.Format("Auto: fold stage {0} -> {1}", stageBefore, stageAfter));

            if (stageAfter <= stageBefore)
            {
                for (int k = 0; k < 10; k++)
                {
                    SendEventToFsm(pack, "Use", "USE");
                    yield return new WaitForSeconds(0.06f);
                }
                Log("Auto: fold fallback USE spam done, stage=" + GetStage(pack));
            }
        }

        private IEnumerator DeliverToPallet(GameObject pack)
        {
            if (pack == null) yield break;

            if (_palletTrigger == null)
            {
                CacheFactoryRefs();
            }

            if (_palletTrigger == null)
            {
                Log("Auto: pallet TriggerBox not found. Skipping deliver.");
                yield break;
            }

            Vector3 pos = _palletTrigger.position;
            pos.y += TeleportYOffset.Value;
            yield return GlideTo(pack, pos, _palletTrigger.rotation, 0.35f, 14);

            SendEventToFsm(_palletTrigger.gameObject, "Assembly", "TRIGGER ENTER");
            yield return new WaitForSeconds(0.05f);
            SendEventToFsm(_palletTrigger.gameObject, "Assembly", "PROCEED");
            yield return new WaitForSeconds(0.05f);
            SendEventToFsm(_palletTrigger.gameObject, "Assembly", "FINISHED");
            yield return new WaitForSeconds(PostEventWaitSec.Value);
        }

        // =========================
        // DEBUG HOTKEYS (F5/F7/F9)
        // =========================

        private IEnumerator ProceedSpawnerLooked()
        {
            _busy = true;
            try
            {
                var looked = PickLookTarget(AimRayDist.Value);
                if (looked == null) { Log("Proceed: no looked object"); yield break; }

                var go = ResolveSpawnerAlias(looked);
                if (go == null) { Log("Proceed: resolve spawner failed"); yield break; }

                string ev = "PROCEED";
                if (DebugLog.Value) Log(string.Format("Proceed: target={0} event={1}", go.name, ev));

                SendEventToFsm(go, "Use", ev);
                yield return new WaitForSeconds(PostEventWaitSec.Value);
            }
            finally { _busy = false; }
        }

        private IEnumerator UsePackageLooked()
        {
            _busy = true;
            try
            {
                var looked = PickLookTarget(AimRayDist.Value);
                if (looked == null)
                {
                    Log("UsePackage: no looked object.");
                    yield break;
                }

                if (!IsPackage(looked))
                {
                    var nearestPack = FindNearestByPrefix("package(Clone)", PlayerPos(), FindRadius.Value * 6f);
                    if (nearestPack != null)
                    {
                        Log($"UsePackage: looked {looked.name} -> using nearest {nearestPack.name}");
                        looked = nearestPack;
                    }
                    else
                    {
                        Log($"UsePackage: looked {looked.name} not package");
                        yield break;
                    }
                }

                yield return SendEventThenWait(looked, "Use", "PROCEED", StepWaitSec.Value);
            }
            finally { _busy = false; }
        }

        private IEnumerator DumpLookedFsms()
        {
            _busy = true;
            try
            {
                var looked = PickLookTarget(AimRayDist.Value);
                if (looked == null) { Log("Looked: none"); yield break; }

                var go = ResolveSpawnerAlias(looked) ?? looked;
                Log("Looked: " + looked.name + " -> dump: " + go.name);
                DumpAllFsms(go, 60);
            }
            finally { _busy = false; }
        }

        // =========================
        // CORE HELPERS
        // =========================

        private GameObject PickLookTarget(float dist)
        {
            var cam = Camera.main;
            if (cam == null) return null;

            var ray = new Ray(cam.transform.position, cam.transform.forward);
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit, dist)) return null;
            return hit.collider != null ? hit.collider.gameObject : null;
        }

        private GameObject FindGO(string pathOrName)
        {
            if (string.IsNullOrEmpty(pathOrName)) return null;

            try
            {
                var go = GameObject.Find(pathOrName);
                if (go != null) return go;
            }
            catch { }

            var name = pathOrName;
            var slash = name.LastIndexOf('/');
            if (slash >= 0 && slash + 1 < name.Length) name = name.Substring(slash + 1);
            return FindGODeep(name);
        }

        private Vector3 PlayerPos()
        {
            var cam = Camera.main;
            if (cam != null) return cam.transform.position;

            var fps = FindGO("PLAYER/Pivot/AnimPivot/Camera/FPSCamera");
            if (fps != null) return fps.transform.position;

            var player = FindGO("PLAYER");
            if (player != null) return player.transform.position;

            return transform.position;
        }

        private void SendEventToFsm(GameObject go, string fsmName, string ev)
        {
            TrySendEventToFsm(go, fsmName, ev);
        }

        private bool TrySendEventToFsm(GameObject go, string fsmName, string ev)
        {
            if (go == null) return false;

            var fsms = go.GetComponentsInChildren<PlayMakerFSM>(true);
            if (fsms == null || fsms.Length == 0) return false;

            if (!string.IsNullOrEmpty(fsmName))
            {
                for (int i = 0; i < fsms.Length; i++)
                {
                    var f = fsms[i];
                    if (f == null) continue;
                    if (!string.Equals(f.FsmName, fsmName, StringComparison.OrdinalIgnoreCase)) continue;
                    try { f.SendEvent(ev); } catch (Exception e) { Logger.LogError(e); }
                    return true;
                }
            }

            try { fsms[0].SendEvent(ev); } catch (Exception e) { Logger.LogError(e); }
            return true;
        }

        private bool TryGetBoolVar(GameObject go, string varName, out bool value)
        {
            value = false;
            if (go == null) return false;

            foreach (var fsm in go.GetComponents<PlayMakerFSM>())
            {
                if (fsm == null || fsm.FsmVariables == null) continue;
                var vb = fsm.FsmVariables.FindFsmBool(varName);
                if (vb != null)
                {
                    value = vb.Value;
                    return true;
                }
            }
            return false;
        }

        private IEnumerator WaitForBool(GameObject go, string varName, bool expected, float timeoutSec)
        {
            var start = Time.time;
            while (Time.time - start < timeoutSec)
            {
                if (TryGetBoolVar(go, varName, out var v) && v == expected) yield break;
                yield return null;
            }
        }

        private bool FsmHasEvent(PlayMakerFSM f, string ev)
        {
            try
            {
                if (f == null || f.Fsm == null || f.Fsm.Events == null) return false;
                for (int i = 0; i < f.Fsm.Events.Length; i++)
                {
                    var e = f.Fsm.Events[i];
                    if (e == null) continue;
                    if (string.Equals(e.Name, ev, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            catch { }
            return false;
        }

        private bool TrySendEventToAnyFsmWithEvent(GameObject go, string ev, params string[] preferredFsmNames)
        {
            if (go == null) return false;

            var fsms = go.GetComponentsInChildren<PlayMakerFSM>(true);
            if (fsms == null || fsms.Length == 0) return false;

            if (preferredFsmNames != null)
            {
                for (int p = 0; p < preferredFsmNames.Length; p++)
                {
                    var name = preferredFsmNames[p];
                    if (string.IsNullOrEmpty(name)) continue;

                    for (int i = 0; i < fsms.Length; i++)
                    {
                        var f = fsms[i];
                        if (f == null) continue;
                        if (!string.Equals(f.FsmName, name, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!FsmHasEvent(f, ev)) continue;
                        try { f.SendEvent(ev); } catch (Exception e) { Logger.LogError(e); }
                        return true;
                    }
                }
            }

            for (int i = 0; i < fsms.Length; i++)
            {
                var f = fsms[i];
                if (f == null) continue;
                if (!FsmHasEvent(f, ev)) continue;
                try { f.SendEvent(ev); } catch (Exception e) { Logger.LogError(e); }
                return true;
            }

            return false;
        }

        private int GetStage(GameObject pack)
        {
            try
            {
                var fsm = GetFsm(pack, "Use");
                if (fsm == null) return -1;

                var vi = fsm.FsmVariables.FindFsmInt("Stage");
                if (vi != null) return vi.Value;

                for (int i = 0; i < fsm.FsmVariables.IntVariables.Length; i++)
                {
                    var v = fsm.FsmVariables.IntVariables[i];
                    if (v == null) continue;
                    if (string.Equals(v.Name, "stage", StringComparison.OrdinalIgnoreCase)) return v.Value;
                }
            }
            catch { }
            return -1;
        }

        private string GetFsmActive(GameObject go, string fsmName)
        {
            var fsm = GetFsm(go, fsmName);
            if (fsm == null) return "";
            return fsm.ActiveStateName ?? "";
        }

        private PlayMakerFSM GetFsm(GameObject go, string fsmName)
        {
            if (go == null) return null;
            var fsms = go.GetComponentsInChildren<PlayMakerFSM>(true);
            if (fsms == null) return null;

            for (int i = 0; i < fsms.Length; i++)
            {
                var f = fsms[i];
                if (f == null) continue;
                if (string.Equals(f.FsmName, fsmName, StringComparison.OrdinalIgnoreCase)) return f;
            }
            return null;
        }

        private void DumpAllFsms(GameObject go, int max)
        {
            if (go == null) return;

            var fsms = go.GetComponentsInChildren<PlayMakerFSM>(true);
            int n = 0;
            for (int i = 0; i < fsms.Length; i++)
            {
                var f = fsms[i];
                if (f == null) continue;

                Log(string.Format("  FSM: {0} active={1} path={2}",
                    f.FsmName,
                    f.ActiveStateName ?? "",
                    GetPath(f.transform)));

                if (f.Fsm != null && f.Fsm.Events != null)
                {
                    List<string> evs = new List<string>();
                    for (int e = 0; e < f.Fsm.Events.Length; e++)
                        if (f.Fsm.Events[e] != null) evs.Add(f.Fsm.Events[e].Name);
                    if (evs.Count > 0) Log("    events: " + string.Join(", ", evs.ToArray()));
                }

                n++;
                if (n >= max) break;
            }
        }

        private static string GetPath(Transform t)
        {
            if (t == null) return "";
            string p = t.name;
            while (t.parent != null)
            {
                t = t.parent;
                p = t.name + "/" + p;
            }
            return p;
        }

        private GameObject ResolveSpawnerAlias(GameObject looked)
        {
            if (looked == null) return null;

            string n = looked.name ?? "";
            Vector3 near = PlayerPos();
            const float r = 150f;

            if (n.StartsWith("packaging sheets", StringComparison.OrdinalIgnoreCase))
                return FindNearestActiveInScene("PickSheets", near, r, go => go.GetComponent<PlayMakerFSM>() != null) ?? FindGODeep("PickSheets");

            if (n.StartsWith("plastic trays", StringComparison.OrdinalIgnoreCase))
                return FindNearestActiveInScene("PickTrays", near, r, go => go.GetComponent<PlayMakerFSM>() != null) ?? FindGODeep("PickTrays");

            if (n.StartsWith("chargers box", StringComparison.OrdinalIgnoreCase))
                return FindNearestActiveInScene("PickChargers", near, r, go => go.GetComponent<PlayMakerFSM>() != null) ?? FindGODeep("PickChargers");

            if (n.StartsWith("manuals box", StringComparison.OrdinalIgnoreCase))
                return FindNearestActiveInScene("PickManuals", near, r, go => go.GetComponent<PlayMakerFSM>() != null) ?? FindGODeep("PickManuals");

            return looked;
        }

        private GameObject FindGODeep(string name)
        {
            Transform[] all = Resources.FindObjectsOfTypeAll<Transform>();

            GameObject any = null;
            GameObject active = null;

            for (int i = 0; i < all.Length; i++)
            {
                var tr = all[i];
                if (tr == null) continue;

                if (!string.Equals(tr.name, name, StringComparison.OrdinalIgnoreCase)) continue;

                var go = tr.gameObject;
                if (go == null) continue;

                if (any == null) any = go;
                if (go.activeInHierarchy) { active = go; break; }
            }

            return active ?? any;
        }

        private GameObject FindNearestActiveInScene(string name, Vector3 around, float maxDist, Func<GameObject, bool> extra = null)
        {
            GameObject best = null;
            float bestDist = float.MaxValue;

            var all = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null) continue;

                var go = t.gameObject;
                if (go == null) continue;
                if (!string.Equals(go.name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (!go.activeInHierarchy) continue;

                if (extra != null && !extra(go)) continue;

                float d = Vector3.Distance(go.transform.position, around);
                if (d <= maxDist && d < bestDist)
                {
                    best = go;
                    bestDist = d;
                }
            }

            return best;
        }

        private static Transform FindChildDeep(Transform root, string childName)
        {
            if (root == null || string.IsNullOrEmpty(childName)) return null;

            Queue<Transform> q = new Queue<Transform>();
            q.Enqueue(root);

            while (q.Count > 0)
            {
                Transform t = q.Dequeue();
                if (t == null) continue;

                if (string.Equals(t.name, childName, StringComparison.OrdinalIgnoreCase))
                    return t;

                for (int i = 0; i < t.childCount; i++)
                    q.Enqueue(t.GetChild(i));
            }

            return null;
        }

        private GameObject FindNearestByPrefix(string nameOrPrefix, Vector3 around, float radius)
        {
            if (string.IsNullOrEmpty(nameOrPrefix)) return null;

            float best = float.MaxValue;
            GameObject bestGo = null;

            var all = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null) continue;

                var go = t.gameObject;
                if (go == null) continue;
                if (!go.activeInHierarchy) continue;

                if (!go.name.StartsWith(nameOrPrefix, StringComparison.OrdinalIgnoreCase)) continue;

                float d = Vector3.Distance(around, go.transform.position);
                if (d <= radius && d < best)
                {
                    best = d;
                    bestGo = go;
                }
            }

            return bestGo;
        }

        private bool NameMatchesExpectedClone(string expectedName, string candidateName)
        {
            if (string.IsNullOrEmpty(expectedName) || string.IsNullOrEmpty(candidateName)) return false;

            if (string.Equals(candidateName, expectedName, StringComparison.OrdinalIgnoreCase))
                return true;

            string prefix = expectedName.Replace("(Clone)", "").Trim();
            if (prefix.Length == 0) return false;
            if (!candidateName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

            int idx = prefix.Length;
            while (idx < candidateName.Length && candidateName[idx] == ' ') idx++;
            return (idx < candidateName.Length && candidateName[idx] == '(');
        }

        private GameObject FindSpawnedNear(string expectedName, GameObject spawner)
        {
            if (string.IsNullOrEmpty(expectedName)) return null;

            Vector3 playerPos = PlayerPos();
            Vector3 anchorPos = spawner != null ? spawner.transform.position : playerPos;

            float baseR = Mathf.Max(FindRadius.Value, 3.0f);
            float rAnchor = Mathf.Max(baseR * 6.0f, 12.0f);
            float rPlayer = Mathf.Max(baseR * 40.0f, 80.0f);

            GameObject found = FindNearestByName(expectedName, playerPos, rPlayer);
            if (found != null) return found;

            found = FindNearestByExpected(expectedName, playerPos, rPlayer);
            if (found != null) return found;

            found = FindNearestByExpected(expectedName, anchorPos, rAnchor);
            if (found != null) return found;

            Vector3 palletPos = (_palletTrigger != null ? _palletTrigger.position :
                                (_palletPlayer != null ? _palletPlayer.transform.position : Vector3.zero));
            if (palletPos != Vector3.zero)
            {
                found = FindNearestByExpected(expectedName, palletPos, rPlayer);
                if (found != null) return found;
            }

            float anyDist;
            var any = FindNearestByExpectedAnyDist(expectedName, playerPos, out anyDist);
            float hardMax = Mathf.Max(rPlayer, 150.0f);
            if (any != null && anyDist <= hardMax)
            {
                Log($"FindSpawnedNear: using far match {any.name} dist={anyDist:0.0}");
                return any;
            }

            return null;
        }

        private GameObject FindNearestByExpected(string expectedName, Vector3 around, float radius)
        {
            if (string.IsNullOrEmpty(expectedName)) return null;

            float best = float.MaxValue;
            GameObject bestGo = null;

            try
            {
                foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                {
                    if (t == null) continue;
                    var go = t.gameObject;
                    if (go == null) continue;
                    if (!go.activeInHierarchy) continue;

                    if (!NameMatchesExpectedClone(expectedName, go.name)) continue;

                    float d = Vector3.Distance(around, go.transform.position);
                    if (d <= radius && d < best)
                    {
                        best = d;
                        bestGo = go;
                    }
                }
            }
            catch (Exception e)
            {
                Log($"FindNearestByExpected({expectedName}) error: {e.Message}");
            }

            return bestGo;
        }

        private GameObject FindNearestByExpectedAnyDist(string expectedName, Vector3 around, out float bestDist)
        {
            bestDist = float.MaxValue;
            if (string.IsNullOrEmpty(expectedName)) return null;

            GameObject bestGo = null;

            try
            {
                foreach (var t in Resources.FindObjectsOfTypeAll<Transform>())
                {
                    if (t == null) continue;
                    var go = t.gameObject;
                    if (go == null) continue;
                    if (!go.activeInHierarchy) continue;

                    if (!NameMatchesExpectedClone(expectedName, go.name)) continue;

                    float d = Vector3.Distance(around, go.transform.position);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestGo = go;
                    }
                }
            }
            catch (Exception e)
            {
                Log($"FindNearestByExpectedAnyDist({expectedName}) error: {e.Message}");
            }

            return bestGo;
        }

        private GameObject FindNearestByName(string exactName, Vector3 around, float radius)
        {
            if (string.IsNullOrEmpty(exactName)) return null;

            float best = float.MaxValue;
            GameObject bestGo = null;

            var all = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null) continue;

                var go = t.gameObject;
                if (go == null) continue;
                if (!go.activeInHierarchy) continue;

                if (!string.Equals(go.name, exactName, StringComparison.OrdinalIgnoreCase)) continue;

                float d = Vector3.Distance(around, go.transform.position);
                if (d <= radius && d < best)
                {
                    best = d;
                    bestGo = go;
                }
            }

            return bestGo;
        }

        private IEnumerator GlideTo(GameObject go, Vector3 targetPos, Quaternion targetRot, float lift = 0.25f, int steps = 10)
        {
            if (go == null) yield break;

            var rb = go.GetComponent<Rigidbody>() ?? go.GetComponentInChildren<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.detectCollisions = true;
                rb.WakeUp();
            }

            var above = targetPos + Vector3.up * lift;
            MoveRigidBody(go, above, targetRot);

            yield return null;
            yield return new WaitForFixedUpdate();

            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                var p = Vector3.Lerp(above, targetPos, t);

                if (rb != null)
                {
                    rb.MovePosition(p);
                    rb.MoveRotation(targetRot);
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                else
                {
                    go.transform.position = p;
                    go.transform.rotation = targetRot;
                }

                SyncTransformsCompat();

                yield return new WaitForFixedUpdate();
            }

            SyncTransformsCompat();
        }

        private static System.Reflection.MethodInfo _miSyncTransforms;

        private static void SyncTransformsCompat()
        {
            try
            {
                if (_miSyncTransforms == null)
                {
                    _miSyncTransforms = typeof(Physics).GetMethod(
                        "SyncTransforms",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static
                    );
                }

                if (_miSyncTransforms != null)
                    _miSyncTransforms.Invoke(null, null);
            }
            catch { }
        }

        private void MoveRigidBody(GameObject go, Vector3 pos, Quaternion rot)
        {
            if (go == null) return;

            try
            {
                var rb = go.GetComponent<Rigidbody>() ?? go.GetComponentInChildren<Rigidbody>();
                if (rb != null)
                {
                    if (rb.isKinematic) rb.isKinematic = false;
                    rb.detectCollisions = true;
                    rb.velocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.position = pos;
                    rb.rotation = rot;
                    rb.WakeUp();

                    SyncTransformsCompat();
                }
                else
                {
                    go.transform.position = pos;
                    go.transform.rotation = rot;
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e);
            }
        }

        private bool ProceedSpawner(GameObject spawner, string ev = "PROCEED")
        {
            if (spawner == null)
            {
                Log("Proceed: spawner is null");
                return false;
            }

            var fsms = spawner.GetComponentsInChildren<PlayMakerFSM>(true);
            if (fsms == null || fsms.Length == 0)
            {
                Log($"Proceed: no FSMs on {spawner.name}");
                return false;
            }

            string[] candidates = new string[] { ev, "PROCEED", "USE", "Proceed", "Use" };
            foreach (var cand in candidates)
            {
                if (string.IsNullOrEmpty(cand)) continue;
                var c = cand.Trim();

                PlayMakerFSM targetFsm = null;
                foreach (var f in fsms)
                {
                    if (FsmHasEvent(f, c))
                    {
                        targetFsm = f;
                        break;
                    }
                }

                if (targetFsm != null)
                {
                    targetFsm.SendEvent(c);
                    if (DebugLog.Value) Log($"Proceed: {spawner.name} fsm={targetFsm.FsmName} ev={c}");
                    return true;
                }
            }

            foreach (var f in fsms)
            {
                try { f.SendEvent(ev); } catch { }
            }

            Log($"Proceed: WARN no FSM declared '{ev}' on {spawner.name} (broadcast sent)");
            return true;
        }

        private void Proceed(GameObject spawner)
        {
            ProceedSpawner(spawner, "PROCEED");
        }

        private void Log(string msg)
        {
            if (!DebugLog.Value) return;
            Logger.LogInfo("[FutufonAutoWorker] " + msg);
        }

        private static string Short(GameObject go)
        {
            if (go == null) return "null";
            return go.name;
        }
    }
}
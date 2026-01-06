using BepInEx;
using BepInEx.Configuration;
using HutongGames.PlayMaker;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FutufonAutoWorker
{
    [BepInPlugin("com.futufon.autoworker", "Futufon AutoWorker", "1.2.0")]
    public class Plugin : BaseUnityPlugin
    {
        private ConfigEntry<KeyCode> Hotkey;
        private ConfigEntry<bool> DebugLog;
        private ConfigEntry<float> StepWaitSec;
        private ConfigEntry<float> SpawnWaitSec;
        private ConfigEntry<float> SearchRadius;
        private ConfigEntry<bool> TeleportPlayerToPick;
        private ConfigEntry<bool> TeleportStuffToWorkbench;
        private ConfigEntry<bool> ManualBoxesMode;
        private ConfigEntry<bool> RequireWaitButton;
        private ConfigEntry<bool> InstantiateFallback;

        private bool _running;
        private Coroutine _loop;

        // Cached key objects
        private GameObject _pickChargers;
        private GameObject _pickSheets;
        private GameObject _pickManuals;
        private GameObject _pickTrays;

        private GameObject _workTable;

        // Cache for inactive templates (prefabs/disabled objects) to spawn without hover-raycast.
        private readonly Dictionary<string, GameObject> _templateCache = new Dictionary<string, GameObject>(StringComparer.Ordinal);

        private void Awake()
        {
            Hotkey = Config.Bind("General", "Hotkey", KeyCode.F8, "Toggle automation");
            DebugLog = Config.Bind("General", "DebugLog", true, "Extra logs");
            StepWaitSec = Config.Bind("Timing", "StepWaitSec", 0.10f, "Delay between small steps");
            SpawnWaitSec = Config.Bind("Timing", "SpawnWaitSec", 0.35f, "Delay after PROCEED/USE to allow spawn");
            SearchRadius = Config.Bind("Search", "SearchRadius", 5.0f, "How far to search for spawned objects");
            TeleportPlayerToPick = Config.Bind("Teleport", "TeleportPlayerToPick", true, "Teleport player near Pick* spawners to ensure Wait button state");
            TeleportStuffToWorkbench = Config.Bind("Teleport", "TeleportStuffToWorkbench", true, "Teleport boxes and parts to work_table2 (if found) or near player");

ManualBoxesMode = Config.Bind("Auto", "ManualBoxesMode", true, "If true, do not spawn initial boxes from Pick*; expects you to place chargers box/packaging sheets/manuals box/plastic trays manually.");
RequireWaitButton = Config.Bind("Auto", "RequireWaitButton", false, "If true, only send PROCEED/USE when FSM Active State is exactly 'Wait button'. Otherwise the step is skipped + logged.");
InstantiateFallback = Config.Bind("Auto", "InstantiateFallback", true, "If true, when expected object is not spawned, try to clone an inactive template by exact name and SetActive(true).");

Logger.LogInfo("[FutufonAutoWorker] Loaded");
        }

        private void Update()
        {
            if (Hotkey.Value != KeyCode.None && Input.GetKeyDown(Hotkey.Value))
                Toggle();
        }

        private void Toggle()
        {
            _running = !_running;

            if (_running)
            {
                RefreshCache();
                _loop = StartCoroutine(AutoLoop());
                Log("AutoWork: ON");
            }
            else
            {
                if (_loop != null) StopCoroutine(_loop);
                _loop = null;
                Log("AutoWork: OFF");
            }
        }

        private IEnumerator AutoLoop()
        {
            while (_running)
            {
                RefreshCache();
                yield return StartCoroutine(RunSafe(AutoOnce(), 1.0f));
                yield return WaitSeconds(Mathf.Max(0.01f, StepWaitSec.Value));
            }
        }



        private IEnumerator AutoOnce()
{
    var player = PlayerGO();
    if (player == null)
    {
        Log("AutoOnce: Player not found");
        yield break;
    }

    var anchor = AnchorPos();

    // 1) Boxes: manual by default, or spawn from Pick* if ManualBoxesMode=false.
    GameObject chargersBox = FindNearestSpawnedByName("chargers box(Clone)", anchor, SearchRadius.Value);
    GameObject sheetsBox   = FindNearestSpawnedByName("packaging sheets(Clone)", anchor, SearchRadius.Value);
    GameObject manualsBox  = FindNearestSpawnedByName("manuals box(Clone)", anchor, SearchRadius.Value);
    GameObject traysBox    = FindNearestSpawnedByName("plastic trays(Clone)", anchor, SearchRadius.Value);

    if (!ManualBoxesMode.Value)
    {
        if (chargersBox == null) yield return SpawnFromPick(_pickChargers, "chargers box(Clone)", r => chargersBox = r);
        if (sheetsBox   == null) yield return SpawnFromPick(_pickSheets,   "packaging sheets(Clone)", r => sheetsBox = r);
        if (manualsBox  == null) yield return SpawnFromPick(_pickManuals,  "manuals box(Clone)", r => manualsBox = r);
        if (traysBox    == null) yield return SpawnFromPick(_pickTrays,    "plastic trays(Clone)", r => traysBox = r);
    }

    if (chargersBox == null || sheetsBox == null || manualsBox == null || traysBox == null)
    {
        Log($"Auto: missing boxes chargers={(chargersBox!=null?"ok":"null")} sheets={(sheetsBox!=null?"ok":"null")} manuals={(manualsBox!=null?"ok":"null")} trays={(traysBox!=null?"ok":"null")}");
        yield break;
    }

    // 2) Stabilize placement near worktable.
    if (TeleportStuffToWorkbench.Value)
    {
        TeleportToAnchor(chargersBox, 0);
        TeleportToAnchor(sheetsBox,   1);
        TeleportToAnchor(manualsBox,  2);
        TeleportToAnchor(traysBox,    3);
        yield return WaitSeconds(Mathf.Max(SpawnWaitSec.Value, 0.7f));
    }

    // 3) Open boxes that support it.
    yield return EnsureOpenedIfHasBool(chargersBox, "open");
    yield return EnsureOpenedIfHasBool(manualsBox, "open");

    // 4) Spawn parts.
    GameObject charger = null;
    GameObject manual  = null;
    GameObject tray    = null;
    GameObject pack    = null;

    yield return SpawnFromSource(chargersBox, "charger(Clone)", r => charger = r);
    yield return SpawnFromSource(manualsBox,  "manual(Clone)",  r => manual  = r);
    yield return SpawnFromSource(traysBox,    "plastic tray(Clone)", r => tray = r);
    yield return SpawnFromSource(sheetsBox,   "package(Clone)", r => pack = r);

    if (charger == null || manual == null || tray == null || pack == null)
    {
        Log($"Auto: missing items charger={(charger!=null?"ok":"null")} manual={(manual!=null?"ok":"null")} tray={(tray!=null?"ok":"null")} pack={(pack!=null?"ok":"null")}");
        yield break;
    }

    // 5) Assemble tray via existing trigger-logic (keeps the game happy).
    yield return AssembleTray(tray, charger, manual);

    // 6) Fold package to Stage=5.
    yield return EnsurePackageStage(pack, 5, trySendUse:true);

    // 7) Put tray into package (existing trigger-logic).
    yield return PutTrayIntoPackage(pack, tray);

    // 8) Finish package - force flags and Stage=4 (final).
    yield return EnsurePackageFlags(pack, true, true, true);
    yield return EnsurePackageStage(pack, 4, trySendUse:true);

    yield break;
}

// ---------------------------
        // Spawning logic
        // ---------------------------

        
private GameObject GetInactiveTemplate(string exactName)
{
    if (string.IsNullOrEmpty(exactName)) return null;

    GameObject cached;
    if (_templateCache.TryGetValue(exactName, out cached) && cached != null)
        return cached;

    // Unity note: Resources.FindObjectsOfTypeAll returns inactive objects too, but it's expensive.
    GameObject found = null;
    foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
    {
        if (go == null) continue;
        if (!string.Equals(go.name, exactName, StringComparison.Ordinal)) continue;
        if (go.activeInHierarchy) continue; // we want the inactive template
        found = go;
        break;
    }

    if (found != null)
        _templateCache[exactName] = found;

    return found;
}

private GameObject SpawnFromTemplate(string exactName, Vector3 pos)
{
    var tmpl = GetInactiveTemplate(exactName);
    if (tmpl == null)
    {
        Log($"TEMPLATE MISSING: {exactName}");
        return null;
    }

    var obj = Instantiate(tmpl);
    // Instantiating an inactive template keeps it inactive, so we must activate it explicitly.
    obj.SetActive(true);
    // Avoid '(Clone)(Clone)' name drift.
    obj.name = exactName;

    obj.transform.position = pos;
    obj.transform.rotation = tmpl.transform.rotation;
    return obj;
}

private bool IsWaitButton(PlayMakerFSM fsm)
{
    return fsm != null && string.Equals(fsm.ActiveStateName, "Wait button", StringComparison.Ordinal);
}

private IEnumerator SpawnFromPick(GameObject pick, string expectedName, Action<GameObject> setFound)
{
    if (pick == null)
    {
        Log($"SpawnFromPick: pick=null expected={expectedName}");
        setFound?.Invoke(null);
        yield break;
    }

    var player = PlayerGO();
    var playerPos = player != null ? player.transform.position : pick.transform.position;

    var fsm = GetFsm(pick, "Use") ?? pick.GetComponent<PlayMakerFSM>();
    var state = fsm != null ? fsm.ActiveStateName : "<no fsm>";
    Log($"DBG SpawnFromPick TRY expected={expectedName}: target={pick.name} fsm={(fsm != null ? fsm.FsmName : "<none>")} state={state}");

    // If the game requires 'hover' for PROCEED, we can enforce it (RequireWaitButton) or skip and fallback to template spawn.
    if (fsm != null)
    {
        if (!RequireWaitButton.Value || IsWaitButton(fsm))
        {
            fsm.SendEvent("PROCEED");
            yield return WaitSeconds(Mathf.Max(SpawnWaitSec.Value, 0.6f));
        }
        else
        {
            Log($"SKIP PROCEED: {pick.name} state='{state}' (need 'Wait button')");
        }
    }

    var found = FindNearestSpawnedByName(expectedName, playerPos, SearchRadius.Value);

    if (found == null && InstantiateFallback.Value)
    {
        found = SpawnFromTemplate(expectedName, playerPos + new Vector3(0.0f, 0.2f, 0.35f));
        if (found != null) Log($"FALLBACK SPAWN (template): {expectedName}");
    }

    if (found == null) Log($"MISSING: {expectedName}");

    setFound?.Invoke(found);
    yield break;
}

private IEnumerator SpawnFromSource(GameObject src, string expectedName, Action<GameObject> setFound)
{
    if (src == null)
    {
        Log($"SpawnFromSource: src=null expected={expectedName}");
        setFound?.Invoke(null);
        yield break;
    }

    var player = PlayerGO();
    var playerPos = player != null ? player.transform.position : src.transform.position;

    var fsm = GetFsm(src, "Use") ?? src.GetComponent<PlayMakerFSM>();
    var state = fsm != null ? fsm.ActiveStateName : "<no fsm>";
    Log($"DBG SpawnFromSource TRY expected={expectedName}: target={src.name} fsm={(fsm != null ? fsm.FsmName : "<none>")} state={state}");

    if (fsm != null)
    {
        if (!RequireWaitButton.Value || IsWaitButton(fsm))
        {
            fsm.SendEvent("PROCEED");
            yield return WaitSeconds(Mathf.Max(SpawnWaitSec.Value, 0.6f));
        }
        else
        {
            Log($"SKIP PROCEED: {src.name} state='{state}' (need 'Wait button')");
        }
    }

    var found = FindNearestSpawnedByName(expectedName, playerPos, SearchRadius.Value);

    if (found == null && InstantiateFallback.Value)
    {
        found = SpawnFromTemplate(expectedName, playerPos + new Vector3(0.0f, 0.2f, 0.35f));
        if (found != null) Log($"FALLBACK SPAWN (template): {expectedName}");
    }

    if (found == null) Log($"MISSING: {expectedName}");

    setFound?.Invoke(found);
    yield break;
}

private IEnumerator EnsureOpenedIfHasBool(GameObject box, string boolName)
{
    if (box == null) yield break;

    var fsm = GetFsm(box, "Use") ?? box.GetComponent<PlayMakerFSM>();
    if (fsm == null)
    {
        Log($"EnsureOpened: no FSM on {box.name}");
        yield break;
    }

    var b = fsm.FsmVariables.FindFsmBool(boolName);
    if (b == null)
        yield break;

    if (b.Value)
        yield break;

    // New canon: set the variable directly first, then optionally poke the FSM with PROCEED to refresh visuals.
    b.Value = true;
    Log($"EnsureOpened: set {box.name}.{boolName}=ON");

    if (!RequireWaitButton.Value || IsWaitButton(fsm))
    {
        fsm.SendEvent("PROCEED");
        yield return WaitSeconds(Mathf.Max(StepWaitSec.Value, 0.2f));
    }
    else
    {
        Log($"EnsureOpened: SKIP PROCEED on {box.name} state='{fsm.ActiveStateName}'");
    }

    yield break;
}

private IEnumerator EnsurePackageStage(GameObject pack, int desiredStage, bool trySendUse)
{
    if (pack == null) yield break;

    var fsm = GetFsm(pack, "Use") ?? pack.GetComponent<PlayMakerFSM>();
    if (fsm == null) yield break;

    var stageInt = fsm.FsmVariables.FindFsmInt("Stage");
    if (stageInt != null && stageInt.Value != desiredStage)
    {
        stageInt.Value = desiredStage;
        Log($"Package: set Stage={desiredStage}");
    }

    if (trySendUse)
    {
        if (!RequireWaitButton.Value || IsWaitButton(fsm))
        {
            fsm.SendEvent("USE");
            yield return WaitSeconds(Mathf.Max(StepWaitSec.Value, 0.2f));
        }
        else
        {
            Log($"Package: SKIP USE (need 'Wait button'), state='{fsm.ActiveStateName}'");
        }
    }

    yield break;
}

private IEnumerator EnsurePackageFlags(GameObject pack, bool chargerOn, bool manualOn, bool mouldOn)
{
    if (pack == null) yield break;

    var fsm = GetFsm(pack, "Use") ?? pack.GetComponent<PlayMakerFSM>();
    if (fsm == null) yield break;

    var bCharger = fsm.FsmVariables.FindFsmBool("Charger");
    var bManual  = fsm.FsmVariables.FindFsmBool("Manual");
    var bMould   = fsm.FsmVariables.FindFsmBool("Mould");

    if (bCharger != null) bCharger.Value = chargerOn;
    if (bManual  != null) bManual.Value  = manualOn;
    if (bMould   != null) bMould.Value   = mouldOn;

    Log($"Package: flags Charger={(bCharger!=null?bCharger.Value.ToString():"<na>")} Manual={(bManual!=null?bManual.Value.ToString():"<na>")} Mould={(bMould!=null?bMould.Value.ToString():"<na>")}");
    yield break;
}

private IEnumerator AssembleTray(GameObject tray, GameObject charger, GameObject manual)
        {
            if (tray == null || charger == null || manual == null)
                yield break;

            // Teleport parts into tray slots and poke the trigger FSMs
            var trigCharger = FindChildByName(tray.transform, "TriggerCharger");
            var trigManual = FindChildByName(tray.transform, "TriggerManual");

            if (trigCharger != null)
            {
                TeleportTo(trigCharger.position, charger);
                yield return WaitSeconds(0.05f);
                yield return SendAnyEvent(trigCharger.gameObject, "PROCEED");
                yield return WaitSeconds(SpawnWaitSec.Value);
            }

            if (trigManual != null)
            {
                TeleportTo(trigManual.position, manual);
                yield return WaitSeconds(0.05f);
                yield return SendAnyEvent(trigManual.gameObject, "PROCEED");
                yield return WaitSeconds(SpawnWaitSec.Value);
            }

            // Give a little time for variables to update
            yield return WaitSeconds(0.25f);
        }

        private IEnumerator PutTrayIntoPackage(GameObject pack, GameObject tray)
        {
            if (pack == null || tray == null)
                yield break;

            var trigTray = FindChildByName(pack.transform, "TriggerTray");
            if (trigTray == null)
                yield break;

            TeleportTo(trigTray.position, tray);
            yield return WaitSeconds(0.05f);

            yield return SendAnyEvent(trigTray.gameObject, "PROCEED");
            yield return WaitSeconds(SpawnWaitSec.Value);
        }

        private IEnumerator UseFsm(GameObject go, string fsmName, string evt, int times)
        {
            if (go == null || times <= 0)
                yield break;

            for (int i = 0; i < times; i++)
            {
                yield return ForceWaitButton(go, 1.0f);
                var fsm = GetFsm(go, fsmName);
                if (fsm == null) yield break;
                fsm.SendEvent(evt);
                yield return WaitSeconds(Mathf.Max(0.03f, SpawnWaitSec.Value));
            }
        }

        // ---------------------------
        // Interaction helpers (Wait button)
        // ---------------------------

        private IEnumerator ForceWaitButton(GameObject target, float maxSec)
        {
            if (target == null)
                yield break;

            var fsm = GetFsm(target, "Use") ?? target.GetComponent<PlayMakerFSM>();
            if (fsm == null)
                yield break;

            float t0 = Time.time;
            while (Time.time - t0 < maxSec)
            {
                AimAt(target);

                // Let the game update cursor/interaction
                yield return null;

                string st = fsm.ActiveStateName ?? "";
                if (string.Equals(st, "Wait button", StringComparison.OrdinalIgnoreCase))
                    yield break;

                yield return WaitSeconds(0.03f);
            }
        }

        private IEnumerator SendUseEvent(GameObject target, string evt)
        {
            if (target == null) yield break;

            var fsm = GetFsm(target, "Use") ?? target.GetComponent<PlayMakerFSM>();
            if (fsm == null) yield break;

            fsm.SendEvent(evt);
            yield return null;
        }

        private IEnumerator SendAnyEvent(GameObject target, string evt)
        {
            if (target == null) yield break;

            var fsm = target.GetComponent<PlayMakerFSM>();
            if (fsm == null) yield break;

            fsm.SendEvent(evt);
            yield return null;
        }

        private void AimAt(GameObject target)
        {
            if (target == null)
                return;

            var cam = Camera.main != null ? Camera.main.transform : null;
            if (cam == null)
                return;

            Vector3 point = TargetPoint(target);

            var dir = (point - cam.position);
            if (dir.sqrMagnitude < 0.0001f)
                return;

            var rot = Quaternion.LookRotation(dir.normalized, Vector3.up);

            // Rotate camera
            cam.rotation = rot;

            // Also rotate player yaw to match (helps crosshair alignment in MWC)
            var player = PlayerGO();
            if (player != null)
            {
                Vector3 flat = new Vector3(dir.x, 0f, dir.z);
                if (flat.sqrMagnitude > 0.0001f)
                    player.transform.rotation = Quaternion.LookRotation(flat.normalized, Vector3.up);
            }
        }

        private Vector3 TargetPoint(GameObject go)
        {
            var col = go.GetComponentInChildren<Collider>();
            if (col != null) return col.bounds.center;

            var rend = go.GetComponentInChildren<Renderer>();
            if (rend != null) return rend.bounds.center;

            return go.transform.position;
        }

        // ---------------------------
        // Teleport helpers
        // ---------------------------

        private void TeleportPlayerNear(GameObject target)
        {
            var player = PlayerGO();
            if (player == null || target == null)
                return;

            Vector3 p = target.transform.position;

            // Place player slightly back from the target, at roughly same height
            Vector3 back = -target.transform.forward;
            if (back.sqrMagnitude < 0.01f)
                back = -player.transform.forward;

            p += back.normalized * 0.9f;
            p.y = player.transform.position.y;

            player.transform.position = p;

            // Try to stop rigidbody drift if present
            var rb = player.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        private void TeleportToAnchor(GameObject go, int slot)
        {
            if (go == null) return;

            var anchor = AnchorPos();
            var player = PlayerGO();

            Vector3 fwd = player != null ? player.transform.forward : Vector3.forward;
            Vector3 right = player != null ? player.transform.right : Vector3.right;

            // Small grid around anchor to avoid overlaps
            float step = 0.45f;
            int row = slot / 4;
            int col = slot % 4;

            Vector3 pos = anchor + right * (col * step) + fwd * (row * step);
            pos.y = anchor.y + 0.05f;

            TeleportTo(pos, go);
        }

        private void TeleportTo(Vector3 pos, GameObject go)
        {
            if (go == null) return;

            go.transform.position = pos;

            var rb = go.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        private Vector3 AnchorPos()
        {
            if (_workTable == null)
                _workTable = FindActiveByName("work_table2");

            if (_workTable != null)
                return _workTable.transform.position;

            var player = PlayerGO();
            return player != null ? player.transform.position : Vector3.zero;
        }

        // ---------------------------
        // Cache + search
        // ---------------------------

        private void RefreshCache()
        {
            _pickChargers = FindActiveByName("PickChargers");
            _pickSheets = FindActiveByName("PickSheets");
            _pickManuals = FindActiveByName("PickManuals");
            _pickTrays = FindActiveByName("PickTrays");

            _workTable = FindActiveByName("work_table2");

            if (DebugLog.Value)
                Log($"Cache: PickChargers={Ok(_pickChargers)} PickSheets={Ok(_pickSheets)} PickManuals={Ok(_pickManuals)} PickTrays={Ok(_pickTrays)} WorkTable={Ok(_workTable)}");
        }

        private static string Ok(GameObject go) => go != null ? "ok" : "null";

        private GameObject PlayerGO()
        {
            return FindActiveByName("PLAYER") ?? GameObject.Find("PLAYER");
        }

        private GameObject FindNearestSpawnedByName(string exactName, Vector3 near, float radius)
        {
            float best = float.MaxValue;
            GameObject bestGo = null;

            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null) continue;
                if (!go.activeInHierarchy) continue;
                if (!string.Equals(go.name, exactName, StringComparison.Ordinal)) continue;

                if (IsUnderSpawnerOrPick(go)) continue;

                float d = (go.transform.position - near).sqrMagnitude;
                if (d <= radius * radius && d < best)
                {
                    best = d;
                    bestGo = go;
                }
            }
            return bestGo;
        }

        private GameObject FindActiveByName(string exactName)
        {
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null) continue;
                if (!go.activeInHierarchy) continue;
                if (string.Equals(go.name, exactName, StringComparison.Ordinal))
                    return go;
            }
            return null;
        }

        private static bool IsUnderSpawnerOrPick(GameObject go)
        {
            if (go == null) return false;

            Transform t = go.transform;
            int depth = 0;
            while (t != null && depth++ < 16)
            {
                string n = t.name ?? "";
                if (string.Equals(n, "Spawner", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (n.StartsWith("Pick", StringComparison.OrdinalIgnoreCase))
                    return true;

                t = t.parent;
            }
            return false;
        }

        private static Transform FindChildByName(Transform root, string name)
        {
            if (root == null) return null;

            // Breadth-first to find in deep hierarchies
            var q = new System.Collections.Generic.Queue<Transform>();
            q.Enqueue(root);

            while (q.Count > 0)
            {
                var t = q.Dequeue();
                if (t != root && string.Equals(t.name, name, StringComparison.Ordinal))
                    return t;

                for (int i = 0; i < t.childCount; i++)
                    q.Enqueue(t.GetChild(i));
            }

            return null;
        }

        private static PlayMakerFSM GetFsm(GameObject go, string fsmName)
        {
            if (go == null) return null;
            var fsms = go.GetComponents<PlayMakerFSM>();
            if (fsms == null || fsms.Length == 0) return null;
            return fsms.FirstOrDefault(f => string.Equals(f.FsmName, fsmName, StringComparison.OrdinalIgnoreCase));
        }

        private static FsmBool GetBoolVar(PlayMakerFSM fsm, string name)
        {
            if (fsm == null || fsm.FsmVariables == null) return null;
            return fsm.FsmVariables.BoolVariables?.FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        private YieldInstruction WaitSeconds(float sec)
        {
            return new WaitForSeconds(Mathf.Max(0.01f, sec));
        }

        
        private IEnumerator RunSafe(IEnumerator inner, float onErrorWaitSec = 1.0f)
        {
            if (inner == null)
                yield break;

            while (true)
            {
                object current = null;
                Exception error = null;

                try
                {
                    if (!inner.MoveNext())
                        yield break;

                    current = inner.Current;
                }
                catch (Exception ex)
                {
                    error = ex;
                }

                if (error != null)
                {
                    Logger.LogError(error);
                    yield return new WaitForSeconds(onErrorWaitSec);
                    yield break;
                }

                yield return current;
            }
        }

private void Log(string msg)
        {
            if (DebugLog.Value)
                Logger.LogInfo("[FutufonAutoWorker] " + msg);
        }
    }
}

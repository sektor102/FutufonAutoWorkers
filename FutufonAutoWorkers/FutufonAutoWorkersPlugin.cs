using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using HutongGames.PlayMaker;
using System;
using System.Collections;
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

        private bool _running;
        private Coroutine _loop;

        // Cached key objects
        private GameObject _pickChargers;
        private GameObject _pickSheets;
        private GameObject _pickManuals;
        private GameObject _pickTrays;

        private GameObject _workTable;

        private void Awake()
        {
            Hotkey = Config.Bind("General", "Hotkey", KeyCode.F8, "Toggle automation");
            DebugLog = Config.Bind("General", "DebugLog", true, "Extra logs");
            StepWaitSec = Config.Bind("Timing", "StepWaitSec", 0.10f, "Delay between small steps");
            SpawnWaitSec = Config.Bind("Timing", "SpawnWaitSec", 0.35f, "Delay after PROCEED/USE to allow spawn");
            SearchRadius = Config.Bind("Search", "SearchRadius", 5.0f, "How far to search for spawned objects");
            TeleportPlayerToPick = Config.Bind("Teleport", "TeleportPlayerToPick", true, "Teleport player near Pick* spawners to ensure Wait button state");
            TeleportStuffToWorkbench = Config.Bind("Teleport", "TeleportStuffToWorkbench", true, "Teleport boxes and parts to work_table2 (if found) or near player");

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
            if (_pickChargers == null || _pickSheets == null || _pickManuals == null || _pickTrays == null)
            {
                Log("Cache: missing Pick* objects. Make sure you are inside the factory job area.");
                yield break;
            }

            var anchor = AnchorPos();

            // 1) Ensure boxes exist (spawn from Pick* if needed), then bring them to workbench
            GameObject chargersBox =
                FindNearestSpawnedByName("chargers box(Clone)", _pickChargers.transform.position, 25.0f) ??
                FindNearestSpawnedByName("chargers box(Clone)", anchor, SearchRadius.Value);
            if (chargersBox == null)
            {
                GameObject tmp_chargersBox = null;
                yield return StartCoroutine(SpawnFromPick(_pickChargers, "chargers box(Clone)", g => tmp_chargersBox = g));
                chargersBox = tmp_chargersBox;
            }
            GameObject sheets =
                FindNearestSpawnedByName("packaging sheets(Clone)", _pickSheets.transform.position, 25.0f) ??
                FindNearestSpawnedByName("packaging sheets(Clone)", anchor, SearchRadius.Value);

            if (sheets == null)
            {
                GameObject tmp_sheets = null;
                yield return StartCoroutine(SpawnFromPick(_pickSheets, "packaging sheets(Clone)", g => tmp_sheets = g));
                sheets = tmp_sheets;
            }
            GameObject manualsBox =
                FindNearestSpawnedByName("manuals box(Clone)", _pickManuals.transform.position, 25.0f) ??
                FindNearestSpawnedByName("manuals box(Clone)", anchor, SearchRadius.Value);

            if (manualsBox == null)
            {
                GameObject tmp_manualsBox = null;
                yield return StartCoroutine(SpawnFromPick(_pickManuals, "manuals box(Clone)", g => tmp_manualsBox = g));
                manualsBox = tmp_manualsBox;
            }
            GameObject traysBox =
                FindNearestSpawnedByName("plastic trays(Clone)", _pickTrays.transform.position, 25.0f) ??
                FindNearestSpawnedByName("plastic trays(Clone)", anchor, SearchRadius.Value);

            if (traysBox == null)
            {
                GameObject tmp_traysBox = null;
                yield return StartCoroutine(SpawnFromPick(_pickTrays, "plastic trays(Clone)", g => tmp_traysBox = g));
                traysBox = tmp_traysBox;
            }

            if (chargersBox == null || sheets == null || manualsBox == null || traysBox == null)
            {
                Log($"Auto: missing boxes chargers={Ok(chargersBox)} sheets={Ok(sheets)} manuals={Ok(manualsBox)} trays={Ok(traysBox)}");
                yield break;
            }

            if (TeleportStuffToWorkbench.Value)
            {
                TeleportToAnchor(chargersBox, 0);
                TeleportToAnchor(sheets, 1);
                TeleportToAnchor(manualsBox, 2);
                TeleportToAnchor(traysBox, 3);
                yield return WaitSeconds(Mathf.Max(SpawnWaitSec.Value, 0.7f));
            }

            // 2) Open chargers/manuals boxes if they have variable "open"
            yield return EnsureOpenedIfHasBool(chargersBox, "open");
            yield return EnsureOpenedIfHasBool(manualsBox, "open");

            // 3) Spawn parts
            GameObject charger = FindNearestSpawnedByName("charger(Clone)", anchor, SearchRadius.Value);
            if (charger == null)
            {
                GameObject tmp_charger = null;
                yield return StartCoroutine(SpawnFromSource(chargersBox, "charger(Clone)", g => tmp_charger = g));
                charger = tmp_charger;
            }
            GameObject manual = FindNearestSpawnedByName("manual(Clone)", anchor, SearchRadius.Value);
            if (manual == null)
            {
                GameObject tmp_manual = null;
                yield return StartCoroutine(SpawnFromSource(manualsBox, "manual(Clone)", g => tmp_manual = g));
                manual = tmp_manual;
            }
            GameObject tray = FindNearestSpawnedByName("plastic tray(Clone)", anchor, SearchRadius.Value);
            if (tray == null)
            {
                GameObject tmp_tray = null;
                yield return StartCoroutine(SpawnFromSource(traysBox, "plastic tray(Clone)", g => tmp_tray = g));
                tray = tmp_tray;
            }
            GameObject pack = FindNearestSpawnedByName("package(Clone)", anchor, SearchRadius.Value);
            if (pack == null)
            {
                GameObject tmp_pack = null;
                yield return StartCoroutine(SpawnFromSource(sheets, "package(Clone)", g => tmp_pack = g));
                pack = tmp_pack;
            }

            if (charger == null || manual == null || tray == null || pack == null)
            {
                Log($"Auto: missing items after spawn charger={Ok(charger)} manual={Ok(manual)} tray={Ok(tray)} pack={Ok(pack)}");
                yield break;
            }

            if (TeleportStuffToWorkbench.Value)
            {
                TeleportToAnchor(charger, 4);
                TeleportToAnchor(manual, 5);
                TeleportToAnchor(tray, 6);
                TeleportToAnchor(pack, 7);
                yield return WaitSeconds(Mathf.Max(SpawnWaitSec.Value, 0.7f));
            }

            // 4) Assemble tray (put charger + manual into tray)
            yield return AssembleTray(tray, charger, manual);

            // 5) Fold package 5 times (USE)
            yield return UseFsm(pack, "Use", "USE", 5);

            // 6) Put assembled tray into package trigger, then final USE
            yield return PutTrayIntoPackage(pack, tray);
            yield return UseFsm(pack, "Use", "USE", 1);
        }

        // ---------------------------
        // Spawning logic
        // ---------------------------

        private IEnumerator SpawnFromPick(GameObject pick, string expectedName, Action<GameObject> setFound)
        {
            if (pick == null)
                yield break;

            // Ensure player is close and looking at Pick* so its FSM becomes "Wait button"
            if (TeleportPlayerToPick.Value)
                TeleportPlayerNear(pick);

            AimCameraAt(pick, 30f);
            yield return ForceWaitButton(pick, 1.2f);

            // Try PROCEED on Pick* itself
            LogCtx($"SpawnFromPick BEFORE PROCEED expected={expectedName}", pick);
            yield return SendUseEvent(pick, "PROCEED");
            yield return WaitSeconds(Mathf.Max(SpawnWaitSec.Value, 0.7f));
            DebugNameStats(expectedName);

            // Fallback: also try PROCEED on spawner child (some versions gate differently)
            var spawnerChild = FindChildByName(pick.transform, "Spawner");
            if (spawnerChild != null)
            {
                yield return SendUseEvent(spawnerChild.gameObject, "PROCEED");
                yield return WaitSeconds(Mathf.Max(SpawnWaitSec.Value, 0.7f));
            }
            DebugNameStats(expectedName);

            // Find spawned box near Pick*
            var player = PlayerGO();
            var nearPos = player != null ? player.transform.position : pick.transform.position;

            // коробка часто появляется перед игроком, а не у Pick*
            var found = FindNearestSpawnedByName(expectedName, nearPos, 12.0f);

            if (found == null)
                Log($"MISSING: {expectedName.Replace("(Clone)", "").Trim()}");

            setFound?.Invoke(found);
            yield break;
        }

        private IEnumerator SpawnFromSource(GameObject src, string expectedName, Action<GameObject> setFound)
        {
            if (src == null)
                yield break;

            // ВАЖНО: для коробок тоже нужно быть рядом и смотреть на них
            if (TeleportPlayerToPick.Value)
                TeleportPlayerNear(src);

            AimCameraAt(src, 30f);
            yield return ForceWaitInteractable(src, 1.2f);
            LogCtx($"SpawnFromSource BEFORE PROCEED expected={expectedName}", src);

            // У коробок ты сам проверил - есть PROCEED
            yield return SendUseEvent(src, "PROCEED");
            yield return WaitSeconds(Mathf.Max(SpawnWaitSec.Value, 0.7f));
            DebugNameStats(expectedName);


            var player = PlayerGO();
            var nearPos = player != null ? player.transform.position : src.transform.position;

            // предметы из коробок тоже часто появляются у игрока
            var found = FindNearestSpawnedByName(expectedName, nearPos, 12.0f);
            if (found == null)
                Log($"MISSING: {expectedName.Replace("(Clone)", "").Trim()}");

            setFound?.Invoke(found);
        }


        private IEnumerator EnsureOpenedIfHasBool(GameObject box, string boolVarName)
        {
            if (box == null)
                yield break;

            var fsm = GetFsm(box, "Use");
            if (fsm == null)
                yield break;

            var b = GetBoolVar(fsm, boolVarName);
            if (b == null)
                yield break; // no open variable, do nothing

            int tries = 0;
            while (tries++ < 5 && !b.Value)
            {
                yield return ForceWaitInteractable(box, 1.2f);
                yield return SendUseEvent(box, "PROCEED");
                yield return WaitSeconds(Mathf.Max(SpawnWaitSec.Value, 0.7f));
                b = GetBoolVar(fsm, boolVarName);
                if (b == null) break;
            }
        }

        // ---------------------------
        // Assembly
        // ---------------------------

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
                yield return WaitSeconds(Mathf.Max(SpawnWaitSec.Value, 0.7f));
            }

            if (trigManual != null)
            {
                TeleportTo(trigManual.position, manual);
                yield return WaitSeconds(0.05f);
                yield return SendAnyEvent(trigManual.gameObject, "PROCEED");
                yield return WaitSeconds(Mathf.Max(SpawnWaitSec.Value, 0.7f));
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
            yield return WaitSeconds(Mathf.Max(SpawnWaitSec.Value, 0.7f));
        }

        private IEnumerator UseFsm(GameObject go, string fsmName, string evt, int times)
        {
            if (go == null || times <= 0)
                yield break;

            for (int i = 0; i < times; i++)
            {
                yield return ForceWaitInteractable(go, 1.2f);
                var fsm = GetFsm(go, fsmName);
                if (fsm == null) yield break;
                fsm.SendEvent(evt);
                yield return WaitSeconds(Mathf.Max(0.03f, SpawnWaitSec.Value));
            }
        }

        // ---------------------------
        // Interaction helpers (Wait button)
        // ---------------------------

        private void AimCameraAt(GameObject target, float pitchDownDeg = 25f)
        {
            if (target == null) return;

            var player = PlayerGO();
            if (player == null) return;

            // Yaw: повернуть игрока по горизонтали на цель
            Vector3 dir = target.transform.position - player.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.001f)
                player.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);

            // Pitch: опустить камеру вниз
            var cam = Camera.main != null ? Camera.main.transform : null;
            if (cam != null)
            {
                var e = cam.localEulerAngles;
                // Unity хранит углы 0..360, поэтому делаем "вниз" через 360 - pitch
                e.x = 360f - Mathf.Clamp(pitchDownDeg, 0f, 80f);
                cam.localEulerAngles = e;
            }
        }

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

        private IEnumerator ForceWaitInteractable(GameObject target, float maxSec)
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
                yield return null;

                string st = (fsm.ActiveStateName ?? "").Trim();

                // коробки часто в "Wait player", Pick* в "Wait button"
                if (string.Equals(st, "Wait button", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(st, "Wait player", StringComparison.OrdinalIgnoreCase))
                    yield break;

                yield return WaitSeconds(0.03f);
            }

            Log($"WARN: can't reach Wait state for {target.name}, state={fsm.ActiveStateName}");
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

            var fsms = target.GetComponents<PlayMakerFSM>();
            if (fsms == null || fsms.Length == 0)
            {
                Log($"DBG SendAnyEvent: target={target.name} NO_FSM evt={evt}");
                yield break;
            }

            PlayMakerFSM fsm = null;

            // 1) Если есть FSM с именем, похожим на имя объекта - берем его
            // (часто удобно для TriggerCharger/TriggerManual)
            fsm = fsms.FirstOrDefault(x =>
                !string.IsNullOrEmpty(x.FsmName) &&
                x.FsmName.IndexOf(target.name, StringComparison.OrdinalIgnoreCase) >= 0);

            // 2) Если есть Use - берем его
            if (fsm == null)
                fsm = fsms.FirstOrDefault(x =>
                    string.Equals(x.FsmName, "Use", StringComparison.OrdinalIgnoreCase));

            // 3) Иначе берем первый
            if (fsm == null)
                fsm = fsms[0];

            // Логи: какой FSM выбрали и какие вообще есть
            if (DebugLog.Value)
            {
                string all = string.Join(", ", fsms.Select(x => $"{x.FsmName}:{x.ActiveStateName}").ToArray());
                Logger.LogInfo($"[FutufonAutoWorker] DBG SendAnyEvent: target={target.name} evt={evt} chosen={fsm.FsmName} state={fsm.ActiveStateName} all=[{all}]");
            }

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

            Vector3 forward = target.transform.forward;
            if (forward.sqrMagnitude < 0.01f)
                forward = player.transform.forward;

            Vector3 right = target.transform.right;
            if (right.sqrMagnitude < 0.01f)
                right = player.transform.right;

            // Телепортируемся не "внутрь" объекта, а немного перед ним и чуть сбоку
            Vector3 dst = p - forward.normalized * 0.75f + right.normalized * 0.15f;

            // По высоте оставляем уровень игрока, чтобы не проваливаться/не подпрыгивать
            dst.y = player.transform.position.y;

            player.transform.position = dst;

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
            pos.y = anchor.y + 0.35f;

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

        private static string Fmt(Vector3 v) => $"{v.x:F2},{v.y:F2},{v.z:F2}";

        private void LogCtx(string tag, GameObject target)
        {
            if (!DebugLog.Value) return;

            var player = PlayerGO();
            var cam = Camera.main;

            Vector3 ppos = player != null ? player.transform.position : Vector3.zero;
            Vector3 tpos = target != null ? target.transform.position : Vector3.zero;

            float dist = (player != null && target != null) ? Vector3.Distance(ppos, tpos) : -1f;

            // Берем Use FSM если есть, иначе любой FSM на объекте
            var fsm = GetFsm(target, "Use") ?? (target != null ? target.GetComponent<PlayMakerFSM>() : null);
            string st = fsm != null ? (fsm.ActiveStateName ?? "null") : "no_fsm";
            string fsmName = fsm != null ? (fsm.FsmName ?? "null") : "no_fsm";

            string camPos = cam != null ? Fmt(cam.transform.position) : "no_cam";

            Logger.LogInfo($"[FutufonAutoWorker] DBG {tag}: target={target?.name} tpos={Fmt(tpos)} player={Fmt(ppos)} dist={dist:F2} cam={camPos} fsm={fsmName} state={st}");
        }

        private void DebugNameStats(string exactName)
        {
            if (!DebugLog.Value) return;

            int total = 0, active = 0, underPick = 0;

            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null) continue;
                if (!string.Equals(go.name, exactName, StringComparison.Ordinal)) continue;

                total++;
                if (go.activeInHierarchy)
                {
                    active++;
                    if (IsUnderSpawnerOrPick(go)) underPick++;
                }
            }

            Logger.LogInfo($"[FutufonAutoWorker] DBG name={exactName} total={total} active={active} underPickOrSpawner={underPick}");
        }


        private void Log(string msg)
        {
            if (DebugLog.Value)
                Logger.LogInfo("[FutufonAutoWorker] " + msg);
        }
    }
}

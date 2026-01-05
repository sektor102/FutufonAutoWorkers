using BepInEx;
using BepInEx.Configuration;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using HutongGames.PlayMaker;

namespace FutufonAutoWorker
{
    [BepInPlugin("com.futufon.autoworker", "Futufon AutoWorker (autofocus8)", "0.3.1")]
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
        private GameObject _srcTrays, _srcChargers, _srcManuals, _srcSheets;
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

            Log("Loaded autofocus7. F8 toggle automation, F5 debug PROCEED, F7 debug package, F9 dump FSMs.");
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

                // StopCoroutine не выполняет finally внутри корутины - сбрасываем сами,
                // иначе Update() будет продолжать блокировать ручные хоткеи.
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

                // TargetCount == 0 (или меньше) - бесконечный режим.
                while (_running && (TargetCount.Value <= 0 || done < TargetCount.Value))
                {
                    CacheWorkObjects();

                    GameObject tray = null;
                    GameObject pack = null;
                    GameObject charger = null;
                    GameObject manual = null;

                    yield return EnsureSpawned("plastic tray(Clone)", _pickTrays, o => tray = o);
                    yield return EnsureSpawned("package(Clone)", _pickSheets, o => pack = o);
                    yield return EnsureSpawned("charger(Clone)", _pickChargers, o => charger = o);
                    yield return EnsureSpawned("manual(Clone)", _pickManuals, o => manual = o);

                    Log(string.Format("Auto: have items tray={0}, pack={1}, charger={2}, manual={3}",
                        Short(tray), Short(pack), Short(charger), Short(manual)));

                    if (tray == null || pack == null || charger == null || manual == null)
                {
                    Log("Auto: missing items after retries, will retry");
                    yield return new WaitForSeconds(Mathf.Max(0.2f, SpawnWaitSec.Value));
                    continue;
                }

                    yield return AssembleTray(tray, charger, manual);
                    yield return new WaitForSeconds(Mathf.Max(0.05f, StepWaitSec.Value));

                    yield return InsertTrayIntoPackage(pack, tray);
                    yield return new WaitForSeconds(Mathf.Max(0.05f, StepWaitSec.Value));

                    yield return FoldPackage(pack);
                    yield return new WaitForSeconds(Mathf.Max(0.05f, StepWaitSec.Value));

                    yield return DeliverToPallet(pack);
                    yield return new WaitForSeconds(Mathf.Max(0.05f, StepWaitSec.Value));

                    done++;
                    if (TargetCount.Value <= 0)
                        Log(string.Format("Auto: done {0} (TargetCount=0 => endless)", done));
                    else
                        Log(string.Format("Auto: done {0}/{1}", done, TargetCount.Value));

                    yield return null;
                }
            }
            finally
            {
                _busy = false;
                Log("AutoWork: OFF");
            }
        }

        private void CacheFactoryRefs()
{
    // В MWC нередко есть несколько копий объектов (LOD/дубликаты). Нам нужны ближайшие АКТИВНЫЕ.
    Vector3 near = PlayerPos();
    const float rSrc = 150f;
    const float rPick = 150f;
    const float rPallet = 250f;

    // Pick-* спавнеры (у них часто есть PROCEED/FINISHED)
    _pickTrays = FindNearestActiveInScene("PickTrays", near, rPick, go => go.GetComponent<PlayMakerFSM>() != null || GetFsm(go, "Use") != null) ?? FindGODeep("PickTrays");
    _pickSheets = FindNearestActiveInScene("PickSheets", near, rPick, go => go.GetComponent<PlayMakerFSM>() != null || GetFsm(go, "Use") != null) ?? FindGODeep("PickSheets");
    _pickChargers = FindNearestActiveInScene("PickChargers", near, rPick, go => go.GetComponent<PlayMakerFSM>() != null || GetFsm(go, "Use") != null) ?? FindGODeep("PickChargers");
    _pickManuals = FindNearestActiveInScene("PickManuals", near, rPick, go => go.GetComponent<PlayMakerFSM>() != null || GetFsm(go, "Use") != null) ?? FindGODeep("PickManuals");

    // Исходные коробки/пачки (на некоторых билдах/локализациях тоже работают)
    _srcTrays = FindNearestActiveInScene("plastic trays(Clone)", near, rSrc) ?? FindGODeep("plastic trays(Clone)");
    _srcSheets = FindNearestActiveInScene("packaging sheets(Clone)", near, rSrc) ?? FindGODeep("packaging sheets(Clone)");
    _srcChargers = FindNearestActiveInScene("chargers box(Clone)", near, rSrc) ?? FindGODeep("chargers box(Clone)");
    _srcManuals = FindNearestActiveInScene("manuals box(Clone)", near, rSrc) ?? FindGODeep("manuals box(Clone)");

    _palletPlayer = FindNearestActiveInScene("PalletPackagesPlayer", near, rPallet, go => go.GetComponent<PlayMakerFSM>() != null) ?? FindGODeep("PalletPackagesPlayer");
    _palletTrigger = null;
    if (_palletPlayer != null)
    {
        // В разных версиях может называться по-разному - пробуем несколько
        Transform t = _palletPlayer.transform.Find("TriggerBox");
        if (t == null) t = FindChildDeep(_palletPlayer.transform, "TriggerBox");
        if (t == null) t = FindChildDeep(_palletPlayer.transform, "TriggerPallet");
        _palletTrigger = t;
    }

    if (DebugLog.Value)
    {
        Log(string.Format(
            "Cache: SrcTrays={0} SrcSheets={1} SrcChargers={2} SrcManuals={3} PickTrays={4} PickSheets={5} PickChargers={6} PickManuals={7} Pallet={8}/{9}",
            _srcTrays ? "ok" : "null",
            _srcSheets ? "ok" : "null",
            _srcChargers ? "ok" : "null",
            _srcManuals ? "ok" : "null",
            _pickTrays ? "ok" : "null",
            _pickSheets ? "ok" : "null",
            _pickChargers ? "ok" : "null",
            _pickManuals ? "ok" : "null",
            _palletPlayer ? "ok" : "null",
            _palletTrigger ? "ok" : "null"));
    }
}


        // Backward-compatible alias (older patches referenced CacheWorkObjects)
        private void CacheWorkObjects()
        {
            CacheFactoryRefs();
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

        private IEnumerator AssembleTray(GameObject tray, GameObject charger, GameObject manual)
        {
            // charger + manual into tray
            yield return AssembleIntoTray(tray, charger, "plastic_tray/TriggerCharger", "charger");
            yield return new WaitForSeconds(Mathf.Max(0.05f, StepWaitSec.Value));

            yield return AssembleIntoTray(tray, manual, "plastic_tray/TriggerManual", "manual");
            yield return new WaitForSeconds(Mathf.Max(0.05f, StepWaitSec.Value));

            Log("Auto: tray assembled (charger+manual)");
        }

        private IEnumerator InsertTrayIntoPackage(GameObject pack, GameObject tray)
        {
            yield return AssembleTrayIntoPackage(pack, tray);
            Log("Auto: tray inserted into package");
        }

        private IEnumerator EnsureSpawned(string itemName, GameObject pick, System.Action<GameObject> setFound, int tries = 6)
        {
            GameObject found = FindSpawnedNear(itemName, pick);
            if (found != null)
            {
                setFound(found);
                yield break;
            }

            if (pick == null)
            {
                setFound(null);
                yield break;
            }

            for (int i = 0; i < tries; i++)
            {
                Log(string.Format("Spawn: request {0} via {1} try={2}/{3}", itemName, Short(pick), i + 1, tries));

                // Some Pick* spawners only have PROCEED/FINISHED.
                TrySendEventToAnyFsmWithEvent(pick, "PROCEED", "Use", "Pick");
                yield return new WaitForSeconds(0.05f);
                TrySendEventToAnyFsmWithEvent(pick, "FINISHED", "Use", "Pick");

                yield return new WaitForSeconds(Mathf.Max(0.05f, SpawnWaitSec.Value));

                found = FindSpawnedNear(itemName, pick);
                if (found != null)
                    break;
            }

            setFound(found);
        }



        private bool ProceedSpawner(GameObject spawner, string ev = "PROCEED")
{
    if (spawner == null) return false;

    // ВАЖНО: у Pick* объектов часто несколько FSM. Первый может быть не "Use".
    // Поэтому целимся сначала в FSM "Use", затем пробуем "Pick", и только потом - в первый FSM.
    if (TrySendEventToFsm(spawner, "Use", ev)) return true;
    if (TrySendEventToFsm(spawner, "Pick", ev)) return true;
    return TrySendEventToFsm(spawner, null, ev);
}

        private GameObject FindSpawnedNear(string expectedName, GameObject spawner)
        {
            if (string.IsNullOrEmpty(expectedName)) return null;

            // Обычно в игре имена вида "foo(Clone)" - для поиска используем префикс без "(Clone)".
            string prefix = expectedName.Replace("(Clone)", "").Trim();

            Vector3 playerPos = PlayerPos();
            Vector3 anchorPos = spawner != null ? spawner.transform.position : playerPos;
            Vector3 palletPos = _pallet != null ? _pallet.transform.position : anchorPos;

            float r = Mathf.Max(0.5f, FindRadius.Value);

            GameObject best = null;
            float bestD = float.MaxValue;

            // 1) рядом со спавнером/пиком (обычно предмет появляется тут)
            var nearAnchor = FindNearestByPrefix(prefix, anchorPos, r * 2.0f);
            if (nearAnchor != null)
            {
                best = nearAnchor;
                bestD = Vector3.Distance(playerPos, nearAnchor.transform.position);
            }

            // 2) рядом с игроком (если предмет уже лежит рядом)
            var nearPlayer = FindNearestByPrefix(prefix, playerPos, r * 12.0f);
            if (nearPlayer != null)
            {
                float d = Vector3.Distance(playerPos, nearPlayer.transform.position);
                if (d < bestD)
                {
                    best = nearPlayer;
                    bestD = d;
                }
            }

            // 3) рядом с паллетой (иногда спавн/складка происходят около нее)
            var nearPallet = FindNearestByPrefix(prefix, palletPos, r * 12.0f);
            if (nearPallet != null)
            {
                float d = Vector3.Distance(playerPos, nearPallet.transform.position);
                if (d < bestD)
                {
                    best = nearPallet;
                    bestD = d;
                }
            }

            if (DebugLog.Value)
            {
                Log(string.Format("FindSpawned: name={0} anchor={1} found={2} dist={3:0.00}",
                    expectedName,
                    spawner != null ? spawner.name : "null",
                    best != null ? best.name : "null",
                    best != null ? bestD : 0f));
            }

            return best;
        }

        private IEnumerator AssembleIntoTray(GameObject tray, GameObject part, string triggerPath, string partName)
        {
            if (tray == null || part == null) { Log("Auto: missing " + partName + " or tray"); yield break; }

            // Ищем триггер внутри лотка по имени (последний сегмент пути), так менее хрупко к иерархии
            string triggerName = triggerPath;
            int slash = triggerName.LastIndexOf('/');
            if (slash >= 0) triggerName = triggerName.Substring(slash + 1);

            Transform trg = tray.transform.Find(triggerPath);
            if (trg == null) trg = FindChildDeep(tray.transform, triggerName);
            if (trg == null) { Log("Auto: tray trigger not found: " + triggerName); yield break; }

            // Перемещаем ТОЛЬКО деталь в триггер (лоток двигать сюда же - ломает из-за того, что триггер дочерний)
            Vector3 pos = trg.position + new Vector3(0f, TeleportYOffset.Value, 0f);
            yield return GlideTo(part, pos, trg.rotation, 0.20f, 10);
            yield return new WaitForSeconds(Mathf.Max(0.05f, PostEventWaitSec.Value));

            // Важно: у деталей (charger/manual) FSM обычно на самой детали: Contents -> events: ASSEMBLE.
            // Если не отправить ASSEMBLE, деталь просто останется лежать в триггере и не защелкнется.
            // Be permissive with event names / FSM names across different game & mod versions.
            TrySendEventToAnyFsmWithEvent(part, "ASSEMBLE", "Contents", "Use", "Assembly");
            TrySendEventToAnyFsmWithEvent(part, "PROCEED",  "Contents", "Use", "Assembly");
            TrySendEventToAnyFsmWithEvent(part, "FINISHED", "Contents", "Use", "Assembly");

            // Если телепортировали предмет внутрь триггера, физика может не дать OnTriggerEnter.
            // Поэтому дополнительно пуляем TRIGGER ENTER/LOOP, если FSM их содержит.
            TrySendEventToAnyFsmWithEvent(trg.gameObject, "TRIGGER ENTER", "Assembly");
            TrySendEventToAnyFsmWithEvent(trg.gameObject, "LOOP", "Assembly");
            TrySendEventToAnyFsmWithEvent(trg.gameObject, "PROCEED",  "Contents", "Use", "Assembly");
            TrySendEventToAnyFsmWithEvent(trg.gameObject, "ASSEMBLE", "Contents", "Use", "Assembly");
            TrySendEventToAnyFsmWithEvent(trg.gameObject, "FINISHED", "Contents", "Use", "Assembly");

            TrySendEventToAnyFsmWithEvent(tray, "PROCEED",  "Contents", "Use", "Assembly");
            TrySendEventToAnyFsmWithEvent(tray, "ASSEMBLE", "Contents", "Use", "Assembly");
            TrySendEventToAnyFsmWithEvent(tray, "FINISHED", "Contents", "Use", "Assembly");

            yield return new WaitForSeconds(Mathf.Max(0.05f, PostEventWaitSec.Value));

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
            yield return new WaitForSeconds(Mathf.Max(0.05f, PostEventWaitSec.Value));

            // Be permissive: different versions may require different events / FSM names.
            // Телепорт внутрь триггера может не дать OnTriggerEnter, поэтому явно дергаем TRIGGER ENTER/LOOP.
            TrySendEventToAnyFsmWithEvent(trg.gameObject, "TRIGGER ENTER", "Contents", "Use", "Assembly");
            TrySendEventToAnyFsmWithEvent(trg.gameObject, "LOOP", "Contents", "Use", "Assembly");
TrySendEventToAnyFsmWithEvent(trg.gameObject, "PROCEED",  "Contents", "Use", "Assembly");
TrySendEventToAnyFsmWithEvent(trg.gameObject, "ASSEMBLE", "Contents", "Use", "Assembly");
TrySendEventToAnyFsmWithEvent(trg.gameObject, "FINISHED", "Contents", "Use", "Assembly");

TrySendEventToAnyFsmWithEvent(tray, "PROCEED",  "Contents", "Use", "Assembly");
TrySendEventToAnyFsmWithEvent(tray, "ASSEMBLE", "Contents", "Use", "Assembly");
TrySendEventToAnyFsmWithEvent(tray, "FINISHED", "Contents", "Use", "Assembly");

TrySendEventToAnyFsmWithEvent(pack, "PROCEED",  "Contents", "Use", "Assembly");
TrySendEventToAnyFsmWithEvent(pack, "ASSEMBLE", "Contents", "Use", "Assembly");
TrySendEventToAnyFsmWithEvent(pack, "FINISHED", "Contents", "Use", "Assembly");

yield return new WaitForSeconds(Mathf.Max(0.05f, PostEventWaitSec.Value));

        }

        private IEnumerator FoldPackage(GameObject pack)
        {
            if (pack == null) yield break;

            // В твоих логах Stage=0 не двигается, потому что ты пытался фолдить "пустой" пакет.
            // После вставки лотка Stage должен начать меняться.
            int stageBefore = GetStage(pack);

            // Try a realistic "spam F" pattern: USE then FOLD1..FOLD6 then FINISHED.
            SendEventToFsm(pack, "Use", "USE");
            yield return new WaitForSeconds(Mathf.Max(0.05f, PostEventWaitSec.Value));

            for (int i = 1; i <= 6; i++)
            {
                SendEventToFsm(pack, "Use", "FOLD" + i);
                yield return new WaitForSeconds(0.08f);
            }

            SendEventToFsm(pack, "Use", "FINISHED");
            yield return new WaitForSeconds(Mathf.Max(0.05f, PostEventWaitSec.Value));

            int stageAfter = GetStage(pack);
            Log(string.Format("Auto: fold stage {0} -> {1}", stageBefore, stageAfter));

            // Fallback: if still stuck, just spam USE a bit (some PlayMaker graphs advance on USE only)
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
                // Try re-cache once
                CacheFactoryRefs();
            }

            if (_palletTrigger == null)
            {
                Log("Auto: pallet TriggerBox not found (PalletPackagesPlayer/TriggerBox). Skipping deliver.");
                yield break;
            }

            Vector3 pos = _palletTrigger.position;
            pos.y += TeleportYOffset.Value;
            yield return GlideTo(pack, pos, _palletTrigger.rotation, 0.35f, 14);

            // Some versions count delivery only if trigger-style events are fired.
            SendEventToFsm(_palletTrigger.gameObject, "Assembly", "TRIGGER ENTER");
            yield return new WaitForSeconds(0.05f);
            SendEventToFsm(_palletTrigger.gameObject, "Assembly", "PROCEED");
            yield return new WaitForSeconds(0.05f);
            SendEventToFsm(_palletTrigger.gameObject, "Assembly", "FINISHED");
            yield return new WaitForSeconds(Mathf.Max(0.05f, PostEventWaitSec.Value));
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
                yield return new WaitForSeconds(Mathf.Max(0.05f, PostEventWaitSec.Value));
            }
            finally { _busy = false; }
        }

        private IEnumerator UsePackageLooked()
{
    if (_busy) yield break;
    _busy = true;

    try
    {
        var looked = PickLookTarget(AimRayDist.Value);
        if (looked == null)
        {
            Log("UsePackage: no looked object.");
            yield break;
        }

        // Если луч попал в коробку/что-то большое, пытаемся найти ближайший package рядом с игроком.
        if (!IsPackage(looked))
        {
            var nearestPack = FindNearestByPrefix("package(Clone)", PlayerPos(), FindRadius.Value * 6f);
            if (nearestPack != null)
            {
                Log($"UsePackage: looked is not package(Clone): {looked.name} -> using nearest {nearestPack.name}");
                looked = nearestPack;
            }
            else
            {
                Log($"UsePackage: looked is not package(Clone): {looked.name}");
                yield break;
            }
        }

        // Fold package
        yield return SendEventThenWait(looked, "Use", "PROCEED", StepWaitSec.Value);
    }
    finally
    {
        _busy = false;
    }
}

        private IEnumerator DumpLookedFsms()
        {
            _busy = true;
            try
            {
                var looked = PickLookTarget(AimRayDist.Value);
                if (looked == null) { Log("Looked: none"); yield break; }

                // Чтобы было удобно дебажить коробки/стопки - мапим их в Pick* как и в Proceed.
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
        private Vector3 PlayerPos()
        {
            var cam = Camera.main;
            if (cam != null) return cam.transform.position;
            return Vector3.zero;
        }

        private void SendEventToFsm(GameObject go, string fsmName, string ev)
        {
            TrySendEventToFsm(go, fsmName, ev);
        }

        private bool TrySendEventToFsm(GameObject go, string fsmName, string ev)
        {
            if (go == null) return false;

            var fsms = go.GetComponents<PlayMakerFSM>();
            if (fsms == null || fsms.Length == 0) return false;

            // 1) пробуем точное имя FSM (если задано)
            if (!string.IsNullOrEmpty(fsmName))
            {
                for (int i = 0; i < fsms.Length; i++)
                {
                    var f = fsms[i];
                    if (f == null) continue;
                    if (!string.Equals(f.FsmName, fsmName, StringComparison.OrdinalIgnoreCase)) continue;
                    try { f.SendEvent(ev); }
                    catch (Exception e) { Logger.LogError(e); }
                    return true;
                }
            }

            // 2) fallback: первый FSM на объекте
            try { fsms[0].SendEvent(ev); }
            catch (Exception e) { Logger.LogError(e); }
            return true;
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
                    if (string.Equals(e.Name, ev, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private bool TrySendEventToAnyFsmWithEvent(GameObject go, string ev, params string[] preferredFsmNames)
        {
            if (go == null) return false;

            var fsms = go.GetComponents<PlayMakerFSM>();
            if (fsms == null || fsms.Length == 0) return false;

            // 1) prefer by FSM name, if provided and the FSM declares the event
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

                        try { f.SendEvent(ev); }
                        catch (Exception e) { Logger.LogError(e); }
                        return true;
                    }
                }
            }

            // 2) any FSM that declares the event
            for (int i = 0; i < fsms.Length; i++)
            {
                var f = fsms[i];
                if (f == null) continue;
                if (!FsmHasEvent(f, ev)) continue;

                try { f.SendEvent(ev); }
                catch (Exception e) { Logger.LogError(e); }
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

                // Most common var name in MSC/MWC graphs
                var vi = fsm.FsmVariables.FindFsmInt("Stage");
                if (vi != null) return vi.Value;

                // fallback: any int named stage
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
            var fsms = go.GetComponents<PlayMakerFSM>();
            if (fsms == null) return null;

            for (int i = 0; i < fsms.Length; i++)
            {
                var f = fsms[i];
                if (f == null) continue;
                if (string.Equals(f.FsmName, fsmName, StringComparison.OrdinalIgnoreCase))
                    return f;
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

        private static bool IsName(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private GameObject ResolveSpawnerAlias(GameObject looked)
{
    if (looked == null) return null;

    string n = looked.name ?? "";
    Vector3 near = PlayerPos();
    const float r = 150f;

    // Берем ближайший АКТИВНЫЙ, чтобы не попасть в неактивный LOD/дубликат.
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
            // GameObject.Find doesn't return inactive objects; Resources scan does.
            try
            {
                Transform[] all = Resources.FindObjectsOfTypeAll<Transform>();
                for (int i = 0; i < all.Length; i++)
                {
                    var tr = all[i];
                    if (tr == null) continue;
                    if (!string.Equals(tr.name, name, StringComparison.Ordinal)) continue;

                    // Ignore prefabs/assets (best-effort; some UnityEngine builds do not expose GameObject.scene)
                    if (tr.gameObject.hideFlags != HideFlags.None && tr.gameObject.hideFlags != HideFlags.HideInHierarchy) continue;
                    return tr.gameObject;
                }
            }
            catch { }

            return GameObject.Find(name);
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

private static bool IsValidActive(GameObject go)
{
    return go != null && go.activeInHierarchy;
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

                if (!go.scene.IsValid()) continue;
                if (!go.activeInHierarchy) continue;

                if (!go.name.StartsWith(nameOrPrefix, StringComparison.Ordinal)) continue;

                float d = Vector3.Distance(around, go.transform.position);
                if (d <= radius && d < best)
                {
                    best = d;
                    bestGo = go;
                }
            }

            return bestGo;
        }


        private GameObject FindNearestByName(string exactName, Vector3 around, float radius)
        {
            if (string.IsNullOrEmpty(exactName)) return null;

            float best = float.MaxValue;
            GameObject bestGo = null;

            // FindObjectsOfType иногда не видит часть объектов (LOD/активность/особенности сцены).
            // Resources.FindObjectsOfTypeAll надежнее для поиска по имени в сцене.
            var all = Resources.FindObjectsOfTypeAll<Transform>();
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null) continue;

                var go = t.gameObject;
                if (go == null) continue;

                // отсекаем ассеты и не-сценовые объекты
                if (!go.scene.IsValid()) continue;

                // для нашей логики нужны только реально существующие объекты сцены
                if (!go.activeInHierarchy) continue;

                if (!string.Equals(go.name, exactName, StringComparison.Ordinal)) continue;

                float d = Vector3.Distance(around, go.transform.position);
                if (d <= radius && d < best)
                {
                    best = d;
                    bestGo = go;
                }
            }

            return bestGo;
        }

        private float NearestByNameDist(string objName, Vector3 around)
        {
            float best = 9999f;

            try
            {
                GameObject[] all = GameObject.FindObjectsOfType<GameObject>();
                for (int i = 0; i < all.Length; i++)
                {
                    var go = all[i];
                    if (go == null) continue;
                    if (!string.Equals(go.name, objName, StringComparison.OrdinalIgnoreCase)) continue;

                    float d = Vector3.Distance(go.transform.position, around);
                    if (d < best) best = d;
                }
            }
            catch { }

            return best;
        }

        private IEnumerator GlideTo(GameObject go, Vector3 targetPos, Quaternion targetRot, float lift = 0.25f, int steps = 10)
        {
            if (go == null) yield break;

            // Rigidbody может сидеть на дочернем объекте - берём и там тоже.
            var rb = go.GetComponent<Rigidbody>() ?? go.GetComponentInChildren<Rigidbody>();
            if (rb != null)
            {
                // Для триггеров важна физика: гарантируем, что объект не кинематический и коллизии включены.
                rb.isKinematic = false;
                rb.detectCollisions = true;
                rb.WakeUp();
            }

            // Teleporting straight into triggers is flaky. Move from above into the trigger over several fixed steps.
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

                // Помогаем физике быстрее увидеть изменение трансформа.
                SyncTransformsCompat();

                yield return new WaitForFixedUpdate();
            }

            SyncTransformsCompat();
        }


        // Unity 5.0 в MWC может не иметь Physics.SyncTransforms().
        // Делаем совместимый вызов через reflection (если метода нет - просто ничего не делаем).
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
            catch
            {
                // ignore
            }
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

                    // Помогаем физике обновить триггеры/коллайдеры после перемещения.
                    try { SyncTransformsCompat(); } catch { /* older Unity */ }
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
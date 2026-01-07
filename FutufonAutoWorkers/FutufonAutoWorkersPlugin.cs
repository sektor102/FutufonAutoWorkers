using System;
using System.Collections;
using BepInEx;
using HutongGames.PlayMaker;
using UnityEngine;

[BepInPlugin("com.futufon.autoworker", "Futufon AutoWorker", "1.1.3")]
public class FutufonAutoWorkersPlugin : BaseUnityPlugin
{
    private bool _busy;

    private void Awake()
    {
        Logger.LogInfo("[AutoWorker] Loaded");
        Logger.LogInfo("[AutoWorker] DLL=" + System.Reflection.Assembly.GetExecutingAssembly().Location);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F9) && !_busy)
            StartCoroutine(TakeOne());

        if (Input.GetKeyDown(KeyCode.F10))
            DumpNearestBox();
    }

    private IEnumerator TakeOne()
    {
        _busy = true;

        try
        {
            GameObject box;
            PlayMakerFSM fsm;
            FindNearestChargersBoxUseFsm(out box, out fsm);

            if (box == null || fsm == null)
            {
                Logger.LogWarning("[AutoWorker] No usable chargers box found");
                yield break;
            }

            int itemsBefore = GetFsmInt(fsm, "Items");
            bool openBefore = GetFsmBool(fsm, "Open");

            Logger.LogInfo("[AutoWorker] Target=" + box.name +
                           " dist=" + Vector3.Distance(GetPlayerPos(), box.transform.position).ToString("0.00") +
                           " state=" + fsm.ActiveStateName +
                           " items=" + itemsBefore +
                           " open=" + openBefore);

            // 1) Доводим до Wait button (это значит реальный фокус/луч/дистанция)
            bool gotWaitButton = false;
            yield return Run(EnsureWaitButton(box, fsm, 12, 1.05f, r => gotWaitButton = r));

            Logger.LogInfo("[AutoWorker] FocusResult state=" + fsm.ActiveStateName + " gotWaitButton=" + gotWaitButton);

            if (!gotWaitButton)
            {
                Logger.LogWarning("[AutoWorker] Still not Wait button - skip");
                yield break;
            }

            // 2) Если коробка закрыта - сначала открываем
            bool openNow = GetFsmBool(fsm, "Open");
            if (!openNow)
            {
                Logger.LogInfo("[AutoWorker] Box closed - send PROCEED to open");
                yield return Run(SendProceedAndWait(fsm));

                // ждём, чтобы Open реально обновился
                yield return null;
                yield return null;

                bool openAfter = GetFsmBool(fsm, "Open");
                Logger.LogInfo("[AutoWorker] Open after=" + openAfter + " state=" + fsm.ActiveStateName);

                // если так и не открылось - это уже сигнал, что фокус не удержался
                if (!openAfter)
                {
                    Logger.LogWarning("[AutoWorker] Open still false - try refocus once");
                    gotWaitButton = false;
                    yield return Run(EnsureWaitButton(box, fsm, 8, 1.05f, r => gotWaitButton = r));
                    if (gotWaitButton)
                    {
                        Logger.LogInfo("[AutoWorker] Retry open PROCEED");
                        yield return Run(SendProceedAndWait(fsm));
                        yield return null;
                        yield return null;
                        Logger.LogInfo("[AutoWorker] Open after retry=" + GetFsmBool(fsm, "Open"));
                    }
                }
            }

            // 3) Теперь берём зарядку
            int beforeTake = GetFsmInt(fsm, "Items");
            Logger.LogInfo("[AutoWorker] Take charger - items before=" + beforeTake);

            yield return Run(SendProceedAndWait(fsm));

            yield return null;
            yield return null;

            int afterTake = GetFsmInt(fsm, "Items");
            Logger.LogInfo("[AutoWorker] Take result - items " + beforeTake + " -> " + afterTake + " state=" + fsm.ActiveStateName);

            if (beforeTake != -1 && afterTake != -1 && afterTake >= beforeTake)
                Logger.LogWarning("[AutoWorker] PROCEED had no effect (focus lost or wrong camera/raycast)");
        }
        finally
        {
            _busy = false;
        }
    }

    // ----------------- Focus: делаем реально Wait button -----------------

    private IEnumerator EnsureWaitButton(GameObject targetGo, PlayMakerFSM fsm, int attempts, float standDist, Action<bool> setResult)
    {
        setResult(false);

        Transform player = FindPlayer();
        if (player == null)
        {
            Logger.LogWarning("[AutoWorker] PLAYER not found");
            yield break;
        }

        Camera cam = FindPlayerCamera(player);
        if (cam == null)
        {
            Logger.LogWarning("[AutoWorker] Player camera not found");
            yield break;
        }

        Collider col = targetGo.GetComponent<Collider>();
        Vector3 center = (col != null) ? col.bounds.center : targetGo.transform.position;

        float dist = standDist;

        for (int i = 0; i < attempts; i++)
        {
            // телепорт рядом
            TeleportNear(player, center, targetGo.transform, dist);

            // наводим камеру
            AimCameraAt(cam, center);

            // даём пару кадров чтобы MousePickEvent/луч отработал
            yield return null;
            yield return null;

            bool hitOk = RayHitsTarget(cam, col, targetGo);
            string st = fsm.ActiveStateName;

            Logger.LogInfo("[AutoWorker] AimTry " + (i + 1) + "/" + attempts + " hitOk=" + hitOk + " state=" + st);

            // важно: нам нужно именно Wait button
            if (st == "Wait button" && hitOk)
            {
                // стабилизируем 2 кадра подряд
                yield return null;
                yield return null;

                if (fsm.ActiveStateName == "Wait button")
                {
                    setResult(true);
                    yield break;
                }
            }

            dist = Mathf.Clamp(dist + 0.12f, 0.8f, 1.6f);
        }

        Logger.LogWarning("[AutoWorker] Could not reach Wait button. state=" + fsm.ActiveStateName);
    }

    private IEnumerator SendProceedAndWait(PlayMakerFSM fsm)
    {
        int before = GetFsmInt(fsm, "Items");
        Logger.LogInfo("[AutoWorker] SEND PROCEED state=" + fsm.ActiveStateName + " items=" + before);

        // 1) обычный SendEvent
        try { fsm.SendEvent("PROCEED"); }
        catch (Exception e) { Logger.LogWarning("[AutoWorker] SendEvent failed: " + e); }

        yield return null;
        yield return null;

        int after1 = GetFsmInt(fsm, "Items");
        Logger.LogInfo("[AutoWorker] After SendEvent state=" + fsm.ActiveStateName + " items=" + before + "->" + after1);

        // 2) fallback через Fsm.Event
        if (before != -1 && after1 != -1 && after1 < before)
            yield break;

        try
        {
            if (fsm != null && fsm.Fsm != null)
                fsm.Fsm.Event(FsmEvent.GetFsmEvent("PROCEED"));
        }
        catch (Exception e) { Logger.LogWarning("[AutoWorker] Fsm.Event failed: " + e); }

        yield return null;
        yield return null;

        int after2 = GetFsmInt(fsm, "Items");
        Logger.LogInfo("[AutoWorker] After Fsm.Event state=" + fsm.ActiveStateName + " items=" + before + "->" + after2);
    }

    // ----------------- Find box + utils -----------------

    private void FindNearestChargersBoxUseFsm(out GameObject box, out PlayMakerFSM fsm)
    {
        box = null;
        fsm = null;

        Transform player = FindPlayer();
        Vector3 p = (player != null) ? player.position : Vector3.zero;

        UnityEngine.Object[] all = UnityEngine.Object.FindObjectsOfType(typeof(PlayMakerFSM));
        if (all == null) return;

        float best = float.MaxValue;

        for (int i = 0; i < all.Length; i++)
        {
            PlayMakerFSM pm = all[i] as PlayMakerFSM;
            if (pm == null) continue;
            if (!pm.enabled) continue;

            if (pm.FsmName != "Use") continue;

            GameObject go = pm.gameObject;
            if (go == null) continue;

            string n = go.name;
            if (n == null) continue;
            if (n.IndexOf("chargers box", StringComparison.OrdinalIgnoreCase) < 0) continue;

            float d = (player != null) ? Vector3.Distance(p, go.transform.position) : 0f;
            if (d < best)
            {
                best = d;
                box = go;
                fsm = pm;
            }
        }
    }

    private void DumpNearestBox()
    {
        GameObject box;
        PlayMakerFSM fsm;
        FindNearestChargersBoxUseFsm(out box, out fsm);

        if (box == null || fsm == null)
        {
            Logger.LogWarning("[AutoWorker] No chargers box candidate found");
            return;
        }

        Logger.LogInfo("[AutoWorker] Dump box=" + box.name);
        Logger.LogInfo("[AutoWorker] FSM name=" + fsm.FsmName + " state=" + fsm.ActiveStateName +
                       " items=" + GetFsmInt(fsm, "Items") + " open=" + GetFsmBool(fsm, "Open"));

        if (fsm.Fsm != null && fsm.Fsm.States != null)
        {
            for (int i = 0; i < fsm.Fsm.States.Length; i++)
            {
                var st = fsm.Fsm.States[i];
                if (st == null || st.Transitions == null) continue;

                for (int t = 0; t < st.Transitions.Length; t++)
                {
                    var tr = st.Transitions[t];
                    if (tr == null) continue;
                    if (st.Name == "Wait button")
                        Logger.LogInfo("[AutoWorker]   " + st.Name + " --[" + tr.EventName + "]-> " + tr.ToState);
                }
            }
        }
    }

    private IEnumerator Run(IEnumerator e)
    {
        while (e != null && e.MoveNext())
            yield return e.Current;
    }

    private Transform FindPlayer()
    {
        GameObject go = GameObject.Find("PLAYER");
        if (go != null) return go.transform;

        go = GameObject.FindWithTag("Player");
        return (go != null) ? go.transform : null;
    }

    private Vector3 GetPlayerPos()
    {
        Transform p = FindPlayer();
        return (p != null) ? p.position : Vector3.zero;
    }

    private Camera FindPlayerCamera(Transform player)
    {
        Camera[] cams = player.GetComponentsInChildren<Camera>(true);
        for (int i = 0; i < cams.Length; i++)
        {
            Camera c = cams[i];
            if (c == null) continue;
            if (!c.enabled) continue;
            if (!c.gameObject.activeInHierarchy) continue;
            return c;
        }

        return Camera.main;
    }

    private int GetFsmInt(PlayMakerFSM fsm, string name)
    {
        try
        {
            var ints = fsm.FsmVariables.IntVariables;
            for (int i = 0; i < ints.Length; i++)
                if (ints[i] != null && ints[i].Name == name)
                    return ints[i].Value;
        }
        catch { }
        return -1;
    }

    private bool GetFsmBool(PlayMakerFSM fsm, string name)
    {
        try
        {
            var b = fsm.FsmVariables.BoolVariables;
            for (int i = 0; i < b.Length; i++)
                if (b[i] != null && b[i].Name == name)
                    return b[i].Value;
        }
        catch { }
        return false;
    }

    private void TeleportNear(Transform player, Vector3 targetCenter, Transform targetTf, float dist)
    {
        Vector3 dir = -targetTf.forward;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.01f)
        {
            dir = (player.position - targetCenter);
            dir.y = 0f;
        }

        if (dir.sqrMagnitude < 0.01f)
            dir = Vector3.back;

        dir.Normalize();

        Vector3 newPos = targetCenter + dir * dist;
        newPos.y = player.position.y;
        player.position = newPos;
    }

    private void AimCameraAt(Camera cam, Vector3 targetCenter)
    {
        Transform t = cam.transform;
        Vector3 dir = (targetCenter - t.position);
        if (dir.sqrMagnitude < 0.0001f) return;

        t.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
    }

    private bool RayHitsTarget(Camera cam, Collider col, GameObject go)
    {
        Ray ray = new Ray(cam.transform.position, cam.transform.forward);

        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 3.0f))
        {
            if (col != null && hit.collider == col) return true;
            if (hit.collider != null && hit.collider.gameObject == go) return true;
            if (hit.collider != null && hit.collider.transform.IsChildOf(go.transform)) return true;
        }
        return false;
    }
}

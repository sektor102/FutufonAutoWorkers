using System;
using System.Collections;
using BepInEx;
using HutongGames.PlayMaker;
using UnityEngine;

[BepInPlugin("com.futufon.autoworker", "Futufon AutoWorker", "1.1.2")]
public class FutufonAutoWorkersPlugin : BaseUnityPlugin
{
    private const string BUILD = "1112-A";
    private bool _busy;

    private void Awake()
    {
        Logger.LogInfo("[AutoWorker] BUILD=" + BUILD);
        Logger.LogInfo("[AutoWorker] DLL=" + System.Reflection.Assembly.GetExecutingAssembly().Location);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F9) && !_busy)
            StartCoroutine(TakeChargerOnce());

        if (Input.GetKeyDown(KeyCode.F10))
            DumpNearestBox();
    }

    private IEnumerator TakeChargerOnce()
    {
        _busy = true;

        GameObject box;
        PlayMakerFSM fsm;
        FindNearestChargersBoxUseFsm(out box, out fsm);

        if (box == null || fsm == null)
        {
            Logger.LogWarning("[AutoWorker] No usable chargers box found");
            _busy = false;
            yield break;
        }

        Logger.LogInfo("[AutoWorker] Target=" + box.name + " state=" + fsm.ActiveStateName + " items=" + GetFsmInt(fsm, "Items"));

        // 1) ѕытаемс€ принудительно перевести в Wait button (без взгл€да)
        if (fsm.ActiveStateName != "Wait button")
        {
            Logger.LogInfo("[AutoWorker] FORCE: SetState(Wait button) try");
            ForceSetState(fsm, "Wait button");
            yield return null;
            yield return null;
            Logger.LogInfo("[AutoWorker] FORCE: after state=" + fsm.ActiveStateName);
        }

        // 2) ≈сли всЄ равно не Wait button - тогда уже пробуем "донаведение" камерой
        if (fsm.ActiveStateName != "Wait button")
        {
            Logger.LogInfo("[AutoWorker] ENTER TryMakeWaitButton");
            yield return Run(TryMakeWaitButton(fsm, box));
        }

        Logger.LogInfo("[AutoWorker] Ready state=" + fsm.ActiveStateName + " items=" + GetFsmInt(fsm, "Items"));

        if (fsm.ActiveStateName == "Wait button")
        {
            Logger.LogInfo("[AutoWorker] ENTER SendProceedSmart");
            yield return Run(SendProceedSmart(fsm));
        }
        else
        {
            Logger.LogWarning("[AutoWorker] Still not Wait button, skip PROCEED");
        }

        Logger.LogInfo("[AutoWorker] After state=" + fsm.ActiveStateName + " items=" + GetFsmInt(fsm, "Items"));
        _busy = false;
    }

    private void ForceSetState(PlayMakerFSM pmFsm, string stateName)
    {
        try
        {
            if (pmFsm == null) return;

            var f = pmFsm.Fsm;
            if (f == null) return;

            var t = f.GetType();

            // 1) пробуем SetState(string)
            var m = t.GetMethod("SetState", new[] { typeof(string) });
            if (m != null)
            {
                Logger.LogInfo("[AutoWorker] FORCE: SetState(string) found");
                m.Invoke(f, new object[] { stateName });
                return;
            }

            // 2) пробуем ChangeState(string)
            m = t.GetMethod("ChangeState", new[] { typeof(string) });
            if (m != null)
            {
                Logger.LogInfo("[AutoWorker] FORCE: ChangeState(string) found");
                m.Invoke(f, new object[] { stateName });
                return;
            }

            // 3) пробуем ChangeState(FsmState) или SetState(FsmState)
            var st = f.GetState(stateName);
            if (st != null)
            {
                m = t.GetMethod("ChangeState", new[] { typeof(HutongGames.PlayMaker.FsmState) });
                if (m != null)
                {
                    Logger.LogInfo("[AutoWorker] FORCE: ChangeState(FsmState) found");
                    m.Invoke(f, new object[] { st });
                    return;
                }

                m = t.GetMethod("SetState", new[] { typeof(HutongGames.PlayMaker.FsmState) });
                if (m != null)
                {
                    Logger.LogInfo("[AutoWorker] FORCE: SetState(FsmState) found");
                    m.Invoke(f, new object[] { st });
                    return;
                }
            }

            Logger.LogWarning("[AutoWorker] FORCE: no SetState/ChangeState method on this PlayMaker build");
        }
        catch (Exception e)
        {
            Logger.LogWarning("[AutoWorker] ForceSetState failed: " + e);
        }
    }


    private IEnumerator SendProceedSmart(PlayMakerFSM f)
    {
        int before = GetFsmInt(f, "Items");
        Logger.LogInfo("[AutoWorker] SEND: SendEvent(PROCEED)");

        try { f.SendEvent("PROCEED"); }
        catch (Exception e) { Logger.LogWarning("[AutoWorker] SendEvent failed: " + e); }

        yield return null;
        yield return null;

        int after1 = GetFsmInt(f, "Items");
        Logger.LogInfo("[AutoWorker] After SendEvent items=" + before + "->" + after1);

        // fallback
        if (before != -1 && after1 != -1 && after1 < before)
            yield break;

        Logger.LogInfo("[AutoWorker] SEND: Fsm.Event(PROCEED) fallback");
        try
        {
            if (f != null && f.Fsm != null)
                f.Fsm.Event(HutongGames.PlayMaker.FsmEvent.GetFsmEvent("PROCEED"));
        }
        catch (Exception e) { Logger.LogWarning("[AutoWorker] Fsm.Event failed: " + e); }

        yield return null;
        yield return null;

        int after2 = GetFsmInt(f, "Items");
        Logger.LogInfo("[AutoWorker] After Fsm.Event items=" + before + "->" + after2);
    }

    private IEnumerator TryMakeWaitButton(PlayMakerFSM fsm, GameObject targetGo)
    {
        Transform player = FindPlayer();
        if (player == null)
        {
            Logger.LogWarning("[AutoWorker] Player not found");
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

        float dist = 1.05f;

        for (int i = 0; i < 10; i++)
        {
            TeleportNear(player, center, targetGo.transform, dist);
            AimCameraAt(cam, center);

            yield return null;
            yield return null;

            Logger.LogInfo("[AutoWorker] AimTry " + (i + 1) + " state=" + fsm.ActiveStateName);

            if (fsm.ActiveStateName == "Wait button")
                yield break;

            dist = Mathf.Clamp(dist + 0.12f, 0.8f, 1.6f);
        }

        Logger.LogWarning("[AutoWorker] Could not reach Wait button (still " + fsm.ActiveStateName + ")");
    }

    private IEnumerator Run(IEnumerator e)
    {
        while (e != null && e.MoveNext())
            yield return e.Current;
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

        PlayMakerFSM[] fsms = box.GetComponents<PlayMakerFSM>();
        for (int i = 0; i < fsms.Length; i++)
        {
            PlayMakerFSM f = fsms[i];
            if (f == null) continue;

            Logger.LogInfo("[AutoWorker] FSM name=" + f.FsmName + " state=" + f.ActiveStateName + " items=" + GetFsmInt(f, "Items"));
            DumpStateTransitions(f, "Wait button");
        }
    }

    private void FindNearestChargersBoxUseFsm(out GameObject box, out PlayMakerFSM fsm)
    {
        box = null;
        fsm = null;

        Transform player = FindPlayer();
        if (player == null) return;

        PlayMakerFSM[] all = FindObjectsOfType<PlayMakerFSM>();
        PlayMakerFSM best = null;
        float bestDist = float.MaxValue;

        for (int i = 0; i < all.Length; i++)
        {
            PlayMakerFSM x = all[i];
            if (x == null) continue;
            if (x.FsmName != "Use") continue;

            string n = x.gameObject.name;
            if (n == null) continue;
            if (n.IndexOf("chargers box") < 0) continue;

            string st = x.ActiveStateName;
            if (st != "Wait player" && st != "Wait button") continue;

            float d = Vector3.Distance(player.position, x.transform.position);
            if (d < bestDist)
            {
                bestDist = d;
                best = x;
            }
        }

        if (best == null) return;
        box = best.gameObject;
        fsm = best;
    }

    private Transform FindPlayer()
    {
        GameObject p = GameObject.Find("PLAYER");
        if (p != null) return p.transform;

        GameObject byTag = GameObject.FindWithTag("Player");
        if (byTag != null) return byTag.transform;

        return null;
    }

    private Camera FindPlayerCamera(Transform player)
    {
        if (player != null)
        {
            Camera[] cams = player.GetComponentsInChildren<Camera>(true);
            for (int i = 0; i < cams.Length; i++)
            {
                Camera c = cams[i];
                if (c != null && c.enabled && c.gameObject.activeInHierarchy)
                    return c;
            }
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

    private void DumpStateTransitions(PlayMakerFSM fsm, string stateName)
    {
        try
        {
            if (fsm == null || fsm.Fsm == null) return;
            var st = fsm.Fsm.GetState(stateName);
            if (st == null || st.Transitions == null) return;

            for (int i = 0; i < st.Transitions.Length; i++)
            {
                var tr = st.Transitions[i];
                if (tr == null) continue;
                Logger.LogInfo("[AutoWorker]   " + stateName + " --[" + tr.EventName + "]-> " + tr.ToState);
            }
        }
        catch { }
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
}

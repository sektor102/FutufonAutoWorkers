using System;
using System.Collections;
using BepInEx.Logging;
using HutongGames.PlayMaker;
using UnityEngine;

static class FsmFocusHelpers
{
    static void DumpFsmTransitions(PlayMakerFSM pm, BepInEx.Logging.ManualLogSource log)
    {
        if (pm?.Fsm == null) return;

        foreach (var st in pm.Fsm.States)
        {
            if (st?.Transitions == null) continue;
            foreach (var tr in st.Transitions)
            {
                log.LogInfo($"[FSM] {st.Name} --[{tr.EventName}]-> {tr.ToState}");
            }
        }
    }
    public static IEnumerator EnsureWaitButtonThenProceed(
        PlayMakerFSM fsm,
        GameObject targetGo,
        ManualLogSource log,
        int aimAttempts = 12,
        float standDist = 1.1f,
        float timeoutSec = 0.6f)
    {
        if (fsm == null || targetGo == null) yield break;

        var col = targetGo.GetComponent<Collider>();
        var center = col ? col.bounds.center : targetGo.transform.position;

        // 1) Найти игрока и камеру
        var player = GameObject.Find("PLAYER")?.transform
                     ?? GameObject.FindWithTag("Player")?.transform;

        var cam = Camera.main ?? UnityEngine.Object.FindObjectOfType<Camera>();

        if (player == null || cam == null)
        {
            log.LogWarning("[AutoWorker] Player or Camera not found - cannot focus.");
            yield break;
        }

        // 2) Если уже Wait button - сразу жмем
        if (fsm.ActiveStateName == "Wait button")
        {
            log.LogInfo("[AutoWorker] Already Wait button - sending PROCEED");
            SendProceed(fsm, log);
            yield break;
        }

        // 3) Пытаемся "сделать как игрок": встать рядом + смотреть точно в центр коллайдера
        for (int i = 0; i < aimAttempts; i++)
        {
            TeleportNear(player, center, targetGo.transform, standDist);
            yield return new WaitForEndOfFrame(); // важно - после апдейта камеры/контроллера
            AimCameraAt(cam, center);

            // Проверим, что реально попали лучом в этот объект (или его коллайдер)
            bool hitOk = RayHitsTarget(cam, col, targetGo);
            log.LogInfo($"[AutoWorker] AimTry {i + 1}/{aimAttempts}: hitOk={hitOk}, state={fsm.ActiveStateName}");

            // Дадим FSM пару кадров чтобы MousePickEvent отработал
            yield return null;
            yield return null;

            if (fsm.ActiveStateName == "Wait button")
                break;

            // Мелкая коррекция позиции, если не получилось
            standDist = Mathf.Clamp(standDist + 0.12f, 0.8f, 1.6f);
        }

        if (fsm.ActiveStateName != "Wait button")
        {
            log.LogWarning($"[AutoWorker] Could not reach Wait button. Current={fsm.ActiveStateName}. Skip PROCEED.");
            yield break;
        }

        // 4) Теперь можно безопасно слать PROCEED
        log.LogInfo("[AutoWorker] Reached Wait button - sending PROCEED");
        SendProceed(fsm, log);

        // 5) Не спамим: дождемся, что FSM отработала цикл (Pick item -> обратно)
        yield return WaitUntilStateChangesBack(fsm, log, timeoutSec);
    }

    static void SendProceed(PlayMakerFSM pm, ManualLogSource log)
    {
        try
        {
            pm.SendEvent("PROCEED");
            log.LogInfo("[AutoWorker] SendEvent(PROCEED) ok");
        }
        catch (Exception e)
        {
            log.LogWarning("[AutoWorker] SendEvent(PROCEED) failed: " + e.Message);
        }

        try
        {
            if (pm != null && pm.Fsm != null)
            {
                pm.Fsm.Event(HutongGames.PlayMaker.FsmEvent.GetFsmEvent("PROCEED"));
                log.LogInfo("[AutoWorker] Fsm.Event(PROCEED) ok");
            }
        }
        catch (Exception e)
        {
            log.LogWarning("[AutoWorker] Fsm.Event(PROCEED) failed: " + e.Message);
        }
    }

    static void TeleportNear(Transform player, Vector3 targetCenter, Transform targetTf, float dist)
    {
        // Вектор "куда встать" - лучше относительно forward объекта, но с фоллбеком
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

        var newPos = targetCenter + dir * dist;
        newPos.y = player.position.y; // сохраняем высоту, чтобы не провалиться/не подпрыгнуть

        player.position = newPos;
    }

    static void AimCameraAt(Camera cam, Vector3 targetCenter)
    {
        var t = cam.transform;
        var dir = (targetCenter - t.position);
        if (dir.sqrMagnitude < 0.0001f) return;

        var rot = Quaternion.LookRotation(dir.normalized, Vector3.up);
        t.rotation = rot;
    }
    static bool RayHitsTarget(Camera cam, Collider col, GameObject go)
    {
        var ray = new Ray(cam.transform.position, cam.transform.forward);

        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, 3.0f))
        {
            if (col != null && hit.collider == col) return true;
            if (hit.collider != null && hit.collider.gameObject == go) return true;
            if (hit.collider != null && hit.collider.transform.IsChildOf(go.transform)) return true;
        }
        return false;
    }

    static IEnumerator WaitUntilStateChangesBack(PlayMakerFSM fsm, ManualLogSource log, float timeoutSec)
    {
        float t = 0f;
        // Обычно после PROCEED будет "Pick item", потом вернется в Wait button/Wait player
        while (t < timeoutSec)
        {
            var st = fsm.ActiveStateName;
            if (st == "Wait button" || st == "Wait player")
                yield break;

            t += Time.deltaTime;
            yield return null;
        }

        log.LogInfo($"[AutoWorker] Timeout waiting FSM to settle. state={fsm.ActiveStateName}");
    }

}

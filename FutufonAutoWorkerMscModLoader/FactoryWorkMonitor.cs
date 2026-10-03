using System;
using System.Globalization;
using HutongGames.PlayMaker;
using UnityEngine;

namespace FutufonAutoWorkerMscModLoader
{
    // Observe the game's decision. Never write IsWorking, AtWork or idle counters.
    internal sealed class FactoryWorkMonitor
    {
        private readonly Action<string> _log;
        private PlayMakerFSM _logic;
        private PlayMakerFSM _playerData;
        private float _nextPoll;
        private float? _previousIdle;
        private string _lastRecord;
        internal WorkCheckState State { get; private set; }
        internal string Status { get; private set; }
        internal bool AtWork { get; private set; }
        internal float? IdleMinutes { get; private set; }

        internal FactoryWorkMonitor(Action<string> log)
        {
            _log = log;
            State = WorkCheckState.Unavailable;
            Status = WorkCheckStatus.Text(State);
        }

        internal void Poll()
        {
            if (Time.realtimeSinceStartup < _nextPoll) return;
            _nextPoll = Time.realtimeSinceStartup + 0.5f;
            if (_logic == null) _logic = FindFsm("CheckSlacking", "Logic");
            if (_playerData == null) _playerData = FindFsm("FACTORY", "PlayerData");
            bool active = _logic != null && _logic.enabled && _logic.gameObject.activeInHierarchy && _logic.Fsm.Active;
            var atWork = _playerData == null ? null : _playerData.FsmVariables.FindFsmBool("AtWork");
            var idle = _playerData == null ? null : _playerData.FsmVariables.FindFsmFloat("SlackMinutesAllTime");
            bool increasing = idle != null && _previousIdle.HasValue && idle.Value > _previousIdle.Value + 0.0001f;
            bool clockedIn = atWork != null && atWork.Value;
            AtWork = clockedIn;
            IdleMinutes = idle == null ? (float?)null : idle.Value;
            string nativeState = active ? _logic.ActiveStateName : "unavailable";
            State = WorkCheckStatus.Read(active && atWork != null, clockedIn, nativeState, increasing);
            Status = WorkCheckStatus.Text(State);
            if (idle != null) Status += " | Idle counter: " + idle.Value.ToString("F2", CultureInfo.InvariantCulture);
            string record = State + "|" + clockedIn + "|" + (idle == null ? "?" : idle.Value.ToString("F1", CultureInfo.InvariantCulture));
            if (record != _lastRecord)
            {
                var pulse = active ? _logic.FsmVariables.FindFsmBool("IsWorking") : null;
                _log("WORK CHECK: " + Status + "; state=" + nativeState + " AtWork=" + clockedIn +
                    " IsWorkingPulse=" + (pulse == null ? "unknown" : pulse.Value.ToString()));
                _lastRecord = record;
            }
            _previousIdle = idle == null ? (float?)null : idle.Value;
        }

        private static PlayMakerFSM FindFsm(string objectName, string fsmName)
        {
            var owner = GameObject.Find(objectName);
            if (owner == null) return null;
            foreach (var component in owner.GetComponents(typeof(PlayMakerFSM)))
            {
                var fsm = component as PlayMakerFSM;
                if (fsm != null && fsm.FsmName == fsmName) return fsm;
            }
            return null;
        }
    }
}

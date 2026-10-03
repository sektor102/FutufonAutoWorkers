using System;
using HutongGames.PlayMaker;
using UnityEngine;

namespace FutufonAutoWorkerMscModLoader
{
    internal sealed class FactoryTelemetry
    {
        internal readonly int[] Stock = new int[4];
        internal bool NearFactory { get; private set; }
        internal int FreePalletSlots { get; private set; }
        internal int PalletCapacity { get; private set; }
        private readonly PlayMakerFSM[] _sources = new PlayMakerFSM[4];
        private PlayMakerFSM _clock, _database;
        private readonly ShiftReminders _reminders = new ShiftReminders();
        private float _nextPoll;
        private readonly Action<ShiftNotice> _notice;
        private readonly Action<string> _log;
        private static readonly string[] Names = { "PickChargers", "PickManuals", "PickTrays", "PickSheets" };

        internal FactoryTelemetry(Action<ShiftNotice> notice, Action<string> log) { _notice = notice; _log = log; }
        internal void Poll(bool notificationsEnabled, bool atWork, bool workerActive)
        {
            if (Time.realtimeSinceStartup < _nextPoll) return;
            _nextPoll = Time.realtimeSinceStartup + 0.5f;
            NearFactory = false;
            for (int i = 0; i < Names.Length; i++)
            {
                if (_sources[i] == null) _sources[i] = FindFsm(Names[i], "Use");
                var source = _sources[i];
                var package = source == null ? null : source.FsmVariables.FindFsmGameObject("Package");
                var container = package == null || package.Value == null ? null : FindFsm(package.Value, "Use");
                var stock = container == null ? null : container.FsmVariables.FindFsmInt("Items");
                Stock[i] = container != null && container.gameObject.activeInHierarchy && stock != null ? Math.Max(0, stock.Value) : 0;
                if (source != null && Camera.main != null && Vector3.Distance(source.transform.position, Camera.main.transform.position) < 40f)
                    NearFactory = true;
            }
            FreePalletSlots = PalletCapacity = 0;
            if (NearFactory)
                foreach (var item in UnityEngine.Object.FindObjectsOfType(typeof(PlayMakerFSM)))
                {
                    var pallet = item as PlayMakerFSM;
                    if (pallet == null || pallet.gameObject.name != "TriggerBox" || pallet.FsmName != "Assembly" || !pallet.Fsm.Active) continue;
                    bool playerPallet = false;
                    for (var node = pallet.transform; node != null; node = node.parent)
                        if (node.name == "PalletPackagesPlayer") { playerPallet = true; break; }
                    if (!playerPallet || Vector3.Distance(pallet.transform.position, Camera.main.transform.position) > 40f) continue;
                    var slot = pallet.FsmVariables.FindFsmInt("Slot");
                    if (slot == null) continue;
                    int capacity = FactoryDemo.CompareLimit(pallet, "Insert box", "Slot");
                    PalletCapacity += capacity;
                    FreePalletSlots += Math.Max(0, capacity - slot.Value);
                }
            if (_clock == null) _clock = FindFsm("FACTORY", "Clock");
            if (_database == null) _database = FindFsm("FACTORY", "Database");
            var hour = _clock == null ? null : _clock.FsmVariables.FindFsmFloat("TimeHourF");
            var lunch = _database == null ? null : _database.FsmVariables.FindFsmInt("Lunchbreak");
            var home = _database == null ? null : _database.FsmVariables.FindFsmBool("Home");
            var day = FsmVariables.GlobalVariables.FindFsmInt("GlobalDay");
            if (hour != null && _clock.Fsm.Active)
            {
                var notice = _reminders.Observe(day == null ? 0 : day.Value, hour.Value, lunch != null && lunch.Value == 1,
                    home != null && home.Value, notificationsEnabled && NearFactory && (atWork || workerActive));
                if (notice != ShiftNotice.None)
                {
                    _log("SHIFT NOTICE: " + notice + "; native hour=" + hour.Value);
                    _notice(notice);
                }
            }
        }
        private static PlayMakerFSM FindFsm(string ownerName, string name)
        {
            var owner = GameObject.Find(ownerName);
            return owner == null ? null : FindFsm(owner, name);
        }
        private static PlayMakerFSM FindFsm(GameObject owner, string name)
        {
            foreach (var component in owner.GetComponents<PlayMakerFSM>()) if (component.FsmName == name) return component;
            return null;
        }
    }
}

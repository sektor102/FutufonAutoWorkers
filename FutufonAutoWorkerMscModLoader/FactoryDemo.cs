using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HutongGames.PlayMaker;
using UnityEngine;

namespace FutufonAutoWorkerMscModLoader
{
    internal sealed class FactoryDemo
    {
        internal const int BatchSize = 44;
        private readonly Action<string> _log;
        private readonly Action<string> _status;
        private readonly Func<float> _paceSeconds;
        private readonly List<HeldBody> _heldBodies = new List<HeldBody>();
        private readonly List<HeldBody> _shippingBodies = new List<HeldBody>();
        private readonly Supply[] _supplies =
        {
            new Supply("PickChargers", "charger(Clone)", "Contents", "Pick item", -0.76f),
            new Supply("PickManuals", "manual(Clone)", "Contents", "Pick item", -0.244f),
            new Supply("PickTrays", "plastic tray(Clone)", "Contents", "State 8", 0.19f),
            new Supply("PickSheets", "package(Clone)", "Use", "State 8", 0.64f)
        };
        private Camera _camera;
        private Collider _table;
        private Vector3 _cameraStart;
        private Vector3 _origin;
        private Vector3 _forward;
        private Vector3 _right;
        private Quaternion _rotation;
        private Vector3 _workSurface;
        private Vector3 _packageSurface;
        private Vector3 _shippingSurface;
        private PlayMakerFSM _shippingSource;
        private GameObject _shippingBox;
        private PlayMakerFSM _shipping;
        private PlayMakerFSM _pallet;
        private GameObject _palletSlot;
        private GameObject _issuedItem;

        internal FactoryDemo(Action<string> log, Action<string> status, Func<float> paceSeconds)
        {
            _log = log;
            _status = status;
            _paceSeconds = paceSeconds;
        }

        internal bool PlayerLeft()
        {
            return _camera == null || _table == null || Vector3.Distance(_camera.transform.position, _cameraStart) > 2f;
        }

        internal void Prepare()
        {
            _camera = Camera.main;
            if (_camera == null) throw new InvalidOperationException("Player camera not found.");
            RaycastHit hit;
            if (!Physics.Raycast(_camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)), out hit, 2.5f) ||
                hit.normal.y < 0.8f || !HasTableAncestor(hit.collider.transform))
                throw new InvalidOperationException("Look down at a clear factory table, then press F8.");

            _table = hit.collider;
            _cameraStart = _camera.transform.position;
            PrepareTableFrame(hit.point.y);
            // Factory models use local +Z as their top. Their XY plane belongs on the table.
            _rotation = Quaternion.LookRotation(_forward, Vector3.up) * Quaternion.Euler(-90f, 0f, 0f);

            foreach (var supply in _supplies)
            {
                var sourceObject = GameObject.Find(supply.SourceName);
                if (sourceObject == null || Vector3.Distance(sourceObject.transform.position, _origin) > 40f)
                    throw new InvalidOperationException("Factory supplies not found nearby: " + supply.SourceName);
                supply.Source = GetFsm(sourceObject, "Use");
                RequireState(supply.Source, "Check old");
                supply.Container = RequiredObject(supply.Source, "Package");
                supply.Use = GetFsm(supply.Container, "Use");
                RequiredInt(supply.Use, "Items");
                RequireState(supply.Use, supply.PickState);
                if (supply.SourceName == "PickChargers" || supply.SourceName == "PickManuals")
                    RequireState(supply.Use, "Open box");
                supply.Position = SurfaceAt(supply.Offset, 0.105f);
                CheckSupplyFootprint(supply);
            }
            _workSurface = SurfaceAt(0f, -0.23f);
            _packageSurface = SurfaceAt(-0.52f, -0.23f);
            foreach (float side in new[] { -0.7f, 0.32f })
                foreach (float forward in new[] { -0.35f, -0.12f }) SurfaceAt(side, forward);
            var shippingSource = GameObject.Find("PickBoxes");
            if (shippingSource == null || Vector3.Distance(shippingSource.transform.position, _origin) > 40f)
                throw new InvalidOperationException("Shipping box supply not found nearby.");
            _shippingSource = GetFsm(shippingSource, "Use");
            RequireState(_shippingSource, "Check old");
            _shippingBox = RequiredObject(_shippingSource, "Package");
            var shippingTrigger = RequiredObject(GetFsm(_shippingBox, "Data"), "Trigger");
            _shipping = GetFsm(shippingTrigger, "Assembly");
            RequireState(_shipping, "Assemble");
            if (CompareLimit(_shipping, "Check full", "TotalPackages") != BatchSize)
                throw new InvalidOperationException("Unexpected shipping box capacity. Expected 44.");
            if (_shippingBox.activeInHierarchy) ValidateShippingCounts();
            _shippingSurface = FindShippingSurface();
            SelectPallet(); // Check available space before consuming any materials.

            // Finished items and unfinished work must be cleared before another batch.
            foreach (var item in UnityEngine.Object.FindObjectsOfType(typeof(PlayMakerFSM)))
            {
                var fsm = item as PlayMakerFSM;
                if (fsm == null || !IsLoosePart(fsm.gameObject.name)) continue;
                if (Vector3.Distance(fsm.transform.position, _workSurface) < 0.35f ||
                    Vector3.Distance(fsm.transform.position, _packageSurface) < 0.3f)
                    throw new InvalidOperationException("Clear finished boxes and unfinished parts from this table before starting another batch.");
            }
            _log("CYCLE prepared at " + _origin + "; table=" + _table.name + "; capacity=" + BatchSize +
                "; shipping=" + DescribeObject(_shippingBox) + "; pallet=" + _pallet.GetInstanceID());
        }

        internal IEnumerator Run()
        {
            try
            {
                yield return PrepareShippingBox();
                int initialCount = RequiredInt(_shipping, "TotalPackages").Value;
                if (initialCount < BatchSize)
                    for (int i = 0; i < _supplies.Length; i++) yield return PrepareSupply(_supplies[i]);
                for (int box = initialCount; box < BatchSize; box++)
                {
                    _status("Box " + (box + 1) + "/" + BatchSize + ": collecting parts");
                    if (!_shippingBox.activeInHierarchy || RequiredInt(_shipping, "TotalPackages").Value != box)
                        throw new InvalidOperationException("Shipping box became unavailable or its contents changed during the cycle.");
                    _log("CYCLE begin package " + (box + 1) + "/44");
                    var work = _workSurface;
                    yield return Issue(_supplies[2], work);
                    var tray = _issuedItem;
                    yield return Issue(_supplies[0], work + _right * 0.23f);
                    var charger = _issuedItem;
                    yield return Issue(_supplies[1], work - _right * 0.23f);
                    var manual = _issuedItem;
                    yield return Issue(_supplies[3], _packageSurface);
                    var package = _issuedItem;
                    var contents = GetFsm(tray, "Contents");
                    var use = GetFsm(package, "Use");

                    _status("Box " + (box + 1) + "/" + BatchSize + ": assembling tray");
                    yield return Assemble(tray, "TriggerCharger", charger, contents, "Chager", "Mould");
                    yield return Assemble(tray, "TriggerManual", manual, contents, "Manual", "Mould");

                    _status("Box " + (box + 1) + "/" + BatchSize + ": folding package");
                    RequireState(use, "State 1");
                    if (RequiredInt(use, "Stage").Value != 0)
                        throw new InvalidOperationException("A new package did not start at Stage 0.");
                    for (int fold = 1; fold <= 5; fold++)
                    {
                        int expected = fold;
                        DispatchState(use, "State 1");
                        yield return WaitFor(() => RequiredInt(use, "Stage").Value == expected, "fold " + fold);
                        yield return Pace();
                    }

                    _status("Box " + (box + 1) + "/" + BatchSize + ": packing and closing");
                    var triggerObject = RequiredObject(use, "TriggerMould");
                    yield return Assemble(package, triggerObject.name, tray, use, "Mould", "ThisPackage");
                    yield return WaitFor(() => RequiredBool(use, "Charger").Value && RequiredBool(use, "Manual").Value,
                        "package contents copied from tray");
                    DispatchState(use, "State 1");
                    yield return WaitFor(() => RequiredInt(use, "Stage").Value == 4, "closed package");
                    yield return Pace();
                    if (!RequiredBool(use, "Charger").Value || !RequiredBool(use, "Manual").Value || !RequiredBool(use, "Mould").Value)
                        throw new InvalidOperationException("The finished package is missing a component.");

                    _status("Shipping box: " + (box + 1) + "/44 - inserting package");
                    yield return PackShippingBox(package, use);
                    ReleaseBodies();
                    _log("CYCLE PACKED " + (box + 1) + "/44; EmptyPackages=0");
                    yield return Pace();
                }
                yield return DeliverShippingBox();
                _status("DONE: 44/44 packed and delivered to the pallet. F8 starts the next box.");
                _log("CYCLE COMPLETE: one shipping box with 44 complete packages delivered. No next cycle started.");
            }
            finally
            {
                ReleaseBodies();
                ReleaseBodies(_shippingBodies);
            }
        }

        private IEnumerator PrepareShippingBox()
        {
            _status("Preparing shipping box and checking pallet space");
            if (!_shippingBox.activeInHierarchy)
            {
                DispatchState(_shippingSource, "Check old");
                yield return WaitFor(() => _shippingBox.activeInHierarchy && _shipping.Fsm.Active &&
                    _shipping.ActiveStateName == "Check package" && RequiredInt(_shipping, "TotalPackages").Value == 0,
                    "new shipping box initialization");
            }
            ValidateShippingCounts();
            HoldBodies(_shippingBox, _shippingBodies);
            yield return PlaceOnSurface(_shippingBox, _shippingSurface);
            _log("CYCLE shipping box ready: TotalPackages=" + RequiredInt(_shipping, "TotalPackages").Value +
                " EmptyPackages=0. Existing complete packages will be retained.");
        }

        private IEnumerator PackShippingBox(GameObject package, PlayMakerFSM contents)
        {
            ValidateShippingCounts();
            int before = RequiredInt(_shipping, "TotalPackages").Value;
            if (!_shippingBox.activeInHierarchy || before >= BatchSize)
                throw new InvalidOperationException("Shipping box is unavailable or already full.");
            if (RequiredInt(contents, "Stage").Value != 4 || !RequiredBool(contents, "Charger").Value ||
                !RequiredBool(contents, "Manual").Value || !RequiredBool(contents, "Mould").Value)
                throw new InvalidOperationException("Only closed and complete packages can be shipped.");
            string packageId = DescribeObject(package);
            RequiredGameObject(_shipping, "Part").Value = package;
            DispatchState(_shipping, "Assemble");
            yield return WaitFor(() => package == null && RequiredInt(_shipping, "TotalPackages").Value == before + 1 &&
                RequiredInt(_shipping, "EmptyPackages").Value == 0 && RequiredBool(_shipping, "ContentOK").Value,
                "shipping package " + (before + 1));
            yield return WaitFor(() => _shipping.ActiveStateName == (before + 1 == BatchSize ? "Close box" : "Check package"),
                "shipping box settling after insertion");
            _log("CYCLE inserted " + packageId + "; ContentOK=True TotalPackages=" + (before + 1) + " EmptyPackages=0");
        }

        private IEnumerator DeliverShippingBox()
        {
            ValidateShippingCounts();
            if (RequiredInt(_shipping, "TotalPackages").Value != BatchSize || _shipping.ActiveStateName != "Close box")
                throw new InvalidOperationException("Shipping box is not closed at 44 packages.");
            SelectPallet(); // Recheck in case the reserved pallet filled during assembly.
            int slotBefore = RequiredInt(_pallet, "Slot").Value;
            var job = GetFsm(RequiredObject(_pallet, "JobData"), "JobProgress");
            int totalBefore = RequiredInt(job, "PackagesTotal").Value;
            int emptyBefore = RequiredInt(job, "PackagesEmpty").Value;
            if (totalBefore > int.MaxValue - BatchSize) throw new InvalidOperationException("Job counter cannot accept another box.");
            _status("44/44 complete - delivering to pallet");
            _shippingBox.transform.position = _palletSlot.transform.position + Vector3.up * 0.2f;
            StopMotion(_shippingBox);
            // Dispatch in the same frame as the move, before collision handlers can replace Part.
            RequiredGameObject(_pallet, "Part").Value = _shippingBox;
            DispatchState(_pallet, "Assemble");
            yield return WaitFor(() => !_shippingBox.activeInHierarchy && _palletSlot.activeInHierarchy &&
                RequiredInt(_pallet, "Slot").Value == slotBefore + 1 &&
                RequiredInt(job, "PackagesTotal").Value == totalBefore + BatchSize &&
                RequiredInt(job, "PackagesEmpty").Value == emptyBefore, "pallet receipt and job accounting");
            _log("CYCLE DELIVERED: pallet=" + _pallet.GetInstanceID() + " slot=" + (slotBefore + 1) +
                " Job.PackagesTotal=" + totalBefore + "->" + (totalBefore + BatchSize) +
                " Job.PackagesEmpty=" + emptyBefore + " (unchanged)");
        }

        private void ValidateShippingCounts()
        {
            int total = RequiredInt(_shipping, "TotalPackages").Value;
            int empty = RequiredInt(_shipping, "EmptyPackages").Value;
            if (total < 0 || total > BatchSize || empty != 0)
                throw new InvalidOperationException("Shipping box has invalid or incomplete contents. Total=" + total + " Empty=" + empty);
        }

        private void SelectPallet()
        {
            _pallet = null;
            _palletSlot = null;
            float nearest = float.MaxValue;
            foreach (var component in UnityEngine.Object.FindObjectsOfType(typeof(PlayMakerFSM)))
            {
                var candidate = component as PlayMakerFSM;
                if (candidate == null || candidate.gameObject.name != "TriggerBox" || candidate.FsmName != "Assembly" ||
                    !HasNamedAncestor(candidate.transform, "PalletPackagesPlayer") || !candidate.Fsm.Active) continue;
                float distance = Vector3.Distance(candidate.transform.position, _origin);
                if (distance > 40f || distance >= nearest) continue;
                int slot = RequiredInt(candidate, "Slot").Value;
                int capacity = CompareLimit(candidate, "Insert box", "Slot");
                if (slot < 0 || slot >= capacity) continue;
                var target = GetSlotObject(candidate, slot + 1);
                if (target == null || target.activeSelf) continue;
                RequireState(candidate, "Assemble");
                _pallet = candidate;
                _palletSlot = target;
                nearest = distance;
            }
            if (_pallet == null) throw new InvalidOperationException("No free player pallet slot nearby. Clear a pallet before F8.");
        }

        private static GameObject GetSlotObject(PlayMakerFSM pallet, int index)
        {
            foreach (var component in pallet.gameObject.GetComponents(typeof(MonoBehaviour)))
            {
                if (component == null || component.GetType().Name != "PlayMakerArrayListProxy") continue;
                if (!string.Equals(ReadMember(component, "referenceName") as string, "Slots", StringComparison.Ordinal)) continue;
                var slots = ReadMember(component, "arrayList") as IList;
                if (slots == null || index >= slots.Count) return null;
                return slots[index] as GameObject;
            }
            throw new InvalidOperationException("Player pallet Slots array is unavailable.");
        }

        private static object ReadMember(object target, string name)
        {
            var type = target.GetType();
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
            if (field != null) return field.GetValue(target);
            var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            return property == null ? null : property.GetValue(target, null);
        }

        private static int CompareLimit(PlayMakerFSM fsm, string stateName, string variable)
        {
            RequireState(fsm, stateName);
            foreach (var action in fsm.Fsm.GetState(stateName).Actions)
            {
                if (action == null || action.GetType().Name != "IntCompare") continue;
                var left = ReadMember(action, "integer1") as FsmInt;
                var right = ReadMember(action, "integer2") as FsmInt;
                if (left != null && right != null && left.Name == variable && !right.UseVariable) return right.Value;
            }
            throw new InvalidOperationException("Capacity comparison not found: " + fsm.gameObject.name + " / " + variable);
        }

        private Vector3 FindShippingSurface()
        {
            foreach (float side in new[] { 0.9f, -0.9f, 1.3f, -1.3f })
                foreach (float forward in new[] { 0.1f, -0.5f })
                {
                    Vector3 position = _cameraStart + _right * side + _forward * forward;
                    RaycastHit hit;
                    if (Physics.Raycast(position + Vector3.up * 0.2f, Vector3.down, out hit, 3f) &&
                        hit.normal.y >= 0.8f && hit.point.y < _origin.y - 0.3f && !HasTableAncestor(hit.collider.transform))
                        return hit.point;
                }
            throw new InvalidOperationException("No floor space beside the table for the shipping box.");
        }

        private IEnumerator PrepareSupply(Supply supply)
        {
            _status("Preparing " + supply.SourceName);
            if (!supply.Container.activeInHierarchy)
            {
                DispatchState(supply.Source, "Check old");
                yield return WaitFor(() => supply.Container.activeInHierarchy, "supply pickup " + supply.SourceName);
                yield return WaitFor(() => supply.Use.Fsm.Active && RequiredInt(supply.Use, "Items").Value > 0,
                    "supply initialization " + supply.SourceName);
                _log("CYCLE supply picked up: " + DescribeObject(supply.Container));
            }
            if (RequiredInt(supply.Use, "Items").Value <= 0)
                throw new InvalidOperationException("An active supply box has no items: " + supply.SourceName);
            yield return PlaceOnSurface(supply.Container, supply.Position);
            if (supply.SourceName == "PickChargers" || supply.SourceName == "PickManuals")
            {
                if (!RequiredBool(supply.Use, "Open").Value)
                {
                    DispatchState(supply.Use, "Open box");
                    yield return WaitFor(() => RequiredBool(supply.Use, "Open").Value, "opening " + supply.SourceName);
                    yield return Pace();
                }
            }
            _log("CYCLE supply ready: " + supply.SourceName + " Items=" + RequiredInt(supply.Use, "Items").Value);
        }

        private IEnumerator Issue(Supply supply, Vector3 position)
        {
            if (!supply.Container.activeInHierarchy) yield return PrepareSupply(supply);
            int before = RequiredInt(supply.Use, "Items").Value;
            if (before <= 0) throw new InvalidOperationException("No stock in " + supply.SourceName);
            var known = ItemIds(supply.ItemName, supply.ItemFsm);
            DispatchState(supply.Use, supply.PickState);
            GameObject created = null;
            yield return WaitFor(() =>
            {
                created = FindNewItem(supply.ItemName, supply.ItemFsm, known);
                return created != null && RequiredInt(supply.Use, "Items").Value == before - 1;
            }, "issuing " + supply.ItemName);
            HoldBodies(created);
            yield return PlaceOnSurface(created, position);
            _issuedItem = created;
            _log("CYCLE issued " + DescribeObject(created) + " from " + supply.SourceName + " Items=" + (before - 1));
            yield return Pace();
        }

        private IEnumerator Assemble(GameObject owner, string triggerName, GameObject part, PlayMakerFSM result,
            string resultName, string ownerVariable)
        {
            var trigger = FindChildFsm(owner, triggerName, "Assembly");
            if (RequiredObject(trigger, ownerVariable) != owner)
                throw new InvalidOperationException("Assembly trigger belongs to another object: " + triggerName);
            if (RequiredBool(result, resultName).Value)
                throw new InvalidOperationException("Component is already installed: " + triggerName);
            RequireState(trigger, "Assemble");
            // Part is an input to the game's assembly actions. Completion flags are set by the game.
            RequiredGameObject(trigger, "Part").Value = part;
            DispatchState(trigger, "Assemble");
            yield return WaitFor(() => RequiredBool(result, resultName).Value && part != null &&
                part.transform.IsChildOf(owner.transform), "assembly " + triggerName);
            _log("CYCLE assembled " + DescribeObject(part) + " into " + DescribeObject(owner) + " via " + triggerName);
            yield return Pace();
        }

        private static IEnumerator WaitFor(Func<bool> predicate, string operation)
        {
            // Freshly created FSMs need at least one frame to run Start().
            yield return null;
            float deadline = Time.time + 5f;
            while (!predicate())
            {
                if (Time.time >= deadline) throw new InvalidOperationException("Timeout: " + operation);
                yield return null;
            }
        }

        private IEnumerator Pace()
        {
            // Read the slider for every new step, so changes also apply mid-cycle.
            float until = Time.time + _paceSeconds();
            do { yield return null; } while (Time.time < until);
        }

        private static void DispatchState(PlayMakerFSM fsm, string stateName)
        {
            RequireState(fsm, stateName);
            if (!fsm.enabled || !fsm.gameObject.activeInHierarchy || !fsm.Fsm.Active || fsm.Fsm.IsSwitchingState)
                throw new InvalidOperationException("FSM is not ready: " + fsm.gameObject.name + " / " + stateName);
            // A temporary local transition calls the existing state actions through the public API.
            // The original graph is restored before returning; nothing is written to the save graph.
            var original = fsm.Fsm.GlobalTransitions ?? new FsmTransition[0];
            var transition = new FsmTransition
            {
                FsmEvent = FsmEvent.GetFsmEvent("AUTOWORKER_CYCLE_STEP"),
                ToState = stateName
            };
            var temporary = new FsmTransition[original.Length + 1];
            Array.Copy(original, temporary, original.Length);
            temporary[original.Length] = transition;
            fsm.Fsm.GlobalTransitions = temporary;
            try
            {
                fsm.Fsm.Event((FsmEventTarget)null, transition.FsmEvent);
                fsm.Fsm.UpdateStateChanges();
            }
            finally { fsm.Fsm.GlobalTransitions = original; }
        }

        private Vector3 SurfaceAt(float side, float forward)
        {
            Vector3 point = _origin + _right * side + _forward * forward;
            RaycastHit hit;
            if (!_table.Raycast(new Ray(point + Vector3.up * 0.2f, Vector3.down), out hit, 0.4f) || hit.normal.y < 0.8f)
                throw new InvalidOperationException("Not enough table space. Look at the centre of a larger factory table.");
            return hit.point;
        }

        private void PrepareTableFrame(float surfaceY)
        {
            _origin = new Vector3(_table.bounds.center.x, surfaceY, _table.bounds.center.z);
            float length = 0f;
            var box = _table as BoxCollider;
            if (box != null)
            {
                var axes = new[] { Vector3.right, Vector3.up, Vector3.forward };
                var sizes = new[] { box.size.x, box.size.y, box.size.z };
                var scales = new[] { box.transform.lossyScale.x, box.transform.lossyScale.y, box.transform.lossyScale.z };
                for (int i = 0; i < axes.Length; i++)
                {
                    var axis = box.transform.TransformDirection(axes[i]);
                    float extent = Mathf.Abs(sizes[i] * scales[i]);
                    if (Mathf.Abs(axis.y) < 0.1f && extent > length)
                    { _right = Vector3.ProjectOnPlane(axis, Vector3.up).normalized; length = extent; }
                }
            }
            if (length > 0f)
            {
                _forward = Vector3.Cross(_right, Vector3.up).normalized;
                if (Vector3.Dot(_forward, _origin - _cameraStart) < 0f)
                { _right = -_right; _forward = -_forward; }
                // Anchor the workspace opposite the player, with room for the whole row at either end.
                float along = Vector3.Dot(_cameraStart - _origin, _right);
                float margin = Mathf.Max(0f, length * 0.5f - 1.05f);
                _origin += _right * Mathf.Clamp(along, -margin, margin);
            }
            else
            {
                _forward = Vector3.ProjectOnPlane(_camera.transform.forward, Vector3.up).normalized;
                if (_forward.sqrMagnitude < 0.5f) _forward = Vector3.ProjectOnPlane(_camera.transform.up, Vector3.up).normalized;
                _right = Vector3.Cross(Vector3.up, _forward).normalized;
            }
        }

        private void CheckSupplyFootprint(Supply supply)
        {
            var box = supply.Container.GetComponent<BoxCollider>();
            if (box == null) throw new InvalidOperationException("Supply collider not found: " + supply.SourceName);
            float halfWidth = Mathf.Abs(box.size.x * box.transform.lossyScale.x) * 0.5f;
            float halfDepth = Mathf.Abs(box.size.y * box.transform.lossyScale.y) * 0.5f;
            foreach (int side in new[] { -1, 1 })
                foreach (int forward in new[] { -1, 1 })
                    SurfaceAt(supply.Offset + side * (halfWidth + 0.01f), 0.105f + forward * (halfDepth + 0.01f));
        }

        private IEnumerator PlaceOnSurface(GameObject item, Vector3 surface)
        {
            item.transform.parent = null;
            item.transform.rotation = _rotation;
            item.transform.position = surface + Vector3.up * 0.2f;
            StopMotion(item);
            yield return null;
            float bottom = item.transform.position.y;
            bool found = false;
            foreach (var component in item.GetComponentsInChildren(typeof(Collider)))
            {
                var collider = component as Collider;
                if (collider == null || !collider.enabled || collider.isTrigger) continue;
                if (!found || collider.bounds.min.y < bottom) bottom = collider.bounds.min.y;
                found = true;
            }
            Vector3 final = item.transform.position;
            final.y += surface.y + 0.01f - bottom;
            item.transform.position = final;
            StopMotion(item);
            yield return null;
        }

        private void HoldBodies(GameObject item)
        {
            HoldBodies(item, _heldBodies);
        }

        private static void HoldBodies(GameObject item, List<HeldBody> bodies)
        {
            foreach (var component in item.GetComponentsInChildren(typeof(Rigidbody)))
            {
                var body = component as Rigidbody;
                if (body == null) continue;
                bodies.Add(new HeldBody(body, body.isKinematic));
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }
        }

        private void ReleaseBodies()
        {
            ReleaseBodies(_heldBodies);
        }

        private static void ReleaseBodies(List<HeldBody> bodies)
        {
            foreach (var held in bodies)
            {
                if (held.Body == null) continue; // The game's ASSEMBLE action can destroy the Rigidbody.
                held.Body.isKinematic = held.WasKinematic;
                held.Body.velocity = Vector3.zero;
                held.Body.angularVelocity = Vector3.zero;
            }
            bodies.Clear();
        }

        private static void StopMotion(GameObject item)
        {
            foreach (var component in item.GetComponentsInChildren(typeof(Rigidbody)))
            {
                var body = component as Rigidbody;
                if (body == null) continue;
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        private static bool HasTableAncestor(Transform target)
        {
            for (var node = target; node != null; node = node.parent)
                if (node.name.IndexOf("table", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static bool HasNamedAncestor(Transform target, string name)
        {
            for (var node = target; node != null; node = node.parent)
                if (string.Equals(node.name, name, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool IsLoosePart(string name)
        {
            return name == "package(Clone)" || name == "plastic tray(Clone)" || name == "charger(Clone)" || name == "manual(Clone)";
        }

        private static HashSet<int> ItemIds(string itemName, string fsmName)
        {
            var result = new HashSet<int>();
            foreach (var fsm in MatchingItems(itemName, fsmName)) result.Add(fsm.gameObject.GetInstanceID());
            return result;
        }

        private static GameObject FindNewItem(string itemName, string fsmName, HashSet<int> known)
        {
            GameObject result = null;
            foreach (var fsm in MatchingItems(itemName, fsmName))
            {
                if (known.Contains(fsm.gameObject.GetInstanceID())) continue;
                if (result != null && result != fsm.gameObject)
                    throw new InvalidOperationException("More than one new item appeared: " + itemName);
                result = fsm.gameObject;
            }
            return result;
        }

        private static IEnumerable<PlayMakerFSM> MatchingItems(string itemName, string fsmName)
        {
            foreach (var item in UnityEngine.Object.FindObjectsOfType(typeof(PlayMakerFSM)))
            {
                var fsm = item as PlayMakerFSM;
                if (fsm != null && fsm.gameObject.name == itemName && fsm.FsmName == fsmName) yield return fsm;
            }
        }

        private static PlayMakerFSM GetFsm(GameObject owner, string name)
        {
            foreach (var component in owner.GetComponents(typeof(PlayMakerFSM)))
            {
                var fsm = component as PlayMakerFSM;
                if (fsm != null && fsm.FsmName == name) return fsm;
            }
            throw new InvalidOperationException("FSM not found: " + owner.name + " / " + name);
        }

        private static PlayMakerFSM FindChildFsm(GameObject owner, string objectName, string fsmName)
        {
            foreach (var component in owner.GetComponentsInChildren(typeof(PlayMakerFSM), true))
            {
                var fsm = component as PlayMakerFSM;
                if (fsm != null && fsm.gameObject.name == objectName && fsm.FsmName == fsmName) return fsm;
            }
            throw new InvalidOperationException("Assembly trigger not found: " + owner.name + " / " + objectName);
        }

        private static void RequireState(PlayMakerFSM fsm, string name)
        {
            if (fsm.Fsm.GetState(name) == null)
                throw new InvalidOperationException("Game state not found: " + fsm.gameObject.name + " / " + name);
        }

        private static FsmInt RequiredInt(PlayMakerFSM fsm, string name)
        {
            var value = fsm.FsmVariables.FindFsmInt(name);
            if (value == null) throw new InvalidOperationException("Missing integer: " + fsm.gameObject.name + " / " + name);
            return value;
        }

        private static FsmBool RequiredBool(PlayMakerFSM fsm, string name)
        {
            var value = fsm.FsmVariables.FindFsmBool(name);
            if (value == null) throw new InvalidOperationException("Missing boolean: " + fsm.gameObject.name + " / " + name);
            return value;
        }

        private static FsmGameObject RequiredGameObject(PlayMakerFSM fsm, string name)
        {
            var value = fsm.FsmVariables.FindFsmGameObject(name);
            if (value == null) throw new InvalidOperationException("Missing object variable: " + fsm.gameObject.name + " / " + name);
            return value;
        }

        private static GameObject RequiredObject(PlayMakerFSM fsm, string name)
        {
            var value = RequiredGameObject(fsm, name).Value;
            if (value == null) throw new InvalidOperationException("Empty object variable: " + fsm.gameObject.name + " / " + name);
            return value;
        }

        private static string DescribeObject(GameObject item) { return item.name + "#" + item.GetInstanceID(); }

        private sealed class Supply
        {
            internal readonly string SourceName, ItemName, ItemFsm, PickState;
            internal readonly float Offset;
            internal PlayMakerFSM Source, Use;
            internal GameObject Container;
            internal Vector3 Position;
            internal Supply(string source, string item, string fsm, string pickState, float offset)
            { SourceName = source; ItemName = item; ItemFsm = fsm; PickState = pickState; Offset = offset; }
        }

        private sealed class HeldBody
        {
            internal readonly Rigidbody Body;
            internal readonly bool WasKinematic;
            internal HeldBody(Rigidbody body, bool kinematic) { Body = body; WasKinematic = kinematic; }
        }
    }
}

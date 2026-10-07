using System;
using System.Collections.Generic;
using HutongGames.PlayMaker;
using UnityEngine;

namespace FutufonAutoWorkerMscModLoader
{
    // Native packing destroys the loose packages and moves a single four-package
    // row upwards. Fill the missing lower rows with mesh-only copies; never change
    // contents, mass, collision, save data or job accounting.
    internal sealed class CartonVisuals
    {
        private GameObject _box, _root;
        private readonly List<GameObject> _rows = new List<GameObject>();
        private int _lastCount = -1;
        internal void Poll(GameObject box, PlayMakerFSM assembly)
        {
            if (box == null || assembly == null || !box.activeInHierarchy || !assembly.Fsm.Active) return;
            // Let the native insertion/reset/restore finish before touching visuals.
            if (assembly.ActiveStateName != "Check package" && assembly.ActiveStateName != "Close box") return;
            var count = assembly.FsmVariables.FindFsmInt("TotalPackages");
            if (count == null || count.Value < 0 || count.Value > 44) return;
            if (_box != box) { Dispose(); _box = box; }
            int total = count.Value;
            if (_lastCount == total && _root != null) return;
            var level = box.transform.Find("PackageLevel");
            var top = level == null ? null : level.Find("package_row");
            var filter = top == null ? null : top.GetComponent<MeshFilter>();
            var renderer = top == null ? null : top.GetComponent<MeshRenderer>();
            if (filter == null || renderer == null) return;
            if (_root == null)
            {
                _root = new GameObject("AutoWorkerLowerRows");
                _root.transform.parent = box.transform;
                _root.transform.localPosition = Vector3.zero;
                _root.transform.localRotation = Quaternion.identity;
                _root.transform.localScale = Vector3.one;
            }
            int rows = CartonLayout.LowerRows(total);
            while (_rows.Count < rows)
            {
                var row = new GameObject("Row" + (_rows.Count + 1));
                row.transform.parent = _root.transform;
                row.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                row.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
                _rows.Add(row);
            }
            for (int i = 0; i < _rows.Count; i++)
            {
                // The native completed row is one level below the current slots.
                _rows[i].transform.localPosition = top.localPosition + Vector3.forward * (0.04f * (i + 1));
                _rows[i].transform.localRotation = top.localRotation;
                _rows[i].transform.localScale = top.localScale;
                _rows[i].SetActive(i < rows);
            }
            _root.SetActive(total < 44);
            // Restore the top row/partial slots from authoritative saved contents.
            // No numeric FSM variables are written here.
            level.gameObject.SetActive(true);
            top.gameObject.SetActive(total >= 4);
            for (int i = 1; i <= 4; i++)
            {
                var slot = level.Find("Slot" + i);
                if (slot != null) slot.gameObject.SetActive(i <= total % 4);
            }
            _lastCount = total;
        }
        internal void Dispose()
        {
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _rows.Clear(); _root = null; _box = null; _lastCount = -1;
        }
    }
}

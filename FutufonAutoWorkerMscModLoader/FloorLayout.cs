using System;
using System.Collections.Generic;
using UnityEngine;

namespace FutufonAutoWorkerMscModLoader
{
    internal static class FloorLayout
    {
        internal const int StackHeight = 4;
        internal const int StackCount = 11;
        internal const float PackageYaw = 90f;
        // Native closed package: 0.31 x 0.20 x 0.055 m, plus placement gaps and a small margin.
        internal static readonly Bounds TowerBounds = new Bounds(new Vector3(0f, 0.14f, 0f), new Vector3(0.22f, 0.28f, 0.33f));

        // Offsets use the workstation frame, anchored at the player's starting position.
        internal static IEnumerable<Vector3> StackOffsets()
        {
            foreach (float forward in new[] { -0.1f, -0.5f, -0.9f, -1.3f })
                foreach (float side in new[] { -0.65f, -0.95f, -1.25f, -1.55f })
                    yield return new Vector3(side, 0f, forward);
        }

        internal static IEnumerable<Vector3> ShippingOffsets()
        {
            foreach (float forward in new[] { -0.7f, -1f, -1.3f })
                foreach (float side in new[] { 0.9f, 1.3f, 1.7f })
                    yield return new Vector3(side, 0f, forward);
        }

        internal static Bounds ShippingBounds(GameObject carton, Quaternion rotation)
        {
            var bounds = ItemBounds(carton, rotation);
            Vector3 centre = bounds.center;
            centre.y -= bounds.min.y;
            return new Bounds(centre, bounds.size);
        }

        internal static Bounds ItemBounds(GameObject carton, Quaternion rotation)
        {
            // Include inactive meshes: open flaps and closed/full-carton geometry must fit.
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (var mesh in carton.GetComponentsInChildren<MeshFilter>(true))
                if (mesh.sharedMesh != null) IncludeBounds(ref bounds, mesh.sharedMesh.bounds, mesh.transform, carton.transform, rotation);
            foreach (var box in carton.GetComponentsInChildren<BoxCollider>(true))
                if (!box.isTrigger) IncludeBounds(ref bounds, new Bounds(box.center, box.size), box.transform, carton.transform, rotation);
            return bounds;
        }

        internal static Bounds WorldBounds(Bounds local, Vector3 right, Vector3 forward)
        {
            var result = new Bounds(right * local.center.x + Vector3.up * local.center.y + forward * local.center.z, Vector3.zero);
            foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 }) foreach (int z in new[] { -1, 1 })
            {
                Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3(x, y, z));
                result.Encapsulate(right * corner.x + Vector3.up * corner.y + forward * corner.z);
            }
            return result;
        }

        private static void IncludeBounds(ref Bounds result, Bounds local, Transform child, Transform root, Quaternion rotation)
        {
            foreach (int x in new[] { -1, 1 }) foreach (int y in new[] { -1, 1 }) foreach (int z in new[] { -1, 1 })
            {
                Vector3 point = local.center + Vector3.Scale(local.extents, new Vector3(x, y, z));
                Vector3 relative = root.InverseTransformPoint(child.TransformPoint(point));
                result.Encapsulate(rotation * Vector3.Scale(relative, root.lossyScale));
            }
        }

        internal static bool TrySurface(Vector3 position, float tableY,
            Bounds footprint, GameObject ignore, out Vector3 surface, Action<string> rejected = null)
        {
            surface = Vector3.zero;
            if (!TryFloor(position, tableY, ignore, out surface)) return Reject(rejected, "no fixed, level floor at centre");
            // Check the whole footprint, not just the centre ray. No table, ledge or loose box can support it.
            foreach (int x in new[] { -1, 1 }) foreach (int z in new[] { -1, 1 })
            {
                Vector3 corner = position + Vector3.right * (footprint.center.x + x * footprint.extents.x) +
                    Vector3.forward * (footprint.center.z + z * footprint.extents.z);
                Vector3 support;
                if (!TryFloor(corner, tableY, ignore, out support) || Mathf.Abs(support.y - surface.y) > 0.025f)
                    return Reject(rejected, "unsupported or uneven footprint corner " + corner);
            }

            // Bounds are expressed in world axes after rotating the item. Keep the floor outside this box.
            var clearance = new Bounds(surface + footprint.center + Vector3.up * 0.01f, footprint.size);
            clearance.Expand(new Vector3(0.06f, 0f, 0.06f));
            foreach (var collider in Physics.OverlapSphere(clearance.center, clearance.extents.magnitude))
            {
                if (collider.isTrigger || Ignored(collider.transform, ignore) || !clearance.Intersects(collider.bounds)) continue;
                var mesh = collider as MeshCollider;
                // A factory collider contains the floor AND walls. Its enclosing box is not solid space.
                if (mesh != null && !mesh.convex && mesh.sharedMesh != null && mesh.sharedMesh.isReadable &&
                    !MeshIntersects(mesh.sharedMesh, mesh.transform, clearance)) continue;
                return Reject(rejected, "solid obstacle " + collider.transform.name);
            }

            // Reserve the space below actual tabletops as well: their visible legs lack colliders.
            // Renderer bounds are unusable here because static batching combines much of the scene.
            foreach (var component in UnityEngine.Object.FindObjectsOfType(typeof(Collider)))
            {
                var table = component as Collider;
                if (table == null || !table.enabled || table.isTrigger || !TableAncestor(table.transform)) continue;
                var zone = table.bounds;
                if (zone.max.y <= surface.y || zone.min.y > tableY + 0.3f) continue;
                zone.SetMinMax(new Vector3(zone.min.x, surface.y, zone.min.z), zone.max);
                zone.Expand(new Vector3(0.06f, 0f, 0.06f));
                if (clearance.Intersects(zone)) return Reject(rejected, "space below table " + table.transform.name);
            }
            return true;
        }

        private static bool Reject(Action<string> rejected, string reason)
        {
            if (rejected != null) rejected(reason);
            return false;
        }

        internal static bool MeshIntersects(Mesh mesh, Transform transform, Bounds box)
        {
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            // Work around the candidate centre to retain precision at the factory's world coordinates.
            for (int i = 0; i < vertices.Length; i++) vertices[i] = transform.TransformPoint(vertices[i]) - box.center;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
                if (TriangleIntersects(vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]], box.extents)) return true;
            return false;
        }

        private static bool TriangleIntersects(Vector3 a, Vector3 b, Vector3 c, Vector3 extents)
        {
            // Triangle/box separating-axis test: three box axes, triangle normal and nine edge cross axes.
            if (Separated(Vector3.right, a, b, c, extents) || Separated(Vector3.up, a, b, c, extents) ||
                Separated(Vector3.forward, a, b, c, extents)) return false;
            Vector3 ab = b - a, bc = c - b, ca = a - c;
            if (Separated(Vector3.Cross(ab, bc), a, b, c, extents)) return false;
            return !EdgeSeparated(ab, a, b, c, extents) && !EdgeSeparated(bc, a, b, c, extents) && !EdgeSeparated(ca, a, b, c, extents);
        }

        private static bool EdgeSeparated(Vector3 edge, Vector3 a, Vector3 b, Vector3 c, Vector3 extents)
        {
            return Separated(Vector3.Cross(edge, Vector3.right), a, b, c, extents) ||
                Separated(Vector3.Cross(edge, Vector3.up), a, b, c, extents) ||
                Separated(Vector3.Cross(edge, Vector3.forward), a, b, c, extents);
        }

        private static bool Separated(Vector3 axis, Vector3 a, Vector3 b, Vector3 c, Vector3 extents)
        {
            float p = Vector3.Dot(axis, a), q = Vector3.Dot(axis, b), r = Vector3.Dot(axis, c);
            float radius = Mathf.Abs(axis.x) * extents.x + Mathf.Abs(axis.y) * extents.y + Mathf.Abs(axis.z) * extents.z;
            return Mathf.Min(p, Mathf.Min(q, r)) > radius || Mathf.Max(p, Mathf.Max(q, r)) < -radius;
        }

        private static bool TryFloor(Vector3 position, float tableY, GameObject ignore, out Vector3 surface)
        {
            surface = Vector3.zero;
            RaycastHit nearest = new RaycastHit();
            bool found = false;
            foreach (var hit in Physics.RaycastAll(position + Vector3.up * 0.2f, Vector3.down, 3f))
            {
                if (hit.collider.isTrigger || Ignored(hit.collider.transform, ignore)) continue;
                if (!found || hit.distance < nearest.distance) { nearest = hit; found = true; }
            }
            if (!found || nearest.normal.y < 0.8f || nearest.point.y >= tableY - 0.3f ||
                nearest.collider.attachedRigidbody != null || TableAncestor(nearest.collider.transform)) return false;
            surface = nearest.point;
            return true;
        }

        private static bool Ignored(Transform transform, GameObject ignore)
        {
            return ignore != null && (transform == ignore.transform || transform.IsChildOf(ignore.transform));
        }

        private static bool TableAncestor(Transform transform)
        {
            for (var node = transform; node != null; node = node.parent)
                if (node.name.IndexOf("table", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }
}

using System;
using System.Collections.Generic;
using FutufonAutoWorkerMscModLoader;
using UnityEngine;

// Deterministic Unity stand-ins run the production FloorLayout against controlled geometry.
// Native physics and mesh placement still require an in-game check.
namespace UnityEngine
{
    public class Object
    {
        internal static Object[] Scene = new Object[0];
        public static Object[] FindObjectsOfType(Type type) { return Scene; }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public static Vector3 down { get { return new Vector3(0, -1, 0); } }
        public static Vector3 right { get { return new Vector3(1, 0, 0); } }
        public static Vector3 forward { get { return new Vector3(0, 0, 1); } }
        public float magnitude { get { return (float)Math.Sqrt(x * x + y * y + z * z); } }
        public static Vector3 Scale(Vector3 a, Vector3 b) { return new Vector3(a.x * b.x, a.y * b.y, a.z * b.z); }
        public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x * b, a.y * b, a.z * b); }
    }
    public struct Quaternion
    {
        internal float Yaw;
        public static Quaternion AngleAxis(float angle, Vector3 axis) { return new Quaternion { Yaw = angle }; }
        public static Vector3 operator *(Quaternion rotation, Vector3 point)
        {
            double radians = rotation.Yaw * Math.PI / 180;
            return new Vector3((float)(point.x * Math.Cos(radians) + point.z * Math.Sin(radians)), point.y,
                (float)(-point.x * Math.Sin(radians) + point.z * Math.Cos(radians)));
        }
    }
    public struct Bounds
    {
        public Vector3 center, size;
        public Bounds(Vector3 centre, Vector3 size) { this.center = centre; this.size = size; }
        public Vector3 extents { get { return size * 0.5f; } }
        public Vector3 min { get { return center - extents; } }
        public Vector3 max { get { return center + extents; } }
        public void Expand(Vector3 amount) { size = size + amount; }
        public bool Intersects(Bounds other)
        {
            return min.x <= other.max.x && max.x >= other.min.x && min.y <= other.max.y && max.y >= other.min.y &&
                min.z <= other.max.z && max.z >= other.min.z;
        }
        public void Encapsulate(Vector3 point)
        {
            var lo = new Vector3(Math.Min(min.x, point.x), Math.Min(min.y, point.y), Math.Min(min.z, point.z));
            var hi = new Vector3(Math.Max(max.x, point.x), Math.Max(max.y, point.y), Math.Max(max.z, point.z));
            center = (lo + hi) * 0.5f; size = hi - lo;
        }
    }
    public class Transform
    {
        public string name = "item";
        public Transform parent;
        public Vector3 position;
        public Vector3 lossyScale = new Vector3(1, 1, 1);
        public Vector3 TransformPoint(Vector3 point) { return position + Vector3.Scale(point, lossyScale); }
        public Vector3 InverseTransformPoint(Vector3 point)
        {
            var relative = point - position;
            return new Vector3(relative.x / lossyScale.x, relative.y / lossyScale.y, relative.z / lossyScale.z);
        }
        public bool IsChildOf(Transform target)
        {
            for (var node = parent; node != null; node = node.parent) if (node == target) return true;
            return false;
        }
    }
    public class GameObject
    {
        public Transform transform = new Transform();
        internal MeshFilter[] Meshes = new MeshFilter[0];
        internal BoxCollider[] Boxes = new BoxCollider[0];
        public T[] GetComponentsInChildren<T>(bool includeInactive) { return typeof(T) == typeof(MeshFilter) ? (T[])(object)Meshes : (T[])(object)Boxes; }
    }
    public class Rigidbody { }
    public class Collider
    {
        public Transform transform = new Transform();
        public bool isTrigger;
        public Bounds bounds;
        public Rigidbody attachedRigidbody;
    }
    public class BoxCollider : Collider { public Vector3 center, size; }
    public class Renderer : Object { public Transform transform = new Transform(); public bool enabled = true; public Bounds bounds; }
    public class Mesh { public Bounds bounds; }
    public class MeshFilter { public Mesh sharedMesh; public Transform transform = new Transform(); }
    public struct RaycastHit { public Collider collider; public float distance; public Vector3 point, normal; }
    public static class Mathf { public static float Abs(float value) { return Math.Abs(value); } }
    public static class Physics
    {
        internal static Func<Vector3, RaycastHit[]> Rays;
        internal static Collider[] Nearby = new Collider[0];
        public static RaycastHit[] RaycastAll(Vector3 start, Vector3 direction, float distance) { return Rays(start); }
        public static Collider[] OverlapSphere(Vector3 centre, float radius) { return Nearby; }
    }
}

public static class FloorLayoutTests
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static RaycastHit Hit(Collider collider, Vector3 origin, float height, float normal = 1)
    {
        return new RaycastHit { collider = collider, distance = origin.y - height, point = new Vector3(origin.x, height, origin.z), normal = Vector3.up * normal };
    }
    private static bool Fits(Bounds bounds, GameObject ignore = null)
    {
        Vector3 surface;
        return FloorLayout.TrySurface(new Vector3(0, 1.6f, 0), 0.9f, bounds, ignore, out surface);
    }
    public static string Run()
    {
        var floor = new Collider { bounds = new Bounds(new Vector3(0, -0.1f, 0), new Vector3(100, 0.2f, 100)) };
        Physics.Rays = origin => new[] { Hit(floor, origin, 0) };
        Physics.Nearby = new[] { floor };
        UnityEngine.Object.Scene = new UnityEngine.Object[0];
        var tower = FloorLayout.TowerBounds;
        Require(Fits(tower), "clear floor accepts a tower without treating its supporting floor as an obstacle");

        var positions = new List<Vector3>(FloorLayout.StackOffsets());
        Require(FloorLayout.StackCount * FloorLayout.StackHeight == 44 && positions.Count >= FloorLayout.StackCount,
            "one carton of output fits in four-package towers");
        foreach (var point in positions) Require(point.x + tower.extents.x < -0.3f && point.z < 0, "every candidate stays left and behind the player");
        for (int a = 0; a < positions.Count; a++) for (int b = a + 1; b < positions.Count; b++)
            Require(!new Bounds(positions[a] + tower.center, tower.size).Intersects(new Bounds(positions[b] + tower.center, tower.size)),
                "tower footprints must not overlap");
        foreach (var point in FloorLayout.ShippingOffsets()) Require(point.x > 0 && point.z <= -0.7f, "carton stays behind, clear of left-side output");

        var table = new Collider { transform = new Transform { name = "work_table2" } };
        Physics.Rays = origin => new[] { Hit(floor, origin, 0), Hit(table, origin, 0.9f) };
        Require(!Fits(tower), "ray ordering cannot hide a colleague's table");
        Physics.Rays = origin => new[] { Hit(table, origin, 0.2f) };
        Require(!Fits(tower), "low table surfaces are not floor");
        Physics.Rays = origin => origin.x > 0.1f ? new[] { Hit(table, origin, 0.9f) } : new[] { Hit(floor, origin, 0) };
        Require(!Fits(tower), "clear centre is insufficient when a corner overlaps a table");
        Physics.Rays = origin => origin.x > 0.1f ? new RaycastHit[0] : new[] { Hit(floor, origin, 0) };
        Require(!Fits(tower), "footprint cannot overhang a floor edge");
        Physics.Rays = origin => new[] { Hit(floor, origin, origin.x > 0.1f ? 0.1f : 0) };
        Require(!Fits(tower), "uneven supports cannot form a stable tower");
        var loose = new Collider { attachedRigidbody = new Rigidbody() };
        Physics.Rays = origin => new[] { Hit(loose, origin, 0.1f) };
        Require(!Fits(tower), "a loose package cannot be adopted as floor");
        Physics.Rays = origin => new[] { Hit(floor, origin, 0, 0.6f) };
        Require(!Fits(tower), "steep floor is rejected");
        var trigger = new Collider { isTrigger = true };
        Physics.Rays = origin => new[] { Hit(trigger, origin, 1), Hit(floor, origin, 0) };
        Require(Fits(tower), "triggers do not obscure the floor");

        var obstacle = new Collider { bounds = new Bounds(new Vector3(0.1f, 0.25f, 0), new Vector3(0.1f, 0.5f, 0.1f)) };
        Physics.Nearby = new[] { floor, obstacle };
        Require(!Fits(tower), "solid obstacles intersecting the tower are rejected");
        obstacle.isTrigger = true;
        Require(Fits(tower), "trigger volumes do not block output");
        Physics.Nearby = new[] { floor };
        UnityEngine.Object.Scene = new[] { new Renderer { transform = new Transform { name = "leg", parent = table.transform }, bounds = obstacle.bounds } };
        Require(!Fits(tower), "table supports without colliders still block placement");
        UnityEngine.Object.Scene = new UnityEngine.Object[0];

        var carton = new GameObject();
        var ownCollider = new Collider { transform = new Transform { parent = carton.transform }, bounds = tower };
        Physics.Rays = origin => new[] { Hit(ownCollider, origin, 0.1f), Hit(floor, origin, 0) };
        Physics.Nearby = new[] { floor, ownCollider };
        Require(Fits(tower, carton), "repositioning ignores the carton's own children");
        Require(!Fits(tower), "another carton's collider is not ignored");

        carton.Boxes = new[] { new BoxCollider { size = new Vector3(0.63f, 0.47f, 0.4f), transform = carton.transform } };
        carton.Meshes = new[] { new MeshFilter { sharedMesh = new Mesh { bounds = new Bounds(new Vector3(0.6f, 0, 0), new Vector3(0.6f, 0.5f, 0.4f)) }, transform = carton.transform } };
        var cartonBounds = FloorLayout.ShippingBounds(carton, Quaternion.AngleAxis(0, Vector3.up));
        Require(cartonBounds.max.x > 0.89f && Math.Abs(cartonBounds.min.y) < 0.001f, "inactive open-flap geometry is included and aligned to the floor");
        var rotated = FloorLayout.ShippingBounds(carton, Quaternion.AngleAxis(90, Vector3.up));
        Require(rotated.min.z < -0.89f, "visual footprint follows carton rotation");
        var across = FloorLayout.WorldBounds(tower, Vector3.forward, Vector3.right * -1);
        Require(Math.Abs(across.size.x - tower.size.z) < 0.001f && Math.Abs(across.size.z - tower.size.x) < 0.001f,
            "tower clearance follows the other side/orientation of a table");
        // Before yaw, the package's local +Y lid extends toward the player (-forward).
        var lid = Quaternion.AngleAxis(FloorLayout.PackageYaw, Vector3.up) * (Vector3.forward * -1);
        Require(lid.x < -0.99f && Math.Abs(lid.z) < 0.001f, "the assembly lid opens left, away from supplies");
        return "PASS: 44-package floor capacity, left/rear positions, nonoverlapping towers, footprint edges, table/obstacle rejection, trigger handling, own-carton exclusion, open flaps and left-opening lid";
    }
}

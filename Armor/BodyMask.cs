using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace MysticalWeapons.Armor
{
    /// <summary>
    /// Valheim's body is one skinned mesh and its chunky limbs poke through the Shardplate as they bend.
    /// Instead of inflating the plates to contain it, the body is not drawn where a worn Shardplate piece
    /// covers it: each body vertex casts a few short rays outwards from its surface, and if they hit the
    /// piece's plates the vertex counts as covered. Triangles with two or more covered corners are left
    /// out of a copy of the body mesh, which replaces the body while the piece is worn.
    ///
    /// Each piece hides only what it covers, so a mixed set still shows the body under other armor, and
    /// skin that shows through gaps between plates (or that no plate reaches) stays. Coverage is tested
    /// in the rest pose, once per body mesh and piece.
    /// </summary>
    internal static class BodyMask
    {
        private class Piece
        {
            public string Prefab;
            public int Hash;
            public AccessTools.FieldRef<VisEquipment, int> Slot;
        }

        private static readonly Piece[] Pieces =
        {
            new Piece { Prefab = "ShardplateBreastplate", Slot = AccessTools.FieldRefAccess<VisEquipment, int>("m_currentChestItemHash") },
            new Piece { Prefab = "ShardplateGreaves", Slot = AccessTools.FieldRefAccess<VisEquipment, int>("m_currentLegItemHash") },
        };

        static BodyMask()
        {
            foreach (var p in Pieces) p.Hash = p.Prefab.GetStableHashCode();
        }

        /// <summary>A plate this far outside the body surface still counts as covering it (metres).</summary>
        private const float Reach = 0.12f;
        /// <summary>Rays start this far inside the body, so skin already through a plate counts as covered.</summary>
        private const float Pierce = 0.03f;
        /// <summary>Rays per vertex (along the normal plus a ring tilted this far off it) and hits needed.</summary>
        private const float RayTilt = 25f;
        private const int RayRing = 4;
        private const int HitsNeeded = 3;

        // Per original body mesh (male/female): the variants by worn-piece mask (bit i = Pieces[i]), and
        // each piece's covered vertices. Variants map back to their original so it can be restored.
        private static readonly Dictionary<Mesh, Dictionary<int, Mesh>> Variants = new Dictionary<Mesh, Dictionary<int, Mesh>>();
        private static readonly Dictionary<Mesh, bool[][]> Covered = new Dictionary<Mesh, bool[][]>();
        private static readonly Dictionary<Mesh, Mesh> OriginalOf = new Dictionary<Mesh, Mesh>();

        /// <summary>Forget computed masks (mw_reload: the armor models may have changed).</summary>
        internal static void Clear()
        {
            Variants.Clear();
            Covered.Clear();
        }

        /// <summary>
        /// Runs every frame after VisEquipment has applied equipment and the base model (which sets the
        /// body mesh when the player model changes), so the right variant always wins.
        /// </summary>
        [HarmonyPatch(typeof(VisEquipment), "UpdateVisuals")]
        private static class UpdateVisualsPatch
        {
            private static void Postfix(VisEquipment __instance)
            {
                var body = __instance.m_bodyModel;
                if (!body || !body.sharedMesh) return;
                var mesh = body.sharedMesh;
                var original = OriginalOf.TryGetValue(mesh, out var o) ? o : mesh;
                var worn = 0;
                for (var i = 0; i < Pieces.Length; i++)
                    if (Pieces[i].Slot(__instance) == Pieces[i].Hash) worn |= 1 << i;
                var target = worn == 0 ? original : GetVariant(body, original, worn);
                if (target && target != mesh) body.sharedMesh = target;
            }
        }

        private static Mesh GetVariant(SkinnedMeshRenderer body, Mesh original, int worn)
        {
            if (!Variants.TryGetValue(original, out var byMask)) Variants[original] = byMask = new Dictionary<int, Mesh>();
            if (byMask.TryGetValue(worn, out var variant)) return variant;
            try { variant = BuildVariant(body, original, worn); }
            catch (System.Exception e) { Plugin.Log.LogError($"Shardplate body mask failed: {e}"); }
            if (!variant) variant = original;
            byMask[worn] = variant;
            if (variant != original) OriginalOf[variant] = original;
            return variant;
        }

        private static Mesh BuildVariant(SkinnedMeshRenderer body, Mesh original, int worn)
        {
            if (!original.isReadable)
            {
                Plugin.Log.LogWarning("Shardplate body mask: the body mesh isn't readable, nothing is hidden");
                return null;
            }
            if (!Covered.TryGetValue(original, out var covered)) Covered[original] = covered = new bool[Pieces.Length][];
            var hide = new bool[original.vertexCount];
            for (var i = 0; i < Pieces.Length; i++)
            {
                if ((worn & (1 << i)) == 0) continue;
                if (covered[i] == null) covered[i] = Coverage(body, original, Pieces[i]) ?? new bool[original.vertexCount];
                for (var v = 0; v < hide.Length; v++) hide[v] |= covered[i][v];
            }

            var copy = Object.Instantiate(original);
            copy.name = original.name + "_shardplate_" + worn;
            int kept = 0, dropped = 0;
            for (var sub = 0; sub < original.subMeshCount; sub++)
            {
                var tris = original.GetTriangles(sub);
                var keep = new List<int>(tris.Length);
                for (var t = 0; t < tris.Length; t += 3)
                {
                    var n = (hide[tris[t]] ? 1 : 0) + (hide[tris[t + 1]] ? 1 : 0) + (hide[tris[t + 2]] ? 1 : 0);
                    if (n >= 2) { dropped++; continue; }
                    keep.Add(tris[t]); keep.Add(tris[t + 1]); keep.Add(tris[t + 2]);
                    kept++;
                }
                copy.SetTriangles(keep, sub, false);
            }
            Plugin.Log.LogInfo($"Shardplate body mask: hid {dropped} of {kept + dropped} body triangles under " +
                               string.Join(" + ", Pieces.Where((p, i) => (worn & (1 << i)) != 0).Select(p => p.Prefab)));
            return copy;
        }

        /// <summary>Which body vertices a piece's plates cover, tested in the rest pose.</summary>
        private static bool[] Coverage(SkinnedMeshRenderer body, Mesh bodyMesh, Piece piece)
        {
            var item = Plugin.Items.FirstOrDefault(x => x.PrefabName == piece.Prefab);
            var armor = item != null && item.Prefab
                ? item.Prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.GetComponent<SkinBoneNames>())
                : null;
            if (!armor || !armor.sharedMesh || !armor.sharedMesh.isReadable)
            {
                Plugin.Log.LogWarning($"Shardplate body mask: no readable skinned model for {piece.Prefab}");
                return null;
            }

            // The armor mesh is in the player's rest pose (Visual space) and its bind poses are the inverse
            // rest pose of each bone, so they also carry the body into that space
            var am = armor.sharedMesh;
            var names = armor.GetComponent<SkinBoneNames>().Names;
            var rest = new Dictionary<string, Matrix4x4>();
            var armorBind = am.bindposes;
            for (var i = 0; i < names.Length && i < armorBind.Length; i++) rest[names[i]] = armorBind[i].inverse;

            var bones = body.bones;
            var bodyBind = bodyMesh.bindposes;
            if (bodyBind.Length != bones.Length) { Plugin.Log.LogWarning("Shardplate body mask: body bind poses don't match its bones"); return null; }
            var toRest = new Matrix4x4[bones.Length];
            for (var b = 0; b < bones.Length; b++)
            {
                if (!bones[b] || !rest.TryGetValue(bones[b].name, out var r))
                {
                    Plugin.Log.LogWarning($"Shardplate body mask: armor has no rest pose for body bone '{(bones[b] ? bones[b].name : "null")}'");
                    return null;
                }
                toRest[b] = r * bodyBind[b];
            }

            var verts = bodyMesh.vertices;
            var normals = bodyMesh.normals;
            var weights = bodyMesh.boneWeights;
            var grid = new TriangleGrid(am.vertices, am.triangles);
            var result = new bool[verts.Length];
            var count = 0;
            for (var v = 0; v < verts.Length && v < weights.Length && v < normals.Length; v++)
            {
                var bw = weights[v];
                Vector3 p = Vector3.zero, n = Vector3.zero;
                void Add(int bi, float w)
                {
                    if (w <= 0f) return;
                    p += toRest[bi].MultiplyPoint3x4(verts[v]) * w;
                    n += toRest[bi].MultiplyVector(normals[v]) * w;
                }
                Add(bw.boneIndex0, bw.weight0); Add(bw.boneIndex1, bw.weight1);
                Add(bw.boneIndex2, bw.weight2); Add(bw.boneIndex3, bw.weight3);
                if (n.sqrMagnitude < 1e-12f) continue;
                n.Normalize();
                if (IsCovered(grid, p, n)) { result[v] = true; count++; }
            }
            Plugin.Log.LogInfo($"Shardplate body mask: {piece.Prefab} covers {count} of {verts.Length} body vertices");
            return result;
        }

        private static bool IsCovered(TriangleGrid grid, Vector3 p, Vector3 n)
        {
            var side = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            var hits = 0;
            for (var k = 0; k <= RayRing; k++)
            {
                var dir = k == 0 ? n
                    : Quaternion.AngleAxis(RayTilt, Quaternion.AngleAxis(360f * k / RayRing, n) * side) * n;
                if (grid.Raycast(p - dir * Pierce, dir, Reach + Pierce)) hits++;
                if (hits >= HitsNeeded) return true;
                if (hits + (RayRing - k) < HitsNeeded) return false;
            }
            return false;
        }

        /// <summary>Armor triangles bucketed into a uniform grid, for short ray tests.</summary>
        private class TriangleGrid
        {
            private const float Cell = 0.04f;
            private readonly Vector3[] _v;
            private readonly int[] _t;
            private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>();
            private readonly int[] _stamp;
            private int _ray;

            public TriangleGrid(Vector3[] verts, int[] tris)
            {
                _v = verts; _t = tris; _stamp = new int[tris.Length / 3];
                for (var i = 0; i < tris.Length; i += 3)
                {
                    var a = verts[tris[i]]; var b = verts[tris[i + 1]]; var c = verts[tris[i + 2]];
                    var lo = Key3(Vector3.Min(a, Vector3.Min(b, c)));
                    var hi = Key3(Vector3.Max(a, Vector3.Max(b, c)));
                    for (var x = lo.x; x <= hi.x; x++)
                    for (var y = lo.y; y <= hi.y; y++)
                    for (var z = lo.z; z <= hi.z; z++)
                    {
                        var key = Key(x, y, z);
                        if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = new List<int>();
                        list.Add(i / 3);
                    }
                }
            }

            private static Vector3Int Key3(Vector3 p) =>
                new Vector3Int(Mathf.FloorToInt(p.x / Cell), Mathf.FloorToInt(p.y / Cell), Mathf.FloorToInt(p.z / Cell));

            private static long Key(int x, int y, int z) =>
                ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (uint)(z & 0x1FFFFF);

            public bool Raycast(Vector3 origin, Vector3 dir, float length)
            {
                _ray++;
                // Sample the cells along the ray finely enough not to skip one
                for (var s = 0f; s <= length + Cell * 0.5f; s += Cell * 0.5f)
                {
                    var k = Key3(origin + dir * Mathf.Min(s, length));
                    if (!_cells.TryGetValue(Key(k.x, k.y, k.z), out var list)) continue;
                    foreach (var tri in list)
                    {
                        if (_stamp[tri] == _ray) continue;
                        _stamp[tri] = _ray;
                        if (Hit(origin, dir, length, tri * 3)) return true;
                    }
                }
                return false;
            }

            /// <summary>Möller-Trumbore, both faces.</summary>
            private bool Hit(Vector3 o, Vector3 d, float length, int i)
            {
                var a = _v[_t[i]]; var e1 = _v[_t[i + 1]] - a; var e2 = _v[_t[i + 2]] - a;
                var pv = Vector3.Cross(d, e2);
                var det = Vector3.Dot(e1, pv);
                if (Mathf.Abs(det) < 1e-10f) return false;
                var inv = 1f / det;
                var tv = o - a;
                var u = Vector3.Dot(tv, pv) * inv;
                if (u < 0f || u > 1f) return false;
                var q = Vector3.Cross(tv, e1);
                var w = Vector3.Dot(d, q) * inv;
                if (w < 0f || u + w > 1f) return false;
                var t = Vector3.Dot(e2, q) * inv;
                return t >= 0f && t <= length;
            }
        }
    }
}

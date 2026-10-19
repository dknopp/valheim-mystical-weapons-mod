using System.Collections.Generic;
using UnityEngine;

namespace MysticalWeapons
{
    /// <summary>
    /// Small toolkit for building low-poly weapon meshes in code. Every triangle is added
    /// double-sided with flat normals, so winding order never matters.
    /// </summary>
    internal class MeshBuilder
    {
        public readonly List<Vector3> Verts = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<int>[] Tris;
        /// <summary>Optional texture per submesh (imported models only).</summary>
        public Texture2D[] Textures;

        public MeshBuilder(int submeshes)
        {
            Tris = new List<int>[submeshes];
            for (var i = 0; i < submeshes; i++) Tris[i] = new List<int>();
        }

        public static Vector3 V(Vector2 p, float z) => new Vector3(p.x, p.y, z);

        public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f) return;
            n.Normalize();
            Add(sub, a, b, c, n, n, n, Vector2.zero, Vector2.zero, Vector2.zero);
            Add(sub, a, c, b, -n, -n, -n, Vector2.zero, Vector2.zero, Vector2.zero);
        }

        /// <summary>
        /// A double-sided triangle with texture coordinates and (optionally smooth) vertex normals,
        /// for imported models. Missing normals fall back to the flat face normal.
        /// </summary>
        public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc,
            Vector3? na = null, Vector3? nb = null, Vector3? nc = null)
        {
            var n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f) return;
            n.Normalize();
            Vector3 n0 = na ?? n, n1 = nb ?? n, n2 = nc ?? n;
            // Give the vertex normals to whichever copy faces the same way they point
            var s = Vector3.Dot(n0 + n1 + n2, n) >= 0f ? 1f : -1f;
            Add(sub, a, b, c, s * n0, s * n1, s * n2, ua, ub, uc);
            Add(sub, a, c, b, -s * n0, -s * n2, -s * n1, ua, uc, ub);
        }

        private void Add(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc,
            Vector2 ua, Vector2 ub, Vector2 uc)
        {
            var i = Verts.Count;
            Verts.Add(a); Verts.Add(b); Verts.Add(c);
            Normals.Add(na); Normals.Add(nb); Normals.Add(nc);
            Uvs.Add(ua); Uvs.Add(ub); Uvs.Add(uc);
            Tris[sub].Add(i); Tris[sub].Add(i + 1); Tris[sub].Add(i + 2);
        }

        public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Tri(sub, a, b, c);
            Tri(sub, a, c, d);
        }

        /// <summary>Extrudes a convex 2D polygon (in XY) to a slab of the given half thickness.</summary>
        public void Slab(int sub, Vector2[] poly, float h)
        {
            for (var i = 1; i < poly.Length - 1; i++)
            {
                Tri(sub, V(poly[0], h), V(poly[i], h), V(poly[i + 1], h));
                Tri(sub, V(poly[0], -h), V(poly[i], -h), V(poly[i + 1], -h));
            }
            for (var i = 0; i < poly.Length; i++)
            {
                var p = poly[i];
                var q = poly[(i + 1) % poly.Length];
                Quad(sub, V(p, h), V(q, h), V(q, -h), V(p, -h));
            }
        }

        public void Box(int sub, Rect r, float h) => Slab(sub, new[]
        {
            new Vector2(r.xMin, r.yMin), new Vector2(r.xMax, r.yMin),
            new Vector2(r.xMax, r.yMax), new Vector2(r.xMin, r.yMax)
        }, h);

        /// <summary>A flat band of the given half width following a 2D path (curved guards, ribbons).</summary>
        public void Ribbon(int sub, Vector2[] path, float halfWidth, float h)
        {
            for (var i = 0; i < path.Length - 1; i++)
            {
                var dir = (path[i + 1] - path[i]).normalized;
                var side = new Vector2(-dir.y, dir.x) * halfWidth;
                var ext = dir * halfWidth * 0.5f; // overlap segments so joints have no gaps
                Slab(sub, new[]
                {
                    path[i] - ext - side, path[i + 1] + ext - side,
                    path[i + 1] + ext + side, path[i] - ext + side
                }, h);
            }
        }

        /// <summary>A prism along Y (shafts, grips, collars).</summary>
        public void Prism(int sub, float y0, float y1, float radius, int sides = 8, float x = 0f) =>
            Taper(sub, y0, y1, radius, radius, sides, x);

        /// <summary>A prism along Y whose radius changes from <paramref name="r0"/> to <paramref name="r1"/>.</summary>
        public void Taper(int sub, float y0, float y1, float r0, float r1, int sides = 8, float x = 0f)
        {
            var bottom = new Vector3[sides];
            var top = new Vector3[sides];
            for (var i = 0; i < sides; i++)
            {
                var a = (i + 0.5f) * Mathf.PI * 2f / sides;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                bottom[i] = dir * r0 + new Vector3(x, y0, 0f);
                top[i] = dir * r1 + new Vector3(x, y1, 0f);
            }
            for (var i = 0; i < sides; i++)
            {
                var j = (i + 1) % sides;
                Quad(sub, bottom[i], bottom[j], top[j], top[i]);
                if (i > 0 && i < sides - 1)
                {
                    Tri(sub, bottom[0], bottom[i], bottom[j]);
                    Tri(sub, top[0], top[i], top[j]);
                }
            }
        }

        /// <summary>One cross-section of a blade along Y.</summary>
        public struct Station
        {
            public float Y, Left, Right, Thickness;
            public Station(float y, float left, float right, float thickness)
            {
                Y = y; Left = left; Right = right; Thickness = thickness;
            }
            public Station(float y, float halfWidth, float thickness) : this(y, halfWidth, halfWidth, thickness) { }
        }

        /// <summary>
        /// A blade along Y with a diamond cross-section: sharp edges at +-X, spine at +-Z.
        /// Left/Right widths can differ for asymmetric blades.
        /// </summary>
        public void Blade(int sub, params Station[] s)
        {
            Vector3[] Ring(Station st) => new[]
            {
                new Vector3(-st.Left, st.Y, 0f), new Vector3(0f, st.Y, st.Thickness),
                new Vector3(st.Right, st.Y, 0f), new Vector3(0f, st.Y, -st.Thickness)
            };

            for (var i = 0; i < s.Length - 1; i++)
            {
                var a = Ring(s[i]);
                var b = Ring(s[i + 1]);
                for (var k = 0; k < 4; k++)
                {
                    var n = (k + 1) % 4;
                    Quad(sub, a[k], a[n], b[n], b[k]);
                }
            }
            var first = Ring(s[0]);
            var last = Ring(s[s.Length - 1]);
            Quad(sub, first[0], first[1], first[2], first[3]);
            Quad(sub, last[0], last[1], last[2], last[3]);
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name + "_mesh" };
            if (Verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(Verts);
            mesh.SetNormals(Normals);
            mesh.SetUVs(0, Uvs);
            mesh.subMeshCount = Tris.Length;
            for (var i = 0; i < Tris.Length; i++) mesh.SetTriangles(Tris[i], i);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}

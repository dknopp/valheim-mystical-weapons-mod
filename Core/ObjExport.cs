using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace MysticalWeapons
{
    /// <summary>
    /// Writes a code-built model as Wavefront OBJ + MTL for editing in Blender.
    /// Unity is left-handed, OBJ is right-handed: X is mirrored and winding reversed on export.
    /// Each submesh becomes a material named "{prefab}_{index}".
    /// </summary>
    internal static class ObjExport
    {
        public static void Write(MeshBuilder b, MaterialSpec[] materials, string name, string directory)
        {
            Directory.CreateDirectory(directory);
            var inv = CultureInfo.InvariantCulture;
            var obj = new StringBuilder();
            obj.AppendLine($"# {name} - exported from Mystical Weapons");
            obj.AppendLine($"mtllib {name}.mtl");
            obj.AppendLine($"o {name}");

            // Weld duplicate positions so Blender gets a connected mesh
            var index = new Dictionary<Vector3Int, int>();
            var faces = new StringBuilder();
            int Vertex(Vector3 p)
            {
                var key = new Vector3Int(Mathf.RoundToInt(p.x * 1e5f), Mathf.RoundToInt(p.y * 1e5f), Mathf.RoundToInt(p.z * 1e5f));
                if (index.TryGetValue(key, out var i)) return i;
                i = index.Count + 1;
                index[key] = i;
                obj.AppendLine(string.Format(inv, "v {0:0.######} {1:0.######} {2:0.######}", -p.x, p.y, p.z));
                return i;
            }

            for (var sub = 0; sub < b.Tris.Length; sub++)
            {
                faces.AppendLine($"usemtl {name}_{sub}");
                var tris = b.Tris[sub];
                // MeshBuilder adds every triangle twice (front, then back); keep the front ones
                for (var t = 0; t < tris.Count; t += 6)
                {
                    int a = Vertex(b.Verts[tris[t]]), c = Vertex(b.Verts[tris[t + 1]]), d = Vertex(b.Verts[tris[t + 2]]);
                    if (a == c || c == d || a == d) continue;
                    faces.AppendLine($"f {a} {d} {c}");
                }
            }
            obj.Append(faces);
            File.WriteAllText(Path.Combine(directory, name + ".obj"), obj.ToString());

            var mtl = new StringBuilder();
            for (var i = 0; i < materials.Length; i++)
            {
                var m = materials[i];
                mtl.AppendLine($"newmtl {name}_{i}");
                mtl.AppendLine(string.Format(inv, "Kd {0:0.###} {1:0.###} {2:0.###}", m.Color.r, m.Color.g, m.Color.b));
                mtl.AppendLine(string.Format(inv, "Ke {0:0.###} {1:0.###} {2:0.###}", m.Emission.r, m.Emission.g, m.Emission.b));
                mtl.AppendLine(string.Format(inv, "Ns {0:0}", m.Gloss * 900f));
                mtl.AppendLine("d 1");
                mtl.AppendLine();
            }
            File.WriteAllText(Path.Combine(directory, name + ".mtl"), mtl.ToString());
        }
    }
}

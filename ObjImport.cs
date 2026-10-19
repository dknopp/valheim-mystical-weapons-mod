using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace MysticalWeapons
{
    /// <summary>
    /// Loads a Wavefront OBJ (e.g. edited in Blender) into a <see cref="MeshBuilder"/>, the reverse
    /// of <see cref="ObjExport"/>: X is mirrored back to Unity's left-handed space.
    /// Materials named "{anything}_{N}" go to submesh N, others to submesh 0.
    /// Texture coordinates and vertex normals are kept when present. Polygons are fan-triangulated,
    /// so triangulate concave faces before exporting.
    /// </summary>
    internal static class ObjImport
    {
        /// <summary>Loads the mesh; returns the diffuse texture file per submesh (from the MTL), if any.</summary>
        public static Dictionary<int, string> Load(string path, MeshBuilder b)
        {
            var inv = CultureInfo.InvariantCulture;
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var normals = new List<Vector3>();
            var materialTextures = new Dictionary<string, string>();
            var textures = new Dictionary<int, string>();
            var sub = 0;
            var faces = 0;

            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);

                switch (p[0])
                {
                    case "mtllib":
                        var mtl = Path.Combine(Path.GetDirectoryName(path) ?? "", line.Substring(7).Trim());
                        if (File.Exists(mtl)) ReadMaterialTextures(mtl, materialTextures);
                        break;

                    case "v":
                        verts.Add(new Vector3(-float.Parse(p[1], inv), float.Parse(p[2], inv), float.Parse(p[3], inv)));
                        break;

                    case "vt":
                        uvs.Add(new Vector2(float.Parse(p[1], inv), p.Length > 2 ? float.Parse(p[2], inv) : 0f));
                        break;

                    case "vn":
                        normals.Add(new Vector3(-float.Parse(p[1], inv), float.Parse(p[2], inv), float.Parse(p[3], inv)).normalized);
                        break;

                    case "usemtl":
                        var name = p.Length > 1 ? p[1] : "";
                        var us = name.LastIndexOf('_');
                        // Blender may append ".001" to duplicated material names
                        var suffix = us >= 0 ? name.Substring(us + 1).Split('.')[0] : "";
                        sub = int.TryParse(suffix, out var n) && n >= 0 && n < b.Tris.Length ? n : 0;
                        if (materialTextures.TryGetValue(name, out var tex) && !textures.ContainsKey(sub)) textures[sub] = tex;
                        break;

                    case "f":
                        var corners = new (Vector3 v, Vector2 uv, Vector3? n)[p.Length - 1];
                        for (var i = 1; i < p.Length; i++)
                        {
                            var parts = p[i].Split('/');
                            var v = Index(parts[0], verts.Count);
                            var uv = parts.Length > 1 && parts[1].Length > 0 ? uvs[Index(parts[1], uvs.Count)] : Vector2.zero;
                            Vector3? nrm = parts.Length > 2 && parts[2].Length > 0 ? normals[Index(parts[2], normals.Count)] : (Vector3?)null;
                            corners[i - 1] = (verts[v], uv, nrm);
                        }
                        // Reverse winding to undo the mirror; MeshBuilder adds both sides anyway
                        for (var i = 1; i < corners.Length - 1; i++)
                        {
                            var a = corners[0];
                            var c = corners[i + 1];
                            var d = corners[i];
                            b.Tri(sub, a.v, c.v, d.v, a.uv, c.uv, d.uv, a.n, c.n, d.n);
                        }
                        faces++;
                        break;
                }
            }

            if (faces == 0) throw new InvalidDataException("no faces found");
            return textures;
        }

        /// <summary>OBJ indices are 1-based; negative ones count back from the end.</summary>
        private static int Index(string s, int count)
        {
            var i = int.Parse(s, CultureInfo.InvariantCulture);
            return i < 0 ? count + i : i - 1;
        }

        private static void ReadMaterialTextures(string mtlPath, Dictionary<string, string> result)
        {
            string current = null;
            foreach (var raw in File.ReadAllLines(mtlPath))
            {
                var line = raw.Trim();
                if (line.StartsWith("newmtl ")) current = line.Substring(7).Trim();
                else if (current != null && line.StartsWith("map_Kd "))
                {
                    // The file name is the last token; options like "-s 1 1 1" may precede it
                    var file = line.Substring(7).Trim().Split(' ');
                    result[current] = Path.Combine(Path.GetDirectoryName(mtlPath) ?? "", file[file.Length - 1]);
                }
            }
        }
    }
}

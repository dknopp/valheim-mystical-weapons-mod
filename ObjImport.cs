using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace MysticalWeapons
{
    /// <summary>
    /// Loads a Wavefront OBJ (e.g. edited in Blender) into a <see cref="MeshBuilder"/>, the reverse
    /// of <see cref="ObjExport"/>: X is mirrored back to Unity's left-handed space.
    /// Materials named "{anything}_{N}" go to submesh N (the weapon's Nth material), others to submesh 0.
    /// Polygons are fan-triangulated, so triangulate concave faces before exporting.
    /// </summary>
    internal static class ObjImport
    {
        public static void Load(string path, MeshBuilder b)
        {
            var inv = CultureInfo.InvariantCulture;
            var verts = new List<Vector3>();
            var sub = 0;
            var faces = 0;

            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var p = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);

                switch (p[0])
                {
                    case "v":
                        verts.Add(new Vector3(
                            -float.Parse(p[1], inv), float.Parse(p[2], inv), float.Parse(p[3], inv)));
                        break;

                    case "usemtl":
                        var name = p.Length > 1 ? p[1] : "";
                        var us = name.LastIndexOf('_');
                        // Blender may append ".001" to duplicated material names
                        var suffix = us >= 0 ? name.Substring(us + 1).Split('.')[0] : "";
                        sub = int.TryParse(suffix, out var n) && n >= 0 && n < b.Tris.Length ? n : 0;
                        break;

                    case "f":
                        var idx = new int[p.Length - 1];
                        for (var i = 1; i < p.Length; i++)
                        {
                            var v = int.Parse(p[i].Split('/')[0], inv);
                            idx[i - 1] = v < 0 ? verts.Count + v : v - 1; // OBJ is 1-based; negatives are relative
                        }
                        for (var i = 1; i < idx.Length - 1; i++)
                            b.Tri(sub, verts[idx[0]], verts[idx[i]], verts[idx[i + 1]]);
                        faces++;
                        break;
                }
            }

            if (faces == 0) throw new InvalidDataException("no faces found");
        }
    }
}

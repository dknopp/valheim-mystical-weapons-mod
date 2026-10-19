using System;
using System.IO;
using System.Linq;
using MysticalWeapons;
using UnityEngine;

// Usage: ModelExporter [outputDirectory] [--verify]
//   Exports every code-built model to OBJ. --verify re-imports each file and checks it matches.
var verify = args.Contains("--verify");
var outDir = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "models");
var failed = false;

foreach (var item in Plugin.Items)
{
    if (!(item is MysticalWeapon weapon)) continue; // armor uses vanilla meshes
    var built = new MeshBuilder(weapon.Materials.Length);
    weapon.BuildModel(built);
    ObjExport.Write(built, weapon.Materials, weapon.PrefabName, outDir);
    Console.Write($"{weapon.PrefabName}.obj  ({built.Verts.Count / 6} triangles)");

    if (verify)
    {
        var loaded = new MeshBuilder(weapon.Materials.Length);
        ObjImport.Load(Path.Combine(outDir, weapon.PrefabName + ".obj"), loaded);
        var sameTris = Enumerable.Range(0, built.Tris.Length).All(s => built.Tris[s].Count == loaded.Tris[s].Count);
        var sameBounds = Bounds(built) == Bounds(loaded);
        Console.Write(sameTris && sameBounds ? "  round trip OK" : $"  ROUND TRIP MISMATCH (tris {sameTris}, bounds {sameBounds})");
        failed |= !(sameTris && sameBounds);
    }
    Console.WriteLine();
}
Console.WriteLine($"Written to {outDir}");
return failed ? 1 : 0;

static string Bounds(MeshBuilder b)
{
    Vector3 min = b.Verts[0], max = b.Verts[0];
    foreach (var v in b.Verts) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
    return $"{min.x:0.0000},{min.y:0.0000},{min.z:0.0000} {max.x:0.0000},{max.y:0.0000},{max.z:0.0000}";
}

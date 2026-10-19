using System;
using System.IO;
using System.Linq;
using MysticalWeapons;
using UnityEngine;

// Usage: ModelExporter [outputDirectory] [--verify]
//   Exports every code-built model to OBJ. --verify re-imports each file and checks it matches.
// Usage: ModelExporter --load <file.obj>
//   Loads a custom model the way the mod will and prints what it found.
if (args.Length == 2 && args[0] == "--load")
{
    var model = new MeshBuilder(4);
    var textures = ObjImport.Load(args[1], model);
    var perSub = string.Join(", ", model.Tris.Select((t, i) => $"submesh {i}: {t.Count / 6} tris"));
    Console.WriteLine($"{model.Verts.Count / 6} triangles ({perSub}); bounds {Bounds(model)}");
    foreach (var kv in textures) Console.WriteLine($"texture for submesh {kv.Key}: {kv.Value} (exists: {File.Exists(kv.Value)})");
    var withUv = model.Uvs.Count(u => u != Vector2.zero);
    Console.WriteLine($"vertices with UVs: {withUv}/{model.Uvs.Count}");
    return 0;
}

var verify = args.Contains("--verify");
var outDir = Path.GetFullPath(args.FirstOrDefault(a => !a.StartsWith("--")) ?? "exports");
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

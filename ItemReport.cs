using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MysticalWeapons
{
    /// <summary>
    /// Developer tool, off by default: compares each mystical item with its vanilla peer
    /// (<see cref="MysticalItem.VanillaPeer"/>). Logs triangles, textures, stats and recipe, and
    /// renders both side by side (front, side, top) into BepInEx/MysticalItemReport.
    /// </summary>
    internal static class ItemReport
    {
        private static ConfigEntry<bool> _enabled;
        private static bool _done;

        private const int Layer = 31;
        private const int Panel = 512;
        private static readonly Vector3 Stage = new Vector3(0f, 5000f, 0f);

        private static string OutputDir => Path.Combine(Paths.BepInExRootPath, "MysticalItemReport");

        public static void BindConfig(ConfigFile cfg)
        {
            _enabled = cfg.Bind("Debug", "ItemReport", false,
                "For mod developers: on startup, compare each item with its vanilla peer (log + images in BepInEx/MysticalItemReport).");
        }

        /// <summary>Runs the report once per launch, a frame after the items are registered.</summary>
        public static void Schedule(ObjectDB db)
        {
            if (_done || _enabled == null || !_enabled.Value || !Plugin.Instance) return;
            _done = true;
            Plugin.Instance.StartCoroutine(Run(db));
        }

        private static IEnumerator Run(ObjectDB db)
        {
            yield return null;
            if (!db) yield break;
            try
            {
                Directory.CreateDirectory(OutputDir);
                var text = new StringBuilder();
                foreach (var item in Plugin.Items)
                {
                    if (!item.Enabled.Value || !item.Prefab) continue;
                    var peerName = item.VanillaPeer ?? item.SourceName;
                    var peer = db.GetItemPrefab(peerName);
                    text.Append(Describe(db, item.Prefab, peer, peerName));
                    if (peer) Render(item.Prefab, peer, Path.Combine(OutputDir, item.PrefabName + ".png"));
                }
                File.WriteAllText(Path.Combine(OutputDir, "report.txt"), text.ToString());
                Plugin.Log.LogInfo("Item report (left: mystical item, right: vanilla peer; images in " +
                                   OutputDir + "):\n" + text);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"Item report failed: {e}");
            }
        }

        // ------------------------------------------------------------- text

        private static string Describe(ObjectDB db, GameObject ours, GameObject peer, string peerName)
        {
            var rows = new List<string[]>
            {
                Row("Triangles", ours, peer, p => Triangles(p, false).ToString()),
                Row("Worn triangles", ours, peer, p => Triangles(p, true).ToString()),
                Row("Textures", ours, peer, Textures),
                Row("Damage", ours, peer, p => Damage(Shared(p).m_damages)),
                Row("Damage/level", ours, peer, p => Damage(Shared(p).m_damagesPerLevel)),
                Row("Durability", ours, peer, p => $"{Shared(p).m_maxDurability:0} (+{Shared(p).m_durabilityPerLevel:0}/level)"),
                Row("Weight", ours, peer, p => $"{Shared(p).m_weight:0.#}"),
                Row("Armor", ours, peer, p => IsArmor(p) ? $"{Shared(p).m_armor:0.#} (+{Shared(p).m_armorPerLevel:0.#}/level)" : "-"),
                Row("Recipe", ours, peer, p => Recipe(db, p)),
                Row("Station", ours, peer, p => Station(db, p)),
            };

            var sb = new StringBuilder();
            sb.AppendLine($"== {ours.name} vs {(peer ? peer.name : peerName + " (not found)")} ==");
            var w0 = rows.Max(r => r[0].Length) + 2;
            var w1 = rows.Max(r => r[1].Length) + 3;
            foreach (var r in rows)
            {
                if (r[1] == r[2] && (r[1] == "0" || r[1] == "-" || r[1].StartsWith("0 (+0"))) continue; // nothing to compare
                sb.AppendLine(r[0].PadRight(w0) + r[1].PadRight(w1) + r[2]);
            }
            sb.AppendLine();
            return sb.ToString();
        }

        private static string[] Row(string label, GameObject ours, GameObject peer, System.Func<GameObject, string> value) =>
            new[] { label, value(ours), peer ? value(peer) : "?" };

        private static ItemDrop.ItemData.SharedData Shared(GameObject prefab) =>
            prefab.GetComponent<ItemDrop>().m_itemData.m_shared;

        private static bool IsArmor(GameObject prefab)
        {
            var type = Shared(prefab).m_itemType;
            return type == ItemDrop.ItemData.ItemType.Helmet || type == ItemDrop.ItemData.ItemType.Chest ||
                   type == ItemDrop.ItemData.ItemType.Legs || type == ItemDrop.ItemData.ItemType.Shoulder;
        }

        private static int Triangles(GameObject prefab, bool worn)
        {
            var count = 0;
            foreach (var r in Renderers(prefab, worn))
            {
                var mesh = MeshOf(r);
                if (!mesh) continue;
                for (var i = 0; i < mesh.subMeshCount; i++) count += (int)mesh.GetIndexCount(i) / 3;
            }
            return count;
        }

        private static string Textures(GameObject prefab)
        {
            var sizes = new Dictionary<string, int>();
            var seen = new HashSet<Texture>();
            foreach (var r in Renderers(prefab, false).Concat(Renderers(prefab, true)))
            foreach (var m in r.sharedMaterials)
            {
                if (!m || !m.HasProperty("_MainTex")) continue;
                var t = m.GetTexture("_MainTex");
                if (!t || !seen.Add(t)) continue;
                var key = $"{t.width}x{t.height}";
                sizes[key] = sizes.TryGetValue(key, out var n) ? n + 1 : 1;
            }
            return sizes.Count == 0 ? "none" : string.Join(", ", sizes.Select(kv => kv.Value > 1 ? $"{kv.Key} x{kv.Value}" : kv.Key));
        }

        private static string Damage(HitData.DamageTypes damage)
        {
            object boxed = damage;
            var parts = typeof(HitData.DamageTypes).GetFields()
                .Where(f => f.FieldType == typeof(float) && f.Name.StartsWith("m_") && (float)f.GetValue(boxed) != 0f)
                .Select(f => $"{f.Name.Substring(2)}:{(float)f.GetValue(boxed):0.#}");
            var s = string.Join(" ", parts);
            return s.Length == 0 ? "-" : s;
        }

        private static Recipe RecipeOf(ObjectDB db, GameObject prefab)
        {
            var drop = prefab.GetComponent<ItemDrop>();
            return db.m_recipes.Find(r => r && r.m_item == drop && r.m_enabled) ??
                   db.m_recipes.Find(r => r && r.m_item && r.m_item.name == prefab.name);
        }

        private static string Recipe(ObjectDB db, GameObject prefab)
        {
            var recipe = RecipeOf(db, prefab);
            if (!recipe) return "not craftable";
            return string.Join(" ", recipe.m_resources.Where(r => r.m_resItem)
                .Select(r => $"{r.m_resItem.name}:{r.m_amount}(+{r.m_amountPerLevel})"));
        }

        private static string Station(ObjectDB db, GameObject prefab)
        {
            var recipe = RecipeOf(db, prefab);
            if (!recipe) return "-";
            return (recipe.m_craftingStation ? recipe.m_craftingStation.name : "by hand") + " level " + recipe.m_minStationLevel;
        }

        // ------------------------------------------------------------- meshes

        /// <summary>
        /// Visible mesh renderers: enabled, not switched off below the prefab root, and only the most
        /// detailed LOD. Worn = the skinned model under "attach_skin" that shows on the player.
        /// </summary>
        private static List<Renderer> Renderers(GameObject prefab, bool worn)
        {
            var lowerLods = new HashSet<Renderer>();
            foreach (var group in prefab.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = group.GetLODs();
                for (var i = 1; i < lods.Length; i++)
                    foreach (var r in lods[i].renderers)
                        if (r) lowerLods.Add(r);
            }

            var list = new List<Renderer>();
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (!r.enabled || lowerLods.Contains(r) || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                var underSkin = false;
                var hidden = false;
                for (var t = r.transform; t && t != prefab.transform; t = t.parent)
                {
                    if (t.name == "attach_skin") underSkin = true;
                    else if (!t.gameObject.activeSelf) hidden = true;
                }
                if (!hidden && underSkin == worn) list.Add(r);
            }
            return list;
        }

        private static Mesh MeshOf(Renderer r) =>
            r is SkinnedMeshRenderer skinned ? skinned.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;

        /// <summary>A components-free copy of the item's visible model, so nothing in it runs.</summary>
        private static GameObject VisualCopy(GameObject prefab)
        {
            var root = new GameObject("ItemReport_" + prefab.name) { layer = Layer };
            root.transform.position = Stage;
            var toRoot = prefab.transform.worldToLocalMatrix;
            foreach (var r in Renderers(prefab, false))
            {
                var mesh = MeshOf(r);
                if (!mesh) continue;
                var m = toRoot * r.transform.localToWorldMatrix;
                var go = new GameObject(r.name) { layer = Layer };
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = m.GetColumn(3);
                go.transform.localRotation = m.rotation;
                go.transform.localScale = m.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = r.sharedMaterials;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return root;
        }

        private static Bounds BoundsOf(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.1f);
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }

        // ------------------------------------------------------------- images

        /// <summary>Width of an axis-aligned box seen along a direction perpendicular to <paramref name="dir"/>.</summary>
        private static float Span(Bounds b, Vector3 dir) =>
            Mathf.Abs(b.size.x * dir.x) + Mathf.Abs(b.size.y * dir.y) + Mathf.Abs(b.size.z * dir.z);

        /// <summary>Dark lines between the pairs, so each view's two items read as one cell.</summary>
        private static void DrawGrid(Texture2D image)
        {
            var line = new Color(0.12f, 0.12f, 0.13f);
            for (var i = 1; i < 4; i++)
            for (var t = -3; t < 3; t++)
            for (var p = 0; p < image.width; p++)
            {
                image.SetPixel(p, i * Panel + t, line);
                if (i == 2) image.SetPixel(i * Panel + t, p, line);
            }
        }

        /// <summary>
        /// One PNG, a 4x4 grid of pairs (our item | peer, same scale), read left to right:
        /// front, back / left, right / top, bottom / three-quarter front, three-quarter back.
        /// Views follow the peer's shape: front faces its broad side, top looks down its length.
        /// </summary>
        private static void Render(GameObject oursPrefab, GameObject peerPrefab, string path)
        {
            var copies = new[] { VisualCopy(oursPrefab), VisualCopy(peerPrefab) };
            var camGo = new GameObject("ItemReportCamera");
            var lightGo = new GameObject("ItemReportLight");
            var rt = new RenderTexture(Panel, Panel, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var image = new Texture2D(Panel * 4, Panel * 4, TextureFormat.RGB24, false);
            var ambientMode = RenderSettings.ambientMode;
            var ambient = RenderSettings.ambientLight;
            var previousActive = RenderTexture.active;
            try
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.6f);

                var cam = camGo.AddComponent<Camera>();
                cam.enabled = false;
                cam.orthographic = true;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.32f, 0.33f, 0.35f);
                cam.cullingMask = 1 << Layer;
                cam.renderingPath = RenderingPath.Forward;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 100f;
                cam.targetTexture = rt;

                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.2f;
                light.color = new Color(1f, 0.97f, 0.92f);
                light.cullingMask = 1 << Layer;
                light.shadows = LightShadows.None;

                var bounds = copies.Select(BoundsOf).ToArray();
                var size = bounds[1].size;
                var axes = new List<int> { 0, 1, 2 };
                axes.Sort((a, b) => size[b].CompareTo(size[a]));
                Vector3 Axis(int i) { var v = Vector3.zero; v[axes[i]] = 1f; return v; }

                Vector3 length = Axis(0), width = Axis(1), depth = Axis(2);
                var views = new[] // look direction, image up
                {
                    (depth, length), (-depth, length),                                  // front, back
                    (width, length), (-width, length),                                  // left, right
                    (-length, width), (length, width),                                  // top, bottom
                    ((depth + width * 0.8f - length * 0.4f).normalized, length),         // three-quarter front
                    ((-depth - width * 0.8f - length * 0.4f).normalized, length),        // three-quarter back
                };
                for (var v = 0; v < views.Length; v++)
                {
                    var (look, up) = views[v];
                    up = (up - Vector3.Dot(up, look) * look).normalized;
                    var right = Vector3.Cross(up, look);
                    var extent = bounds.Max(b => Mathf.Max(Span(b, up), Span(b, right)));
                    cam.orthographicSize = extent * 0.55f;
                    light.transform.rotation = Quaternion.LookRotation(look - up * 0.6f + right * 0.4f, up);

                    for (var col = 0; col < 2; col++)
                    {
                        for (var k = 0; k < copies.Length; k++) copies[k].SetActive(k == col);
                        var b = bounds[col];
                        cam.transform.SetPositionAndRotation(b.center - look * (b.extents.magnitude + 5f),
                            Quaternion.LookRotation(look, up));
                        cam.Render();
                        RenderTexture.active = rt;
                        image.ReadPixels(new Rect(0, 0, Panel, Panel), (v % 2 * 2 + col) * Panel, (3 - v / 2) * Panel);
                    }
                }
                DrawGrid(image);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previousActive;
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambient;
                // Immediately: a deferred Destroy would leave these in the next item's pictures
                foreach (var c in copies) Object.DestroyImmediate(c);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(lightGo);
                Object.DestroyImmediate(image);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }
    }
}

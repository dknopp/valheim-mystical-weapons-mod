using System.Collections.Generic;
using UnityEngine;

namespace MysticalWeapons
{
    internal static class ModelUtil
    {
        /// <summary>Hides the cloned weapon's renderers and puts our model under its "attach" child.</summary>
        public static void ReplaceModel(GameObject prefab, MysticalWeapon weapon, Mesh mesh)
        {
            var attach = FindDeep(prefab.transform, "attach") ?? prefab.transform;

            Material baseMat = null;
            var hasBounds = false;
            var bounds = new Bounds();
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                if (!baseMat) baseMat = r.sharedMaterial;
                r.enabled = false;

                // Original model bounds in attach space, used to line our grip up with its handle
                var mf = r.GetComponent<MeshFilter>();
                var m = mf ? mf.sharedMesh : (r as SkinnedMeshRenderer)?.sharedMesh;
                if (!m || !r.transform.IsChildOf(attach)) continue;
                var mb = m.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = attach.InverseTransformPoint(r.transform.TransformPoint(corner));
                    if (!hasBounds) { bounds = new Bounds(p, Vector3.zero); hasBounds = true; }
                    else bounds.Encapsulate(p);
                }
            }
            Plugin.Log.LogInfo($"{weapon.PrefabName}: original model bounds center {bounds.center}, size {bounds.size}");

            var materials = new Material[weapon.Materials.Length];
            for (var i = 0; i < materials.Length; i++)
                materials[i] = MakeMaterial(baseMat, weapon.Materials[i], $"{weapon.PrefabName}_mat{i}");

            var model = new GameObject(weapon.PrefabName + "_model");
            model.layer = attach.gameObject.layer;
            model.transform.SetParent(attach, false);
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            model.AddComponent<MeshRenderer>().sharedMaterials = materials;

            var rotation = weapon.AutoAlign.Value && hasBounds ? AlignToHandle(bounds) : Quaternion.identity;
            model.transform.localRotation = rotation * Quaternion.Euler(weapon.ModelRotation.Value);
            model.transform.localPosition = weapon.ModelOffset.Value;
            model.transform.localScale = Vector3.one * weapon.ModelScale.Value;

            weapon.DecorateModel(model);
        }

        /// <summary>
        /// Model +Y along the original's longest axis (pointing the way most of the weapon extends
        /// from the hand), model +X along the second longest axis.
        /// </summary>
        private static Quaternion AlignToHandle(Bounds b)
        {
            var size = b.size;
            var axes = new List<int> { 0, 1, 2 };
            axes.Sort((a, c) => size[c].CompareTo(size[a]));
            var up = Vector3.zero;
            up[axes[0]] = b.center[axes[0]] < 0f ? -1f : 1f;
            var right = Vector3.zero;
            right[axes[1]] = 1f;
            return Quaternion.LookRotation(Vector3.Cross(right, up), up);
        }

        private static Material MakeMaterial(Material baseMat, MaterialSpec spec, string name)
        {
            var mat = baseMat ? new Material(baseMat) : new Material(Shader.Find("Standard"));
            mat.name = name;
            foreach (var prop in new[] { "_MainTex", "_BumpMap", "_MetallicGlossMap", "_EmissionMap" })
                if (mat.HasProperty(prop)) mat.SetTexture(prop, null);
            mat.color = spec.Color;
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", spec.Gloss);
            if (spec.Emission.maxColorComponent > 0f && mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", spec.Emission);
            }
            return mat;
        }

        public static Light AddGlow(GameObject model, Vector3 localPos, Color color, float range, float intensity)
        {
            var go = new GameObject("glow");
            go.transform.SetParent(model.transform, false);
            go.transform.localPosition = localPos;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            return light;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform child in t)
            {
                var found = FindDeep(child, name);
                if (found) return found;
            }
            return null;
        }

        // ---------------------------------------------------------------- icon

        /// <summary>Rasterises the model, seen from the front, into an inventory icon.</summary>
        public static Sprite RenderIcon(MeshBuilder b, MaterialSpec[] mats, float angleDeg, int size = 128)
        {
            var rot = Quaternion.Euler(0f, 0f, angleDeg);
            var pts = new Vector3[b.Verts.Count];
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (var i = 0; i < pts.Length; i++)
            {
                pts[i] = rot * b.Verts[i];
                min = Vector2.Min(min, pts[i]);
                max = Vector2.Max(max, pts[i]);
            }

            const float margin = 4f;
            var extent = max - min;
            var scale = (size - 2 * margin) / Mathf.Max(extent.x, extent.y);
            var offset = new Vector2(size, size) * 0.5f - (min + extent * 0.5f) * scale;

            var color = new Color[size * size];
            var depth = new float[size * size];
            for (var i = 0; i < depth.Length; i++) depth[i] = float.MinValue;
            var light = new Vector3(-0.4f, 0.6f, 0.7f).normalized;

            for (var sub = 0; sub < b.Tris.Length; sub++)
            {
                var tris = b.Tris[sub];
                for (var t = 0; t < tris.Count; t += 3)
                {
                    var n = rot * b.Normals[tris[t]];
                    if (n.z <= 0.001f) continue; // facing away from the camera

                    var shade = 0.55f + 0.45f * Mathf.Max(0f, Vector3.Dot(n, light));
                    var c = mats[sub].Color * shade + mats[sub].Emission * 0.6f;
                    c.a = 1f;

                    Vector3 p0 = pts[tris[t]], p1 = pts[tris[t + 1]], p2 = pts[tris[t + 2]];
                    Vector2 a = (Vector2)p0 * scale + offset, bb = (Vector2)p1 * scale + offset, cc = (Vector2)p2 * scale + offset;
                    FillTriangle(a, bb, cc, p0.z, p1.z, p2.z, c, color, depth, size);
                }
            }

            // Dark outline around the silhouette
            var outlined = (Color[])color.Clone();
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                if (color[y * size + x].a > 0f) continue;
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= size || ny >= size || color[ny * size + nx].a <= 0f) continue;
                    outlined[y * size + x] = new Color(0.04f, 0.04f, 0.05f, 0.9f);
                }
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels(outlined);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        /// <summary>
        /// Copies a vanilla icon out of its (unreadable) atlas via the GPU and recolours it:
        /// greys are multiplied by <paramref name="tint"/>, warm glowing pixels become <paramref name="glow"/>.
        /// Returns null if the copy fails.
        /// </summary>
        public static Sprite RecolorIcon(Sprite source, Color tint, Color glow)
        {
            try
            {
                var src = source.texture;
                var rect = source.textureRect;
                var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
                var previous = RenderTexture.active;
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                var tex = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGBA32, false);
                tex.ReadPixels(rect, 0, 0);
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);

                var pixels = tex.GetPixels();
                for (var i = 0; i < pixels.Length; i++)
                {
                    var c = pixels[i];
                    var max = c.maxColorComponent;
                    var sat = max - Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                    var grey = c.grayscale * 1.25f;
                    var plate = new Color(grey * tint.r, grey * tint.g, grey * tint.b);
                    var glowing = glow.maxColorComponent > 0f && sat > 0.3f && c.r > c.b;
                    var result = glowing ? Color.Lerp(plate, glow * (0.6f + max), Mathf.Clamp01(sat * 1.5f)) : plate;
                    result.a = c.a;
                    pixels[i] = result;
                }
                tex.SetPixels(pixels);
                tex.Apply();
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"Could not recolour icon {source.name}: {e.Message}");
                return null;
            }
        }

        private static void FillTriangle(Vector2 a, Vector2 b, Vector2 c, float za, float zb, float zc,
            Color col, Color[] color, float[] depth, int size)
        {
            var area = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
            if (Mathf.Abs(area) < 1e-6f) return;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
            int x1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
            int y1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));

            for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                var w0 = ((b.x - p.x) * (c.y - p.y) - (c.x - p.x) * (b.y - p.y)) / area;
                var w1 = ((c.x - p.x) * (a.y - p.y) - (a.x - p.x) * (c.y - p.y)) / area;
                var w2 = 1f - w0 - w1;
                if (w0 < -0.02f || w1 < -0.02f || w2 < -0.02f) continue;
                var z = w0 * za + w1 * zb + w2 * zc;
                var i = y * size + x;
                if (z <= depth[i]) continue;
                depth[i] = z;
                color[i] = col;
            }
        }
    }
}

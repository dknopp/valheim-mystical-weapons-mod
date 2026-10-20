using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MysticalWeapons
{
    /// <summary>How a replacement model is positioned inside the cloned item.</summary>
    internal struct ModelPlacement
    {
        /// <summary>Rotate model +Y onto the original's longest axis (weapons: grip along the handle).</summary>
        public bool AutoAlign;
        /// <summary>Scale the model to the original's largest dimension and centre it on the original.</summary>
        public bool FitToOriginal;
        public Vector3 Rotation;
        public Vector3 Offset;
        public float Scale;
    }

    internal static class ModelUtil
    {
        /// <summary>Folder next to the plugin DLL where edited models can be dropped.</summary>
        public static string CustomModelDir =>
            Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "", "models");

        /// <summary>
        /// Loads "{prefabName}.obj" (and its textures) from <see cref="CustomModelDir"/>.
        /// Returns null if there is none or it fails to load.
        /// </summary>
        public static MeshBuilder LoadCustomModel(string prefabName, int submeshes) =>
            LoadModelFile(Path.Combine(CustomModelDir, prefabName + ".obj"), submeshes, prefabName);

        /// <summary>Textures shared between model files (e.g. one per Shardplate material) load once.</summary>
        private static readonly Dictionary<string, Texture2D> TextureCache = new Dictionary<string, Texture2D>();

        /// <summary>Forget loaded textures so the next load re-reads them from disk.</summary>
        public static void ClearTextureCache() => TextureCache.Clear();

        /// <summary>Loads an OBJ (and its textures, cached by path). Returns null if missing or broken.</summary>
        public static MeshBuilder LoadModelFile(string path, int submeshes, string label)
        {
            if (!File.Exists(path)) return null;
            try
            {
                var builder = new MeshBuilder(submeshes);
                var texturePaths = ObjImport.Load(path, builder);
                builder.Textures = new Texture2D[submeshes];
                foreach (var kv in texturePaths)
                {
                    var full = Path.GetFullPath(kv.Value);
                    if (!TextureCache.TryGetValue(full, out var tex))
                    {
                        if (!File.Exists(full))
                        {
                            Plugin.Log.LogWarning($"{label}: texture {full} not found");
                            continue;
                        }
                        tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = Path.GetFileName(full) };
                        if (!LoadImage(tex, File.ReadAllBytes(full))) continue;
                        TextureCache[full] = tex;
                    }
                    builder.Textures[kv.Key] = tex;
                }
                Plugin.Log.LogInfo($"{label}: using custom model {path} ({builder.TriangleCount} triangles, " +
                                   $"{texturePaths.Count} textures)");
                return builder;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"{label}: could not load {path}, using the default model. {e.Message}");
                return null;
            }
        }

        /// <summary>Loads a PNG/JPG as an inventory icon sprite.</summary>
        public static Sprite LoadSprite(string path)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = Path.GetFileName(path) };
            if (!LoadImage(tex, File.ReadAllBytes(path))) return null;
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        /// <summary>
        /// Gives a GameObject a mesh and materials built from a <see cref="MeshBuilder"/>: a skinned
        /// renderer when the model has bone weights (bones are bound when Valheim attaches it), else a
        /// plain one.
        /// </summary>
        public static void AddRenderer(GameObject go, MeshBuilder builder, MaterialSpec[] specs, Material baseMat, string name)
        {
            var mesh = builder.ToMesh(name);
            var materials = new Material[specs.Length];
            for (var i = 0; i < materials.Length; i++)
                materials[i] = MakeMaterial(baseMat, specs[i], $"{name}_mat{i}",
                    builder.Textures != null && i < builder.Textures.Length ? builder.Textures[i] : null);
            if (builder.IsSkinned)
            {
                var smr = go.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;
                smr.sharedMaterials = materials;
                go.AddComponent<SkinBoneNames>().Names = builder.BoneNames.ToArray();
            }
            else
            {
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = materials;
            }
            if (materials.Length > 0) LogMaterial(name, baseMat, materials[0]);
        }

        /// <summary>Logs where a model's material came from and its main shader settings.</summary>
        private static void LogMaterial(string name, Material source, Material mat)
        {
            float F(string prop) => mat.HasProperty(prop) ? mat.GetFloat(prop) : float.NaN;
            Plugin.Log.LogInfo($"{name}: material from {(source ? source.name : "none")}, shader {mat.shader.name}, " +
                               $"metallic {F("_Metallic"):0.##}, gloss {F("_Glossiness"):0.##}, " +
                               $"emission {(mat.IsKeywordEnabled("_EMISSION") ? "on" : "off")}");
        }

        /// <summary>
        /// ImageConversion.LoadImage(Texture2D, byte[]). Called through reflection because Unity 6 also
        /// has a ReadOnlySpan overload whose type lives in Unity's mscorlib, which the compiler cannot see.
        /// </summary>
        private static bool LoadImage(Texture2D tex, byte[] data)
        {
            var method = typeof(ImageConversion).GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
            return method != null && (bool)method.Invoke(null, new object[] { tex, data });
        }

        /// <summary>
        /// Recolours a (readable) texture in place: each pixel's brightness is multiplied by <paramref name="multiply"/>
        /// (components may exceed 1 to brighten; the source colours are dropped so stray hues like red
        /// scratches take on the tint), then <paramref name="glow"/> is added - a little everywhere
        /// and strongly on bright details such as glyphs and edges - scaled by <paramref name="glowAmount"/>.
        /// </summary>
        public static void GradeTexture(Texture2D tex, Color multiply, Color glow, float glowAmount)
        {
            var px = tex.GetPixels();
            for (var i = 0; i < px.Length; i++)
            {
                var c = px[i];
                var grey = c.grayscale;
                var highlight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.7f, grey));
                var g = glow * (glowAmount * (0.15f + 0.85f * highlight));
                px[i] = new Color(
                    Mathf.Clamp01(grey * multiply.r + g.r),
                    Mathf.Clamp01(grey * multiply.g + g.g),
                    Mathf.Clamp01(grey * multiply.b + g.b),
                    c.a);
            }
            tex.SetPixels(px);
            tex.Apply(true);
        }

        /// <summary>Textured parts show their texture as-is: their base colour becomes white.</summary>
        public static MaterialSpec[] WithTextures(MaterialSpec[] specs, MeshBuilder builder)
        {
            var result = (MaterialSpec[])specs.Clone();
            for (var i = 0; i < result.Length; i++)
                if (builder.Textures != null && i < builder.Textures.Length && builder.Textures[i])
                    result[i].Color = Color.white;
            return result;
        }

        /// <summary>Hides the cloned item's renderers and puts our model under its "attach" child.</summary>
        /// <param name="baseMatOverride">Material whose shader settings (metallic, gloss, emission...) to start
        /// from; by default the cloned item's own.</param>
        public static GameObject ReplaceModel(GameObject prefab, string name, MeshBuilder builder,
            MaterialSpec[] specs, ModelPlacement place, Material baseMatOverride = null)
        {
            var attach = FindDeep(prefab.transform, "attach") ?? prefab.transform;

            var baseMat = baseMatOverride;
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
            Plugin.Log.LogInfo($"{name}: original model bounds center {bounds.center}, size {bounds.size}");

            var mesh = builder.ToMesh(name);
            var materials = new Material[specs.Length];
            for (var i = 0; i < materials.Length; i++)
                materials[i] = MakeMaterial(baseMat, specs[i], $"{name}_mat{i}",
                    builder.Textures != null && i < builder.Textures.Length ? builder.Textures[i] : null);

            if (materials.Length > 0) LogMaterial(name, baseMat, materials[0]);

            var model = new GameObject(name + "_model");
            model.layer = attach.gameObject.layer;
            model.transform.SetParent(attach, false);
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            model.AddComponent<MeshRenderer>().sharedMaterials = materials;

            var rotation = place.AutoAlign && hasBounds ? AlignToHandle(bounds) : Quaternion.identity;
            rotation *= Quaternion.Euler(place.Rotation);
            var scale = place.Scale;
            var position = place.Offset;
            if (place.FitToOriginal && hasBounds)
            {
                // Uniformly scale our rotated mesh to the original's largest dimension, centred on it
                var ours = RotatedBounds(mesh.bounds, rotation);
                scale *= bounds.size.MaxComponent() / Mathf.Max(ours.size.MaxComponent(), 1e-4f);
                position += bounds.center - ours.center * scale;
                Plugin.Log.LogInfo($"{name}: fitted custom model at {position}, scale {scale:0.###}");
            }
            model.transform.localRotation = rotation;
            model.transform.localPosition = position;
            model.transform.localScale = Vector3.one * scale;
            return model;
        }

        private static Bounds RotatedBounds(Bounds b, Quaternion rotation)
        {
            var result = new Bounds(rotation * b.center, Vector3.zero);
            for (var i = 0; i < 8; i++)
                result.Encapsulate(rotation * (b.center + Vector3.Scale(b.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            return result;
        }

        private static float MaxComponent(this Vector3 v) => Mathf.Max(v.x, Mathf.Max(v.y, v.z));

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

        private static Material MakeMaterial(Material baseMat, MaterialSpec spec, string name, Texture2D texture)
        {
            var mat = baseMat ? new Material(baseMat) : new Material(Shader.Find("Standard"));
            mat.name = name;
            foreach (var prop in new[] { "_MainTex", "_BumpMap", "_MetallicGlossMap", "_EmissionMap" })
                if (mat.HasProperty(prop)) mat.SetTexture(prop, null);
            if (texture && mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", texture);
            mat.color = spec.Color;
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", spec.Gloss);
            if (spec.Emission.maxColorComponent > 0f && mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", spec.Emission);
                // Textured: glow follows the texture, so bright details (glyphs, edges) glow most
                if (texture && mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", texture);
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
        public static Sprite RenderIcon(MeshBuilder b, MaterialSpec[] mats, float angleDeg, int size = 128,
            Quaternion? view = null)
        {
            // view turns the model to face the camera (looking down -Z), angleDeg then spins the icon
            var rot = Quaternion.Euler(0f, 0f, angleDeg) * (view ?? Quaternion.identity);
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
                var texture = b.Textures != null && sub < b.Textures.Length ? b.Textures[sub] : null;
                for (var t = 0; t < tris.Count; t += 3)
                {
                    var n = rot * (b.Normals[tris[t]] + b.Normals[tris[t + 1]] + b.Normals[tris[t + 2]]).normalized;
                    if (n.z <= 0.001f) continue; // facing away from the camera

                    var shade = 0.55f + 0.45f * Mathf.Max(0f, Vector3.Dot(n, light));
                    var c = mats[sub].Color * shade + mats[sub].Emission * 0.6f;
                    c.a = 1f;

                    Vector3 p0 = pts[tris[t]], p1 = pts[tris[t + 1]], p2 = pts[tris[t + 2]];
                    Vector2 a = (Vector2)p0 * scale + offset, bb = (Vector2)p1 * scale + offset, cc = (Vector2)p2 * scale + offset;
                    var textured = texture ? new TexturedTri
                    {
                        Texture = texture, Ua = b.Uvs[tris[t]], Ub = b.Uvs[tris[t + 1]], Uc = b.Uvs[tris[t + 2]], Tint = c
                    } : (TexturedTri?)null;
                    FillTriangle(a, bb, cc, p0.z, p1.z, p2.z, c, color, depth, size, textured);
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

        private struct TexturedTri
        {
            public Texture2D Texture;
            public Vector2 Ua, Ub, Uc;
            public Color Tint;
        }

        /// <summary>
        /// Copies any texture (even an unreadable game texture) via the GPU and recolours each pixel
        /// with <paramref name="recolor"/>. Returns null if the copy fails.
        /// </summary>
        public static Texture2D RecolorTexture(Texture source, System.Func<Color, Color> recolor)
        {
            try
            {
                var rt = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32);
                var previous = RenderTexture.active;
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var tex = new Texture2D(source.width, source.height, TextureFormat.RGBA32, true) { name = source.name + "_recoloured" };
                tex.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                var px = tex.GetPixels();
                for (var i = 0; i < px.Length; i++) px[i] = recolor(px[i]);
                tex.SetPixels(px);
                tex.Apply(true);
                return tex;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"Could not recolour texture {source.name}: {e.Message}");
                return null;
            }
        }

        private static void FillTriangle(Vector2 a, Vector2 b, Vector2 c, float za, float zb, float zc,
            Color col, Color[] color, float[] depth, int size, TexturedTri? tex = null)
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
                if (tex is TexturedTri t)
                {
                    var uv = t.Ua * w0 + t.Ub * w1 + t.Uc * w2;
                    var texel = t.Texture.GetPixelBilinear(uv.x, uv.y) * t.Tint;
                    texel.a = 1f;
                    color[i] = texel;
                }
                else color[i] = col;
            }
        }
    }
}

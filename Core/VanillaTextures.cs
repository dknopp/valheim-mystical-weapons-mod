using System;
using System.Collections.Generic;
using UnityEngine;

namespace MysticalWeapons
{
    /// <summary>
    /// Textures borrowed from vanilla items while the game runs (never copied into the repo).
    /// Raw-material items are used because their whole texture is one material; a weapon's texture
    /// is an atlas of hilt, blade and so on, which would show up as random patches on our shapes.
    /// </summary>
    internal static class VanillaTextures
    {
        public const string Iron = "Iron";
        public const string Leather = "LeatherScraps";
        public const string Wood = "Wood";
        public const string FineWood = "FineWood";
        public const string Bark = "ElderBark";
        public const string Stone = "Stone";
        public const string Flametal = "FlametalNew";

        private const int MaxSize = 512;

        /// <summary>A borrowed texture as brightness only, scaled so its average is 1.</summary>
        private class Grey
        {
            public int Width, Height;
            public float[] Values;
        }

        private static readonly Dictionary<string, Grey> Greys = new Dictionary<string, Grey>();
        private static readonly Dictionary<string, Texture2D> Tinted = new Dictionary<string, Texture2D>();

        /// <summary>
        /// The texture of vanilla <paramref name="prefab"/>, turned to greyscale and tinted with
        /// <paramref name="tint"/>. Null if the prefab or its texture can't be found.
        /// </summary>
        public static Texture2D Get(string prefab, Color tint, Func<string, GameObject> findVanilla)
        {
            var key = prefab + "|" + ColorUtility.ToHtmlStringRGB(tint);
            if (Tinted.TryGetValue(key, out var cached) && cached) return cached;

            if (!Greys.TryGetValue(prefab, out var grey)) Greys[prefab] = grey = Load(prefab, findVanilla);
            if (grey == null) return null;

            var px = new Color[grey.Values.Length];
            for (var i = 0; i < px.Length; i++)
            {
                var v = grey.Values[i];
                px[i] = new Color(Mathf.Clamp01(tint.r * v), Mathf.Clamp01(tint.g * v), Mathf.Clamp01(tint.b * v), 1f);
            }
            var tex = new Texture2D(grey.Width, grey.Height, TextureFormat.RGBA32, true)
            {
                name = "MysticalWeapons_" + prefab,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear
            };
            tex.SetPixels(px);
            tex.Apply(true);
            Tinted[key] = tex;
            return tex;
        }

        /// <summary>
        /// Lays vanilla <paramref name="prefab"/>'s grain over a (readable) texture in place, repeated
        /// <paramref name="repeats"/> times across it. <paramref name="strength"/> 0 leaves it as is,
        /// 1 multiplies by the full grain; the average brightness stays the same.
        /// </summary>
        public static bool Overlay(Texture2D target, string prefab, float repeats, float strength,
            Func<string, GameObject> findVanilla)
        {
            if (!Greys.TryGetValue(prefab, out var grey)) Greys[prefab] = grey = Load(prefab, findVanilla);
            if (grey == null) return false;

            int w = target.width, h = target.height;
            var px = target.GetPixels();
            for (var y = 0; y < h; y++)
            {
                var gy = (int)(y * repeats * grey.Height / h) % grey.Height;
                for (var x = 0; x < w; x++)
                {
                    var gx = (int)(x * repeats * grey.Width / w) % grey.Width;
                    var m = Mathf.Lerp(1f, grey.Values[gy * grey.Width + gx], strength);
                    var c = px[y * w + x];
                    px[y * w + x] = new Color(Mathf.Clamp01(c.r * m), Mathf.Clamp01(c.g * m), Mathf.Clamp01(c.b * m), c.a);
                }
            }
            target.SetPixels(px);
            target.Apply(true);
            return true;
        }

        private static Grey Load(string prefab, Func<string, GameObject> findVanilla)
        {
            var go = findVanilla?.Invoke(prefab);
            if (!go)
            {
                Plugin.Log.LogWarning($"Texture source '{prefab}' not found");
                return null;
            }

            Texture source = null;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                // The model only: dropped items also carry particle effects (e.g. a sparkle)
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                var mat = r.sharedMaterial;
                if (mat && mat.HasProperty("_MainTex") && mat.mainTexture) { source = mat.mainTexture; break; }
            }
            if (!source)
            {
                Plugin.Log.LogWarning($"Texture source '{prefab}' has no texture");
                return null;
            }

            try
            {
                // Vanilla textures aren't readable: copy through the GPU, at most MaxSize on a side
                var shrink = Mathf.Max(1f, Mathf.Max(source.width, source.height) / (float)MaxSize);
                int w = Mathf.RoundToInt(source.width / shrink), h = Mathf.RoundToInt(source.height / shrink);
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
                var previous = RenderTexture.active;
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);

                var px = copy.GetPixels();
                UnityEngine.Object.Destroy(copy);
                var values = new float[px.Length];
                var sum = 0f;
                for (var i = 0; i < px.Length; i++) sum += values[i] = px[i].grayscale;
                var mean = Mathf.Max(sum / px.Length, 0.01f);
                for (var i = 0; i < values.Length; i++) values[i] /= mean;

                Plugin.Log.LogInfo($"Borrowed texture '{source.name}' ({source.width}x{source.height}) from {prefab}, " +
                                   $"average brightness {mean:0.00}");
                return new Grey { Width = w, Height = h, Values = values };
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not copy texture '{source.name}' from {prefab}: {e.Message}");
                return null;
            }
        }
    }
}

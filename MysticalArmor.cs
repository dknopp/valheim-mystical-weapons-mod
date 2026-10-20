using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using UnityEngine;

namespace MysticalWeapons
{
    /// <summary>
    /// An armor piece. Body armor is skinned to the player's skeleton, so instead of building a
    /// model we keep the vanilla one and recolour it: textures are kept for detail, multiplied by
    /// <see cref="Tint"/>, and any glowing parts are switched to <see cref="Glow"/>.
    ///
    /// Rigid pieces (helmets) can instead use a custom "{PrefabName}.obj" from the plugin's models
    /// folder; it is scaled and centred to fill the vanilla piece's space.
    ///
    /// Body armor can use a folder "models/{PrefabName}/" of "{BoneName}.obj" files instead: each is
    /// a rigid plate group already in that bone's local frame (see tools/blender), attached through
    /// Valheim's "attach_{BoneName}" convention so it follows the bone as the player moves.
    /// </summary>
    internal abstract class MysticalArmor : MysticalItem
    {
        protected abstract float DefaultArmor { get; }
        protected virtual float DefaultArmorPerLevel => 2f;
        protected abstract Color DefaultTint { get; }
        protected virtual Color DefaultGlow => Color.black;
        /// <summary>Rotation that turns a custom model's front towards its icon's camera.</summary>
        protected virtual Vector3 IconView => new Vector3(0f, 200f, 0f);

        // Placement of a custom model inside the vanilla piece (code only, not configurable).
        // It is first scaled to the vanilla piece's largest dimension and centred on it.
        protected virtual Vector3 CustomModelRotation => Vector3.zero;
        protected virtual Vector3 CustomModelOffset => Vector3.zero;
        protected virtual float CustomModelScale => 1f;
        /// <summary>Extra per-axis scale in the model's own axes (e.g. X to widen a helm).</summary>
        protected virtual Vector3 CustomModelStretch => Vector3.one;
        /// <summary>Where the custom model's light sits, in the model's own space (in front of it, so it lights the outside).</summary>
        protected virtual Vector3 CustomModelLightPosition => Vector3.zero;

        /// <summary>
        /// Vanilla prefab whose material a custom model starts from (shader settings like metallic sheen
        /// and emission), so pieces of one set look alike. Null = the cloned item's own material.
        /// </summary>
        protected virtual string CustomModelMaterialSource => null;

        protected virtual float DefaultCustomBrightness => 1f;
        protected virtual float DefaultCustomGlow => 0f;
        protected virtual float DefaultCustomLight => 0f;

        private ConfigEntry<float> _armor;
        private ConfigEntry<float> _armorPerLevel;
        private ConfigEntry<Color> _tint;
        private ConfigEntry<Color> _glow;
        private ConfigEntry<float> _glowIntensity;
        private ConfigEntry<float> _customBrightness;
        private ConfigEntry<float> _customGlow;
        private ConfigEntry<float> _customLight;

        protected Color Tint => _tint.Value;
        protected Color Glow => _glow.Value;

        public override void BindConfig(ConfigFile cfg)
        {
            base.BindConfig(cfg);
            var s = PrefabName;
            _armor = cfg.Bind(s, "Armor", DefaultArmor, "Base armor value.");
            _armorPerLevel = cfg.Bind(s, "ArmorPerLevel", DefaultArmorPerLevel, "Extra armor per upgrade level.");
            _tint = cfg.Bind(s, "Tint", DefaultTint, "Colour multiplied over the armor textures (RGBA hex).");
            _glow = cfg.Bind(s, "Glow", DefaultGlow, "Colour of the armor's glowing parts (RGBA hex, black = no glow).");
            _glowIntensity = cfg.Bind(s, "GlowIntensity", 2.5f,
                "Brightness multiplier for the glow. The vanilla glow texture is tinted by this colour, so it needs a boost.");
            _customBrightness = cfg.Bind(s, "CustomModelBrightness", DefaultCustomBrightness,
                "Only with a textured custom model: the texture is multiplied by Tint x this (above 1 brightens).");
            _customGlow = cfg.Bind(s, "CustomModelGlow", DefaultCustomGlow,
                "Only with a custom model: how strongly it glows in the Glow colour, mostly on bright details. 0 = off.");
            _customLight = cfg.Bind(s, "CustomModelLight", DefaultCustomLight,
                "Only with a custom model: intensity of a soft light in the Glow colour around the piece. 0 = off.");
        }

        protected override void Setup(GameObject prefab, ItemDrop.ItemData.SharedData shared)
        {
            shared.m_armor = _armor.Value;
            shared.m_armorPerLevel = _armorPerLevel.Value;

            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                var mats = r.sharedMaterials;
                for (var i = 0; i < mats.Length; i++) mats[i] = Recolor(mats[i]);
                r.sharedMaterials = mats;
            }
            // Valheim paints the body under chest/leg armor with this material's _ChestTex/_LegsTex
            // textures; recolour those so any gap between plates shows matching under-armor
            if (shared.m_armorMaterial)
            {
                shared.m_armorMaterial = Recolor(shared.m_armorMaterial);
                RecolorBodyPaint(shared.m_armorMaterial);
            }

            // Glowing armor often also carries lights and embers; recolour those too
            foreach (var light in prefab.GetComponentsInChildren<Light>(true)) light.color = Glow;
            foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.startColor = Glow;
            }

            if (shared.m_icons != null && shared.m_icons.Length > 0)
            {
                Icon = ModelUtil.RecolorIcon(shared.m_icons[0], Tint, Glow) ?? shared.m_icons[0];
                shared.m_icons = new[] { Icon };
            }

            if (!UsePerBoneModels(prefab)) UseCustomModel(prefab, shared);
        }

        /// <summary>Most materials a custom model may use (submeshes named "{anything}_{N}").</summary>
        private const int MaxCustomMaterials = 8;

        /// <summary>Textures are shared between pieces and items, so each is recoloured only once.</summary>
        private static readonly HashSet<Texture2D> GradedTextures = new HashSet<Texture2D>();

        /// <summary>
        /// Colour the textures themselves (tint, brighten, glow) rather than relying on material
        /// properties: Valheim's armor shader does not honour _Color or emission for these.
        /// </summary>
        private MaterialSpec[] PrepareCustomMaterials(MeshBuilder builder)
        {
            var specs = new MaterialSpec[builder.Tris.Length];
            for (var i = 0; i < specs.Length; i++)
            {
                specs[i] = new MaterialSpec(Color.white, 0.7f, Glow * (_customGlow.Value * 0.5f));
                var tex = builder.Textures[i];
                if (!tex) specs[i].Color = Tint * _customBrightness.Value;
                else if (GradedTextures.Add(tex))
                    ModelUtil.GradeTexture(tex, Tint * _customBrightness.Value, Glow, _customGlow.Value);
            }
            return specs;
        }

        private Material _pieceBaseMat;
        private int _pieceLayer = -1;

        /// <summary>
        /// Rebuilds this item's custom model from disk (used by the mw_reload dev command). Players
        /// must re-equip to see it; the command does that for the local player.
        /// </summary>
        public override void ReloadCustomModel()
        {
            if (!Prefab) return;
            GradedTextures.Clear();
            // The helm's model lives under its "attach" child; body pieces are attach_* children
            foreach (var t in Prefab.GetComponentsInChildren<Transform>(true))
                if (t && t.name == PrefabName + "_model") Object.DestroyImmediate(t.gameObject);
            var shared = Prefab.GetComponent<ItemDrop>().m_itemData.m_shared;
            if (!UsePerBoneModels(Prefab)) UseCustomModel(Prefab, shared);
        }

        private bool UsePerBoneModels(GameObject prefab)
        {
            var dir = Path.Combine(ModelUtil.CustomModelDir, PrefabName);
            if (!Directory.Exists(dir)) return false;
            var files = Directory.GetFiles(dir, "*.obj");
            if (files.Length == 0) return false;

            // Take the vanilla pieces' shader and layer (remembered for reloads), then drop them so
            // only ours get attached
            var old = new List<GameObject>();
            foreach (Transform child in prefab.transform)
            {
                if (!child.name.StartsWith("attach_")) continue;
                if (!_pieceBaseMat)
                {
                    var r = child.GetComponentInChildren<Renderer>(true);
                    if (r) _pieceBaseMat = r.sharedMaterial;
                    _pieceLayer = child.gameObject.layer;
                }
                old.Add(child.gameObject);
            }
            foreach (var go in old) Object.DestroyImmediate(go);
            var baseMat = _pieceBaseMat;
            var layer = _pieceLayer >= 0 ? _pieceLayer : prefab.layer;

            foreach (var file in files)
            {
                var bone = Path.GetFileNameWithoutExtension(file);
                var builder = ModelUtil.LoadModelFile(file, MaxCustomMaterials, $"{PrefabName}/{bone}");
                if (builder == null) continue;
                // Inactive like vanilla attach pieces: VisEquipment instantiates and activates it on the bone
                var piece = new GameObject("attach_" + bone) { layer = layer };
                piece.SetActive(false);
                piece.transform.SetParent(prefab.transform, false);
                ModelUtil.AddRenderer(piece, builder, PrepareCustomMaterials(builder), baseMat, $"{PrefabName}_{bone}");
            }

            var icon = Path.Combine(dir, "icon.png");
            if (File.Exists(icon))
            {
                var sprite = ModelUtil.LoadSprite(icon);
                if (sprite)
                {
                    Icon = sprite;
                    prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_icons = new[] { sprite };
                }
            }
            Plugin.Log.LogInfo($"{PrefabName}: attached {files.Length} custom pieces from {dir}");
            return true;
        }

        private void UseCustomModel(GameObject prefab, ItemDrop.ItemData.SharedData shared)
        {
            var builder = ModelUtil.LoadCustomModel(PrefabName, 1);
            if (builder == null) return;

            var specs = PrepareCustomMaterials(builder);

            var model = ModelUtil.ReplaceModel(prefab, PrefabName, builder, specs, new ModelPlacement
            {
                FitToOriginal = true,
                Rotation = CustomModelRotation,
                Offset = CustomModelOffset,
                Scale = CustomModelScale,
                Stretch = CustomModelStretch
            }, MaterialFrom(CustomModelMaterialSource));
            if (_customLight.Value > 0f)
                ModelUtil.AddGlow(model, CustomModelLightPosition, Glow, 2.5f, _customLight.Value);
            Icon = ModelUtil.RenderIcon(builder, specs, 0f, view: Quaternion.Euler(IconView));
            shared.m_icons = new[] { Icon };
        }

        private void RecolorBodyPaint(Material mat)
        {
            var tint = Tint * _customBrightness.Value;
            var glow = Glow * (0.15f * _customGlow.Value);
            foreach (var prop in new[] { "_ChestTex", "_LegsTex" })
            {
                if (!mat.HasProperty(prop) || !mat.GetTexture(prop)) continue;
                var tex = ModelUtil.RecolorTexture(mat.GetTexture(prop), c =>
                {
                    var g = c.grayscale;
                    return new Color(Mathf.Clamp01(g * tint.r + glow.r), Mathf.Clamp01(g * tint.g + glow.g),
                        Mathf.Clamp01(g * tint.b + glow.b), c.a);
                });
                if (tex) mat.SetTexture(prop, tex);
            }
        }

        /// <summary>The first renderer material of a vanilla prefab (its worn piece if it has one).</summary>
        private Material MaterialFrom(string prefabName)
        {
            var source = prefabName != null ? FindVanilla?.Invoke(prefabName) : null;
            if (!source) return null;
            foreach (Transform child in source.transform)
                if (child.name.StartsWith("attach_"))
                {
                    var r = child.GetComponentInChildren<Renderer>(true);
                    if (r) return r.sharedMaterial;
                }
            return source.GetComponentInChildren<Renderer>(true)?.sharedMaterial;
        }

        private Material Recolor(Material source)
        {
            if (!source) return source;
            var mat = new Material(source) { name = source.name + "_" + PrefabName };
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", source.GetColor("_Color") * Tint);
            if (mat.HasProperty("_EmissionColor") && source.GetColor("_EmissionColor").maxColorComponent > 0.01f)
                mat.SetColor("_EmissionColor", Glow * _glowIntensity.Value);
            return mat;
        }
    }
}

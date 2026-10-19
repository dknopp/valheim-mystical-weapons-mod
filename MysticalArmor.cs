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
        /// <summary>Where the custom model's light sits, in the model's own space (in front of it, so it lights the outside).</summary>
        protected virtual Vector3 CustomModelLightPosition => Vector3.zero;

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
            // Some armor is partly painted onto the player's body through this material
            if (shared.m_armorMaterial) shared.m_armorMaterial = Recolor(shared.m_armorMaterial);

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

            UseCustomModel(prefab, shared);
        }

        private void UseCustomModel(GameObject prefab, ItemDrop.ItemData.SharedData shared)
        {
            var builder = ModelUtil.LoadCustomModel(PrefabName, 1);
            if (builder == null) return;

            // Colour the texture itself (tint, brighten, glow) rather than relying on material
            // properties: Valheim's armor shader does not necessarily honour _Color or emission
            var specs = new[] { new MaterialSpec(Color.white, 0.7f, Glow * (_customGlow.Value * 0.5f)) };
            if (builder.Textures[0])
                ModelUtil.GradeTexture(builder.Textures[0], Tint * _customBrightness.Value, Glow, _customGlow.Value);
            else
                specs[0].Color = Tint * _customBrightness.Value;

            var model = ModelUtil.ReplaceModel(prefab, PrefabName, builder, specs, new ModelPlacement
            {
                FitToOriginal = true,
                Rotation = CustomModelRotation,
                Offset = CustomModelOffset,
                Scale = CustomModelScale
            });
            if (_customLight.Value > 0f)
                ModelUtil.AddGlow(model, CustomModelLightPosition, Glow, 2.5f, _customLight.Value);
            Icon = ModelUtil.RenderIcon(builder, specs, 0f, view: Quaternion.Euler(IconView));
            shared.m_icons = new[] { Icon };
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

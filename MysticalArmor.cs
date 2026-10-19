using BepInEx.Configuration;
using UnityEngine;

namespace MysticalWeapons
{
    /// <summary>
    /// An armor piece. Body armor is skinned to the player's skeleton, so instead of building a
    /// model we keep the vanilla one and recolour it: textures are kept for detail, multiplied by
    /// <see cref="Tint"/>, and any glowing parts are switched to <see cref="Glow"/>.
    /// </summary>
    internal abstract class MysticalArmor : MysticalItem
    {
        protected abstract float DefaultArmor { get; }
        protected virtual float DefaultArmorPerLevel => 2f;
        protected abstract Color DefaultTint { get; }
        protected virtual Color DefaultGlow => Color.black;

        private ConfigEntry<float> _armor;
        private ConfigEntry<float> _armorPerLevel;
        private ConfigEntry<Color> _tint;
        private ConfigEntry<Color> _glow;
        private ConfigEntry<float> _glowIntensity;

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

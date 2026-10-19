using BepInEx.Configuration;
using UnityEngine;

namespace MysticalWeapons
{
    /// <summary>Colour (and optional glow) for one part of a weapon model.</summary>
    internal struct MaterialSpec
    {
        public Color Color;
        public Color Emission;
        public float Gloss;

        public MaterialSpec(Color color, float gloss = 0.6f, Color emission = default)
        {
            Color = color;
            Gloss = gloss;
            Emission = emission;
        }
    }

    /// <summary>
    /// A weapon (or any other item, see <see cref="HasDamage"/>) whose model is built in code.
    ///
    /// Model convention: the wielder's hand is at the origin, the grip runs along +Y,
    /// the flat of the blade lies in the XY plane and Z is thickness.
    /// </summary>
    internal abstract class MysticalWeapon : MysticalItem
    {
        protected virtual string DefaultDamage => "";
        protected virtual string DefaultDamagePerLevel => "";
        // Model placement inside the cloned item (code only, not configurable)
        protected virtual float ModelScale => 1f;
        protected virtual bool AutoAlign => true;
        protected virtual Vector3 ModelRotation => Vector3.zero;
        protected virtual Vector3 ModelOffset => Vector3.zero;
        /// <summary>False for non-weapons with a code-built model (e.g. trinkets): no damage config.</summary>
        protected virtual bool HasDamage => true;

        /// <summary>Materials, indexed by the submesh numbers used in <see cref="BuildModel"/>.</summary>
        public abstract MaterialSpec[] Materials { get; }
        public abstract void BuildModel(MeshBuilder b);

        /// <summary>Rotation of the model in its inventory icon (degrees, counter-clockwise).</summary>
        public virtual float IconAngle => -45f;

        /// <summary>Extra things the weapon needs inside the model, e.g. lights.</summary>
        public virtual void DecorateModel(GameObject model) { }

        private ConfigEntry<string> _damage;
        private ConfigEntry<string> _damagePerLevel;

        public override void BindConfig(ConfigFile cfg)
        {
            base.BindConfig(cfg);
            var s = PrefabName;
            if (HasDamage)
            {
                _damage = cfg.Bind(s, "Damage", DefaultDamage,
                    "Damage types, e.g. slash:80,pierce:20. Types: blunt slash pierce chop pickaxe fire frost lightning poison spirit");
                _damagePerLevel = cfg.Bind(s, "DamagePerLevel", DefaultDamagePerLevel, "Extra damage per upgrade level.");
            }
        }

        protected override void Setup(GameObject prefab, ItemDrop.ItemData.SharedData shared)
        {
            // A "{PrefabName}.obj" in the plugin's models folder replaces the code-built model
            var builder = ModelUtil.LoadCustomModel(PrefabName, Materials.Length);
            if (builder == null)
            {
                builder = new MeshBuilder(Materials.Length);
                BuildModel(builder);
            }
            var specs = ModelUtil.WithTextures(Materials, builder);
            Icon = ModelUtil.RenderIcon(builder, specs, IconAngle);

            if (HasDamage)
            {
                shared.m_damages = ParseDamage(_damage.Value);
                shared.m_damagesPerLevel = ParseDamage(_damagePerLevel.Value);
            }
            shared.m_icons = new[] { Icon };

            var model = ModelUtil.ReplaceModel(prefab, PrefabName, builder, specs, new ModelPlacement
            {
                AutoAlign = AutoAlign,
                Rotation = ModelRotation,
                Offset = ModelOffset,
                Scale = ModelScale
            });
            DecorateModel(model);
        }

        private static HitData.DamageTypes ParseDamage(string spec)
        {
            object damage = new HitData.DamageTypes();
            foreach (var part in spec.Split(','))
            {
                var kv = part.Split(':');
                if (kv.Length != 2) continue;
                var field = typeof(HitData.DamageTypes).GetField("m_" + kv[0].Trim().ToLowerInvariant());
                if (field != null && float.TryParse(kv[1].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var value))
                    field.SetValue(damage, value);
                else
                    Plugin.Log.LogWarning($"Bad damage entry '{part}'");
            }
            return (HitData.DamageTypes)damage;
        }
    }
}

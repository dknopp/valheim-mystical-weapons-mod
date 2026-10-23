using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace MysticalWeapons
{
    /// <summary>
    /// Colour (and optional glow) for one part of a weapon model, optionally textured with a
    /// texture borrowed from a vanilla item (see <see cref="Borrow"/>).
    /// </summary>
    internal struct MaterialSpec
    {
        public Color Color;
        public Color Emission;
        public float Gloss;
        /// <summary>Vanilla prefab whose texture this part borrows (see <see cref="VanillaTextures"/>), or null.</summary>
        public string Texture;
        /// <summary>How many times the borrowed texture repeats per metre.</summary>
        public float Tiling;
        /// <summary>Gloss used instead of <see cref="Gloss"/> when textured: textures look best matte.</summary>
        public float TexturedGloss;
        /// <summary>Vanilla prefab whose grain is laid over a custom model's own texture, or null.</summary>
        public string Grain;
        /// <summary>How many times the grain repeats across the custom texture.</summary>
        public float GrainRepeats;
        /// <summary>How strongly the grain shows (0 = not at all, 1 = full).</summary>
        public float GrainStrength;

        public MaterialSpec(Color color, float gloss = 0.6f, Color emission = default)
        {
            Color = color;
            Gloss = gloss;
            Emission = emission;
            Texture = null;
            Tiling = 1f;
            TexturedGloss = gloss;
            Grain = null;
            GrainRepeats = 1f;
            GrainStrength = 0f;
        }

        /// <summary>Texture this part with a vanilla item's texture, tinted by <see cref="Color"/>.</summary>
        public MaterialSpec Borrow(string prefab, float tiling, float gloss)
        {
            var spec = this;
            spec.Texture = prefab;
            spec.Tiling = tiling;
            spec.TexturedGloss = gloss;
            return spec;
        }

        /// <summary>
        /// When this part's texture comes from a custom model (models/{PrefabName}.png), lay a vanilla
        /// item's grain over it so it sits closer to Valheim's look. Ignored by code-built models.
        /// </summary>
        public MaterialSpec WithGrain(string prefab, float repeats, float strength)
        {
            var spec = this;
            spec.Grain = prefab;
            spec.GrainRepeats = repeats;
            spec.GrainStrength = strength;
            return spec;
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
        /// <summary>
        /// Opt in to clearing everything the cloned item's material had: every texture, its metal
        /// colour and its glow, so none of the vanilla look shows through. Off: only the main textures
        /// are cleared, as for every item made before this option.
        /// </summary>
        protected virtual bool CleanMaterials => false;
        /// <summary>Cloned from a dual-wield item: also put the model in both hands (see <see cref="ModelUtil.ReplaceSkinnedModel"/>).</summary>
        protected virtual bool DualWield => false;

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
                builder = new MeshBuilder(Materials.Length)
                {
                    ProjectUvs = Plugin.TexturedModels.Value && Materials.Any(m => !string.IsNullOrEmpty(m.Texture))
                };
                BuildModel(builder);
                if (Plugin.TexturedModels.Value) BorrowTextures(builder);
            }
            else if (Plugin.TexturedModels.Value) AddGrain(builder);
            var specs = ModelUtil.WithTextures(Materials, builder);
            Icon = ModelUtil.RenderIcon(builder, specs, IconAngle);

            if (HasDamage)
            {
                shared.m_damages = ParseDamage(_damage.Value);
                shared.m_damagesPerLevel = ParseDamage(_damagePerLevel.Value);
            }
            shared.m_icons = new[] { Icon };

            var place = new ModelPlacement
            {
                AutoAlign = AutoAlign,
                Rotation = ModelRotation,
                Offset = ModelOffset,
                Scale = ModelScale,
                CleanMaterials = CleanMaterials
            };
            var model = ModelUtil.ReplaceModel(prefab, PrefabName, builder, specs, place);
            if (DualWield) ModelUtil.ReplaceSkinnedModel(prefab, PrefabName, model, place);
            DecorateModel(model);
        }

        /// <summary>Gives each part that names a vanilla texture a tinted copy of it.</summary>
        private void BorrowTextures(MeshBuilder builder)
        {
            var specs = Materials;
            builder.Textures = new Texture2D[specs.Length];
            for (var i = 0; i < specs.Length; i++)
            {
                if (string.IsNullOrEmpty(specs[i].Texture)) continue;
                var tex = VanillaTextures.Get(specs[i].Texture, specs[i].Color, FindVanilla);
                if (!tex)
                {
                    Plugin.Log.LogWarning($"{PrefabName}: no texture from '{specs[i].Texture}', part {i} stays a flat colour");
                    continue;
                }
                builder.Textures[i] = tex;
                builder.ScaleUvs(i, specs[i].Tiling);
            }
        }

        /// <summary>Lays vanilla grain over the custom model's textures where a part asks for it.</summary>
        private void AddGrain(MeshBuilder builder)
        {
            var specs = Materials;
            for (var i = 0; i < specs.Length; i++)
            {
                if (string.IsNullOrEmpty(specs[i].Grain) || specs[i].GrainStrength <= 0f) continue;
                var tex = builder.Textures != null && i < builder.Textures.Length ? builder.Textures[i] : null;
                if (!tex) continue;
                if (VanillaTextures.Overlay(tex, specs[i].Grain, specs[i].GrainRepeats, specs[i].GrainStrength, FindVanilla))
                    Plugin.Log.LogInfo($"{PrefabName}: laid '{specs[i].Grain}' grain over part {i} at strength {specs[i].GrainStrength:0.00}");
                else
                    Plugin.Log.LogWarning($"{PrefabName}: no grain from '{specs[i].Grain}', part {i} keeps its own texture");
            }
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

using BepInEx.Configuration;
using UnityEngine;

namespace MysticalWeapons.Weapons
{
    /// <summary>
    /// The Brotherhood's hidden blades (Assassin's Creed): a blade under each wrist, cloned from the Flesh Rippers (Fenris
    /// claws) for their punches and Mountain tier, with the model on the forearms. Unarmed levels faster with them in hand.
    /// With the full Brotherhood set they assassinate (see <see cref="Armor.Assassination"/>); they don't count towards the set.
    /// </summary>
    internal class HiddenBlades : MysticalWeapon
    {
        public const string Id = "BrotherhoodHiddenBlades";
        public override string PrefabName => Id;
        public override string[] SourcePrefabs => new[] { "FistFenrirClaw" };
        public override string DisplayName => "Hidden Blades";
        public override string Description =>
            "A blade under each wrist, unseen until it strikes.\n\nUnarmed levels faster with them in hand.";

        // The Flesh Rippers' total (slash 60, +4 a level), mostly pierce for a stabbing blade
        protected override string DefaultDamage => "slash:20,pierce:40";
        protected override string DefaultDamagePerLevel => "slash:1,pierce:3";
        protected override string DefaultRecipe => "Silver:10,LeatherScraps:6,WolfFang:6";
        protected override int DefaultStationLevel => 3;  // as the Flesh Rippers
        protected override bool DualWield => true;
        protected override bool WristMounted => true;
        protected override bool CleanMaterials => true;
        protected override bool AutoAlign => false;

        private const int Steel = 0, Leather = 1;

        private ConfigEntry<float> _unarmedSkill;
        private SE_Stats _equipEffect;

        public override void BindConfig(ConfigFile cfg)
        {
            base.BindConfig(cfg);
            _unarmedSkill = cfg.Bind(PrefabName, "UnarmedSkillGain", 0.5f,
                "Extra Unarmed skill gain with the blades in hand (0.5 = 50% faster levelling). Restart to apply.");
        }

        public override MaterialSpec[] Materials => new[]
        {
            // Bright, glossy and a faint glint, or the metal goes dark in shade and the blade is lost against the glove
            new MaterialSpec(new Color(0.88f, 0.87f, 0.85f), 0.97f, new Color(0.1f, 0.1f, 0.1f)),   // blade and its housing
            new MaterialSpec(new Color(0.28f, 0.18f, 0.11f), 0.3f),   // leather bracer
        };

        protected override void Setup(GameObject prefab, ItemDrop.ItemData.SharedData shared)
        {
            Plugin.Log.LogInfo($"{PrefabName}: the Flesh Rippers deal {DescribeDamage(shared.m_damages)}, per level " +
                               $"{DescribeDamage(shared.m_damagesPerLevel)}, backstab x{shared.m_backstabBonus:0.#}, skill {shared.m_skillType}, " +
                               $"block {shared.m_blockPower:0.#} (+{shared.m_blockPowerPerLevel:0.#}/level, parry x{shared.m_timedBlockBonus:0.#}); " +
                               $"attacks '{shared.m_attack?.m_attackAnimation}'/'{shared.m_secondaryAttack?.m_attackAnimation}' " +
                               $"({shared.m_animationState}); replacing equip effect '{(shared.m_equipStatusEffect ? shared.m_equipStatusEffect.name : "")}'");
            base.Setup(prefab, shared);
        }

        protected override void Customize(ItemDrop.ItemData.SharedData shared)
        {
            _equipEffect = ScriptableObject.CreateInstance<SE_Stats>();
            _equipEffect.name = "SE_BrotherhoodHiddenBlades";
            _equipEffect.m_name = DisplayName;
            _equipEffect.m_tooltip = "Unarmed levels faster.";
            // No icon: the item's own tooltip says it, and the set's icon is enough on screen
            _equipEffect.m_raiseSkill = Skills.SkillType.Unarmed;
            _equipEffect.m_raiseSkillModifier = _unarmedSkill.Value;
            shared.m_equipStatusEffect = _equipEffect;
            Armor.BrotherhoodPiece.SetTierIcon(4, Icon); // the full set's icon: the blades it unlocks
        }

        public override void RegisterExtras(ObjectDB db)
        {
            if (_equipEffect && !db.m_StatusEffects.Contains(_equipEffect)) db.m_StatusEffects.Add(_equipEffect);
        }

        /// <summary>
        /// Placeholder until a Blender model: a leather bracer round the forearm and a slim blade along its palm side,
        /// reaching well past the fist, as in the hero reference. Wrist space: the wrist at the origin, +Y towards the hand, +Z out of the palm.
        /// </summary>
        public override void BuildModel(MeshBuilder b)
        {
            b.Taper(Leather, -0.2f, -0.03f, 0.05f, 0.043f);
            var first = b.Verts.Count;
            b.Box(Steel, new Rect(-0.016f, -0.17f, 0.032f, 0.14f), 0.008f);
            b.Blade(Steel,
                new MeshBuilder.Station(-0.04f, 0.014f, 0.005f),
                new MeshBuilder.Station(0.31f, 0.012f, 0.005f),
                new MeshBuilder.Station(0.38f, 0f, 0.001f));
            // Lay the housing and blade along the palm side of the bracer
            for (var i = first; i < b.Verts.Count; i++) b.Verts[i] += new Vector3(0f, 0f, 0.055f);
        }

        /// <summary>
        /// The Flesh Rippers' material is fully metallic, which mirrors the dark surroundings (no reflections to catch):
        /// less metal lets the steel's own colour and highlights show. The hands share these materials.
        /// </summary>
        public override void DecorateModel(GameObject model)
        {
            var steel = model.GetComponent<MeshRenderer>().sharedMaterials[Steel];
            if (!steel || !steel.HasProperty("_Metallic")) return;
            steel.SetFloat("_Metallic", 0.5f);
            Plugin.Log.LogInfo($"{PrefabName}: steel metallic lowered to 0.5 so it reads polished");
        }
    }
}

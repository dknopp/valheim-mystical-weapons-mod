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
        // The Unarmed bonus is in the tooltip already, under its equip effect
        public override string Description => "A blade under each wrist, unseen until it strikes.";

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
            // Bright, cool steel with a faint glint, or the metal goes dark in shade and the blade is lost against the glove;
            // a little less than mirror gloss, which reflected the sky and turned it blue
            new MaterialSpec(new Color(0.82f, 0.84f, 0.86f), 0.85f, new Color(0.1f, 0.1f, 0.1f)),   // blade
            new MaterialSpec(new Color(0.28f, 0.18f, 0.11f), 0.3f),   // leather sheath and straps
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
        }

        public override void RegisterExtras(ObjectDB db)
        {
            if (_equipEffect && !db.m_StatusEffects.Contains(_equipEffect)) db.m_StatusEffects.Add(_equipEffect);
        }

        /// <summary>
        /// A short leather sheath on the palm side of the forearm, held by two straps, and a ridged dagger blade sliding
        /// out of it to well past the fist, as in the hero reference. The sheath is leather so it blends into the glove
        /// and the bright steel starts at the wrist (a steel housing read as one very long blade). No full bracer: the
        /// robe paints its own. Code-built, not a Blender model: the blades are a few centimetres on screen.
        /// Wrist space: the wrist at the origin, +Y towards the hand, +Z out of the palm.
        /// </summary>
        public override void BuildModel(MeshBuilder b)
        {
            // Straps wide enough to sit over the robe's own bracer
            b.Taper(Leather, -0.16f, -0.135f, 0.059f, 0.058f, 12);
            b.Taper(Leather, -0.08f, -0.055f, 0.057f, 0.056f, 12);
            var first = b.Verts.Count;
            // Sheath: just big enough to hide the blade's base, sunk into the bracer so only its mouth shows at the wrist
            b.Box(Leather, new Rect(-0.008f, -0.1f, 0.016f, 0.085f), 0.0095f);
            // Blade: a slight leaf before the long taper to the point, stood on its edge (edges out of and into the palm),
            // so the camera behind the player sees its flat, not a line. Turning it any less would need the two arms
            // mirrored, which the wrist frame doesn't do.
            var blade = b.Verts.Count;
            b.Blade(Steel,
                new MeshBuilder.Station(-0.04f, 0.009f, 0.004f),
                new MeshBuilder.Station(0.01f, 0.012f, 0.0045f),
                new MeshBuilder.Station(0.06f, 0.0125f, 0.0045f),
                new MeshBuilder.Station(0.26f, 0.0095f, 0.004f),
                new MeshBuilder.Station(0.33f, 0.0045f, 0.0025f),
                new MeshBuilder.Station(0.37f, 0f, 0.001f));
            var edgeOn = Quaternion.AngleAxis(90f, Vector3.up);
            for (var i = blade; i < b.Verts.Count; i++)
            {
                b.Verts[i] = edgeOn * b.Verts[i];
                b.Normals[i] = edgeOn * b.Normals[i];
            }
            // Lay the sheath and blade along the palm side, half inside the bracer
            for (var i = first; i < b.Verts.Count; i++) b.Verts[i] += new Vector3(0f, 0f, 0.052f);
        }

        /// <summary>
        /// The Flesh Rippers' material is fully metallic, which mirrors the dark surroundings (no reflections to catch):
        /// less metal lets the steel's own colour and highlights show, and none lets the leather look like leather.
        /// The hands share these materials.
        /// </summary>
        public override void DecorateModel(GameObject model)
        {
            var materials = model.GetComponent<MeshRenderer>().sharedMaterials;
            if (!materials[Steel] || !materials[Steel].HasProperty("_Metallic")) return;
            materials[Steel].SetFloat("_Metallic", 0.5f);
            if (materials[Leather]) materials[Leather].SetFloat("_Metallic", 0f);
            Plugin.Log.LogInfo($"{PrefabName}: steel metallic lowered to 0.5 so it reads polished, leather to 0 so it isn't black in shade");
        }
    }
}

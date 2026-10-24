using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace MysticalWeapons.Armor
{
    /// <summary>
    /// The Brotherhood (Assassin's Creed): a stealth set of hood, robe, legs and cape, recoloured from the Eitr-weave
    /// and Embla mage gear and the linen cape. Mountain tier, in line with Fenris. Its bonuses grow with the pieces
    /// worn (see <see cref="ArmorSet"/>); design decisions are in docs/BROTHERHOOD.md.
    /// </summary>
    internal abstract class BrotherhoodPiece : MysticalArmor
    {
        private const string SetName = "Brotherhood";
        private static readonly ArmorSet Set =
            new ArmorSet(SetName, "BrotherhoodHood", "BrotherhoodRobe", "BrotherhoodLegs", "BrotherhoodCape");

        private static SneakEffect _sneakEffect;
        private static ConfigEntry<float> _sneakSpeed;
        private static ConfigEntry<float> _sneakStamina;
        private static ConfigEntry<float> _sneakNoise;
        private static ConfigEntry<float> _sneakSkill;
        private static EagleVision.Effect _eagleVision;
        private static ConfigEntry<bool> _eagleVisionOn;
        private static ConfigEntry<float> _eagleVisionRange;
        private static ConfigEntry<Color> _hostileColour;
        private static ConfigEntry<Color> _starredColour;
        private static ConfigEntry<float> _inSightOpacity;

        // Fenris, the Mountain cloth set, is made at the workbench; the bases' Galdr table can't be built until Mistlands
        protected override string DefaultStation => "piece_workbench";
        protected override int DefaultStationLevel => 2;
        protected override float DefaultArmor => 10f;  // as each Fenris piece
        protected override Color DefaultTint => new Color(0.6f, 0.6f, 0.6f);  // placeholder until our own textures

        public override void BindConfig(ConfigFile cfg)
        {
            base.BindConfig(cfg);
            if (_sneakSpeed != null) return; // the set's own settings, once for all four pieces
            _sneakSpeed = cfg.Bind(SetName, "SneakSpeed", 0.25f,
                "2 pieces: extra speed while sneaking (0.25 = 25% faster). Restart to apply.");
            _sneakStamina = cfg.Bind(SetName, "SneakStaminaCost", -0.5f,
                "2 pieces: change in the stamina sneaking costs (-0.5 = half). Restart to apply.");
            _sneakNoise = cfg.Bind(SetName, "SneakNoise", -0.5f,
                "2 pieces: change in the noise you make (-0.5 = half as loud). Restart to apply.");
            _sneakSkill = cfg.Bind(SetName, "SneakSkillGain", 0.5f,
                "2 pieces: extra Sneak skill gain (0.5 = 50% faster levelling). Restart to apply.");
            _eagleVisionOn = cfg.Bind(SetName, "EagleVision", true,
                "3 pieces: while crouched, nearby hostile creatures glow through walls, on your screen only. Restart to apply.");
            _eagleVisionRange = cfg.Bind(SetName, "EagleVisionRange", 30f,
                "3 pieces: how far away creatures glow, in metres.");
            _hostileColour = cfg.Bind(SetName, "EagleVisionHostileColour", new Color(1f, 0.1f, 0.1f, 0.7f),
                "3 pieces: glow of hostile creatures (alpha = how solid behind walls). Applies the next time you crouch.");
            _starredColour = cfg.Bind(SetName, "EagleVisionStarredColour", new Color(1f, 0.75f, 0.1f, 0.7f),
                "3 pieces: glow of starred hostile creatures (alpha = how solid behind walls). Applies the next time you crouch.");
            _inSightOpacity = cfg.Bind(SetName, "EagleVisionInSightOpacity", 0.1f,
                "3 pieces: how solid the glow is on creatures in plain sight (0 = none, 1 = solid colour), so you can " +
                "still see which way they face. Applies the next time you crouch.");
        }

        protected override void Customize(ItemDrop.ItemData.SharedData shared)
        {
            // Drop what the vanilla bases carry: the mage gear's eitr regeneration and speed penalty, any set
            shared.m_movementModifier = 0f;
            shared.m_eitrRegenModifier = 0f;
            shared.m_equipStatusEffect = null;
            shared.m_damageModifiers = new List<HitData.DamageModPair>();
            shared.m_setName = "";
            shared.m_setSize = 0;
            shared.m_setStatusEffect = null;

            if (!_sneakEffect)
            {
                _sneakEffect = ScriptableObject.CreateInstance<SneakEffect>();
                _sneakEffect.name = "SE_BrotherhoodSneak";
                _sneakEffect.m_name = "Brotherhood: Unseen Step";
                _sneakEffect.m_tooltip = $"You sneak {_sneakSpeed.Value * 100f:0}% faster.";
                _sneakEffect.m_icon = Icon;
                _sneakEffect.SneakSpeed = _sneakSpeed.Value;
                _sneakEffect.m_sneakStaminaUseModifier = _sneakStamina.Value;
                _sneakEffect.m_noiseModifier = _sneakNoise.Value;
                _sneakEffect.m_raiseSkill = Skills.SkillType.Sneak;
                _sneakEffect.m_raiseSkillModifier = _sneakSkill.Value;
                Set.SetBonus(2, _sneakEffect);
            }

            if (!_eagleVision && _eagleVisionOn.Value)
            {
                _eagleVision = ScriptableObject.CreateInstance<EagleVision.Effect>();
                _eagleVision.name = "SE_BrotherhoodEagleVision";
                _eagleVision.m_name = "Brotherhood: Eagle Vision";
                _eagleVision.m_tooltip = "Crouch to see hostile creatures through walls.";
                _eagleVision.m_icon = Icon;
                _eagleVision.Range = _eagleVisionRange;
                _eagleVision.HostileColour = _hostileColour;
                _eagleVision.StarredColour = _starredColour;
                _eagleVision.InSightOpacity = _inSightOpacity;
                Set.SetBonus(3, _eagleVision);
            }
        }

        public override void RegisterExtras(ObjectDB db) => Set.Register(db);

        /// <summary>The game's speed setting applies to all movement; this one only while crouched.</summary>
        private class SneakEffect : SE_Stats
        {
            public float SneakSpeed;

            public override void ModifySpeed(float baseSpeed, ref float speed, Character character, Vector3 dir)
            {
                base.ModifySpeed(baseSpeed, ref speed, character, dir);
                if (character && character.IsCrouching()) speed += baseSpeed * SneakSpeed;
            }
        }
    }

    internal class BrotherhoodHood : BrotherhoodPiece
    {
        public override string PrefabName => "BrotherhoodHood";
        public override string[] SourcePrefabs => new[] { "HelmetMage", "HelmetTrollLeather" };
        public override string DisplayName => "Brotherhood Hood";
        public override string Description =>
            "A peaked hood that keeps your eyes in shadow. Nothing is true; everything is permitted.\n\n" +
            "Brotherhood set: wear 2 pieces to sneak faster and quieter, 3 to see foes through walls while crouched.";
        protected override string DefaultRecipe => "WolfHairBundle:10,WolfPelt:2,LeatherScraps:5,Silver:2";
    }

    internal class BrotherhoodRobe : BrotherhoodPiece
    {
        public override string PrefabName => "BrotherhoodRobe";
        public override string[] SourcePrefabs => new[] { "ArmorMageChest_Ashlands", "ArmorMageChest", "ArmorTrollLeatherChest" };
        public override string DisplayName => "Brotherhood Robe";
        public override string Description =>
            "A fitted, belted coat with split tails, lined with wolf fur against the mountain cold.\n\n" +
            "Brotherhood set: wear 2 pieces to sneak faster and quieter, 3 to see foes through walls while crouched.";
        protected override string DefaultRecipe => "WolfHairBundle:15,WolfPelt:5,LeatherScraps:10,Silver:4";

        protected override void Customize(ItemDrop.ItemData.SharedData shared)
        {
            base.Customize(shared);
            // As the Fenris coat: a Mountain set has to keep out the cold, and the cape slot is one of ours
            shared.m_damageModifiers.Add(new HitData.DamageModPair
                { m_type = HitData.DamageType.Frost, m_modifier = HitData.DamageModifier.Resistant });
        }
    }

    internal class BrotherhoodLegs : BrotherhoodPiece
    {
        public override string PrefabName => "BrotherhoodLegs";
        public override string[] SourcePrefabs => new[] { "ArmorMageLegs", "ArmorTrollLeatherLegs" };
        public override string DisplayName => "Brotherhood Legs";
        public override string Description =>
            "Close-fitting trousers and tall soft boots that make no sound on stone.\n\n" +
            "Brotherhood set: wear 2 pieces to sneak faster and quieter, 3 to see foes through walls while crouched.";
        protected override string DefaultRecipe => "WolfHairBundle:15,WolfPelt:5,LeatherScraps:10";
    }

    internal class BrotherhoodCape : BrotherhoodPiece
    {
        public override string PrefabName => "BrotherhoodCape";
        public override string[] SourcePrefabs => new[] { "CapeLinen", "CapeDeerHide" };
        public override string DisplayName => "Brotherhood Cape";
        public override string Description =>
            "A crisp cape that falls without a rustle.\n\n" +
            "Brotherhood set: wear 2 pieces to sneak faster and quieter, 3 to see foes through walls while crouched.";
        protected override float DefaultArmor => 1f;  // as the linen cape
        protected override float DefaultArmorPerLevel => 1f;
        protected override string DefaultRecipe => "WolfHairBundle:10,WolfPelt:2,Silver:1";
    }
}

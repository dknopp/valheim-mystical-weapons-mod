using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
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
        private static ConfigEntry<float> _sneakVisibility;
        private static EagleVision.Effect _eagleVision;
        private static ConfigEntry<bool> _eagleVisionOn;
        private static ConfigEntry<float> _eagleVisionRange;
        private static ConfigEntry<Color> _hostileColour;
        private static ConfigEntry<Color> _starredColour;
        private static ConfigEntry<float> _inSightOpacity;
        private static SE_Stats _creed;
        private static ConfigEntry<bool> _assassinationOn;
        private static ConfigEntry<bool> _leapOfFaith;
        private static ConfigEntry<bool> _landingRoll;
        private static ConfigEntry<float> _landingRollHeight;

        // Fenris, the Mountain cloth set, is made at the workbench; the bases' Galdr table can't be built until Mistlands
        protected override string DefaultStation => "piece_workbench";
        protected override int DefaultStationLevel => 2;
        protected override float DefaultArmor => 10f;  // as each Fenris piece
        protected override Color DefaultTint => new Color(0.6f, 0.6f, 0.6f);  // placeholder until our own textures

        public override void BindConfig(ConfigFile cfg)
        {
            base.BindConfig(cfg);
            if (_sneakSpeed != null) return; // the set's own settings, once for all four pieces
            Set.ShowOn(Weapons.HiddenBlades.Id, 4); // the full set's bonus needs them in hand
            _sneakSpeed = cfg.Bind(SetName, "SneakSpeed", 0.25f,
                "2 pieces: extra speed while sneaking (0.25 = 25% faster). Restart to apply.");
            _sneakStamina = cfg.Bind(SetName, "SneakStaminaCost", -0.5f,
                "2 pieces: change in the stamina sneaking costs (-0.5 = half). Restart to apply.");
            _sneakNoise = cfg.Bind(SetName, "SneakNoise", -0.5f,
                "2 pieces: change in the noise you make (-0.5 = half as loud). Restart to apply.");
            _sneakSkill = cfg.Bind(SetName, "SneakSkillGain", 0.5f,
                "2 pieces: extra Sneak skill gain (0.5 = 50% faster levelling). Restart to apply.");
            _sneakVisibility = cfg.Bind(SetName, "SneakVisibility", -0.4f,
                "2 pieces: change in how easily creatures see you while crouched (-0.4 = 40% less), on top of darkness " +
                "and your Sneak skill. The game's stealth eye shows the result. Restart to apply.");
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
            _assassinationOn = cfg.Bind(SetName, "Assassination", true,
                "4 pieces, hidden blades in hand: a hit on a creature that hasn't noticed you takes a share of its health " +
                "(all of it for most). Restart to apply.");
            Assassination.GiantHealth = cfg.Bind(SetName, "AssassinationGiantHealth", 500f,
                "4 pieces: creatures with at least this base health (before stars) count as giants: trolls, Lox, " +
                "Abominations... Giants and 2-star creatures take the smaller share below.");
            Assassination.HardShare = cfg.Bind(SetName, "AssassinationGiantShare", 0.5f,
                "4 pieces: share of a giant's or 2-star creature's health an assassination takes with no skill (0.5 = half). " +
                "It rises to a kill as your Unarmed and Sneak skills grow.");
            // The share never falls behind as biomes get harder, so the big creatures past the Plains are never a kill
            Assassination.ChampionHealth = cfg.Bind(SetName, "AssassinationChampionHealth", 1200f,
                "4 pieces: creatures with at least this base health that aren't bosses count as champions: Seeker soldiers, " +
                "Morgen, frost trolls, Hildir's bosses... They take the champion share below, never a kill.");
            Assassination.ChampionShare = cfg.Bind(SetName, "AssassinationChampionShare", 0.25f,
                "4 pieces: share of a champion's health an assassination takes with no skill.");
            Assassination.ChampionShareFull = cfg.Bind(SetName, "AssassinationChampionShareFullSkill", 0.5f,
                "4 pieces: share of a champion's health an assassination takes at full skill.");
            Assassination.BossShare = cfg.Bind(SetName, "AssassinationBossShare", 0.1f,
                "4 pieces: share of a boss's health an assassination takes with no skill, once per summoning.");
            Assassination.BossShareFull = cfg.Bind(SetName, "AssassinationBossShareFullSkill", 0.2f,
                "4 pieces: share of a boss's health an assassination takes at full skill.");
            Assassination.FullSkill = cfg.Bind(SetName, "AssassinationFullSkill", 80f,
                "4 pieces: average of your Unarmed and Sneak skills at which the shares above reach their full value.");
            _leapOfFaith = cfg.Bind(SetName, "LeapOfFaith", true, "4 pieces: no fall damage. Restart to apply.");
            _landingRoll = cfg.Bind(SetName, "LeapOfFaithRoll", true,
                "4 pieces, with LeapOfFaith on: land from a long fall in a roll instead of on your feet. It costs no " +
                "stamina, makes no noise creatures hear and gives no dodge protection.");
            _landingRollHeight = cfg.Bind(SetName, "LeapOfFaithRollHeight", 4f,
                "4 pieces: you land in a roll from a fall longer than this, in metres, counted from the top of your jump. " +
                "The game's fall damage starts above 4.");
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
                _sneakEffect.m_name = "Unseen Step";
                _sneakEffect.m_tooltip = $"Sneak {_sneakSpeed.Value * 100f:0}% faster, quieter and less seen, for {(1f + _sneakStamina.Value) * 100f:0}% of the stamina.";
                _sneakEffect.m_icon = Icon;
                _sneakEffect.SneakSpeed = _sneakSpeed.Value;
                _sneakEffect.m_sneakStaminaUseModifier = _sneakStamina.Value;
                _sneakEffect.m_noiseModifier = _sneakNoise.Value;
                _sneakEffect.m_raiseSkill = Skills.SkillType.Sneak;
                _sneakEffect.m_raiseSkillModifier = _sneakSkill.Value;
                // The game only applies stealth changes while crouched, and syncs the result to every creature's owner
                _sneakEffect.m_stealthModifier = _sneakVisibility.Value;
                Set.SetBonus(2, _sneakEffect);
            }

            if (!_eagleVision && _eagleVisionOn.Value)
            {
                _eagleVision = ScriptableObject.CreateInstance<EagleVision.Effect>();
                _eagleVision.name = "SE_BrotherhoodEagleVision";
                _eagleVision.m_name = "Eagle Vision";
                _eagleVision.m_tooltip = "Crouch to see hostile creatures through walls.";
                _eagleVision.m_icon = Icon;
                _eagleVision.Range = _eagleVisionRange;
                _eagleVision.HostileColour = _hostileColour;
                _eagleVision.StarredColour = _starredColour;
                _eagleVision.InSightOpacity = _inSightOpacity;
                Set.SetBonus(3, _eagleVision);
            }

            if (!_creed && (_assassinationOn.Value || _leapOfFaith.Value))
            {
                _creed = ScriptableObject.CreateInstance<SE_Stats>();
                _creed.name = "SE_BrotherhoodCreed";
                _creed.m_name = "Creed";
                _creed.m_tooltip = string.Join(" ", new[]
                {
                    _leapOfFaith.Value ? "Falls don't hurt you." : null,
                    _assassinationOn.Value ? "With the hidden blades in hand, unaware foes fall in one blow." : null
                }).Trim();
                _creed.m_icon = Icon;
                if (_leapOfFaith.Value) _creed.m_fallDamageModifier = -1f;
                if (_assassinationOn.Value) Assassination.SetBonus = _creed;
                Set.SetBonus(4, _creed);
            }
            SetTierIcon(IconTier, Icon);
        }

        public override void RegisterExtras(ObjectDB db) => Set.Register(db);

        /// <summary>The bonus that shows this piece's picture as the set's one icon (0 = none).</summary>
        protected virtual int IconTier => 0;

        /// <summary>Gives the bonus for <paramref name="tier"/> pieces worn this picture.</summary>
        internal static void SetTierIcon(int tier, Sprite icon)
        {
            var effect = tier == 2 ? _sneakEffect : tier == 3 ? _eagleVision : tier == 4 ? (StatusEffect)_creed : null;
            if (effect && icon) effect.m_icon = icon;
        }

        private static readonly AccessTools.FieldRef<Player, bool> CrouchToggled =
            AccessTools.FieldRefAccess<Player, bool>("m_crouchToggled");
        private static readonly AccessTools.FieldRef<Player, float> StealthFactor =
            AccessTools.FieldRefAccess<Player, float>("m_stealthFactor");
        private static readonly AccessTools.FieldRef<Character, ZNetView> NetView =
            AccessTools.FieldRefAccess<Character, ZNetView>("m_nview");

        /// <summary>
        /// Crouch is on, whatever the animation shows: the game stands you up for an attack (it has no crouched attack)
        /// and takes a moment to crouch you again after, and the set's bonuses shouldn't flicker off in between. The game
        /// turns crouch off itself when you run, block, swim or run out of stamina.
        /// </summary>
        internal static bool Crouched(Character character) =>
            character.IsCrouching() || (character is Player player && CrouchToggled(player));

        /// <summary>
        /// The game raises your stealth factor (how easily creatures see you) whenever the crouch animation isn't
        /// playing, so an attack from a crouch would give you away. With 2 pieces worn, hold it where it was while crouch
        /// is on but the animation is away. Runs on the wearer's client, which syncs the factor to the creatures' owners.
        /// </summary>
        [HarmonyPatch(typeof(Player), "UpdateStealth")]
        private static class HoldStealthPatch
        {
            private static void Prefix(Player __instance, out float __state) => __state = StealthFactor(__instance);

            private static void Postfix(Player __instance, float __state)
            {
                if (!_sneakEffect || StealthFactor(__instance) <= __state || !CrouchToggled(__instance) || __instance.IsCrouching()) return;
                if (!__instance.GetSEMan().HaveStatusEffect(_sneakEffect.NameHash())) return;
                StealthFactor(__instance) = __state;
                var nview = NetView(__instance);
                if (nview && nview.IsValid()) nview.GetZDO().Set(ZDOVars.s_stealth, __state);
            }
        }

        private static readonly AccessTools.FieldRef<Character, bool> GroundContact =
            AccessTools.FieldRefAccess<Character, bool>("m_groundContact");
        private static readonly AccessTools.FieldRef<Character, float> MaxAirAltitude =
            AccessTools.FieldRefAccess<Character, float>("m_maxAirAltitude");
        private static readonly AccessTools.FieldRef<Player, bool> DodgeInvincible =
            AccessTools.FieldRefAccess<Player, bool>("m_dodgeInvincible");

        /// <summary>
        /// Leap of Faith: with 4 pieces, a long fall ends in the dodge roll. The game measures the fall and resets it
        /// in UpdateGroundContact, so the prefix reads it first. Unlike a dodge, the roll costs no stamina, makes no AI
        /// noise and isn't invincible. Runs on the wearer's client; the animation trigger syncs to everyone.
        /// </summary>
        [HarmonyPatch(typeof(Character), "UpdateGroundContact")]
        private static class LandingRollPatch
        {
            private static void Prefix(Character __instance, out float __state) =>
                __state = __instance == Player.m_localPlayer && GroundContact(__instance)
                    ? MaxAirAltitude(__instance) - __instance.transform.position.y : 0f;

            private static void Postfix(Character __instance, float __state)
            {
                if (!_creed || !_leapOfFaith.Value || !_landingRoll.Value || __state <= _landingRollHeight.Value) return;
                var player = (Player)__instance;
                if (!player.GetSEMan().HaveStatusEffect(_creed.NameHash())) return;
                if (player.IsDead() || player.InLiquid() || player.InAttack() || player.InDodge() ||
                    player.IsStaggering() || player.IsEncumbered()) return;

                // Roll the way you face: the velocity at touchdown is deflected by the ground, so it isn't a direction
                DodgeInvincible(player) = false;  // a dodge cut short can leave this on
                player.GetZAnim().SetTrigger("dodge");
                player.m_dodgeEffects.Create(player.transform.position, Quaternion.identity, player.transform);
            }
        }

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
            "A peaked hood that keeps your eyes in shadow. Nothing is true; everything is permitted.";
        protected override string DefaultRecipe => "WolfHairBundle:20,WolfPelt:2,LeatherScraps:5,Silver:2";
        protected override int IconTier => 3;
    }

    internal class BrotherhoodRobe : BrotherhoodPiece
    {
        public override string PrefabName => "BrotherhoodRobe";
        public override string[] SourcePrefabs => new[] { "ArmorMageChest_Ashlands", "ArmorMageChest", "ArmorTrollLeatherChest" };
        public override string DisplayName => "Brotherhood Robe";
        public override string Description =>
            "A fitted, belted coat with split tails, lined with wolf fur against the mountain cold.";
        protected override string DefaultRecipe => "WolfHairBundle:20,WolfPelt:5,LeatherScraps:10,Silver:4";
        protected override int IconTier => 4;  // until the hidden blades take it

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
            "Close-fitting trousers and tall soft boots that make no sound on stone.";
        protected override string DefaultRecipe => "WolfHairBundle:20,WolfPelt:5,LeatherScraps:10";
    }

    internal class BrotherhoodCape : BrotherhoodPiece
    {
        public override string PrefabName => "BrotherhoodCape";
        public override string[] SourcePrefabs => new[] { "CapeLinen", "CapeDeerHide" };
        public override string DisplayName => "Brotherhood Cape";
        public override string Description =>
            "A crisp cape that falls without a rustle.";
        protected override float DefaultArmor => 1f;  // as the linen cape
        protected override float DefaultArmorPerLevel => 1f;
        protected override string DefaultRecipe => "WolfHairBundle:10,WolfPelt:4,Silver:2";
        protected override int IconTier => 2;
    }
}

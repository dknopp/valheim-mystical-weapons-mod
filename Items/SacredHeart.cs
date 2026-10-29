using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MysticalWeapons.Items
{
    /// <summary>
    /// The Binding of Isaac's Sacred Heart, as a trinket: more health and harder-hitting projectiles, slower to draw.
    /// Projectile hits fill the adrenaline bar; full, it gives a burst where your shots home in and hit much harder.
    /// "Projectiles" are anything you fire or throw (bows, crossbows, spears, bombs), not staff spells. Decisions are in
    /// references/sacred-heart.md.
    /// </summary>
    internal class SacredHeart : MysticalItem
    {
        public override string PrefabName => "SacredHeart";
        public override string[] SourcePrefabs => new[] { "TrinketBronzeHealth" };
        public override string DisplayName => "Sacred Heart";
        public override string Description =>
            "A heart wreathed in flame beneath a cross. It beats slow and heavy: your aim grows patient, and your shots " +
            "remember where you meant them to go.";

        // Vanilla trinkets: 5 metal, 5 of a material, a trophy, Forge 1. The Elder's trophy is the trophy here
        protected override string DefaultRecipe => "Bronze:5,Bloodbag:5,SurtlingCore:1";
        protected override int DefaultStationLevel => 1;

        private static ConfigEntry<float> _maxHealth, _projectileDamage, _drawSlowdown, _maxAdrenaline, _hitAdrenaline,
            _duration, _burstDamage, _burstHeal, _homingRange, _homingCone, _homingTurnRate, _burstShotSpeed, _burstGravity;
        private static ConfigEntry<bool> _homing, _burstMessage;

        public override void BindConfig(ConfigFile cfg)
        {
            base.BindConfig(cfg);
            var s = PrefabName;
            _maxHealth = cfg.Bind(s, "MaxHealth", 15f, "Extra maximum health while worn.");
            _projectileDamage = cfg.Bind(s, "ProjectileDamage", 0.1f,
                "Extra damage for everything you fire or throw while worn (0.1 = +10%). Not staff spells.");
            _drawSlowdown = cfg.Bind(s, "DrawSlowdown", 0.15f,
                "Bows draw and crossbows reload this much slower while worn (0.15 = 15% longer). 0 = no slowdown.");
            _maxAdrenaline = cfg.Bind(s, "MaxAdrenaline", 30f,
                "Adrenaline needed for the burst. It shares the bar with other worn trinkets. Restart to apply.");
            _hitAdrenaline = cfg.Bind(s, "HitAdrenaline", 5f,
                "Extra adrenaline each time something you fire or throw hits a creature, on top of the game's own.");
            _duration = cfg.Bind(s, "Duration", 25f,
                "Seconds the burst lasts. The bar doesn't fill while it runs. Restart to apply.");
            _burstDamage = cfg.Bind(s, "BurstDamage", 0.5f,
                "Extra damage for everything you fire or throw during the burst (0.5 = +50%), on top of ProjectileDamage.");
            _burstHeal = cfg.Bind(s, "BurstHeal", 0.25f, "Health restored when the burst starts, as a share of your maximum (0.25 = 25%).");
            _homing = cfg.Bind(s, "Homing", true, "During the burst, your shots curve toward the nearest enemy ahead.");
            _homingRange = cfg.Bind(s, "HomingRange", 20f, "Metres a homing shot looks for an enemy.");
            _homingCone = cfg.Bind(s, "HomingCone", 45f,
                "Degrees either side of a homing shot's path it looks for an enemy, so it only chases roughly where you aimed.");
            _homingTurnRate = cfg.Bind(s, "HomingTurnRate", 180f,
                "Degrees per second a homing shot can turn. Lower curves in more gently; higher snaps on.");
            _burstShotSpeed = cfg.Bind(s, "BurstShotSpeed", 0.15f,
                "During the burst, your shots fly this much slower (0.15 = 15%), giving them room to curve.");
            _burstGravity = cfg.Bind(s, "BurstGravity", 0.6f, "During the burst, your shots drop this much less (0.6 = 60%).");
            _burstMessage = cfg.Bind(s, "BurstMessage", true, "Show \"Homing shots + DMG up\" mid-screen as the burst starts. Restart to apply.");
        }

        private static Burst _burst;
        private static int _burstHash;

        protected override void Setup(GameObject prefab, ItemDrop.ItemData.SharedData shared)
        {
            // Placeholder icon until our own flame-and-cross: the Morgen's heart
            var heart = FindVanilla("MorgenHeart")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_icons;
            if (heart != null && heart.Length > 0) shared.m_icons = new[] { heart[0] };
            else Plugin.Log.LogWarning($"{PrefabName}: MorgenHeart icon not found; keeping the bronze trinket's");
            Icon = shared.m_icons[0];
        }

        protected override void Customize(ItemDrop.ItemData.SharedData shared)
        {
            var vanilla = shared.m_fullAdrenalineSE;
            _burst = ScriptableObject.CreateInstance<Burst>();
            _burst.name = "SE_SacredHeart";
            _burst.m_name = DisplayName;
            _burst.m_tooltip = "Your shots seek out the nearest enemy and hit much harder.";
            _burst.m_icon = Icon;
            _burst.m_ttl = _duration.Value;
            if (_burstMessage.Value)
            {
                _burst.m_startMessageType = MessageHud.MessageType.Center;
                _burst.m_startMessage = "Homing shots + DMG up";
            }
            // The bronze trinket's own burst look and sound
            if (vanilla)
            {
                _burst.m_startEffects = vanilla.m_startEffects;
                _burst.m_stopEffects = vanilla.m_stopEffects;
            }
            _burstHash = _burst.NameHash();
            Plugin.Log.LogInfo($"{PrefabName}: burst effects from {(vanilla ? vanilla.name : "none")}");

            shared.m_maxAdrenaline = _maxAdrenaline.Value;
            shared.m_fullAdrenalineSE = _burst;
        }

        public override void RegisterExtras(ObjectDB db)
        {
            if (_burst && !db.m_StatusEffects.Contains(_burst)) db.m_StatusEffects.Add(_burst);
        }

        /// <summary>The burst: heals on start, and no adrenaline while it runs, so hits can't keep restarting it.</summary>
        private class Burst : SE_Stats
        {
            public override void Setup(Character character)
            {
                base.Setup(character);
                if (character == Player.m_localPlayer) character.Heal(character.GetMaxHealth() * _burstHeal.Value, true);
            }

            public override void ModifyAdrenaline(float baseValue, ref float use) => use = 0f;
        }

        // ------------------------------------------------------------ effects

        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> TrinketSlot =
            AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_trinketItem");

        private static bool Worn(Character c) =>
            c is Humanoid h && TrinketSlot(h)?.m_dropPrefab?.name == "SacredHeart";

        private static bool Bursting(Character c) => _burst && c.GetSEMan().HaveStatusEffect(_burstHash);

        [HarmonyPatch(typeof(Player), "GetTotalFoodValue")]
        private static class MaxHealthPatch
        {
            private static void Postfix(Player __instance, ref float hp)
            {
                if (Worn(__instance)) hp += _maxHealth.Value;
            }
        }

        /// <summary>
        /// Runs on the shooter's client, where the projectile is spawned, so the bonuses apply once. The damage is a copy
        /// per projectile; the hit data is copied before scaling because bombs' blasts read it.
        /// </summary>
        [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
        private static class ShotPatch
        {
            private static void Postfix(Projectile __instance, Character owner, ItemDrop.ItemData item,
                ref Vector3 ___m_vel, ref HitData.DamageTypes ___m_damage, ref HitData ___m_originalHitData,
                ref float ___m_adrenaline, ref float ___m_gravity)
            {
                if (owner != Player.m_localPlayer || !Worn(owner) || item == null) return;
                var skill = item.m_shared.m_skillType;
                if (skill == Skills.SkillType.ElementalMagic || skill == Skills.SkillType.BloodMagic) return;

                var bursting = Bursting(owner);
                var mult = 1f + _projectileDamage.Value + (bursting ? _burstDamage.Value : 0f);
                ___m_damage.Modify(mult);
                if (___m_originalHitData != null)
                {
                    ___m_originalHitData = ___m_originalHitData.Clone();
                    ___m_originalHitData.m_damage.Modify(mult);
                }
                ___m_adrenaline += _hitAdrenaline.Value;

                if (!bursting) return;
                ___m_vel *= 1f - _burstShotSpeed.Value;
                ___m_gravity *= 1f - _burstGravity.Value;
                if (_homing.Value)
                {
                    ProjectileHoming.Range = _homingRange.Value;
                    ProjectileHoming.ConeDegrees = _homingCone.Value;
                    ProjectileHoming.TurnRateDegrees = _homingTurnRate.Value;
                    ProjectileHoming.Mark(__instance);
                }
            }
        }

        /// <summary>Bow draw time builds up from <c>dt</c>; less of it draws slower.</summary>
        [HarmonyPatch(typeof(Player), "UpdateAttackBowDraw")]
        private static class DrawPatch
        {
            private static void Prefix(Player __instance, ref float dt)
            {
                if (_drawSlowdown.Value > 0f && Worn(__instance)) dt /= 1f + _drawSlowdown.Value;
            }
        }

        /// <summary>Crossbow reloading, the same way as the bow draw.</summary>
        [HarmonyPatch(typeof(Player), "UpdateWeaponLoading")]
        private static class ReloadPatch
        {
            private static void Prefix(Player __instance, ref float dt)
            {
                if (_drawSlowdown.Value > 0f && Worn(__instance)) dt /= 1f + _drawSlowdown.Value;
            }
        }
    }
}

using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MysticalWeapons.Hallows
{
    /// <summary>
    /// The Elder Wand, the Deathstick. Held in one hand, every quick jab (the butcher knife's) looses Dundr's spray of
    /// lightning, matched to Lightning Strike's damage over 5 seconds; its secondary attack summons the Staff of the
    /// Wild's roots. Raise it to block and the Staff of Protection's shield springs up around you. By default it runs
    /// on stamina instead of eitr so anyone can wield it.
    /// </summary>
    internal class ElderWand : MysticalWeapon
    {
        private const string Name = "ElderWand";

        public override string PrefabName => Name;
        // The butcher knife: a one-handed hold and a quick jab that plays once a click. Then the Staff of Frost (a cast
        // that loops while held); not the Staff of Embers first, which carries a burning orb over the staff
        public override string[] SourcePrefabs => new[] { "KnifeButcher", "StaffIceShards", "StaffFireball" };
        public override string VanillaPeer => "StaffThunderBlood"; // a Deep North staff
        public override string DisplayName => "The Elder Wand";
        public override string Description =>
            "Fifteen inches of elder wood, knotted like a string of beads. The Deathstick, the " +
            "Wand of Destiny - unbeatable, they say, in the hand of its true master. It knows one " +
            "curse, and it does not miss twice.";

        // Each of the lightning spray's 12 bolts, matched to Lightning Strike (Deep North: 300 lightning a cast, +20 a
        // level, a slow-to-moderate ~1.5 s cast, ~1000 in 5 s) over 5 seconds: ~9 knife jabs, about half the bolts on one
        // target at range, 9 x 6 x 18 = ~970. Up close all 12 land; Lightning Strike hits all within 3 m and staggers.
        private const string DefaultBoltDamage = "lightning:18";
        private const string DefaultBoltDamagePerLevel = "lightning:1.2"; // Lightning Strike's +20 of 300 a level
        // The released wand's Damage was its frost bolts' damage; a saved value used for 12 lightning bolts a jab would
        // be several times too strong, so the bolts read keys of their own and the old Damage is left unused
        protected override bool HasDamage => false;
        // Very late game (Deep North), so Master of Death comes last: a frozen branch for the elder, Elaking hair for the core
        protected override string DefaultRecipe => "BarkaBranch:2,ElakingHairBundle:3,Gold:4,Eitr:20";
        protected override string DefaultStation => "piece_magetable";
        protected override int DefaultStationLevel => 5;

        private ConfigEntry<string> _boltDamageConfig;
        private ConfigEntry<string> _boltDamagePerLevelConfig;
        private ConfigEntry<float> _eitrCost;
        private ConfigEntry<float> _staminaCost;
        private static ConfigEntry<bool> _blockShield;
        private static ConfigEntry<float> _shieldCooldown;

        public override void BindConfig(ConfigFile cfg)
        {
            base.BindConfig(cfg);
            _boltDamageConfig = cfg.Bind(PrefabName, "BoltDamage", DefaultBoltDamage,
                "Damage of each of the 12 lightning bolts a jab looses, e.g. lightning:18. Types: blunt slash pierce chop " +
                "pickaxe fire frost lightning poison spirit. (The old Damage key is no longer used.)");
            _boltDamagePerLevelConfig = cfg.Bind(PrefabName, "BoltDamagePerLevel", DefaultBoltDamagePerLevel,
                "Extra damage per bolt per upgrade level.");
            _eitrCost = cfg.Bind(PrefabName, "EitrCost", 0f, "Eitr used per spell (vanilla staffs use eitr).");
            // About nine jabs in 5 s: a cost a player can keep up, so the damage above is reachable
            _staminaCost = cfg.Bind(PrefabName, "StaminaCost", 10f, "Stamina used per spell.");
            _blockShield = cfg.Bind(PrefabName, "ShieldOnBlock", true,
                "Raising the wand to block puts up the Staff of Protection's shield around you (it grows with Elemental magic).");
            _shieldCooldown = cfg.Bind(PrefabName, "ShieldCooldown", 20f, "Seconds before blocking can put up another shield.");
        }

        private const int Wood = 0, Knot = 1;

        public override MaterialSpec[] Materials => new[]
        {
            new MaterialSpec(new Color(0.62f, 0.56f, 0.46f), 0.3f), // pale elder wood
            new MaterialSpec(new Color(0.4f, 0.34f, 0.27f), 0.25f), // darker knots
        };

        private SE_Stats _wandEffect;

        protected override void Customize(ItemDrop.ItemData.SharedData shared)
        {
            foreach (var attack in new[] { shared.m_attack, shared.m_secondaryAttack })
            {
                if (attack == null) continue;
                attack.m_attackEitr = _eitrCost.Value;
                attack.m_attackStamina = _staminaCost.Value;
                attack.m_attackHealth = 0f;
                attack.m_attackHealthPercentage = 0f;
            }
            // The jab casts at once, never drawn or reloaded: no loading bar
            if (shared.m_attack != null)
            {
                shared.m_attack.m_bowDraw = false;
                shared.m_attack.m_requiresReload = false;
            }
            shared.m_skillType = Skills.SkillType.ElementalMagic; // a wand, whatever it is built on
            shared.m_useDurability = false; // the wand of destiny does not wear out
            shared.m_weight = 0.5f;

            _wandEffect = DeathlyHallows.MakeEffect("SE_ElderWand", "The Elder Wand",
                "The Deathstick answers to you.", Icon);
            DeathlyHallows.JoinSet(shared, Icon);
        }

        public override void RegisterExtras(ObjectDB db) => DeathlyHallows.Register(db, _wandEffect);

        // ------------------------------------------------------------- spells

        private const string LightningSource = "StaffLightning"; // Dundr
        private const string RootsSource = "StaffGreenRoots";    // the Staff of the Wild
        private static HitData.DamageTypes _boltDamage, _boltPerLevel, _rootsDamage, _rootsPerLevel;

        protected override void Setup(GameObject prefab, ItemDrop.ItemData.SharedData shared)
        {
            base.Setup(prefab, shared);
            _boltDamage = shared.m_damages = ParseDamage(_boltDamageConfig.Value);
            _boltPerLevel = shared.m_damagesPerLevel = ParseDamage(_boltDamagePerLevelConfig.Value);
            LoadLightning(shared);
            LoadRoots(shared);
        }

        /// <summary>
        /// A copy of a vanilla staff's primary attack and that staff's damage, or null (logged) if it has no attack
        /// that casts something (a projectile, or an object it spawns).
        /// </summary>
        private Attack Borrow(string staffName, string use, out ItemDrop.ItemData.SharedData staffData)
        {
            var staff = FindVanilla(staffName);
            staffData = staff ? staff.GetComponent<ItemDrop>()?.m_itemData.m_shared : null;
            var attack = staffData?.m_attack;
            if (attack != null && (attack.m_attackProjectile || attack.m_spawnOnTrigger)) return attack.Clone();
            Plugin.Log.LogWarning($"{PrefabName}: no '{staffName}' to borrow {use} from");
            return null;
        }

        /// <summary>
        /// The attack keeps the source item's animation and hold (the butcher knife's jab), with Dundr's lightning
        /// loaded in: a projectile attack, launched from where the staff launches it, flying and sounding like it.
        /// Every jab looses a spray: an endgame wand, with no cooldown.
        /// </summary>
        private void LoadLightning(ItemDrop.ItemData.SharedData shared)
        {
            var spray = Borrow(LightningSource, "the lightning", out _);
            if (spray == null || shared.m_attack == null || SourceName == LightningSource) return;
            var cast = shared.m_attack.Clone();
            cast.m_attackType = spray.m_attackType;
            cast.m_attackOriginJoint = spray.m_attackOriginJoint;
            cast.m_attackHeight = spray.m_attackHeight;
            cast.m_attackRange = spray.m_attackRange;
            cast.m_attackOffset = spray.m_attackOffset;
            cast.m_useCharacterFacing = spray.m_useCharacterFacing;
            cast.m_useCharacterFacingYAim = spray.m_useCharacterFacingYAim;
            cast.m_damageMultiplier = spray.m_damageMultiplier;
            cast.m_lastChainDamageMultiplier = 1f; // every jab of the knife's combo looses the same spray
            cast.m_raiseSkillAmount = spray.m_raiseSkillAmount;
            cast.m_attackProjectile = spray.m_attackProjectile;
            cast.m_projectiles = spray.m_projectiles;
            cast.m_projectileBursts = spray.m_projectileBursts;
            cast.m_burstInterval = spray.m_burstInterval;
            cast.m_projectileVel = spray.m_projectileVel;
            cast.m_projectileVelMin = spray.m_projectileVelMin;
            cast.m_randomVelocity = spray.m_randomVelocity;
            cast.m_projectileAccuracy = spray.m_projectileAccuracy;
            cast.m_projectileAccuracyMin = spray.m_projectileAccuracyMin;
            cast.m_launchAngle = spray.m_launchAngle;
            cast.m_startEffect = spray.m_startEffect;
            cast.m_triggerEffect = spray.m_triggerEffect;
            cast.m_burstEffect = spray.m_burstEffect;
            shared.m_attack = cast;
            Plugin.Log.LogInfo($"{PrefabName}: attack is the '{SourceName}' animation ('{cast.m_attackAnimation}', looping {cast.m_loopingAttack}) " +
                               $"with the lightning of '{LightningSource}' ('{spray.m_attackProjectile.name}', {spray.m_projectiles} per cast)");
        }

        /// <summary>The secondary attack is a copy of the Staff of the Wild's, with that staff's own damage, cast at once.</summary>
        private void LoadRoots(ItemDrop.ItemData.SharedData shared)
        {
            var roots = Borrow(RootsSource, "the roots", out var staffData);
            if (roots == null) return;
            // The game only charges and reloads a weapon's primary attack: a secondary that must be charged never
            // charges, and one that must be reloaded never loads, so it never starts. Cast it at once instead.
            roots.m_bowDraw = false;
            roots.m_requiresReload = false;
            shared.m_secondaryAttack = roots;
            _rootsDamage = staffData.m_damages;
            _rootsPerLevel = staffData.m_damagesPerLevel;
            var cast = roots.m_attackProjectile ? roots.m_attackProjectile : roots.m_spawnOnTrigger;
            Plugin.Log.LogInfo($"{PrefabName}: secondary is the roots of '{RootsSource}' ('{cast.name}', animation '{roots.m_attackAnimation}')");
        }


        /// <summary>
        /// The game reads a weapon's damage when its spell flies, and the wand has one damage for both attacks, so
        /// swap in the lightning's or the roots' as an attack starts. Only the caster's client starts attacks; what
        /// they cast syncs to everyone.
        /// </summary>
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
        private static class CastPatch
        {
            private static void Prefix(Humanoid __instance, bool secondaryAttack)
            {
                if (__instance != Player.m_localPlayer || __instance.InAttack()) return;
                var wand = __instance.GetCurrentWeapon();
                if (!DeathlyHallows.IsItem(wand, Name)) return;
                wand.m_shared.m_damages = secondaryAttack ? _rootsDamage : _boltDamage;
                wand.m_shared.m_damagesPerLevel = secondaryAttack ? _rootsPerLevel : _boltPerLevel;
            }
        }

        // ------------------------------------------------------------- shield on block

        private static SE_Shield _shield;
        private static bool _shieldLookedUp;
        private static float _nextShield;

        private const string ShieldSource = "StaffShield"; // the Staff of Protection

        /// <summary>
        /// The Staff of Protection's shield effect, found once: the status effect its cast gives (other shields, such
        /// as the Fuling shamans', are in the ObjectDB too), else any shield whose name says staff.
        /// </summary>
        private static SE_Shield Shield()
        {
            if (_shieldLookedUp) return _shield;
            var db = ObjectDB.instance;
            if (!db) return null; // look again next time: nothing to cache yet
            _shieldLookedUp = true;

            var staff = db.GetItemPrefab(ShieldSource);
            var attack = staff ? staff.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_attack : null;
            foreach (var cast in new[] { attack?.m_attackProjectile, attack?.m_spawnOnTrigger })
            {
                if (!cast || _shield) continue;
                foreach (var name in cast.GetComponentsInChildren<Aoe>(true).Select(a => a.m_statusEffect)
                             .Concat(cast.GetComponentsInChildren<Projectile>(true).Select(p => p.m_statusEffect)))
                    if (!string.IsNullOrEmpty(name) && (_shield = db.GetStatusEffect(name.GetStableHashCode()) as SE_Shield)) break;
            }
            if (!_shield)
                _shield = db.m_StatusEffects.OfType<SE_Shield>()
                    .FirstOrDefault(se => se.name.IndexOf("staff", System.StringComparison.OrdinalIgnoreCase) >= 0);

            if (_shield) Plugin.Log.LogInfo($"{Name}: block shield is '{_shield.name}' from '{ShieldSource}'");
            else Plugin.Log.LogWarning($"{Name}: no '{ShieldSource}' shield found; blocking puts up no shield");
            return _shield;
        }

        /// <summary>
        /// Pressing block with the wand in hand puts up the shield, at most every ShieldCooldown seconds. The game
        /// sets the local player's controls here each frame, so this does nothing until block is pressed.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
        private static class BlockShieldPatch
        {
            private static void Postfix(Player __instance, bool block)
            {
                if (!block || _blockShield == null || !_blockShield.Value || __instance != Player.m_localPlayer) return;
                if (Time.time < _nextShield) return;
                var wand = __instance.GetCurrentWeapon();
                if (!DeathlyHallows.IsItem(wand, Name) || !Shield()) return;
                _nextShield = Time.time + _shieldCooldown.Value;
                __instance.GetSEMan().AddStatusEffect(_shield, true, wand.m_quality,
                    __instance.GetSkillLevel(Skills.SkillType.ElementalMagic));
            }
        }

        private static float ShaftRadius(float y) => Mathf.Lerp(0.0095f, 0.0045f, Mathf.InverseLerp(0.04f, 0.33f, y));

        public override void BuildModel(MeshBuilder b)
        {
            // Hand at the origin: knobbly handle below, tapering shaft above
            b.Taper(Wood, -0.10f, 0.04f, 0.012f, 0.011f, 7);
            foreach (var y in new[] { -0.095f, -0.06f, -0.025f, 0.035f })
                b.Taper(Knot, y - 0.007f, y + 0.007f, 0.0155f, 0.0155f, 7);
            b.Taper(Knot, -0.11f, -0.10f, 0.009f, 0.012f, 7); // rounded butt

            b.Taper(Wood, 0.04f, 0.33f, ShaftRadius(0.04f), ShaftRadius(0.33f), 7);
            foreach (var y in new[] { 0.09f, 0.15f, 0.21f, 0.27f })
            {
                var r = ShaftRadius(y) + 0.003f;
                b.Taper(Knot, y - 0.006f, y, r - 0.002f, r, 7);
                b.Taper(Knot, y, y + 0.006f, r, r - 0.002f, 7);
            }
            b.Taper(Wood, 0.33f, 0.345f, ShaftRadius(0.33f), 0.001f, 7); // tip
        }
    }
}

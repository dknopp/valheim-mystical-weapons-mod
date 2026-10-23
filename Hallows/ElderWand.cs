using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MysticalWeapons.Hallows
{
    /// <summary>
    /// The Elder Wand, the Deathstick. Held and flicked like the Staff of Frost, but its attack looses the Staff of
    /// Embers' fireball, slow to gather but far stronger; its secondary attack is Dundr's spray of lightning, also stronger.
    /// Raise it to block and the Staff of Protection's shield springs up around you. By default it runs on
    /// stamina instead of eitr so anyone can wield it.
    /// </summary>
    internal class ElderWand : MysticalWeapon
    {
        private const string Name = "ElderWand";

        public override string PrefabName => Name;
        // The Staff of Frost: its quick one-handed flick and its plain staff (the Staff of Embers carries a burning orb)
        public override string[] SourcePrefabs => new[] { "StaffIceShards", "StaffFireball" };
        public override string VanillaPeer => "StaffThunderBlood"; // a Deep North staff
        public override string DisplayName => "The Elder Wand";
        public override string Description =>
            "Fifteen inches of elder wood, knotted like a string of beads. The Deathstick, the " +
            "Wand of Destiny - unbeatable, they say, in the hand of its true master. It knows one " +
            "curse, and it does not miss twice.";

        // The fireball: twice the Staff of Embers (120 blunt + 120 fire, Mistlands), one every SpellCooldown, Deep North tier
        protected override string DefaultDamage => "blunt:240,fire:240";
        protected override string DefaultDamagePerLevel => "blunt:10,fire:10";
        // Very late game (Deep North), so Master of Death comes last: a frozen branch for the elder, Elaking hair for the core
        protected override string DefaultRecipe => "BarkaBranch:2,ElakingHairBundle:3,Gold:4,Eitr:20";
        protected override string DefaultStation => "piece_magetable";
        protected override int DefaultStationLevel => 5;

        private ConfigEntry<float> _eitrCost;
        private ConfigEntry<float> _staminaCost;
        private static ConfigEntry<float> _cooldown;
        private ConfigEntry<string> _lightningDamage;
        private ConfigEntry<string> _lightningDamagePerLevel;
        private static ConfigEntry<bool> _blockShield;
        private static ConfigEntry<float> _shieldCooldown;

        public override void BindConfig(ConfigFile cfg)
        {
            base.BindConfig(cfg);
            _eitrCost = cfg.Bind(PrefabName, "EitrCost", 0f, "Eitr used per spell (vanilla staffs use eitr).");
            _staminaCost = cfg.Bind(PrefabName, "StaminaCost", 20f, "Stamina used per spell.");
            _cooldown = cfg.Bind(PrefabName, "SpellCooldown", 1.25f, "Seconds the wand needs to gather power between fireballs.");
            _lightningDamage = cfg.Bind(PrefabName, "LightningDamage", "lightning:35",
                "Secondary attack (Dundr's spray of 12 bolts): damage of each bolt. Dundr's is 20. Restart to apply.");
            _lightningDamagePerLevel = cfg.Bind(PrefabName, "LightningDamagePerLevel", "lightning:4",
                "Secondary attack: extra damage per bolt per upgrade level. Restart to apply.");
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
            // The fireball is cast at once, never drawn or reloaded: no loading bar
            if (shared.m_attack != null)
            {
                shared.m_attack.m_bowDraw = false;
                shared.m_attack.m_requiresReload = false;
            }
            shared.m_useDurability = false; // the wand of destiny does not wear out
            shared.m_weight = 0.5f;

            _wandEffect = DeathlyHallows.MakeEffect("SE_ElderWand", "The Elder Wand",
                "The Deathstick answers to you.", Icon);
            DeathlyHallows.JoinSet(shared, Icon);
        }

        public override void RegisterExtras(ObjectDB db) => DeathlyHallows.Register(db, _wandEffect);

        // ------------------------------------------------------------- spells

        private const string FireballSource = "StaffFireball"; // the Staff of Embers
        private const string LightningSource = "StaffLightning"; // Dundr
        private static HitData.DamageTypes _fireballDamage, _fireballPerLevel, _boltDamage, _boltPerLevel;

        protected override void Setup(GameObject prefab, ItemDrop.ItemData.SharedData shared)
        {
            base.Setup(prefab, shared);
            _fireballDamage = shared.m_damages;
            _fireballPerLevel = shared.m_damagesPerLevel;
            _boltDamage = ParseDamage(_lightningDamage.Value);
            _boltPerLevel = ParseDamage(_lightningDamagePerLevel.Value);
            LoadFireball(shared);
            LoadLightning(shared);
        }

        /// <summary>A copy of a vanilla staff's primary attack, or null (logged) if it has none that fires.</summary>
        private Attack Borrow(string staffName, string use)
        {
            var staff = FindVanilla(staffName);
            var attack = staff ? staff.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_attack : null;
            if (attack != null && attack.m_attackProjectile) return attack.Clone();
            Plugin.Log.LogWarning($"{PrefabName}: no '{staffName}' to borrow {use} from");
            return null;
        }

        /// <summary>
        /// The attack keeps the Staff of Frost's animation, hold and aim, with the Staff of Embers' fireball loaded in:
        /// its projectile, how it flies and how it sounds. One fireball a cast: the frost staff's attack repeats while
        /// held, which would get around the cooldown.
        /// </summary>
        private void LoadFireball(ItemDrop.ItemData.SharedData shared)
        {
            var fireball = Borrow(FireballSource, "the fireball");
            if (fireball == null || shared.m_attack == null || SourceName == FireballSource) return;
            var cast = shared.m_attack.Clone();
            cast.m_attackProjectile = fireball.m_attackProjectile;
            cast.m_projectiles = fireball.m_projectiles;
            cast.m_projectileBursts = fireball.m_projectileBursts;
            cast.m_burstInterval = fireball.m_burstInterval;
            cast.m_projectileVel = fireball.m_projectileVel;
            cast.m_projectileVelMin = fireball.m_projectileVelMin;
            cast.m_randomVelocity = fireball.m_randomVelocity;
            cast.m_projectileAccuracy = fireball.m_projectileAccuracy;
            cast.m_projectileAccuracyMin = fireball.m_projectileAccuracyMin;
            cast.m_launchAngle = fireball.m_launchAngle;
            cast.m_startEffect = fireball.m_startEffect;
            cast.m_triggerEffect = fireball.m_triggerEffect;
            cast.m_burstEffect = fireball.m_burstEffect;
            cast.m_loopingAttack = false;
            cast.m_perBurstResourceUsage = false;
            shared.m_attack = cast;
            Plugin.Log.LogInfo($"{PrefabName}: attack is the '{SourceName}' cast (animation '{cast.m_attackAnimation}') " +
                               $"with the fireball of '{FireballSource}' ('{fireball.m_attackProjectile.name}')");
        }

        /// <summary>The secondary attack is a copy of Dundr's, cast at once; it gets its own lightning damage.</summary>
        private void LoadLightning(ItemDrop.ItemData.SharedData shared)
        {
            var lightning = Borrow(LightningSource, "the lightning");
            if (lightning == null) return;
            // The game only charges a weapon's primary attack: a charged secondary never charges, and its bolts
            // fly at the speed of a 0% charge (none). Cast it at once at its full speed instead.
            lightning.m_bowDraw = false;
            shared.m_secondaryAttack = lightning;
            Plugin.Log.LogInfo($"{PrefabName}: secondary is the lightning of '{LightningSource}' ('{lightning.m_attackProjectile.name}', " +
                               $"{lightning.m_projectiles} per cast, animation '{lightning.m_attackAnimation}')");
        }

        private static float _nextCast;

        /// <summary>
        /// The game reads a weapon's damage when its spell flies, and the wand has one damage for both attacks, so
        /// swap in the fireball's or the lightning's as an attack starts. Skips a fireball while the wand gathers
        /// power: staffs have no cooldown of their own to lengthen. Only the caster's client starts attacks; the
        /// projectiles sync to everyone.
        /// </summary>
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
        private static class CastPatch
        {
            private static bool Prefix(Humanoid __instance, bool secondaryAttack, ref bool __result, out bool __state)
            {
                __state = false;
                if (_cooldown == null || __instance != Player.m_localPlayer) return true;
                var wand = __instance.GetCurrentWeapon();
                if (!DeathlyHallows.IsItem(wand, Name) || __instance.InAttack()) return true;
                if (secondaryAttack)
                {
                    wand.m_shared.m_damages = _boltDamage;
                    wand.m_shared.m_damagesPerLevel = _boltPerLevel;
                    return true;
                }
                if (Time.time < _nextCast)
                {
                    __result = false;
                    return false;
                }
                wand.m_shared.m_damages = _fireballDamage;
                wand.m_shared.m_damagesPerLevel = _fireballPerLevel;
                __state = true;
                return true;
            }

            private static void Postfix(bool __result, bool __state)
            {
                if (__state && __result) _nextCast = Time.time + _cooldown.Value;
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
            _shieldLookedUp = true;
            var db = ObjectDB.instance;
            if (!db) return null;

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

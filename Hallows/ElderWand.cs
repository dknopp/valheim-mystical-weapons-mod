using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MysticalWeapons.Hallows
{
    /// <summary>
    /// The Elder Wand, the Deathstick. Built on a Mistlands staff so it casts spell bolts,
    /// but by default it runs on stamina instead of eitr so anyone can wield it. Each cast moves on
    /// to the next spell (bolts, a fireball, a burst of lightning, borrowed from the vanilla staffs),
    /// and spells are slow to gather but hit harder.
    /// </summary>
    internal class ElderWand : MysticalWeapon
    {
        public override string PrefabName => "ElderWand";
        public override string[] SourcePrefabs => new[] { "StaffIceShards", "StaffFireball" };
        public override string VanillaPeer => "StaffThunderBlood"; // a Deep North staff
        public override string DisplayName => "The Elder Wand";
        public override string Description =>
            "Fifteen inches of elder wood, knotted like a string of beads. The Deathstick, the " +
            "Wand of Destiny - unbeatable, they say, in the hand of its true master.";

        protected override string DefaultDamage => "pierce:16,lightning:22";
        protected override string DefaultDamagePerLevel => "pierce:2,lightning:3";
        // Very late game (Deep North), so Master of Death comes last: a frozen branch for the elder, Elaking hair for the core
        protected override string DefaultRecipe => "BarkaBranch:2,ElakingHairBundle:3,Gold:4,Eitr:20";
        protected override string DefaultStation => "piece_magetable";
        protected override int DefaultStationLevel => 5;

        private ConfigEntry<float> _eitrCost;
        private ConfigEntry<float> _staminaCost;
        private ConfigEntry<bool> _cycle;
        private ConfigEntry<string> _confringoDamage;
        private ConfigEntry<string> _expulsoDamage;
        private static ConfigEntry<float> _spellPower;
        private static ConfigEntry<float> _cooldown;

        public override void BindConfig(ConfigFile cfg)
        {
            base.BindConfig(cfg);
            _eitrCost = cfg.Bind(PrefabName, "EitrCost", 0f, "Eitr used per spell (vanilla staffs use eitr).");
            _staminaCost = cfg.Bind(PrefabName, "StaminaCost", 4f, "Stamina used per spell.");
            _cycle = cfg.Bind(PrefabName, "CycleSpells", true,
                "Each cast moves on to the next spell: Stupefy (bolts, the Damage above), Confringo (a fireball) and " +
                "Expulso (a burst of lightning). Off = Stupefy only. Restart to apply.");
            _confringoDamage = cfg.Bind(PrefabName, "ConfringoDamage", "blunt:20,fire:25",
                "Damage of Confringo, the fireball (it bursts on impact, like the Staff of Embers'). Restart to apply.");
            _expulsoDamage = cfg.Bind(PrefabName, "ExpulsoDamage", "lightning:5",
                "Damage of each of Expulso's bolts (a short-range spray, like Dundr's). Restart to apply.");
            _spellPower = cfg.Bind(PrefabName, "SpellPower", 2f,
                "Every spell's damage is multiplied by this, to make up for the time between spells.");
            _cooldown = cfg.Bind(PrefabName, "SpellCooldown", 1.5f, "Seconds the wand needs to gather power between spells.");
        }

        private const int Wood = 0, Knot = 1;

        public override MaterialSpec[] Materials => new[]
        {
            new MaterialSpec(new Color(0.62f, 0.56f, 0.46f), 0.3f), // pale elder wood
            new MaterialSpec(new Color(0.4f, 0.34f, 0.27f), 0.25f), // darker knots
        };

        private SE_Stats _wandEffect;

        private void CustomizeWand(ItemDrop.ItemData.SharedData shared)
        {
            foreach (var attack in new[] { shared.m_attack, shared.m_secondaryAttack })
            {
                if (attack == null) continue;
                attack.m_attackEitr = _eitrCost.Value;
                attack.m_attackStamina = _staminaCost.Value;
            }
            shared.m_useDurability = false; // the wand of destiny does not wear out
            shared.m_weight = 0.5f;

            _wandEffect = DeathlyHallows.MakeEffect("SE_ElderWand", "The Elder Wand",
                "The Deathstick answers to you.", Icon);
            DeathlyHallows.JoinSet(shared, Icon);
        }

        public override void RegisterExtras(ObjectDB db) => DeathlyHallows.Register(db, _wandEffect);

        // ------------------------------------------------------------- spells

        private class Spell
        {
            public string Name;
            public Attack Attack;
            public HitData.DamageTypes Damage;
        }

        private static readonly List<Spell> Spells = new List<Spell>();
        private static int _nextSpell;
        private static float _nextCast;

        protected override void Setup(GameObject prefab, ItemDrop.ItemData.SharedData shared)
        {
            base.Setup(prefab, shared);
            Spells.Clear();
            Spells.Add(new Spell { Name = "Stupefy", Attack = shared.m_attack, Damage = shared.m_damages });
            if (!_cycle.Value) return;
            AddSpell("Confringo", "StaffFireball", _confringoDamage.Value);
            AddSpell("Expulso", "StaffLightning", _expulsoDamage.Value); // Dundr
        }

        /// <summary>A spell that casts like a vanilla staff's primary attack, with our damage and costs.</summary>
        private void AddSpell(string name, string staffName, string damage)
        {
            var staff = FindVanilla(staffName);
            var source = staff ? staff.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_attack : null;
            if (source == null || !source.m_attackProjectile)
            {
                Plugin.Log.LogWarning($"{PrefabName}: no '{staffName}' to borrow {name} from; the wand skips it");
                return;
            }
            Spells.Add(new Spell { Name = name, Attack = source.Clone(), Damage = ParseDamage(damage) });
            Plugin.Log.LogInfo($"{PrefabName}: {name} cast like '{staffName}' (projectile '{source.m_attackProjectile.name}', " +
                               $"animation '{source.m_attackAnimation}', {source.m_projectiles} per cast)");
        }

        protected override void Customize(ItemDrop.ItemData.SharedData shared)
        {
            foreach (var spell in Spells)
            {
                spell.Attack.m_attackEitr = _eitrCost.Value;
                spell.Attack.m_attackStamina = _staminaCost.Value;
                spell.Attack.m_attackHealth = 0f;
                spell.Attack.m_attackHealthPercentage = 0f;
            }
            CustomizeWand(shared);
        }

        /// <summary>
        /// Before the local player casts with the wand, swap in the next spell's attack and damage (the game
        /// copies the attack when it starts, and reads the damage when the spell flies). Only the caster's client
        /// starts attacks; the projectiles sync to everyone.
        /// </summary>
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
        private static class CastPatch
        {
            private static bool Prefix(Humanoid __instance, bool secondaryAttack, ref bool __result, out Spell __state)
            {
                __state = null;
                if (secondaryAttack || Spells.Count == 0 || __instance != Player.m_localPlayer) return true;
                var wand = __instance.GetCurrentWeapon();
                if (wand?.m_dropPrefab == null || wand.m_dropPrefab.name != "ElderWand" || __instance.InAttack()) return true;
                // Skips the cast while the wand gathers power: staffs have no cooldown of their own to lengthen
                if (Time.time < _nextCast)
                {
                    __result = false;
                    return false;
                }
                __state = Spells[_nextSpell % Spells.Count];
                wand.m_shared.m_attack = __state.Attack;
                wand.m_shared.m_damages = __state.Damage;
                __state.Attack.m_damageMultiplier = _spellPower.Value;
                return true;
            }

            private static void Postfix(Humanoid __instance, bool __result, Spell __state)
            {
                if (__state == null || !__result) return;
                _nextSpell = (_nextSpell + 1) % Spells.Count;
                _nextCast = Time.time + _cooldown.Value;
                __instance.Message(MessageHud.MessageType.TopLeft, __state.Name + "!");
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

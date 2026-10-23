using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MysticalWeapons.Hallows
{
    /// <summary>
    /// The Deathly Hallows (Harry Potter). Each Hallow is useful alone; carrying all three -
    /// wand in hand, stone in the utility slot, cloak on your shoulders - makes you Master of Death.
    /// Vanilla set bonuses count the weapons in your hands, so the wand counts towards the set.
    /// </summary>
    internal static class DeathlyHallows
    {
        private const string SetName = "DeathlyHallows";
        private static SE_Stats _masterOfDeath;

        public static void JoinSet(ItemDrop.ItemData.SharedData shared, Sprite icon)
        {
            shared.m_setName = SetName;
            shared.m_setSize = 3;
            shared.m_setStatusEffect = GetSetEffect(icon);
        }

        private static SE_Stats GetSetEffect(Sprite icon)
        {
            if (_masterOfDeath) return _masterOfDeath;

            _masterOfDeath = ScriptableObject.CreateInstance<SE_Stats>();
            _masterOfDeath.name = "SE_MasterOfDeath";
            _masterOfDeath.m_name = "Master of Death";
            _masterOfDeath.m_tooltip = "You hold all three Hallows. Death greets you as an old friend: " +
                                       "your wounds close quickly, poison and spirits cannot touch you, and if you fall " +
                                       "you keep everything you carry.";
            _masterOfDeath.m_icon = icon;
            _masterOfDeath.m_healthRegenMultiplier = 1.5f;
            _masterOfDeath.m_staminaRegenMultiplier = 1.25f;
            _masterOfDeath.m_mods = new List<HitData.DamageModPair>
            {
                new HitData.DamageModPair { m_type = HitData.DamageType.Poison, m_modifier = HitData.DamageModifier.Resistant },
                new HitData.DamageModPair { m_type = HitData.DamageType.Spirit, m_modifier = HitData.DamageModifier.Immune },
            };
            return _masterOfDeath;
        }

        public static void Register(ObjectDB db, params StatusEffect[] effects)
        {
            foreach (var se in effects)
                if (se && !db.m_StatusEffects.Contains(se)) db.m_StatusEffects.Add(se);
            if (_masterOfDeath && !db.m_StatusEffects.Contains(_masterOfDeath)) db.m_StatusEffects.Add(_masterOfDeath);
        }

        // ------------------------------------------------------------ death

        private static ConfigEntry<bool> _masterKeepsItems;
        private static bool _keepAll;

        public static void BindConfig(ConfigFile cfg)
        {
            _masterKeepsItems = cfg.Bind(SetName, "MasterOfDeathKeepsItems", true,
                "Die as Master of Death (all three Hallows) and you keep everything you carry: no tombstone.");
        }

        private static bool IsMasterOfDeath(Player player) =>
            _masterOfDeath && player.GetSEMan().HaveStatusEffect(_masterOfDeath.NameHash());

        /// <summary>The set bonus is still on when the player dies; read it before anything is taken off.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
        private static class PlayerDeathPatch
        {
            private static void Prefix(Player __instance) =>
                _keepAll = __instance == Player.m_localPlayer && _masterKeepsItems != null && _masterKeepsItems.Value &&
                           IsMasterOfDeath(__instance);

            private static void Postfix() => _keepAll = false;
        }

        /// <summary>
        /// A Master of Death keeps everything; anyone else keeps their Resurrection Stones (if configured) and
        /// loses the rest to the tombstone as usual. Runs on the dying player's client, which owns their items.
        /// </summary>
        [HarmonyPatch(typeof(Player), "CreateTombStone")]
        private static class TombStonePatch
        {
            private static bool Prefix(Player __instance, out List<ItemDrop.ItemData> __state)
            {
                __state = null;
                if (__instance != Player.m_localPlayer) return true;
                if (_keepAll)
                {
                    // Skips the tombstone: like the "keep inventory" world modifier, which is checked in here too
                    __instance.Message(MessageHud.MessageType.Center, "Death greets you as an old friend, and takes nothing.");
                    return false;
                }
                if (!ResurrectionStone.StaysOnDeath) return true;

                var inventory = __instance.GetInventory();
                __state = inventory.GetAllItems().FindAll(i => IsItem(i, "ResurrectionStone"));
                foreach (var stone in __state)
                {
                    if (stone.m_equipped) __instance.UnequipItem(stone, false);
                    inventory.RemoveItem(stone);
                }
                return true;
            }

            private static void Postfix(Player __instance, List<ItemDrop.ItemData> __state)
            {
                if (__state == null) return;
                foreach (var stone in __state) __instance.GetInventory().AddItem(stone);
            }
        }

        public static bool IsItem(ItemDrop.ItemData item, string prefabName) =>
            item != null && item.m_dropPrefab && item.m_dropPrefab.name == prefabName;

        /// <summary>Simple status effect that only shows a name, icon and tooltip, plus whatever the caller sets.</summary>
        public static SE_Stats MakeEffect(string id, string name, string tooltip, Sprite icon)
        {
            var se = ScriptableObject.CreateInstance<SE_Stats>();
            se.name = id;
            se.m_name = name;
            se.m_tooltip = tooltip;
            se.m_icon = icon;
            return se;
        }
    }
}

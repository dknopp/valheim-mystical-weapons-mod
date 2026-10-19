using System.Collections.Generic;
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
                                       "your wounds close quickly and poison and spirits cannot touch you.";
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

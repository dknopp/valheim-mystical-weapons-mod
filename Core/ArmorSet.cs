using System.Collections.Generic;
using HarmonyLib;

namespace MysticalWeapons
{
    /// <summary>
    /// An armour set whose bonuses grow with the pieces worn (vanilla grants one bonus, at the full set). Each tier is
    /// its own status effect; tiers stack, so a 3-piece wearer keeps the 2-piece bonus. Applied on the wearer's own
    /// client, alongside the game's own equipment effects.
    /// </summary>
    internal class ArmorSet
    {
        private static readonly List<ArmorSet> Sets = new List<ArmorSet>();

        public readonly string Name;
        private readonly HashSet<string> _pieces;
        /// <summary>Pieces needed, and the bonus they grant.</summary>
        private readonly SortedDictionary<int, StatusEffect> _tiers = new SortedDictionary<int, StatusEffect>();

        public ArmorSet(string name, params string[] piecePrefabNames)
        {
            Name = name;
            _pieces = new HashSet<string>(piecePrefabNames);
            Sets.Add(this);
        }

        public void SetBonus(int pieces, StatusEffect effect) => _tiers[pieces] = effect;

        public void Register(ObjectDB db)
        {
            foreach (var se in _tiers.Values)
                if (se && !db.m_StatusEffects.Contains(se)) db.m_StatusEffects.Add(se);
        }

        private int CountWorn(Humanoid wearer)
        {
            var count = 0;
            foreach (var item in wearer.GetInventory().GetEquippedItems())
                if (item.m_dropPrefab && _pieces.Contains(item.m_dropPrefab.name)) count++;
            return count;
        }

        private void Apply(Humanoid wearer)
        {
            var worn = CountWorn(wearer);
            var seman = wearer.GetSEMan();
            foreach (var tier in _tiers)
            {
                if (!tier.Value) continue;
                var hash = tier.Value.NameHash();
                var on = worn >= tier.Key;
                if (on && !seman.HaveStatusEffect(hash)) seman.AddStatusEffect(tier.Value);
                else if (!on && seman.HaveStatusEffect(hash)) seman.RemoveStatusEffect(hash, true);
            }
        }

        /// <summary>The game re-checks equipment effects whenever gear goes on or off, and when the player loads.</summary>
        [HarmonyPatch(typeof(Humanoid), "UpdateEquipmentStatusEffects")]
        private static class EquipmentPatch
        {
            private static void Postfix(Humanoid __instance)
            {
                if (!(__instance is Player) || !__instance.IsOwner() || __instance.GetInventory() == null) return;
                foreach (var set in Sets) set.Apply(__instance);
            }
        }
    }
}

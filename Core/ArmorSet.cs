using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
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
        private readonly string[] _pieceOrder;
        private readonly HashSet<string> _pieces;
        /// <summary>Items that aren't pieces but show the one bonus they're needed for (a weapon), and that bonus's tier.</summary>
        private readonly Dictionary<string, int> _shownOn = new Dictionary<string, int>();
        /// <summary>Pieces needed, and the bonus they grant.</summary>
        private readonly SortedDictionary<int, StatusEffect> _tiers = new SortedDictionary<int, StatusEffect>();

        public ArmorSet(string name, params string[] piecePrefabNames)
        {
            Name = name;
            _pieceOrder = piecePrefabNames;
            _pieces = new HashSet<string>(piecePrefabNames);
            Sets.Add(this);
        }

        public void SetBonus(int pieces, StatusEffect effect) => _tiers[pieces] = effect;

        /// <summary>Show the set's name, the pieces worn and the bonus for <paramref name="tier"/> pieces on another item.</summary>
        public void ShowOn(string prefabName, int tier) => _shownOn[prefabName] = tier;

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
            ShowTopIcon(seman, worn);
        }

        /// <summary>
        /// One icon for the set, as vanilla shows: the highest bonus worn shows its own name and picture, and the ones
        /// below it still apply, unseen (the game lists only effects with an icon).
        /// </summary>
        private void ShowTopIcon(SEMan seman, int worn)
        {
            StatusEffect top = null;
            foreach (var tier in _tiers)
                if (tier.Value && worn >= tier.Key) top = tier.Value;
            foreach (var tier in _tiers)
            {
                if (!tier.Value) continue;
                var active = seman.GetStatusEffect(tier.Value.NameHash());
                if (active) active.m_icon = tier.Value == top ? tier.Value.m_icon : null;
            }
        }

        private const string HeaderColour = "#7CD36B", HaveColour = "orange", MissingColour = "#8A8A8A";

        /// <summary>
        /// The set's block for an item tooltip, as Diablo shows sets: the pieces (grey if you aren't wearing them) and
        /// each bonus by the pieces it needs (orange once you have it, grey until then). With <paramref name="onlyTier"/>,
        /// just the name, the pieces worn and that one bonus: for an item the bonus needs, whose own stats come first.
        /// </summary>
        private string Tooltip(int onlyTier = 0)
        {
            var worn = new HashSet<string>();
            var inventory = Player.m_localPlayer ? Player.m_localPlayer.GetInventory() : null;
            if (inventory != null)
                foreach (var item in inventory.GetEquippedItems())
                    if (item.m_dropPrefab) worn.Add(item.m_dropPrefab.name);
            var count = _pieceOrder.Count(worn.Contains);

            var text = new StringBuilder();
            text.Append($"\n\n<color={HeaderColour}>{Name} ({count}/{_pieceOrder.Length})</color>");
            foreach (var piece in onlyTier > 0 ? new string[0] : _pieceOrder)
            {
                var prefab = ObjectDB.instance ? ObjectDB.instance.GetItemPrefab(piece) : null;
                var name = prefab && prefab.GetComponent<ItemDrop>() ? prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name : piece;
                text.Append(worn.Contains(piece) ? $"\n  {name}" : $"\n  <color={MissingColour}>{name}</color>");
            }
            foreach (var tier in _tiers)
            {
                if (!tier.Value || (onlyTier > 0 && tier.Key != onlyTier)) continue;
                var have = count >= tier.Key;
                text.Append($"\n<color={(have ? HaveColour : MissingColour)}>({tier.Key}) {tier.Value.m_name}</color>");
                text.Append(have ? $"\n  {tier.Value.m_tooltip}" : $"\n  <color={MissingColour}>{tier.Value.m_tooltip}</color>");
            }
            return text.ToString();
        }

        /// <summary>Adds the set's block under the item's own stats, on every piece and on the items it's shown on.</summary>
        [HarmonyPatch]
        private static class TooltipPatch
        {
            private static MethodBase TargetMethod() => AccessTools.GetDeclaredMethods(typeof(ItemDrop.ItemData))
                .FirstOrDefault(m => m.Name == "GetTooltip" && m.IsStatic);
            private static bool Prepare() => TargetMethod() != null;

            // __0: the item (the parameter's name isn't part of the game's API)
            private static void Postfix(ItemDrop.ItemData __0, ref string __result)
            {
                var prefab = __0 != null && __0.m_dropPrefab ? __0.m_dropPrefab.name : null;
                if (prefab == null) return;
                foreach (var set in Sets)
                {
                    if (set._pieces.Contains(prefab)) __result += set.Tooltip();
                    else if (set._shownOn.TryGetValue(prefab, out var tier)) __result += set.Tooltip(tier);
                }
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

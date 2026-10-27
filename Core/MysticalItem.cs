using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MysticalWeapons
{
    /// <summary>
    /// Base class for every item in the mod. An item clones a vanilla prefab (for animations,
    /// sounds, equip behaviour), renames it, changes it in <see cref="Setup"/> and adds a recipe.
    /// </summary>
    internal abstract class MysticalItem
    {
        public abstract string PrefabName { get; }
        /// <summary>Vanilla prefabs to clone, first one found wins.</summary>
        public abstract string[] SourcePrefabs { get; }
        /// <summary>
        /// Vanilla item to compare against in the item report (see <see cref="ItemReport"/>).
        /// Null = the source prefab this item was cloned from.
        /// </summary>
        public virtual string VanillaPeer => null;
        public abstract string DisplayName { get; }
        public abstract string Description { get; }

        protected abstract string DefaultRecipe { get; }
        protected virtual string DefaultStation => "forge";
        protected abstract int DefaultStationLevel { get; }

        /// <summary>Turn the freshly cloned prefab into this item (model, icon, stats).</summary>
        protected abstract void Setup(GameObject prefab, ItemDrop.ItemData.SharedData shared);

        /// <summary>Item-specific tweaks run after <see cref="Setup"/>: knockback, durability, effects...</summary>
        protected virtual void Customize(ItemDrop.ItemData.SharedData shared) { }

        /// <summary>Register status effects etc. with an ObjectDB.</summary>
        public virtual void RegisterExtras(ObjectDB db) { }

        /// <summary>Register networked prefabs this item spawns (creatures, projectiles...) with a ZNetScene.</summary>
        public virtual void RegisterNetExtras(ZNetScene scene) { }

        /// <summary>Rebuild a custom model from disk (dev command mw_reload). Most items have none.</summary>
        public virtual void ReloadCustomModel() { }

        // ------------------------------------------------------------- config

        public ConfigEntry<bool> Enabled;
        private ConfigEntry<string> _recipe;
        private ConfigEntry<string> _station;
        private ConfigEntry<int> _stationLevel;
        private ConfigEntry<string> _bossTrophy;

        /// <summary>
        /// Boss trophy needed to craft the items that are stronger than their vanilla tier (above-tier stats or a big
        /// hidden power): the boss that opens the item's home biome, so the player beats it again first. Items in
        /// line with their vanilla peers need none. Kept in one table so the whole progression reads at a glance.
        /// </summary>
        private static readonly Dictionary<string, string> BossTrophies = new Dictionary<string, string>
        {
            { "ResurrectionStone", "TrophyBonemass" },        // no skill loss on death
            { "ShardplateHelm", "TrophyDragonQueen" },        // Moder: armour above its tier, and the set bonus
            { "ShardplateBreastplate", "TrophyDragonQueen" },
            { "ShardplateGreaves", "TrophyDragonQueen" },
            { "BrotherhoodHiddenBlades", "TrophyDragonQueen" }, // Moder, past the Mountains: Assassination; Bonemass would gate nothing
            { "Anduril", "TrophyGoblinKing" },                // Yagluth: spirits that fight for you
            { "Sylspear", "TrophyGoblinKing" },               // fells trees and breaks rock, never wears out
            { "Shardblade", "TrophyGoblinKing" },
            { "ElderWand", "TrophyFader" },                   // completes Master of Death
        };

        public virtual void BindConfig(ConfigFile cfg)
        {
            var s = PrefabName;
            Enabled = cfg.Bind(s, "Enabled", true, "Add this item to the game.");
            _recipe = cfg.Bind(s, "Recipe", DefaultRecipe, "Comma separated PrefabName:Amount list.");
            _station = cfg.Bind(s, "CraftingStation", DefaultStation,
                "Prefab name of the crafting station (forge, piece_workbench, blackforge...). Empty = craft by hand.");
            _stationLevel = cfg.Bind(s, "StationLevel", DefaultStationLevel, "Minimum crafting station level.");
            _bossTrophy = cfg.Bind(s, "BossTrophy", BossTrophies.TryGetValue(s, out var trophy) ? trophy : "",
                "Boss trophy also needed to craft it, once (upgrades don't take another). The recipe stays hidden until " +
                "you have picked one up, so beat that boss first. Empty = no trophy.");
        }

        // ------------------------------------------------------------- prefab

        private static Transform _holder;
        public GameObject Prefab { get; private set; }
        /// <summary>Name of the vanilla prefab this item was cloned from.</summary>
        public string SourceName { get; private set; }
        public Sprite Icon { get; protected set; }
        /// <summary>Looks up a vanilla prefab by name; only set during <see cref="Setup"/>.</summary>
        protected Func<string, GameObject> FindVanilla { get; private set; }

        public GameObject GetOrCreatePrefab(Func<string, GameObject> findVanilla)
        {
            if (Prefab) return Prefab;

            GameObject source = null;
            foreach (var name in SourcePrefabs)
                if ((source = findVanilla(name))) break;
            if (!source)
            {
                Plugin.Log.LogError($"{PrefabName}: none of the source prefabs found ({string.Join(", ", SourcePrefabs)})");
                return null;
            }

            SourceName = source.name;
            Prefab = CloneVanilla(source, PrefabName);

            var drop = Prefab.GetComponent<ItemDrop>();
            drop.m_itemData.m_dropPrefab = Prefab;
            var shared = drop.m_itemData.m_shared;
            shared.m_name = DisplayName;
            shared.m_description = Description;
            FindVanilla = findVanilla;
            Setup(Prefab, shared);
            FindVanilla = null;
            Customize(shared);

            Plugin.Log.LogInfo($"Created {PrefabName} from {source.name}");
            return Prefab;
        }

        /// <summary>A renamed copy of a vanilla prefab that is never live itself, to register as a new prefab.</summary>
        protected static GameObject CloneVanilla(GameObject source, string name)
        {
            var clone = Object.Instantiate(source, Holder);
            clone.name = name;
            return clone;
        }

        /// <summary>An empty prefab of our own (never live itself), to fill with components and register.</summary>
        internal static GameObject NewPrefab(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(Holder, false);
            return go;
        }

        private static Transform Holder
        {
            get
            {
                if (!_holder)
                {
                    // Instantiate under an inactive parent so no Awake() runs on the clones
                    var go = new GameObject("MysticalWeaponsPrefabs");
                    go.SetActive(false);
                    Object.DontDestroyOnLoad(go);
                    _holder = go.transform;
                }
                return _holder;
            }
        }

        /// <summary>Adds a prefab to a ZNetScene so it can be spawned and synced; false if the name is taken.</summary>
        public static bool RegisterNetPrefab(ZNetScene scene, GameObject prefab)
        {
            if (scene.m_prefabs.Contains(prefab)) return true;
            if (scene.m_prefabs.Exists(go => go && go.name == prefab.name)) return false;
            scene.m_prefabs.Add(prefab);
            if (NamedPrefabs(scene) is Dictionary<int, GameObject> named) named[prefab.name.GetStableHashCode()] = prefab;
            return true;
        }

        private static readonly HarmonyLib.AccessTools.FieldRef<ZNetScene, Dictionary<int, GameObject>> NamedPrefabs =
            HarmonyLib.AccessTools.FieldRefAccess<ZNetScene, Dictionary<int, GameObject>>("m_namedPrefabs");

        // ------------------------------------------------------------- recipe

        public void AddRecipe(ObjectDB db)
        {
            var drop = Prefab.GetComponent<ItemDrop>();
            if (db.m_recipes.Exists(r => r && r.m_item == drop)) return;

            CraftingStation station = null;
            var stationName = _station.Value.Trim();
            if (stationName.Length > 0)
            {
                var withStation = db.m_recipes.Find(r => r && r.m_craftingStation && r.m_craftingStation.name == stationName);
                station = withStation ? withStation.m_craftingStation : null;
                if (!station) Plugin.Log.LogWarning($"{PrefabName}: crafting station '{stationName}' not found");
            }

            var recipe = ScriptableObject.CreateInstance<Recipe>();
            recipe.name = "Recipe_" + PrefabName;
            recipe.m_item = drop;
            recipe.m_amount = 1;
            recipe.m_enabled = true;
            recipe.m_craftingStation = station;
            recipe.m_repairStation = station;
            recipe.m_minStationLevel = _stationLevel.Value;
            recipe.m_resources = WithBossTrophy(db, ParseRequirements(db, _recipe.Value), out var trophy);
            if (trophy) TrophyGates[PrefabName] = trophy.m_itemData.m_shared.m_name;
            else TrophyGates.Remove(PrefabName);
            // The crafting panel has four ingredient slots: a fifth is still required but never shown
            if (recipe.m_resources.Length > MaxIngredients)
                Plugin.Log.LogWarning($"{PrefabName}: recipe needs {recipe.m_resources.Length} ingredients (boss trophy " +
                                      $"included) but the crafting panel shows only {MaxIngredients}; shorten its Recipe");
            db.m_recipes.Add(recipe);
        }

        /// <summary>Ingredient slots in the game's crafting panel.</summary>
        private const int MaxIngredients = 4;

        /// <summary>The trophy (its shared name) each gated item's recipe waits for, by the item's prefab name.</summary>
        private static readonly Dictionary<string, string> TrophyGates = new Dictionary<string, string>();

        /// <summary>
        /// Leaves a trophy-gated recipe out of the crafting list until the player has picked up that trophy (the game's
        /// own "known material" check). The game already does this for recipes a player hasn't seen yet, but once
        /// known a recipe stays listed, so a character that knew one before it needed a trophy would still see it.
        /// </summary>
        [HarmonyLib.HarmonyPatch(typeof(Player), nameof(Player.GetAvailableRecipes))]
        private static class TrophyGatePatch
        {
            private static void Postfix(Player __instance, ref List<Recipe> available)
            {
                if (TrophyGates.Count == 0 || available == null) return;
                available.RemoveAll(r => r && r.m_item && TrophyGates.TryGetValue(r.m_item.name, out var trophy) &&
                                         !__instance.IsMaterialKnown(trophy));
            }
        }

        /// <summary>
        /// Adds the boss trophy to the recipe, needed once: none per upgrade level. <see cref="TrophyGatePatch"/>
        /// keeps the recipe hidden until the player has picked up that trophy.
        /// </summary>
        private Piece.Requirement[] WithBossTrophy(ObjectDB db, Piece.Requirement[] resources, out ItemDrop trophyDrop)
        {
            trophyDrop = null;
            var name = _bossTrophy.Value.Trim();
            if (name.Length == 0) return resources;
            var trophy = db.GetItemPrefab(name);
            trophyDrop = trophy ? trophy.GetComponent<ItemDrop>() : null;
            if (!trophyDrop)
            {
                Plugin.Log.LogWarning($"{PrefabName}: boss trophy '{name}' not found; the recipe doesn't need one");
                return resources;
            }
            var list = new List<Piece.Requirement>(resources);
            list.RemoveAll(r => r.m_resItem && r.m_resItem.name == name); // already in the recipe: the trophy setting wins
            list.Add(new Piece.Requirement
            {
                m_resItem = trophyDrop,
                m_amount = 1,
                m_amountPerLevel = 0,
                m_recover = true
            });
            return list.ToArray();
        }

        private Piece.Requirement[] ParseRequirements(ObjectDB db, string spec)
        {
            var list = new List<Piece.Requirement>();
            foreach (var part in spec.Split(','))
            {
                var kv = part.Split(':');
                if (kv.Length != 2 || !int.TryParse(kv[1].Trim(), out var amount)) continue;
                var item = db.GetItemPrefab(kv[0].Trim());
                if (!item)
                {
                    Plugin.Log.LogWarning($"{PrefabName}: recipe item '{kv[0].Trim()}' not found, skipping");
                    continue;
                }
                var drop = item.GetComponent<ItemDrop>();
                // Gear the item is reforged from (an Iron Sword, a Battleaxe: anything with upgrade levels of its
                // own) is consumed once, by the first craft; materials are paid again at every upgrade
                var consumedGear = drop && drop.m_itemData.m_shared.m_maxQuality > 1;
                list.Add(new Piece.Requirement
                {
                    m_resItem = drop,
                    m_amount = amount,
                    m_amountPerLevel = consumedGear ? 0 : Mathf.Max(1, amount / 2),
                    m_recover = true
                });
            }
            return list.ToArray();
        }
    }
}

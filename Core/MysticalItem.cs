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

            if (!_holder)
            {
                // Instantiate under an inactive parent so no Awake() runs on the clones
                var go = new GameObject("MysticalWeaponsPrefabs");
                go.SetActive(false);
                Object.DontDestroyOnLoad(go);
                _holder = go.transform;
            }

            SourceName = source.name;
            Prefab = Object.Instantiate(source, _holder);
            Prefab.name = PrefabName;

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
            recipe.m_resources = WithBossTrophy(db, ParseRequirements(db, _recipe.Value));
            db.m_recipes.Add(recipe);
        }

        /// <summary>
        /// Adds the boss trophy to the recipe, needed once: none per upgrade level. The game lists a recipe only
        /// once the player has picked up every ingredient, so this also hides it until the boss is beaten.
        /// </summary>
        private Piece.Requirement[] WithBossTrophy(ObjectDB db, Piece.Requirement[] resources)
        {
            var name = _bossTrophy.Value.Trim();
            if (name.Length == 0) return resources;
            var trophy = db.GetItemPrefab(name);
            if (!trophy)
            {
                Plugin.Log.LogWarning($"{PrefabName}: boss trophy '{name}' not found; the recipe doesn't need one");
                return resources;
            }
            var list = new List<Piece.Requirement>(resources);
            list.RemoveAll(r => r.m_resItem && r.m_resItem.name == name); // already in the recipe: the trophy setting wins
            list.Add(new Piece.Requirement
            {
                m_resItem = trophy.GetComponent<ItemDrop>(),
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
                list.Add(new Piece.Requirement
                {
                    m_resItem = item.GetComponent<ItemDrop>(),
                    m_amount = amount,
                    m_amountPerLevel = Mathf.Max(1, amount / 2),
                    m_recover = true
                });
            }
            return list.ToArray();
        }
    }
}

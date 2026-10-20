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
        public abstract string DisplayName { get; }
        public abstract string Description { get; }

        protected abstract string DefaultRecipe { get; }
        protected virtual string DefaultStation => "forge";
        protected abstract int DefaultStationLevel { get; }

        /// <summary>Turn the freshly cloned prefab into this item (model, icon, stats).</summary>
        protected abstract void Setup(GameObject prefab, ItemDrop.ItemData.SharedData shared);

        /// <summary>Looks up a vanilla prefab by name; valid during <see cref="Setup"/>.</summary>
        protected Func<string, GameObject> FindVanilla { get; private set; }

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

        public virtual void BindConfig(ConfigFile cfg)
        {
            var s = PrefabName;
            Enabled = cfg.Bind(s, "Enabled", true, "Add this item to the game.");
            _recipe = cfg.Bind(s, "Recipe", DefaultRecipe, "Comma separated PrefabName:Amount list.");
            _station = cfg.Bind(s, "CraftingStation", DefaultStation,
                "Prefab name of the crafting station (forge, piece_workbench, blackforge...). Empty = craft by hand.");
            _stationLevel = cfg.Bind(s, "StationLevel", DefaultStationLevel, "Minimum crafting station level.");
        }

        // ------------------------------------------------------------- prefab

        private static Transform _holder;
        public GameObject Prefab { get; private set; }
        public Sprite Icon { get; protected set; }

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

            Prefab = Object.Instantiate(source, _holder);
            Prefab.name = PrefabName;

            var drop = Prefab.GetComponent<ItemDrop>();
            drop.m_itemData.m_dropPrefab = Prefab;
            var shared = drop.m_itemData.m_shared;
            shared.m_name = DisplayName;
            shared.m_description = Description;
            FindVanilla = findVanilla;
            Setup(Prefab, shared);
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
            recipe.m_resources = ParseRequirements(db, _recipe.Value);
            db.m_recipes.Add(recipe);
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

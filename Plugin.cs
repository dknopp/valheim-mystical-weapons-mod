using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using MysticalWeapons.Armor;
using MysticalWeapons.Hallows;
using MysticalWeapons.Weapons;
using UnityEngine;

namespace MysticalWeapons
{
    [BepInPlugin(Guid, "Mystical Weapons from Far Off Lands", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dankn.valheim.mysticalweapons";
        public const string Version = "1.6.3";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> TexturedModels;

        /// <summary>Every item in the mod. Add new ones here.</summary>
        internal static readonly List<MysticalItem> Items = new List<MysticalItem>
        {
            new Batleth(),
            new Sylspear(),
            new Shardblade(),
            new Anduril(),
            new Sulfuras(),

            new ShardplateHelm(),
            new ShardplateBreastplate(),
            new ShardplateGreaves(),

            new ElderWand(),
            new ResurrectionStone(),
            new InvisibilityCloak(),
        };

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            TexturedModels = Config.Bind("General", "TexturedModels", true,
                "Texture code-built weapons with tinted textures borrowed from vanilla items. Off = flat colours.");
            foreach (var item in Items) item.BindConfig(Config);
            ItemReport.BindConfig(Config);
            new Harmony(Guid).PatchAll();
            Log.LogInfo($"Loaded {Items.Count} mystical items.");
        }

        /// <summary>Any vanilla item; if it is missing the database is not loaded yet.</summary>
        private const string VanillaProbe = "Wood";

        internal static void RegisterItems(ObjectDB db)
        {
            if (db.m_items == null || !db.m_items.Exists(go => go && go.name == VanillaProbe)) return;

            var added = false;
            foreach (var item in Items)
            {
                if (!item.Enabled.Value) continue;
                Guard(item, () =>
                {
                    var prefab = item.GetOrCreatePrefab(name => db.m_items.Find(go => go && go.name == name));
                    if (!prefab) return;

                    if (!db.m_items.Contains(prefab))
                    {
                        if (TakenByOther(db.m_items, prefab)) return;
                        db.m_items.Add(prefab);
                        added = true;
                    }
                    item.RegisterExtras(db);
                });
            }
            if (added)
                Guard(null, () => AccessTools.Method(typeof(ObjectDB), "UpdateRegisters")?.Invoke(db, null));

            foreach (var item in Items)
                if (item.Enabled.Value && item.Prefab && db.m_items.Contains(item.Prefab))
                    Guard(item, () => item.AddRecipe(db));

            ItemReport.Schedule(db);
        }

        internal static void RegisterNetPrefabs(ZNetScene scene)
        {
            if (!scene.m_prefabs.Exists(go => go && go.name == VanillaProbe)) return;
            var named = AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs").GetValue(scene)
                as Dictionary<int, GameObject>;

            foreach (var item in Items)
            {
                if (!item.Enabled.Value) continue;
                Guard(item, () =>
                {
                    var prefab = item.GetOrCreatePrefab(name => scene.m_prefabs.Find(go => go && go.name == name));
                    if (!prefab || scene.m_prefabs.Contains(prefab) || TakenByOther(scene.m_prefabs, prefab)) return;

                    scene.m_prefabs.Add(prefab);
                    if (named != null) named[prefab.name.GetStableHashCode()] = prefab;
                });
            }
        }

        /// <summary>Another mod already registered a prefab with this name (e.g. the old standalone Bat'leth).</summary>
        private static bool TakenByOther(List<GameObject> prefabs, GameObject ours)
        {
            if (!prefabs.Exists(go => go && go != ours && go.name == ours.name)) return false;
            Log.LogWarning($"Another mod already adds an item named '{ours.name}'; skipping ours. " +
                           "If that is the old Bat'leth mod, uninstall it - Mystical Weapons includes the bat'leth.");
            return true;
        }

        /// <summary>
        /// Registration runs inside Valheim's startup; an exception escaping here would abort the
        /// main menu setup (matchmaking, PlayFab...). Log it and carry on instead.
        /// </summary>
        private static void Guard(MysticalItem item, System.Action action)
        {
            try { action(); }
            catch (System.Exception e) { Log.LogError($"{item?.PrefabName ?? "Registration"} failed: {e}"); }
        }
    }

    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class ObjectDBAwakePatch
    {
        private static void Postfix(ObjectDB __instance) => Plugin.RegisterItems(__instance);
    }

    [HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
    internal static class ObjectDBCopyPatch
    {
        private static void Postfix(ObjectDB __instance) => Plugin.RegisterItems(__instance);
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class ZNetSceneAwakePatch
    {
        private static void Postfix(ZNetScene __instance) => Plugin.RegisterNetPrefabs(__instance);
    }
}

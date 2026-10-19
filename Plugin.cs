using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using MysticalWeapons.Armor;
using MysticalWeapons.Weapons;
using UnityEngine;

namespace MysticalWeapons
{
    [BepInPlugin(Guid, "Mystical Weapons from Far Off Lands", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "dankn.valheim.mysticalweapons";
        public const string Version = "1.3.0";

        internal static ManualLogSource Log;

        /// <summary>Every item in the mod. Add new ones here.</summary>
        internal static readonly List<MysticalItem> Items = new List<MysticalItem>
        {
            new Batleth(),
            new Sylspear(),
            new Shardblade(),
            new Anduril(),
            new ShardplateHelm(),
            new ShardplateBreastplate(),
            new ShardplateGreaves(),
        };

        private void Awake()
        {
            Log = Logger;
            foreach (var item in Items) item.BindConfig(Config);
            new Harmony(Guid).PatchAll();
            Log.LogInfo($"Loaded {Items.Count} mystical items.");
        }

        internal static void RegisterItems(ObjectDB db)
        {
            if (db.m_items == null || db.m_items.Count == 0) return;

            var added = false;
            foreach (var item in Items)
            {
                if (!item.Enabled.Value) continue;
                var prefab = item.GetOrCreatePrefab(name => db.m_items.Find(go => go && go.name == name));
                if (!prefab) continue;

                if (!db.m_items.Contains(prefab))
                {
                    db.m_items.Add(prefab);
                    added = true;
                }
                item.RegisterExtras(db);
            }
            if (added) AccessTools.Method(typeof(ObjectDB), "UpdateRegisters")?.Invoke(db, null);

            foreach (var item in Items)
                if (item.Enabled.Value && item.Prefab) item.AddRecipe(db);
        }

        internal static void RegisterNetPrefabs(ZNetScene scene)
        {
            var named = AccessTools.Field(typeof(ZNetScene), "m_namedPrefabs").GetValue(scene)
                as Dictionary<int, GameObject>;

            foreach (var item in Items)
            {
                if (!item.Enabled.Value) continue;
                var prefab = item.GetOrCreatePrefab(name => scene.m_prefabs.Find(go => go && go.name == name));
                if (!prefab) continue;

                if (!scene.m_prefabs.Contains(prefab)) scene.m_prefabs.Add(prefab);
                if (named != null) named[prefab.name.GetStableHashCode()] = prefab;
            }
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

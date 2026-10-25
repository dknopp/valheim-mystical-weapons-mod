using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace DevAutoRun
{
    /// <summary>Checks run in the loaded world. Each logs "DevAutoRun: PROBE ..." lines; the summary line is what run.ps1 reads.</summary>
    internal static class Probes
    {
        public static void Run(ManualLogSource log)
        {
            var items = DevAutoRunPlugin.MysticalItems()?.ToList();
            if (items == null)
            {
                log.LogError("DevAutoRun: PROBE Mystical Weapons plugin not loaded");
                log.LogInfo("DevAutoRun: RESULT pass 0 fail 1");
                return;
            }

            int pass = 0, fail = 0;
            foreach (var item in items)
            {
                var type = item.GetType();
                var name = (string)type.GetProperty("PrefabName").GetValue(item);
                var enabled = (type.GetField("Enabled").GetValue(item) as ConfigEntry<bool>)?.Value ?? true;
                if (!enabled) { log.LogInfo($"DevAutoRun: PROBE {name} SKIP (disabled in config)"); continue; }

                var problems = new List<string>();
                var prefab = ObjectDB.instance.GetItemPrefab(name);
                if (!prefab) problems.Add("not in ObjectDB");
                else if (!prefab.GetComponent<ItemDrop>()) problems.Add("no ItemDrop");
                if (!ZNetScene.instance.GetPrefab(name)) problems.Add("not in ZNetScene");
                if (!ObjectDB.instance.m_recipes.Any(r => r && r.m_item && r.m_item.name == name)) problems.Add("no recipe");

                if (problems.Count == 0) { pass++; log.LogInfo($"DevAutoRun: PROBE {name} OK"); }
                else { fail++; log.LogError($"DevAutoRun: PROBE {name} FAIL ({string.Join(", ", problems)})"); }
            }
            if (ObjectDB.instance.GetItemPrefab("PortalScroll"))
            {
                var problem = PortalPair();
                if (problem == null) { pass++; log.LogInfo("DevAutoRun: PROBE PortalScroll opens a pair OK"); }
                else { fail++; log.LogError($"DevAutoRun: PROBE PortalScroll opens a pair FAIL ({problem})"); }
            }
            log.LogInfo($"DevAutoRun: RESULT pass {pass} fail {fail}");
        }

        /// <summary>Reads a scroll as a player would and checks both portal ends exist as ZDOs, each pointing at the other.</summary>
        private static string PortalPair()
        {
            const string portal = "PortalScroll_Portal";
            var player = Player.m_localPlayer;
            var before = Zdos(portal);
            var inventory = player.GetInventory();
            var scroll = inventory.AddItem("PortalScroll", 1, 1, 0, 0L, "", false);
            if (scroll == null) return "couldn't add a scroll to the inventory";
            if (!player.ConsumeItem(inventory, scroll)) return "the scroll couldn't be read";

            var added = Zdos(portal).Where(z => !before.Contains(z)).ToList();
            if (added.Count != 2) return $"{added.Count} new portal ZDOs, expected 2";
            foreach (var (end, other) in new[] { (added[0], added[1]), (added[1], added[0]) })
            {
                if (end.GetZDOID("mw_portal_pair") != other.m_uid) return $"{end.m_uid} isn't paired with {other.m_uid}";
                if (Vector3.Distance(end.GetVec3("mw_portal_target", Vector3.zero), other.GetPosition()) > 0.1f)
                    return $"{end.m_uid} targets {end.GetVec3("mw_portal_target", Vector3.zero)}, the other end is at {other.GetPosition()}";
            }
            return null;
        }

        private static List<ZDO> Zdos(string prefab)
        {
            var list = new List<ZDO>();
            var index = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefab, list, ref index)) { }
            return list;
        }
    }
}

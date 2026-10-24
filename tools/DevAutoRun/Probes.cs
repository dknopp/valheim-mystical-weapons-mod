using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;

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
            log.LogInfo($"DevAutoRun: RESULT pass {pass} fail {fail}");
        }
    }
}

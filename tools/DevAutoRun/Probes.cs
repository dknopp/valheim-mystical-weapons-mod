using System.Collections;
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
        public static IEnumerator Run(ManualLogSource log)
        {
            var items = DevAutoRunPlugin.MysticalItems()?.ToList();
            if (items == null)
            {
                log.LogError("DevAutoRun: PROBE Mystical Weapons plugin not loaded");
                log.LogInfo("DevAutoRun: RESULT pass 0 fail 1");
                yield break;
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
                var first = new PortalRead();
                yield return first.Run();
                Report(log, "opens a pair after the cast", first.Problem, ref pass, ref fail);

                if (first.Problem == null)
                {
                    var second = new PortalRead();
                    yield return second.Run();
                    var problem = second.Problem;
                    if (problem == null && first.Added.Any(id => ZDOMan.instance.GetZDO(id) != null))
                        problem = "the first pair is still there";
                    Report(log, "a second scroll closes the first pair", problem, ref pass, ref fail);
                }
            }
            log.LogInfo($"DevAutoRun: RESULT pass {pass} fail {fail}");
        }

        private static void Report(ManualLogSource log, string check, string problem, ref int pass, ref int fail)
        {
            if (problem == null) { pass++; log.LogInfo($"DevAutoRun: PROBE PortalScroll {check} OK"); }
            else { fail++; log.LogError($"DevAutoRun: PROBE PortalScroll {check} FAIL ({problem})"); }
        }

        /// <summary>
        /// Reads a scroll as a player would, waits for the cast (the scroll's status effect), and checks two new portal
        /// ends exist as ZDOs, each pointing at the other. Opening early or late is a problem.
        /// </summary>
        private class PortalRead
        {
            public string Problem;
            public List<ZDOID> Added = new List<ZDOID>();

            public IEnumerator Run()
            {
                const string portal = "PortalScroll_Portal";
                var player = Player.m_localPlayer;
                // IDs, not ZDOs: the game pools ZDO objects and reuses removed ones
                var before = new HashSet<ZDOID>(Zdos(portal).Select(z => z.m_uid));
                var inventory = player.GetInventory();
                var scroll = inventory.AddItem("PortalScroll", 1, 1, 0, 0L, "", false);
                if (scroll == null) { Problem = "couldn't add a scroll to the inventory"; yield break; }
                var cast = scroll.m_shared.m_consumeStatusEffect ? scroll.m_shared.m_consumeStatusEffect.m_ttl : 0f;
                if (!player.ConsumeItem(inventory, scroll)) { Problem = "the scroll couldn't be read"; yield break; }

                var start = Time.time;
                var instant = Zdos(portal).Any(z => !before.Contains(z.m_uid));
                while (!instant && Zdos(portal).Count(z => !before.Contains(z.m_uid)) < 2 && Time.time - start < cast + 2f)
                    yield return null;
                var took = Time.time - start;
                if (!instant && took < cast - 0.5f) { Problem = $"opened after {took:0.0}s, the cast is {cast:0.0}s"; yield break; }
                yield return null; // the effect is removed the frame it runs out; a second read needs it gone

                var added = Zdos(portal).Where(z => !before.Contains(z.m_uid)).ToList();
                Added = added.Select(z => z.m_uid).ToList();
                Problem = Check(added);
            }
        }

        private static string Check(List<ZDO> added)
        {
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

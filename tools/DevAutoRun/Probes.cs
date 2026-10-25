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

                foreach (var how in new[] { "hit", "step" })
                {
                    var cancel = new PortalRead { CancelBy = how };
                    yield return cancel.Run();
                    Report(log, $"a {how} cancels the cast and keeps the scroll", cancel.Problem, ref pass, ref fail);
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
        /// Reads a scroll as a player would and waits out the cast (the scroll's status effect). Normally checks two new
        /// portal ends appear as the portal starts forming (a second before the end), each pointing at the other, and the
        /// scroll is spent then. With <see cref="CancelBy"/>, hits or steps the player mid-cast and checks nothing opens and
        /// the scroll is kept.
        /// </summary>
        private class PortalRead
        {
            public string CancelBy;
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
                var scrolls = inventory.CountItems(scroll.m_shared.m_name);
                var effect = scroll.m_shared.m_consumeStatusEffect;
                var cast = effect ? effect.m_ttl : 0f;
                var opensAt = cast - Mathf.Min(1f, cast);
                player.ConsumeItem(inventory, scroll); // false by design: the scroll isn't spent on reading
                if (!effect || !player.GetSEMan().HaveStatusEffect(effect.NameHash())) { Problem = "reading didn't start the cast"; yield break; }
                if (inventory.CountItems(scroll.m_shared.m_name) != scrolls) { Problem = "the scroll was spent on reading"; yield break; }
                player.GetActionProgress(out var barText, out _);
                if (cast > 0f && string.IsNullOrEmpty(barText)) { Problem = "the cast isn't on the action bar"; yield break; }

                var start = Time.time;
                if (CancelBy != null)
                {
                    while (Time.time - start < opensAt / 2f) yield return null;
                    if (CancelBy == "hit")
                    {
                        var hit = new HitData { m_hitType = HitData.HitType.EnemyHit, m_point = player.GetCenterPoint() };
                        hit.m_damage.m_blunt = 1f;
                        player.ApplyDamage(hit, false, false);
                    }
                    else player.transform.position += player.transform.right * 1f;
                    while (Time.time - start < cast + 0.5f) yield return null;
                    if (player.GetSEMan().HaveStatusEffect(effect.NameHash())) Problem = "the cast is still running";
                    else if (Zdos(portal).Any(z => !before.Contains(z.m_uid))) Problem = "a portal opened anyway";
                    else if (inventory.CountItems(scroll.m_shared.m_name) != scrolls) Problem = "the scroll was spent";
                    inventory.RemoveItem(scroll.m_shared.m_name, 1);
                    yield break;
                }

                while (Zdos(portal).Count(z => !before.Contains(z.m_uid)) < 2 && Time.time - start < cast + 2f)
                    yield return null;
                var took = Time.time - start;
                if (took < opensAt - 0.5f || took > opensAt + 0.5f) { Problem = $"opened after {took:0.0}s, expected {opensAt:0.0}s"; yield break; }
                if (inventory.CountItems(scroll.m_shared.m_name) != scrolls - 1) { Problem = "the scroll wasn't spent when the portal opened"; yield break; }
                // A second read needs the cast over
                while (player.GetSEMan().HaveStatusEffect(effect.NameHash())) yield return null;

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
                if (end.GetLong("mw_portal_ready") <= end.GetLong("mw_portal_opened")) return $"{end.m_uid} has no forming time";
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

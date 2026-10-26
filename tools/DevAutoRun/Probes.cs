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
                var trader = ZNetScene.instance.GetPrefab("Haldor")?.GetComponent<Trader>();
                if (trader)
                    log.LogInfo("DevAutoRun: Haldor sells " + string.Join(", ", trader.m_items.Select(t =>
                        $"{(t.m_prefab ? t.m_prefab.name : "?")} x{t.m_stack} {t.m_price}" +
                        (string.IsNullOrEmpty(t.m_requiredGlobalKey) ? "" : $" [{t.m_requiredGlobalKey}]"))));
                Report(log, "Haldor sells it", !trader ? "no Haldor prefab" :
                    trader.m_items.Any(t => t.m_prefab && t.m_prefab.name == "PortalScroll") ? null : "not in his stock",
                    ref pass, ref fail);
                var portalPrefab = ZNetScene.instance.GetPrefab("PortalScroll_Portal");
                Report(log, "portal clears mist", portalPrefab && portalPrefab.GetComponentInChildren<Demister>(true) ? null : "no Demister",
                    ref pass, ref fail);

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

                foreach (var how in new[] { "hit", "step", "late hit" })
                {
                    var cancel = new PortalRead { CancelBy = how };
                    yield return cancel.Run();
                    Report(log, $"a {how} cancels the cast and keeps the scroll and the old pair", cancel.Problem, ref pass, ref fail);
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
        /// Reads a scroll as a player would and waits out the cast (the scroll's status effect). Normally checks only the
        /// forming end exists in the last second with the scroll unspent, then that two ends, each pointing at the other,
        /// exist when the cast completes and the scroll is spent then. With <see cref="CancelBy"/>, hits or steps the
        /// player mid-cast ("late hit": while the portal forms) and checks nothing opens and the scroll and the older
        /// portals are kept.
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
                var formsAt = cast - Mathf.Min(1f, cast);
                player.ConsumeItem(inventory, scroll); // false by design: the scroll isn't spent on reading
                if (!effect || !player.GetSEMan().HaveStatusEffect(effect.NameHash())) { Problem = "reading didn't start the cast"; yield break; }
                if (inventory.CountItems(scroll.m_shared.m_name) != scrolls) { Problem = "the scroll was spent on reading"; yield break; }
                player.GetActionProgress(out var barText, out _);
                if (cast > 0f && string.IsNullOrEmpty(barText)) { Problem = "the cast isn't on the action bar"; yield break; }

                var start = Time.time;
                if (CancelBy != null)
                {
                    while (Time.time - start < (CancelBy == "late hit" ? cast - 0.4f : formsAt / 2f)) yield return null;
                    if (CancelBy != "step")
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
                    else if (before.Any(id => ZDOMan.instance.GetZDO(id) == null)) Problem = "an older portal closed";
                    inventory.RemoveItem(scroll.m_shared.m_name, 1);
                    yield break;
                }

                if (cast > 0f)
                {
                    while (Time.time - start < cast - 0.4f) yield return null;
                    var forming = Zdos(portal).Count(z => !before.Contains(z.m_uid));
                    if (forming != 1) { Problem = $"{forming} new portal ZDOs while forming, expected 1"; yield break; }
                    if (inventory.CountItems(scroll.m_shared.m_name) != scrolls) { Problem = "the scroll was spent before the cast completed"; yield break; }
                }
                while (Zdos(portal).Count(z => !before.Contains(z.m_uid)) < 2 && Time.time - start < cast + 2f)
                    yield return null;
                var took = Time.time - start;
                if (took < cast - 0.3f || took > cast + 0.5f) { Problem = $"opened after {took:0.0}s, expected {cast:0.0}s"; yield break; }
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
                // The field end formed during the cast; the home end grows in once it completes
                if (end.GetBool("mw_portal_home") && end.GetLong("mw_portal_ready") <= end.GetLong("mw_portal_opened"))
                    return $"home end {end.m_uid} has no forming time";
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

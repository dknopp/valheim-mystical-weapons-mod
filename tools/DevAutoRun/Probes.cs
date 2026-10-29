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

                var drop = new ScrollDrop();
                yield return drop.Run();
                Report(log, "a dropped scroll lies flat on the floor", drop.Problem, ref pass, ref fail);

                foreach (var how in new[] { "hit", "step", "late hit" })
                {
                    var cancel = new PortalRead { CancelBy = how };
                    yield return cancel.Run();
                    Report(log, $"a {how} cancels the cast and keeps the scroll and the old pair", cancel.Problem, ref pass, ref fail);
                }

                var bed = new BedHome();
                yield return bed.Run();
                Report(log, "at a bed the home end stands over its foot, facing out", bed.Problem, ref pass, ref fail);
            }

            var homingTest = DevAutoRunPlugin.MysticalConfig<bool>("Debug", "HomingTest");
            if (homingTest == null) Report(log, "Debug.HomingTest config entry exists", "missing", ref pass, ref fail, "Homing");
            else
            {
                var was = homingTest.Value;
                homingTest.Value = true;
                var on = new HomingShot();
                yield return on.Run(log);
                Report(log, "an arrow aimed 2.5 m wide curves into the enemy", on.Hit ? null : $"missed by {on.Closest:0.0} m", ref pass, ref fail, "Homing");
                homingTest.Value = false;
                var off = new HomingShot();
                yield return off.Run(log);
                Report(log, "with the toggle off the same arrow flies straight past", off.Hit ? "it hit" : null, ref pass, ref fail, "Homing");
                homingTest.Value = was;
            }

            var heartPrefab = ObjectDB.instance.GetItemPrefab("SacredHeart");
            if (heartPrefab)
            {
                var heart = new HeartRead();
                yield return heart.Run(log, heartPrefab);
                foreach (var problem in heart.Results)
                    Report(log, problem.Key, problem.Value, ref pass, ref fail, "SacredHeart");
            }
            log.LogInfo($"DevAutoRun: RESULT pass {pass} fail {fail}");
        }

        private static void Report(ManualLogSource log, string check, string problem, ref int pass, ref int fail, string subject = "PortalScroll")
        {
            if (problem == null) { pass++; log.LogInfo($"DevAutoRun: PROBE {subject} {check} OK"); }
            else { fail++; log.LogError($"DevAutoRun: PROBE {subject} {check} FAIL ({problem})"); }
        }

        /// <summary>
        /// Puts a frozen Greydwarf 14 m behind the player and a little to the side, fires an arrow straight that way at its height and
        /// watches whether it gets hit.
        /// </summary>
        private class HomingShot
        {
            public bool Hit;
            public float Closest = float.MaxValue;
            /// <summary>How far to the side of the arrow's line the enemy stands, in metres (2.5 m is about 11 degrees off).</summary>
            public float Side = 2.5f;

            public IEnumerator Run(ManualLogSource log, ItemDrop.ItemData bow = null)
            {
                var player = Player.m_localPlayer;
                // Behind the player: the portal probes leave things in front that stop the arrow
                var fwd = -Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
                var right = Vector3.Cross(Vector3.up, fwd);
                var at = player.transform.position + fwd * 14f + right * Side;
                if (ZoneSystem.instance.FindFloor(at + Vector3.up * 5f, out var floor)) at.y = floor;
                var enemy = Object.Instantiate(ZNetScene.instance.GetPrefab("Greydwarf"), at, Quaternion.LookRotation(-fwd)).GetComponent<Character>();
                enemy.GetComponent<BaseAI>().enabled = false; // stands still
                yield return new WaitForSeconds(0.5f);
                var health = enemy.GetHealth();

                var target = enemy.GetCenterPoint();
                var origin = player.transform.position + fwd * 1.5f;
                origin.y = target.y;
                var arrow = Object.Instantiate(ZNetScene.instance.GetPrefab("bow_projectile"), origin, Quaternion.LookRotation(fwd));
                var hit = new HitData { m_skill = Skills.SkillType.Bows };
                hit.m_damage.m_pierce = 5f;
                arrow.GetComponent<Projectile>().Setup(player, fwd * 50f, -1f, hit, bow, null);

                var last = origin;
                for (var t = 0f; t < 2f && !Hit; t += Time.deltaTime)
                {
                    if (arrow)
                    {
                        last = arrow.transform.position;
                        Closest = Mathf.Min(Closest, Vector3.Distance(last, enemy.GetCenterPoint()));
                    }
                    Hit = enemy.GetHealth() < health;
                    yield return null;
                }
                var end = last - origin;
                log.LogInfo($"DevAutoRun: homing shot hit={Hit} closest={Closest:0.00} m, ended {Vector3.Dot(end, fwd):0.0} m ahead, " +
                    $"{Vector3.Dot(end, right):0.0} m right, {end.y:0.0} m up; enemy {Vector3.Dot(target - origin, fwd):0.0} ahead, " +
                    $"{Vector3.Dot(target - origin, right):0.0} right");
                if (arrow) ZNetScene.instance.Destroy(arrow);
                ZNetScene.instance.Destroy(enemy.gameObject);
            }
        }

        /// <summary>
        /// Wears the Sacred Heart and checks its icon and extra health, then starts the burst at half health and checks
        /// it heals and that a bow shot homes in without the Debug toggle.
        /// </summary>
        private class HeartRead
        {
            public readonly List<KeyValuePair<string, string>> Results = new List<KeyValuePair<string, string>>();

            public IEnumerator Run(ManualLogSource log, GameObject prefab)
            {
                var player = Player.m_localPlayer;
                var shared = prefab.GetComponent<ItemDrop>().m_itemData.m_shared;
                var icon = shared.m_icons.Length > 0 && shared.m_icons[0] ? shared.m_icons[0].name : "none";
                Results.Add(new KeyValuePair<string, string>("has the Morgen heart icon", icon == "MorgenHeart" ? null : icon));

                // The test character can still carry one from an earlier run; start without it
                foreach (var old in player.GetInventory().GetAllItems().Where(i => i.m_shared.m_name == shared.m_name).ToList())
                {
                    player.UnequipItem(old);
                    player.GetInventory().RemoveItem(old);
                }
                yield return new WaitForSeconds(1.5f);
                var before = player.GetMaxHealth();
                player.GetInventory().AddItem(prefab, 1);
                var item = player.GetInventory().GetAllItems().Last(i => i.m_shared.m_name == shared.m_name);
                var equipped = player.EquipItem(item);
                // The game refreshes max health from food about once a second
                for (var t = 0f; t < 3f && player.GetMaxHealth() - before < 1f; t += 0.25f)
                    yield return new WaitForSeconds(0.25f);
                var gained = player.GetMaxHealth() - before;
                log.LogInfo($"DevAutoRun: Sacred Heart equipped={equipped} ({item.m_equipped}), max health {before:0} -> {player.GetMaxHealth():0}");
                Results.Add(new KeyValuePair<string, string>("adds max health", gained > 1f ? null : $"gained {gained:0.0}"));

                player.SetHealth(player.GetMaxHealth() * 0.5f);
                var low = player.GetHealth();
                player.GetSEMan().AddStatusEffect(shared.m_fullAdrenalineSE.NameHash(), true);
                yield return null;
                log.LogInfo($"DevAutoRun: Sacred Heart burst health {low:0} -> {player.GetHealth():0}");
                Results.Add(new KeyValuePair<string, string>("the burst heals", player.GetHealth() > low + 1f ? null : "no heal"));

                var bow = ObjectDB.instance.GetItemPrefab("Bow").GetComponent<ItemDrop>().m_itemData.Clone();
                var shot = new HomingShot();
                yield return shot.Run(log, bow);
                Results.Add(new KeyValuePair<string, string>("during the burst an arrow aimed 2.5 m wide curves into the enemy",
                    shot.Hit ? null : $"missed by {shot.Closest:0.0} m"));
                var wide = new HomingShot { Side = 6f };
                yield return wide.Run(log, bow);
                Results.Add(new KeyValuePair<string, string>("during the burst an arrow aimed 6 m wide (outside the lock cone) flies straight",
                    wide.Hit ? "it hit" : null));

                player.GetSEMan().RemoveStatusEffect(shared.m_fullAdrenalineSE.NameHash());
                player.UnequipItem(item);
                player.GetInventory().RemoveItem(item);
            }
        }

        /// <summary>Drops a scroll beside the player and checks it settles lying down, resting on the floor rather than in it.</summary>
        private class ScrollDrop
        {
            public string Problem;

            public IEnumerator Run()
            {
                var player = Player.m_localPlayer;
                var at = player.transform.position + player.transform.right * 3f + Vector3.up * 1.5f;
                var item = Object.Instantiate(ObjectDB.instance.GetItemPrefab("PortalScroll"), at, Quaternion.Euler(0f, 30f, 0f));
                item.GetComponent<ItemDrop>().m_autoPickup = false; // else the player picks it up as it lands
                yield return new WaitForSeconds(4f);
                if (!item) { Problem = "the dropped scroll is gone"; yield break; }
                var bounds = item.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled)
                    .Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
                var flat = bounds.size.y < Mathf.Max(bounds.size.x, bounds.size.z) * 0.5f;
                var floorFound = ZoneSystem.instance.FindFloor(new Vector3(bounds.center.x, at.y, bounds.center.z), out var floor);
                if (!flat) Problem = $"not lying flat (bounds {bounds.size})";
                // A few cm is slope and terrain mesh; standing on end put half the scroll (~0.2 m) under the floor
                else if (floorFound && bounds.min.y < floor - 0.08f) Problem = $"{floor - bounds.min.y:0.000} m into the floor";
                ZNetScene.instance.Destroy(item);
            }
        }

        /// <summary>
        /// Builds a bed beside the player, makes it their spawn point (so the bed is noted as it loads), reads a scroll and
        /// checks the home end stands at the foot of the mattress, facing the way the bed does.
        /// </summary>
        private class BedHome
        {
            public string Problem;

            public IEnumerator Run()
            {
                var player = Player.m_localPlayer;
                var profile = Game.instance.GetPlayerProfile();
                var at = player.transform.position + player.transform.right * 6f;
                if (ZoneSystem.instance.FindFloor(at + Vector3.up * 2f, out var floor)) at.y = floor;
                var rot = Quaternion.Euler(0f, 70f, 0f);
                profile.SetCustomSpawnPoint(at + Vector3.up * 0.437f); // the wooden bed's spawn point
                var bed = Object.Instantiate(ZNetScene.instance.GetPrefab("bed"), at, rot);
                yield return null;
                if (!player.m_customData.ContainsKey("mw_portal_bed2")) Problem = "the bed wasn't noted as it loaded";
                else
                {
                    var read = new PortalRead();
                    yield return read.Run();
                    var home = read.Added.Select(id => ZDOMan.instance.GetZDO(id)).FirstOrDefault(z => z != null && z.GetBool("mw_portal_home"));
                    var expected = at + rot * Vector3.forward * 1.5f;
                    if (read.Problem != null) Problem = read.Problem;
                    else if (home == null) Problem = "no home end";
                    else if (Vector3.Distance(home.GetPosition(), expected) > 0.05f) Problem = $"home end at {home.GetPosition()}, expected {expected}";
                    else if (Quaternion.Angle(home.GetRotation(), rot) > 1f) Problem = $"home end faces {home.GetRotation().eulerAngles.y:0}, the bed {rot.eulerAngles.y:0}";
                }
                ZNetScene.instance.Destroy(bed);
            }
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
                    else
                    {
                        // Away from the portals: the test world's start is also where home ends open, and stepping into
                        // one closes the pair. The body too: moving only the transform is sometimes undone by physics
                        var away = player.transform.right;
                        var nearest = Zdos(portal).OrderBy(z => Vector3.Distance(z.GetPosition(), player.transform.position)).FirstOrDefault();
                        if (nearest != null && Vector3.Dot(nearest.GetPosition() - player.transform.position, away) > 0f) away = -away;
                        var step = away * 1f;
                        player.transform.position += step;
                        var body = player.GetComponent<Rigidbody>();
                        if (body) body.position = player.transform.position;
                    }
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

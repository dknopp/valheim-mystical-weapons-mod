using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MysticalWeapons
{
    /// <summary>
    /// Steers the local player's projectiles toward the nearest enemy ahead of the shot, turning at a limited rate so
    /// they curve in. The Sacred Heart's burst marks the shots that home; the Debug toggle makes every shot home.
    /// </summary>
    internal static class ProjectileHoming
    {
        private static ConfigEntry<bool> _test;

        // The Sacred Heart sets these from its config as it marks a shot.
        internal static float Range = 20f;
        internal static float ConeDegrees = 45f;
        internal static float TurnRateDegrees = 180f;

        /// <summary>The enemy each projectile is locked on to; entries go when the projectile does.</summary>
        private static readonly ConditionalWeakTable<Projectile, Character> Targets = new ConditionalWeakTable<Projectile, Character>();
        /// <summary>Projectiles that home without the Debug toggle.</summary>
        private static readonly ConditionalWeakTable<Projectile, object> Marked = new ConditionalWeakTable<Projectile, object>();

        internal static void Mark(Projectile p)
        {
            if (!Marked.TryGetValue(p, out _)) Marked.Add(p, null);
        }

        public static void BindConfig(ConfigFile cfg)
        {
            _test = cfg.Bind("Debug", "HomingTest", false,
                "For mod developers: every projectile you fire or throw homes in on the nearest enemy ahead, to test the steering.");
        }

        private static bool Active(Projectile p, Character owner) =>
            owner && owner == Player.m_localPlayer && (_test != null && _test.Value || Marked.TryGetValue(p, out _));

        /// <summary>
        /// Runs before the game moves the projectile, which it only does on the owner's client; the position syncs to
        /// others, so they see the curve too.
        /// </summary>
        [HarmonyPatch(typeof(Projectile), "FixedUpdate")]
        private static class SteerPatch
        {
            private static void Prefix(Projectile __instance, ref Vector3 ___m_vel, Character ___m_owner, ZNetView ___m_nview, bool ___m_didHit)
            {
                if (___m_didHit || !Active(__instance, ___m_owner) || !___m_nview || !___m_nview.IsValid() || !___m_nview.IsOwner()) return;
                var speed = ___m_vel.magnitude;
                if (speed < 0.1f) return;

                var from = __instance.transform.position;
                var heading = ___m_vel / speed;
                if (!Targets.TryGetValue(__instance, out var target) || !InCone(target, ___m_owner, from, heading))
                {
                    Targets.Remove(__instance);
                    target = FindTarget(___m_owner, from, heading);
                    if (!target) return;
                    Targets.Add(__instance, target);
                }

                var want = target.GetCenterPoint() - from;
                ___m_vel = Vector3.RotateTowards(___m_vel, want.normalized * speed,
                    TurnRateDegrees * Mathf.Deg2Rad * Time.fixedDeltaTime, 0f);
            }
        }

        /// <summary>Still worth chasing: alive, in range and ahead. Dropping a target the shot has passed stops it circling back.</summary>
        private static bool InCone(Character c, Character owner, Vector3 from, Vector3 heading)
        {
            if (!c || c == owner || c.IsDead() || !BaseAI.IsEnemy(owner, c)) return false;
            var to = c.GetCenterPoint() - from;
            return to.sqrMagnitude <= Range * Range && Vector3.Angle(heading, to) <= ConeDegrees;
        }

        private static Character FindTarget(Character owner, Vector3 from, Vector3 heading)
        {
            Character best = null;
            var bestDist = float.MaxValue;
            foreach (var c in Character.GetAllCharacters())
            {
                if (!InCone(c, owner, from, heading)) continue;
                var d = (c.GetCenterPoint() - from).sqrMagnitude;
                if (d < bestDist) { best = c; bestDist = d; }
            }
            return best;
        }
    }
}

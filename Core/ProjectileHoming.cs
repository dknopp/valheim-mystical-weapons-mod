using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MysticalWeapons
{
    /// <summary>
    /// Steers the local player's projectiles toward the enemy they were aimed at, turning at a limited rate so they curve
    /// in. The target is picked once, as the shot leaves, and never changes: the shot goes for what you aimed at or flies
    /// straight. The Sacred Heart's burst locks its shots; the Debug toggle locks every shot.
    /// </summary>
    internal static class ProjectileHoming
    {
        private static ConfigEntry<bool> _test;

        // The Sacred Heart sets these from its config as it locks a shot.
        internal static float Range = 20f;
        internal static float ConeDegrees = 15f;
        internal static float TurnRateDegrees = 180f;

        /// <summary>A shot's target; null once it has none (nothing aimed at, dead, or passed).</summary>
        private class Lock { public Character Target; }

        /// <summary>Locked shots; entries go when the projectile does.</summary>
        private static readonly ConditionalWeakTable<Projectile, Lock> Locks = new ConditionalWeakTable<Projectile, Lock>();

        public static void BindConfig(ConfigFile cfg)
        {
            _test = cfg.Bind("Debug", "HomingTest", false,
                "For mod developers: every projectile you fire or throw homes in on the enemy it was aimed at, to test the steering.");
        }

        /// <summary>Picks the enemy closest to the shot's line within the cone and range; once per shot.</summary>
        internal static void LockOn(Projectile p, Character owner, Vector3 from, Vector3 aim)
        {
            if (Locks.TryGetValue(p, out _)) return;
            Locks.Add(p, new Lock { Target = FindTarget(owner, from, aim) });
        }

        /// <summary>
        /// Runs before the game moves the projectile, which it only does on the owner's client; the position syncs to
        /// others, so they see the curve too.
        /// </summary>
        [HarmonyPatch(typeof(Projectile), "FixedUpdate")]
        private static class SteerPatch
        {
            private static void Prefix(Projectile __instance, ref Vector3 ___m_vel, Character ___m_owner, ZNetView ___m_nview, bool ___m_didHit)
            {
                if (___m_didHit || !___m_owner || ___m_owner != Player.m_localPlayer) return;
                if (!___m_nview || !___m_nview.IsValid() || !___m_nview.IsOwner()) return;
                var speed = ___m_vel.magnitude;
                if (speed < 0.1f) return;

                var from = __instance.transform.position;
                var heading = ___m_vel / speed;
                if (!Locks.TryGetValue(__instance, out var lk))
                {
                    if (_test == null || !_test.Value) return;
                    LockOn(__instance, ___m_owner, from, heading);
                    Locks.TryGetValue(__instance, out lk);
                }
                var target = lk.Target;
                if (!target) return;

                var want = target.GetCenterPoint() - from;
                // Dropping a target the shot has passed stops it circling back
                if (target.IsDead() || Vector3.Angle(heading, want) > 90f) { lk.Target = null; return; }
                ___m_vel = Vector3.RotateTowards(___m_vel, want.normalized * speed,
                    TurnRateDegrees * Mathf.Deg2Rad * Time.fixedDeltaTime, 0f);
            }
        }

        private static Character FindTarget(Character owner, Vector3 from, Vector3 aim)
        {
            Character best = null;
            var bestAngle = ConeDegrees;
            foreach (var c in Character.GetAllCharacters())
            {
                if (!c || c == owner || c.IsDead() || !BaseAI.IsEnemy(owner, c)) continue;
                var to = c.GetCenterPoint() - from;
                if (to.sqrMagnitude > Range * Range) continue;
                var angle = Vector3.Angle(aim, to);
                if (angle <= bestAngle) { best = c; bestAngle = angle; }
            }
            return best;
        }
    }
}

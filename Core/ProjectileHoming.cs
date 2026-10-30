using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MysticalWeapons
{
    /// <summary>
    /// Steers the local player's projectiles toward an enemy, turning at a limited rate so they curve in. Until it has a
    /// target, a shot keeps looking for the enemy closest to its line; once it has one it never switches, so it doesn't
    /// curve off to another enemy mid-flight. The Sacred Heart's burst marks its shots; the Debug toggle marks every shot.
    /// </summary>
    internal static class ProjectileHoming
    {
        private static ConfigEntry<bool> _test;

        // The Sacred Heart sets these from its config as it marks a shot.
        internal static float Range = 20f;
        internal static float ConeDegrees = 45f;
        internal static float TurnRateDegrees = 180f;

        /// <summary>A shot's target, null while it is still looking; Done once its target died or was passed.</summary>
        private class Lock { public Character Target; public bool Done; }

        /// <summary>Homing shots; entries go when the projectile does.</summary>
        private static readonly ConditionalWeakTable<Projectile, Lock> Locks = new ConditionalWeakTable<Projectile, Lock>();

        public static void BindConfig(ConfigFile cfg)
        {
            _test = cfg.Bind("Debug", "HomingTest", false,
                "For mod developers: every projectile you fire or throw homes in on an enemy ahead, to test the steering.");
        }

        /// <summary>Makes a shot home; it picks its target in flight.</summary>
        internal static void Mark(Projectile p)
        {
            if (!Locks.TryGetValue(p, out _)) Locks.Add(p, new Lock());
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
                    Locks.Add(__instance, lk = new Lock());
                }
                if (lk.Done) return;
                // Still looking every step, so an enemy out of range at release is picked up as the shot closes in
                if (!lk.Target && !(lk.Target = FindTarget(___m_owner, from, heading))) return;
                var target = lk.Target;

                var want = target.GetCenterPoint() - from;
                // Dropping a target the shot has passed stops it circling back; it doesn't take another
                if (target.IsDead() || Vector3.Angle(heading, want) > 90f) { lk.Target = null; lk.Done = true; return; }
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

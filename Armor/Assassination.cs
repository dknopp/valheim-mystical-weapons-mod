using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using MysticalWeapons.Weapons;
using UnityEngine;

namespace MysticalWeapons.Armor
{
    /// <summary>
    /// The Brotherhood's 4-piece bonus with the hidden blades in hand: a hit on a creature that isn't alerted (the game's
    /// own sneak-attack test, synced to every player) takes a share of its max health, by star, size and boss. Decided
    /// on the attacker's client, where <see cref="Character.Damage"/> runs before the hit is sent to the creature's
    /// owner, and added as plain damage that armour and resistances don't reduce, so the owner applies it once through
    /// the normal damage path. Design in docs/BROTHERHOOD.md ("Assassination").
    /// </summary>
    internal static class Assassination
    {
        /// <summary>The 4-piece set bonus the attacker must be wearing; null when Assassination is off.</summary>
        internal static StatusEffect SetBonus;
        internal static ConfigEntry<float> GiantHealth;
        internal static ConfigEntry<float> HardShare;
        internal static ConfigEntry<float> ChampionHealth;
        internal static ConfigEntry<float> ChampionShare;
        internal static ConfigEntry<float> ChampionShareFull;
        internal static ConfigEntry<float> BossShare;
        internal static ConfigEntry<float> BossShareFull;
        internal static ConfigEntry<float> FullSkill;

        /// <summary>Vanilla lets a creature take a sneak attack once every 5 minutes; bosses once per summoning.</summary>
        private const float Cooldown = 300f;
        private static readonly Dictionary<ZDOID, float> LastAssassinated = new Dictionary<ZDOID, float>();

        /// <summary>Share of max health a hit takes: all of a normal creature, less of 2-star, giant, champion and boss ones.</summary>
        private static float Share(Character target, Player player)
        {
            var skills = player.GetSkills();
            var skill = (skills.GetSkillLevel(Skills.SkillType.Unarmed) + skills.GetSkillLevel(Skills.SkillType.Sneak)) / 2f;
            var t = Mathf.Clamp01(skill / Mathf.Max(1f, FullSkill.Value));
            var share = 1f;
            // Level 3 is two stars; m_health is the base health, before stars
            if (target.GetLevel() >= 3 || target.m_health >= GiantHealth.Value) share = Mathf.Lerp(HardShare.Value, 1f, t);
            if (target.m_health >= ChampionHealth.Value) share = Mathf.Min(share, Mathf.Lerp(ChampionShare.Value, ChampionShareFull.Value, t));
            if (target.IsBoss()) share = Mathf.Min(share, Mathf.Lerp(BossShare.Value, BossShareFull.Value, t));
            return share;
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        private static class DamagePatch
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                if (!SetBonus) return;
                var player = Player.m_localPlayer;
                if (!player || hit.GetAttacker() != player || __instance.IsPlayer() || __instance.IsTamed() || __instance.IsDead()) return;
                var weapon = player.GetCurrentWeapon();
                if (weapon?.m_dropPrefab == null || weapon.m_dropPrefab.name != HiddenBlades.Id) return;
                if (!player.GetSEMan().HaveStatusEffect(SetBonus.NameHash())) return;
                var ai = __instance.GetBaseAI();
                if (!ai || ai.IsAlerted()) return;
                var id = __instance.GetZDOID();
                if (LastAssassinated.TryGetValue(id, out var last) && (__instance.IsBoss() || Time.time - last < Cooldown)) return;

                // Whichever is higher: the share, or the blades' own sneak attack. Either way the owner mustn't multiply
                // it again, and its sneak-attack timer is its own, so the hit carries the full amount itself.
                var share = Share(__instance, player);
                var needed = share * __instance.GetMaxHealth();
                var total = hit.GetTotalDamage();
                var extra = Mathf.Max(needed, total * hit.m_backstabBonus - total);
                hit.m_backstabBonus = 1f;
                hit.m_damage.m_damage += extra;
                // Only bosses need an entry past the cooldown, so drop the rest as they expire
                if (LastAssassinated.Count > 64)
                {
                    var expired = new List<ZDOID>();
                    foreach (var pair in LastAssassinated)
                        if (Time.time - pair.Value >= Cooldown && !ZNetScene.instance?.FindInstance(pair.Key)) expired.Add(pair.Key);
                    foreach (var key in expired) LastAssassinated.Remove(key);
                }
                LastAssassinated[id] = Time.time;
                __instance.m_backstabHitEffects.Create(hit.m_point, Quaternion.identity, __instance.transform);
                Plugin.Log.LogInfo($"{HiddenBlades.Id}: assassinated {__instance.m_name} (level {__instance.GetLevel()}, base health " +
                                   $"{__instance.m_health:0}{(__instance.IsBoss() ? ", boss" : "")}) for {extra:0} extra, {share:P0} share");
            }
        }
    }
}

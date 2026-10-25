using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MysticalWeapons.Items
{
    /// <summary>
    /// Scroll of Town Portal (Diablo II). Reading it opens a pair of portals: one in front of you, one at your bed
    /// (the world start if you have none). Anyone can step through either end. Mechanics decisions are in
    /// references/town-portal.md.
    /// </summary>
    internal class PortalScroll : MysticalWeapon
    {
        public override string PrefabName => "PortalScroll";
        public override string[] SourcePrefabs => new[] { "MeadHealthMinor", "MeadStaminaMinor" };
        public override string DisplayName => "Scroll of Town Portal";
        public override string Description =>
            "Read it to open a gateway home, and another back to where you stood. Ore and metal still refuse the road.";

        protected override bool HasDamage => false;
        protected override bool AutoAlign => false;
        // Placeholder until chunk (d): Haldor sells it, then the Galdr table
        protected override string DefaultRecipe => "LeatherScraps:2,GreydwarfEye:2";
        protected override string DefaultStation => "piece_workbench";
        protected override int DefaultStationLevel => 1;

        private const int Parchment = 0, Rod = 1, Ribbon = 2;

        public override MaterialSpec[] Materials => new[]
        {
            new MaterialSpec(new Color(0.86f, 0.78f, 0.6f), 0.2f),  // parchment
            new MaterialSpec(new Color(0.3f, 0.19f, 0.1f), 0.4f),   // wooden rods
            new MaterialSpec(new Color(0.2f, 0.3f, 0.85f), 0.5f),   // ribbon
        };

        public override void BuildModel(MeshBuilder b)
        {
            // A rolled scroll along Y: parchment, rod ends poking out, a ribbon round the middle
            b.Prism(Parchment, -0.1f, 0.1f, 0.025f, 12);
            b.Prism(Rod, -0.125f, -0.1f, 0.012f);
            b.Prism(Rod, 0.1f, 0.125f, 0.012f);
            b.Prism(Ribbon, -0.012f, 0.012f, 0.027f, 12);
        }

        internal enum CastPose { Point, Kneel }

        /// <summary>The portal grows in over the last this-many seconds of the cast.</summary>
        private const float FormSeconds = 1f;
        /// <summary>Walking further than this from where you started reading cancels the cast.</summary>
        private const float MoveTolerance = 0.3f;

        private ConfigEntry<int> _stackSize;
        private ConfigEntry<float> _castSeconds;
        private ConfigEntry<CastPose> _castAnimation;
        internal static ConfigEntry<float> TimeoutMinutes;

        public override void BindConfig(ConfigFile cfg)
        {
            base.BindConfig(cfg);
            _stackSize = cfg.Bind(PrefabName, "StackSize", 20, "How many scrolls fit in one inventory slot.");
            _castSeconds = cfg.Bind(PrefabName, "CastSeconds", 3f,
                "Seconds between reading the scroll and the portal opening; it forms over the last second. Moving or getting hit before then cancels it and keeps the scroll. 0 = instant.");
            _castAnimation = cfg.Bind(PrefabName, "CastAnimation", CastPose.Point, "What your character does while reading the scroll.");
            TimeoutMinutes = cfg.Bind(PrefabName, "TimeoutMinutes", 60f,
                "Minutes before an unused pair of portals closes by itself. 0 = they stay until you come back through the home end or read another scroll.");
        }

        private static OpenEffect _effect;

        protected override void Customize(ItemDrop.ItemData.SharedData shared)
        {
            shared.m_maxStackSize = _stackSize.Value;
            shared.m_weight = 0.1f;
            shared.m_teleportable = true;

            _effect = ScriptableObject.CreateInstance<OpenEffect>();
            _effect.name = "SE_PortalScroll";
            _effect.m_name = DisplayName;
            _effect.m_tooltip = "The gateway is opening.";
            _effect.m_icon = Icon;
            // The effect's countdown is the cast. Instant still lasts a second, so one press can't read two scrolls
            // (a ttl of 0 would never end).
            var cast = Mathf.Max(0f, _castSeconds.Value);
            _effect.m_ttl = cast > 0f ? cast : 1f;
            _effect.FormSeconds = Mathf.Min(FormSeconds, cast);
            _effect.OpenAt = cast - _effect.FormSeconds;
            _effect.Pose = _castAnimation.Value;
            // Makes the game treat the scroll as usable; Read below takes over from there
            shared.m_consumeStatusEffect = _effect;
        }

        /// <summary>Starts reading, unless a scroll is already being read. The scroll is spent when the portal starts forming.</summary>
        private static void StartCast(Player player, Inventory inventory, ItemDrop.ItemData scroll)
        {
            var seman = player.GetSEMan();
            if (!_effect || seman.HaveStatusEffect(_effect.NameHash())) return;
            if (seman.AddStatusEffect(_effect) is OpenEffect cast)
            {
                cast.Inventory = inventory;
                cast.Scroll = scroll;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.ConsumeItem))]
        private static class Read
        {
            // Skips the original for the scroll: it would spend the scroll now and play the drinking animation and
            // sound (Humanoid.UseItem plays those when this returns true). Ours is spent only if the cast completes.
            private static bool Prefix(Player __instance, Inventory inventory, ItemDrop.ItemData item, ref bool __result)
            {
                if (item?.m_dropPrefab == null || item.m_dropPrefab.name != "PortalScroll") return true;
                StartCast(__instance, inventory, item);
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
        private static class Interrupt
        {
            private static void Postfix(Character __instance, HitData hit)
            {
                if (__instance != Player.m_localPlayer || !_effect || hit.GetTotalDamage() <= 0.1f) return;
                // Burning, poison and the like tick while you stand there; they aren't a hit
                switch (hit.m_hitType)
                {
                    case HitData.HitType.Burning: case HitData.HitType.Freezing: case HitData.HitType.Poisoned:
                    case HitData.HitType.Smoke: case HitData.HitType.Drowning: case HitData.HitType.Water:
                        return;
                }
                (__instance.GetSEMan().GetStatusEffect(_effect.NameHash()) as OpenEffect)?.Cancel($"hit, {hit.m_hitType}");
            }
        }

        public override void RegisterExtras(ObjectDB db)
        {
            if (_effect && !db.m_StatusEffects.Contains(_effect)) db.m_StatusEffects.Add(_effect);
        }

        public override void RegisterNetExtras(ZNetScene scene)
        {
            var portal = TownPortal.GetOrCreatePrefab(name => scene.m_prefabs.Find(go => go && go.name == name));
            if (portal && !RegisterNetPrefab(scene, portal))
                Plugin.Log.LogWarning($"{PrefabName}: another mod already registers '{portal.name}'; scrolls won't open portals");
        }

        /// <summary>
        /// The cast. Reading the scroll applies this (only on the reader's machine; the emote and the portal are what
        /// others see). The portal's spot is fixed when reading starts; the pair opens <see cref="FormSeconds"/> before the
        /// end and grows in. Moving or a hit before then cancels it and keeps the scroll; so does death or logging out.
        /// </summary>
        internal class OpenEffect : StatusEffect
        {
            public float OpenAt;
            public float FormSeconds;
            public CastPose Pose;

            // Per cast, on the copy SEMan makes
            internal Inventory Inventory;
            internal ItemDrop.ItemData Scroll;
            private Vector3 _start, _facing;
            private bool _opened, _cancelled;

            private static readonly Action<Character> StopEmote =
                AccessTools.MethodDelegate<Action<Character>>(AccessTools.Method(typeof(Character), "StopEmote"));

            public override void Setup(Character character)
            {
                base.Setup(character);
                _start = character.transform.position;
                _facing = Vector3.ProjectOnPlane(character.transform.forward, Vector3.up);
                if (character is Player player) player.StartEmote(Pose.ToString().ToLowerInvariant());
            }

            public override void UpdateStatusEffect(float dt)
            {
                base.UpdateStatusEffect(dt);
                if (_opened || _cancelled) return;
                if (Moved()) Cancel("moved");
                else if (m_time >= OpenAt) Open();
            }

            public override bool IsDone() => _cancelled || base.IsDone();

            private bool Moved()
            {
                return m_character.GetMoveDir().sqrMagnitude > 0.01f || m_character.InDodge() ||
                       Vector3.ProjectOnPlane(m_character.transform.position - _start, Vector3.up).magnitude > MoveTolerance;
            }

            /// <summary>Ends the cast without a portal, before it starts forming. SEMan removes the effect next update.</summary>
            internal void Cancel(string why)
            {
                if (_opened || _cancelled) return;
                _cancelled = true;
                Plugin.Log.LogInfo($"PortalScroll: cast cancelled ({why}) after {m_time:0.0}s");
                StopEmote(m_character);
            }

            private void Open()
            {
                _opened = true;
                if (!(m_character is Player player) || player != Player.m_localPlayer) return;
                // The read scroll, or another one if that stack was moved or split
                var scroll = Inventory != null && Inventory.ContainsItem(Scroll) ? Scroll : Inventory?.GetItem(Scroll?.m_shared.m_name);
                if (scroll == null)
                {
                    Plugin.Log.LogInfo("PortalScroll: no scroll left to spend; the cast opens nothing");
                    _cancelled = true;
                    return;
                }
                Inventory.RemoveOneItem(scroll);
                TownPortal.OpenPair(player, _start, _facing, FormSeconds);
            }
        }
    }
}

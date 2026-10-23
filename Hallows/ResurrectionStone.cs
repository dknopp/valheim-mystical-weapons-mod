using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MysticalWeapons.Hallows
{
    /// <summary>
    /// The Resurrection Stone, worn in the utility slot. If you die with it equipped,
    /// your skills are not lowered. It also stays with you when you die.
    /// </summary>
    internal class ResurrectionStone : MysticalWeapon
    {
        public override string PrefabName => "ResurrectionStone";
        public override string[] SourcePrefabs => new[] { "Wishbone", "BeltStrength" };
        public override string DisplayName => "The Resurrection Stone";
        public override string Description =>
            "A small black stone, split once and set back together, marked with the sign of the " +
            "Hallows. Turn it thrice in your hand: what death takes from you, it returns.";

        protected override bool HasDamage => false;
        protected override bool AutoAlign => false;
        protected override float ModelScale => 2f; // a real-size pebble would vanish on the ground
        protected override string DefaultRecipe => "Obsidian:10,Silver:4,SurtlingCore:3";
        protected override int DefaultStationLevel => 3;
        public override float IconAngle => 0f;

        private const int Stone = 0, Sign = 1;

        public override MaterialSpec[] Materials => new[]
        {
            new MaterialSpec(new Color(0.07f, 0.07f, 0.08f), 0.85f),                                 // black stone
            new MaterialSpec(new Color(0.82f, 0.82f, 0.85f), 0.8f, new Color(0.06f, 0.06f, 0.08f)), // the sign
        };

        private static ConfigEntry<bool> _staysOnDeath;
        /// <summary>The stone stays in your inventory when you die (see <see cref="DeathlyHallows"/>' tombstone patch).</summary>
        internal static bool StaysOnDeath => _staysOnDeath != null && _staysOnDeath.Value;

        public override void BindConfig(ConfigFile cfg)
        {
            base.BindConfig(cfg);
            _staysOnDeath = cfg.Bind(PrefabName, "StaysOnDeath", true,
                "When you die, the stone stays in your inventory instead of going to your tombstone with everything else.");
        }

        private SE_Stats _stoneEffect;

        protected override void Customize(ItemDrop.ItemData.SharedData shared)
        {
            _stoneEffect = DeathlyHallows.MakeEffect("SE_ResurrectionStone", "The Resurrection Stone",
                "If you die while wearing the stone, your skills are not lost.", Icon);
            shared.m_equipStatusEffect = _stoneEffect;
            shared.m_weight = 0.5f;
            DeathlyHallows.JoinSet(shared, Icon);
        }

        public override void RegisterExtras(ObjectDB db) => DeathlyHallows.Register(db, _stoneEffect);

        public override void BuildModel(MeshBuilder b)
        {
            // A rounded pebble: a thin wide layer under a thicker narrower one gives a bevelled look
            b.Slab(Stone, Pebble(0.035f), 0.008f);
            b.Slab(Stone, Pebble(0.027f), 0.014f);

            // The sign of the Hallows on both faces: triangle (cloak), circle (stone), line (wand)
            const float h = 0.0152f, w = 0.0012f;
            var top = new Vector2(0f, 0.022f);
            var left = new Vector2(-0.019f, -0.011f);
            var right = new Vector2(0.019f, -0.011f);
            b.Ribbon(Sign, new[] { top, right, left, top }, w, h);
            var circle = new Vector2[17];
            for (var i = 0; i <= 16; i++)
            {
                var a = i * Mathf.PI * 2f / 16f;
                circle[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.011f;
            }
            b.Ribbon(Sign, circle, w, h);
            b.Ribbon(Sign, new[] { top, new Vector2(0f, -0.011f) }, w, h);
        }

        private static Vector2[] Pebble(float r)
        {
            // Slightly irregular, still convex
            var stretch = new[] { 1f, 0.95f, 0.9f, 0.93f, 1f, 0.97f, 0.88f, 0.92f, 0.98f, 0.94f };
            var pts = new Vector2[stretch.Length];
            for (var i = 0; i < pts.Length; i++)
            {
                var a = i * Mathf.PI * 2f / pts.Length;
                pts[i] = new Vector2(Mathf.Cos(a) * 1.1f, Mathf.Sin(a)) * r * stretch[i];
            }
            return pts;
        }

        // ------------------------------------------------------------ death

        private static bool _spareSkills;

        /// <summary>Items move to the tombstone before skills are lowered, so check the stone first.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
        private static class PlayerDeathPatch
        {
            private static void Prefix(Player __instance)
            {
                _spareSkills = __instance == Player.m_localPlayer &&
                               __instance.GetInventory().GetEquippedItems().Exists(i => DeathlyHallows.IsItem(i, "ResurrectionStone"));
            }

            private static void Postfix(Player __instance)
            {
                if (_spareSkills)
                    __instance.Message(MessageHud.MessageType.Center, "The Resurrection Stone turns... your skills are spared.");
                _spareSkills = false;
            }
        }

        [HarmonyPatch(typeof(Skills), nameof(Skills.OnDeath))]
        private static class SkillsDeathPatch
        {
            private static bool Prefix() => !_spareSkills;
        }
    }
}

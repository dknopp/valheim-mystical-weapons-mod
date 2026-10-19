using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MysticalWeapons.Weapons
{
    /// <summary>
    /// Andúril, Flame of the West (The Lord of the Rings): Narsil reforged for Aragorn.
    /// Spirit damage (which only hurts undead in Valheim) plus a damage multiplier against
    /// every creature of the Undead faction: skeletons, ghosts, draugr, wraiths...
    /// </summary>
    internal class Anduril : MysticalWeapon
    {
        public override string PrefabName => "Anduril";
        public override string[] SourcePrefabs => new[] { "SwordSilver", "SwordIron" };
        public override string DisplayName => "Andúril";
        public override string Description =>
            "The Flame of the West, forged anew from the shards of Narsil. Elven runes run down its " +
            "blade, and the dead remember the oath they broke when they see it drawn.";

        protected override string DefaultDamage => "slash:90,spirit:40";
        protected override string DefaultDamagePerLevel => "slash:6,spirit:5";
        protected override string DefaultRecipe => "SwordIron:1,Silver:15,Crystal:4"; // the shards of Narsil
        protected override int DefaultStationLevel => 3;

        internal static ConfigEntry<float> UndeadMultiplier;

        public override void BindConfig(ConfigFile cfg)
        {
            base.BindConfig(cfg);
            UndeadMultiplier = cfg.Bind(PrefabName, "UndeadDamageMultiplier", 1.5f,
                "All damage dealt to undead (skeletons, ghosts, draugr, wraiths...) is multiplied by this.");
        }

        private const int Steel = 0, Hilt = 1, Grip = 2, Runes = 3;

        public override MaterialSpec[] Materials => new[]
        {
            new MaterialSpec(new Color(0.86f, 0.88f, 0.9f), 0.9f),                                  // blade
            new MaterialSpec(new Color(0.62f, 0.64f, 0.68f), 0.75f),                                // guard, pommel, wire
            new MaterialSpec(new Color(0.1f, 0.07f, 0.06f), 0.25f),                                 // leather grip
            new MaterialSpec(new Color(0.85f, 0.7f, 0.35f), 0.8f, new Color(0.12f, 0.09f, 0.03f)),  // engraved runes
        };

        protected override void Customize(ItemDrop.ItemData.SharedData shared)
        {
            shared.m_maxDurability = Mathf.Max(shared.m_maxDurability, 300f);
            shared.m_backstabBonus = 3f;
        }

        public override void BuildModel(MeshBuilder b)
        {
            // Hand at the origin; hand-and-a-half grip below, long straight blade above
            b.Blade(Steel,
                new MeshBuilder.Station(0.10f, 0.032f, 0.012f),
                new MeshBuilder.Station(0.20f, 0.030f, 0.011f),
                new MeshBuilder.Station(0.85f, 0.024f, 0.009f),
                new MeshBuilder.Station(0.98f, 0.012f, 0.006f),
                new MeshBuilder.Station(1.04f, 0f, 0.002f));

            // A column of elvish runes near the base of the blade
            var i = 0;
            for (var y = 0.15f; y < 0.55f; y += 0.035f, i++)
            {
                switch (i % 3)
                {
                    case 0: b.Slab(Runes, Diamond(0f, y, 0.006f, 0.012f), 0.0125f); break;
                    case 1: b.Box(Runes, Rect.MinMaxRect(-0.009f, y - 0.002f, 0.009f, y + 0.002f), 0.0125f); break;
                    default:
                        b.Box(Runes, Rect.MinMaxRect(-0.002f, y - 0.011f, 0.002f, y + 0.011f), 0.0125f);
                        b.Slab(Runes, Diamond(0.007f, y + 0.006f, 0.003f, 0.004f), 0.0125f);
                        break;
                }
            }

            // Crossguard: a central block with arms that sweep down towards the hand and end in flared tips
            b.Slab(Hilt, Diamond(0f, 0.085f, 0.03f, 0.035f), 0.02f);
            foreach (var s in new[] { 1f, -1f })
            {
                b.Ribbon(Hilt, new[]
                {
                    new Vector2(0f, 0.085f), new Vector2(0.05f * s, 0.08f), new Vector2(0.09f * s, 0.068f),
                    new Vector2(0.12f * s, 0.05f), new Vector2(0.135f * s, 0.03f)
                }, 0.009f, 0.014f);
                b.Slab(Hilt, Diamond(0.138f * s, 0.024f, 0.014f, 0.016f), 0.016f);
            }

            // Leather grip wrapped with silver wire, and a rounded pommel
            b.Prism(Grip, -0.24f, 0.07f, 0.017f);
            for (var y = -0.2f; y < 0.06f; y += 0.06f) b.Prism(Hilt, y, y + 0.008f, 0.0185f);
            b.Prism(Hilt, -0.255f, -0.24f, 0.022f);
            b.Slab(Hilt, Octagon(0f, -0.285f, 0.034f), 0.02f);
            b.Slab(Hilt, Diamond(0f, -0.325f, 0.012f, 0.014f), 0.014f);
        }

        private static Vector2[] Diamond(float x, float y, float halfWidth, float halfHeight) => new[]
        {
            new Vector2(x, y - halfHeight), new Vector2(x + halfWidth, y),
            new Vector2(x, y + halfHeight), new Vector2(x - halfWidth, y)
        };

        private static Vector2[] Octagon(float x, float y, float r)
        {
            var pts = new Vector2[8];
            for (var k = 0; k < 8; k++)
            {
                var a = (k + 0.5f) * Mathf.PI / 4f;
                pts[k] = new Vector2(x + Mathf.Cos(a) * r, y + Mathf.Sin(a) * r);
            }
            return pts;
        }

        /// <summary>Boosts hits from Andúril against undead. Runs on the attacker's client.</summary>
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        private static class UndeadBanePatch
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                if (UndeadMultiplier == null || __instance.m_faction != Character.Faction.Undead) return;
                if (!(hit.GetAttacker() is Humanoid attacker)) return;
                var weapon = attacker.GetCurrentWeapon();
                if (weapon?.m_dropPrefab == null || weapon.m_dropPrefab.name != "Anduril") return;
                hit.m_damage.Modify(UndeadMultiplier.Value);
            }
        }
    }
}

using UnityEngine;

namespace MysticalWeapons.Weapons
{
    /// <summary>
    /// A Shardblade (The Stormlight Archive): a huge, impossibly light sword that slices
    /// through stone and wood alike. Never dulls, never breaks.
    /// </summary>
    internal class Shardblade : MysticalWeapon
    {
        public override string PrefabName => "Shardblade";
        public override string[] SourcePrefabs => new[] { "THSwordKrom", "THSwordSlayer", "SwordBlackmetal" };
        public override string DisplayName => "Shardblade";
        public override string Description =>
            "Six feet of silvery metal etched with glyphs, yet light as a branch. It parts trees " +
            "and stone like water, and those it strikes feel their very soul burn.";

        // Mistlands, beside Krom (150 slash, black forge 1), which it is reforged from
        protected override string DefaultDamage => "slash:150,spirit:30,chop:100,pickaxe:80";
        protected override string DefaultDamagePerLevel => "slash:6,spirit:4";
        protected override string DefaultRecipe => "THSwordKrom:1,Silver:20,Crystal:15,Eitr:10";
        protected override string DefaultStation => "blackforge";
        protected override int DefaultStationLevel => 2;

        private const int Steel = 0, Guard = 1, Grip = 2, Glyph = 3;

        public override MaterialSpec[] Materials => new[]
        {
            new MaterialSpec(new Color(0.8f, 0.82f, 0.86f), 0.9f),                                // blade
            new MaterialSpec(new Color(0.34f, 0.36f, 0.42f), 0.7f),                               // guard, pommel
            new MaterialSpec(new Color(0.12f, 0.1f, 0.1f), 0.2f),                                 // grip wrap
            new MaterialSpec(new Color(0.55f, 0.8f, 1f), 0.9f, new Color(0.3f, 0.6f, 1f)),        // glowing glyphs
        };

        protected override void Customize(ItemDrop.ItemData.SharedData shared)
        {
            shared.m_useDurability = false; // Shardblades never dull or break
            shared.m_toolTier = 4;          // can fell any tree and break any rock
            shared.m_weight = 0f;           // summoned from a spren, not carried
            shared.m_attackForce = 80f;
            shared.m_backstabBonus = 3f;
        }

        public override void BuildModel(MeshBuilder b)
        {
            // Hand at the origin; long two-handed grip below, blade from y=0.17 to 1.8
            b.Blade(Steel,
                new MeshBuilder.Station(0.17f, 0.07f, 0.07f, 0.016f),
                new MeshBuilder.Station(0.30f, 0.075f, 0.10f, 0.017f),  // flourish on one edge...
                new MeshBuilder.Station(0.40f, 0.075f, 0.068f, 0.017f), // ...with a notch above it
                new MeshBuilder.Station(0.50f, 0.08f, 0.09f, 0.016f),
                new MeshBuilder.Station(0.60f, 0.08f, 0.076f, 0.016f),
                new MeshBuilder.Station(1.40f, 0.08f, 0.08f, 0.013f),
                new MeshBuilder.Station(1.62f, 0.06f, 0.072f, 0.01f),
                new MeshBuilder.Station(1.80f, 0f, 0f, 0.004f));

            // Glyphs inlaid down the middle of the blade (slightly thicker than it, so they show on both faces)
            for (var y = 0.26f; y < 1.3f; y += 0.12f)
                b.Slab(Glyph, Diamond(0f, y, 0.016f, 0.035f), 0.0185f);

            // Crossguard with upswept fins and a central crown
            b.Slab(Guard, new[]
            {
                new Vector2(-0.08f, 0.08f), new Vector2(0.08f, 0.08f), new Vector2(0.22f, 0.14f),
                new Vector2(0.10f, 0.17f), new Vector2(-0.10f, 0.17f), new Vector2(-0.22f, 0.14f)
            }, 0.03f);
            b.Slab(Guard, new[] { new Vector2(0.12f, 0.15f), new Vector2(0.23f, 0.13f), new Vector2(0.21f, 0.32f) }, 0.02f);
            b.Slab(Guard, new[] { new Vector2(-0.12f, 0.15f), new Vector2(-0.21f, 0.32f), new Vector2(-0.23f, 0.13f) }, 0.02f);
            b.Slab(Guard, Diamond(0f, 0.18f, 0.05f, 0.09f), 0.025f);
            b.Slab(Glyph, Diamond(0f, 0.18f, 0.018f, 0.035f), 0.028f);

            // Long two-handed grip with a metal band, and a diamond pommel with a glowing gem
            b.Prism(Grip, -0.40f, 0.08f, 0.022f);
            b.Prism(Guard, -0.17f, -0.13f, 0.026f);
            b.Slab(Guard, Diamond(0f, -0.45f, 0.045f, 0.07f), 0.03f);
            b.Slab(Glyph, Diamond(0f, -0.45f, 0.015f, 0.025f), 0.033f);
        }

        private static Vector2[] Diamond(float x, float y, float halfWidth, float halfHeight) => new[]
        {
            new Vector2(x, y - halfHeight), new Vector2(x + halfWidth, y),
            new Vector2(x, y + halfHeight), new Vector2(x - halfWidth, y)
        };
    }
}

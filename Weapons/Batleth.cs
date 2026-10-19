using UnityEngine;

namespace MysticalWeapons.Weapons
{
    /// <summary>Klingon bat'leth (Star Trek). A crescent blade held by a grip on its back.</summary>
    internal class Batleth : MysticalWeapon
    {
        public override string PrefabName => "Batleth";
        public override string[] SourcePrefabs => new[] { "Battleaxe" }; // wide two-handed swings
        public override string DisplayName => "Bat'leth";
        public override string Description =>
            "The sword of honor. A curved Klingon blade with four points, forged for a warrior " +
            "who intends to die well - just not today.";

        protected override string DefaultDamage => "slash:80,pierce:20";
        protected override string DefaultDamagePerLevel => "slash:6";
        protected override string DefaultRecipe => "Iron:20,FineWood:6,LeatherScraps:6";
        protected override int DefaultStationLevel => 2;
        protected override float DefaultModelScale => 0.9f;
        public override float IconAngle => 90f; // cutting edge up

        public override MaterialSpec[] Materials => new[]
        {
            new MaterialSpec(new Color(0.72f, 0.72f, 0.76f), 0.75f), // 0 metal
            new MaterialSpec(new Color(0.22f, 0.13f, 0.07f), 0.2f),  // 1 leather
        };

        protected override void Customize(ItemDrop.ItemData.SharedData shared)
        {
            shared.m_attackForce = 90f;
            shared.m_backstabBonus = 4f;
        }

        // Shape in the XY plane: Y along the grip, +X towards the cutting edge
        private static readonly Vector2 Center = new Vector2(-0.45f, 0f);
        private const float OuterRadius = 0.75f;
        private const float BladeWidth = 0.12f;
        private const float HalfAngle = 62f * Mathf.Deg2Rad;
        private const float BladeHalfThickness = 0.012f;

        private static float InnerRadius(float angle)
        {
            var t = Mathf.Abs(angle) / HalfAngle; // taper to a point at each tip
            return OuterRadius - BladeWidth * (1f - t * t * t);
        }

        private static Vector2 Polar(float angle, float radius) =>
            Center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

        /// <summary>Back prong: a spike on the inner edge pointing back at the wielder.</summary>
        private static Vector2[] Prong(float sign)
        {
            float a0 = 36f * Mathf.Deg2Rad * sign, a1 = 48f * Mathf.Deg2Rad * sign;
            var p0 = Polar(a0, InnerRadius(a0) + 0.02f);
            var p1 = Polar(a1, InnerRadius(a1) + 0.02f);
            return new[] { p0, p1, (p0 + p1) * 0.5f + new Vector2(-0.17f, -0.03f * sign) };
        }

        public override void BuildModel(MeshBuilder b)
        {
            const int metal = 0, leather = 1;
            const int segments = 48;

            // Crescent blade with a sharp (zero-thickness) outer edge
            for (var i = 0; i < segments; i++)
            {
                var a0 = Mathf.Lerp(-HalfAngle, HalfAngle, i / (float)segments);
                var a1 = Mathf.Lerp(-HalfAngle, HalfAngle, (i + 1) / (float)segments);
                var in0 = Polar(a0, InnerRadius(a0));
                var in1 = Polar(a1, InnerRadius(a1));
                var out0 = MeshBuilder.V(Polar(a0, OuterRadius), 0f);
                var out1 = MeshBuilder.V(Polar(a1, OuterRadius), 0f);
                var h0 = BladeHalfThickness * (1f - Mathf.Abs(a0) / HalfAngle * 0.7f);
                var h1 = BladeHalfThickness * (1f - Mathf.Abs(a1) / HalfAngle * 0.7f);

                b.Quad(metal, MeshBuilder.V(in0, h0), MeshBuilder.V(in1, h1), out1, out0);
                b.Quad(metal, MeshBuilder.V(in0, -h0), MeshBuilder.V(in1, -h1), out1, out0);
                b.Quad(metal, MeshBuilder.V(in0, h0), MeshBuilder.V(in1, h1),
                    MeshBuilder.V(in1, -h1), MeshBuilder.V(in0, -h0));
            }

            b.Slab(metal, Prong(1f), BladeHalfThickness);
            b.Slab(metal, Prong(-1f), BladeHalfThickness);
            b.Box(metal, Rect.MinMaxRect(0f, 0.11f, 0.2f, 0.15f), BladeHalfThickness * 1.2f);   // struts
            b.Box(metal, Rect.MinMaxRect(0f, -0.15f, 0.2f, -0.11f), BladeHalfThickness * 1.2f);
            b.Box(leather, Rect.MinMaxRect(-0.02f, -0.15f, 0.02f, 0.15f), 0.022f);               // grip
        }
    }
}

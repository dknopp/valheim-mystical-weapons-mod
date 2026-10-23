using UnityEngine;

namespace MysticalWeapons.Weapons
{
    /// <summary>
    /// The Warglaives of Azzinoth (World of Warcraft): Illidan's twin blades, one in each hand.
    /// Cloned from Skoll and Hati for the dual-wield animations. Placeholder model and vanilla stats for now.
    /// </summary>
    internal class Warglaives : MysticalWeapon
    {
        public override string PrefabName => "WarglaivesOfAzzinoth";
        public override string[] SourcePrefabs => new[] { "KnifeSkollAndHati" };
        public override string DisplayName => "Warglaives of Azzinoth";
        public override string Description => "Twin blades torn from the guardian of the Black Temple.";

        protected override string DefaultRecipe => "KnifeSkollAndHati:1";
        protected override string DefaultStation => "blackforge";
        protected override int DefaultStationLevel => 1;
        // Keep Skoll and Hati's damage until the effect and balance are decided
        protected override bool HasDamage => false;
        protected override bool DualWield => true;

        private const int Blade = 0, Grip = 1;

        public override MaterialSpec[] Materials => new[]
        {
            new MaterialSpec(new Color(0.35f, 0.9f, 0.3f), 0.8f, new Color(0.1f, 0.4f, 0.08f)), // fel-green blade
            new MaterialSpec(new Color(0.3f, 0.2f, 0.12f), 0.3f),                                // grip
        };

        public override void BuildModel(MeshBuilder b)
        {
            // Placeholder: a plain short blade above the hand and a grip through it
            b.Blade(Blade,
                new MeshBuilder.Station(0.06f, 0.03f, 0.008f),
                new MeshBuilder.Station(0.35f, 0.025f, 0.007f),
                new MeshBuilder.Station(0.45f, 0f, 0.002f));
            b.Prism(Grip, -0.08f, 0.06f, 0.015f);
        }
    }
}

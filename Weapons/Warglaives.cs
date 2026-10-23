using System.IO;
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
        protected override bool CleanMaterials => true;
        // The OBJ import mirrors X, which put the hub behind the fist: turn it round the grip so the hub
        // sits over the back of the hand, tilted a little forward
        protected override Vector3 ModelRotation => new Vector3(0f, -110f, 0f);
        // The glaive is flat across the fingers (YZ), not in XY: show the star face in the icon
        protected override Vector3 IconView => new Vector3(0f, 90f, 0f);

        private const int Blade = 0, Grip = 1;
        private bool Textured => File.Exists(Path.Combine(ModelUtil.CustomModelDir, PrefabName + ".obj"));
        private static readonly Color FelColor = new Color(0.4f, 1f, 0.3f);

        // With the custom model the texture sets the colour and the fel blades glow by it (edges most)
        public override MaterialSpec[] Materials => new[]
        {
            new MaterialSpec(new Color(0.35f, 0.9f, 0.3f), 0.8f, FelColor * (Textured ? 0.6f : 0.25f)), // fel blades
            new MaterialSpec(new Color(0.3f, 0.2f, 0.12f), 0.6f),                                       // hub, star, grip
        };

        /// <summary>
        /// On the ground the hidden vanilla knives lie either side of the attach point and keep their
        /// upgrade glow and lights: switch those off and centre our glaive between the knives.
        /// </summary>
        public override void DecorateModel(GameObject model)
        {
            var attach = model.transform.parent;
            var centre = Vector3.zero;
            var knives = 0;
            foreach (Transform knife in attach)
            {
                if (knife == model.transform) continue;
                var glow = knife.Find("UpgraderGlow");
                if (glow) glow.gameObject.SetActive(false);
                var mesh = knife.GetComponent<MeshFilter>()?.sharedMesh;
                if (!mesh) continue;
                centre += attach.InverseTransformPoint(knife.TransformPoint(mesh.bounds.center));
                knives++;
            }
            if (knives == 0) return;
            var ours = model.GetComponent<MeshFilter>().sharedMesh.bounds.center;
            model.transform.localPosition = centre / knives - model.transform.localRotation * (ours * model.transform.localScale.x);
            Plugin.Log.LogInfo($"{PrefabName}: dropped model centred at {centre / knives} between {knives} vanilla knives, their glow off");
        }

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

using System.IO;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace MysticalWeapons.Weapons
{
    /// <summary>
    /// The Warglaives of Azzinoth (World of Warcraft): Illidan's twin blades, one in each hand.
    /// Cloned from Skoll and Hati for the dual-wield animations. The Dead Raiser's aura, turned fel
    /// green, wreathes both blades in hand and crossed on the back; hits add fel poison.
    /// </summary>
    internal class Warglaives : MysticalWeapon
    {
        public override string PrefabName => "WarglaivesOfAzzinoth";
        public override string[] SourcePrefabs => new[] { "KnifeSkollAndHati" };
        public override string DisplayName => "Warglaives of Azzinoth";
        public override string Description => "Twin blades torn from the guardian of the Black Temple.";

        protected override string DefaultRecipe => "KnifeSkollAndHati:1,FlametalNew:15,SurtlingCore:6";
        protected override string DefaultStation => "blackforge";
        protected override int DefaultStationLevel => 3;
        // About 40% above Skoll and Hati (slash:45,pierce:45), plus fel poison
        protected override string DefaultDamage => "slash:63,pierce:63,poison:30";
        protected override string DefaultDamagePerLevel => "slash:1,pierce:1,poison:3";
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

        private ConfigEntry<bool> _aura;
        private ConfigEntry<float> _auraSize;
        private ConfigEntry<float> _auraDensity;
        private ConfigEntry<float> _glow;

        public override void BindConfig(ConfigFile cfg)
        {
            base.BindConfig(cfg);
            _aura = cfg.Bind(PrefabName, "FelAura", true, "Fel green aura around the blades, in hand and holstered on the back.");
            _auraSize = cfg.Bind(PrefabName, "FelAuraSize", 0.15f, "Size of the fel flames, relative to the Dead Raiser's.");
            _auraDensity = cfg.Bind(PrefabName, "FelAuraDensity", 3f, "How many fel flames, relative to the Dead Raiser's.");
            _glow = cfg.Bind(PrefabName, "FelGlowStrength", 1.5f, "How brightly the fel blades glow (0 = not at all). Brighter blades give off a halo. Needs the models folder.");
        }

        // With the custom model the texture sets the colour and the fel blades glow by it (edges most)
        public override MaterialSpec[] Materials => new[]
        {
            new MaterialSpec(new Color(0.35f, 0.9f, 0.3f), 0.8f, FelColor * (Textured ? _glow?.Value ?? 1.5f : 0.25f)), // fel blades
            new MaterialSpec(new Color(0.3f, 0.2f, 0.12f), 0.6f),                                       // hub, star, grip
        };

        protected override void Setup(GameObject prefab, ItemDrop.ItemData.SharedData shared)
        {
            Plugin.Log.LogInfo($"{PrefabName}: Skoll and Hati deal {DescribeDamage(shared.m_damages)}, per level {DescribeDamage(shared.m_damagesPerLevel)}");
            base.Setup(prefab, shared);
            if (_aura.Value) AddAura(prefab);
        }

        /// <summary>
        /// Borrows the Dead Raiser's (StaffSkeleton) aura, the flames and embers it shows while equipped,
        /// turned fel green, and has them rise off the blades themselves: off the skinned mesh in hand (so they
        /// follow the hands) and off each glaive crossed on the back. Its item sparkle and upgrade glow stay behind.
        /// </summary>
        private void AddAura(GameObject prefab)
        {
            var staff = FindVanilla("StaffSkeleton");
            var sources = staff ? staff.GetComponentsInChildren<ParticleSystem>(true)
                .Where(p => p.transform.parent && p.transform.parent.name == "equiped").ToArray() : new ParticleSystem[0];
            if (sources.Length == 0)
            {
                Plugin.Log.LogWarning($"{PrefabName}: no Dead Raiser (StaffSkeleton) particles found, blades stay without an aura");
                return;
            }
            foreach (var source in sources)
            {
                var r = source.GetComponent<ParticleSystemRenderer>();
                var main = source.main;
                Plugin.Log.LogInfo($"{PrefabName}: borrowing StaffSkeleton particles '{source.name}' (under '{source.transform.parent?.name}', " +
                                   $"material '{(r && r.sharedMaterial ? r.sharedMaterial.name : "")}', shader '{(r && r.sharedMaterial ? r.sharedMaterial.shader.name : "")}', " +
                                   $"rate {source.emission.rateOverTimeMultiplier:0.#}, size {main.startSizeMultiplier:0.###}, life {main.startLifetimeMultiplier:0.##}, " +
                                   $"colour {main.startColor.color}, shape {source.shape.shapeType})");
            }

            var skin = prefab.transform.Find("attach_skin");
            var hands = skin ? skin.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(s => s.sharedMesh) : null;
            if (hands) AddAura(sources, hands.transform, hands, _auraSize.Value, _auraDensity.Value);
            var back = prefab.transform.Find("attach_back");
            if (back)
                foreach (var glaive in back.GetComponentsInChildren<MeshRenderer>(true))
                    AddAura(sources, glaive.transform, glaive, _auraSize.Value, _auraDensity.Value);
        }

        private static void AddAura(ParticleSystem[] sources, Transform parent, Renderer emitter, float size, float density)
        {
            foreach (var source in sources)
            {
                var go = Object.Instantiate(source.gameObject, parent, false);
                go.name = "fel_" + source.name;
                foreach (Transform child in go.transform) Object.Destroy(child.gameObject);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                go.SetActive(true);

                var ps = go.GetComponent<ParticleSystem>();
                var main = ps.main;
                main.playOnAwake = true;
                // Trail behind a swing instead of moving with the blade
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                // The hand bones carry the rig's import scale (x100), which would blow the flames up to fill the screen
                main.scalingMode = ParticleSystemScalingMode.Local;
                main.startSizeMultiplier *= size;
                main.maxParticles = Mathf.CeilToInt(main.maxParticles * density);
                var emission = ps.emission;
                emission.rateOverTimeMultiplier *= density;
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(FelColor.r, FelColor.g, FelColor.b, StartAlpha(main.startColor)));
                var colour = ps.colorOverLifetime;
                if (colour.enabled)
                {
                    var gradient = new Gradient();
                    gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                        colour.color.gradient?.alphaKeys ?? new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                    colour.color = gradient;
                }
                // Come off the blade's own surface
                var shape = ps.shape;
                shape.enabled = true;
                shape.position = Vector3.zero;
                shape.rotation = Vector3.zero;
                shape.scale = Vector3.one;
                if (emitter is SkinnedMeshRenderer skinned)
                {
                    shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
                    shape.skinnedMeshRenderer = skinned;
                }
                else
                {
                    shape.shapeType = ParticleSystemShapeType.MeshRenderer;
                    shape.meshRenderer = (MeshRenderer)emitter;
                }

                var r = go.GetComponent<ParticleSystemRenderer>();
                if (r && r.sharedMaterial)
                {
                    var mat = new Material(r.sharedMaterial) { name = "fel_" + r.sharedMaterial.name };
                    foreach (var prop in new[] { "_Color", "_TintColor", "_EmissionColor" })
                        if (mat.HasProperty(prop))
                            mat.SetColor(prop, new Color(FelColor.r, FelColor.g, FelColor.b, mat.GetColor(prop).a));
                    r.sharedMaterial = mat;
                }
            }
        }

        /// <summary>How opaque a particle system's start colour is, whichever way it is set (only Color mode has a plain colour).</summary>
        private static float StartAlpha(ParticleSystem.MinMaxGradient c)
        {
            switch (c.mode)
            {
                case ParticleSystemGradientMode.Color: return c.color.a;
                case ParticleSystemGradientMode.TwoColors: return Mathf.Max(c.colorMin.a, c.colorMax.a);
                case ParticleSystemGradientMode.TwoGradients: return Mathf.Max(c.gradientMin.Evaluate(0f).a, c.gradientMax.Evaluate(0f).a);
                default: return c.gradient != null ? c.gradient.Evaluate(0f).a : 1f;
            }
        }

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

        // Fallback when models/WarglaivesOfAzzinoth.obj is missing: a plain short blade and a grip through it
        public override void BuildModel(MeshBuilder b)
        {
            b.Blade(Blade,
                new MeshBuilder.Station(0.06f, 0.03f, 0.008f),
                new MeshBuilder.Station(0.35f, 0.025f, 0.007f),
                new MeshBuilder.Station(0.45f, 0f, 0.002f));
            b.Prism(Grip, -0.08f, 0.06f, 0.015f);
        }
    }
}

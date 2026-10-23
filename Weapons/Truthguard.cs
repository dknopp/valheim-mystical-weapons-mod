using BepInEx.Configuration;
using UnityEngine;

namespace MysticalWeapons.Weapons
{
    /// <summary>
    /// Truthguard (World of Warcraft): the shield Keeper Tyr gave the vrykul Yrgrim. Titan-forged gold
    /// round a stone disc carved by Ironaya, with light flowing out of the disc along its seams.
    /// A parrying shield, a little better than its Flametal peer. Ships as three models from
    /// tools/blender/make_truthguard_texture.py sharing models/Truthguard.png: the body (with the beams),
    /// and the rune ring and stone face, which turn slowly in opposite directions about the disc centre.
    /// The shape below is only the fallback.
    /// </summary>
    internal class Truthguard : MysticalWeapon
    {
        public override string PrefabName => "Truthguard";
        // Not the tower shield: towers can't parry
        public override string[] SourcePrefabs => new[] { "ShieldFlametal", "ShieldSilver" };
        public override string DisplayName => "Truthguard";
        public override string Description =>
            "Forged by the Keepers for Tyr's oathsworn. Its stone eye opens to the light when struck true.";

        protected override bool HasDamage => false;
        protected override bool CleanMaterials => true;
        // The Flametal shield's material draws its paint styles over any texture, even with styles off
        protected override string MaterialFrom => "SwordIron";
        protected override bool AutoAlign => false;
        protected override bool FitToOriginal => true;
        // The Flametal shield faces along attach Y: turn ours (facing +Z, point down -Y) to match
        protected override Vector3 ModelRotation => new Vector3(90f, 0f, 0f);
        // ~1.45 m long: shoulder to knee like the tower shields (1.6-1.7 m), a little smaller as it parries
        protected override float ModelScale => 0.95f;
        // Fitted, it is centred on the grip. Tower shields hang lower: the wood tower's grip is ~45% down
        // its length (item report, shield placement). Move it down along attach -Z so the grip sits ~43%
        // down, and out along -Y (the way it faces) so its back clears the arm when blocking.
        protected override Vector3 ModelOffset => new Vector3(0f, -0.04f, -0.1f);
        public override float IconAngle => 0f;

        // The consumed shield stands in for Archaedas' forging, the silver for Tyr's silver hand
        protected override string DefaultRecipe => "ShieldFlametal:1,FlametalNew:20,Silver:20";
        protected override string DefaultStation => "blackforge";
        protected override int DefaultStationLevel => 2;

        private ConfigEntry<float> _grain;
        private ConfigEntry<float> _blockArmor;
        private ConfigEntry<float> _blockForce;
        private ConfigEntry<float> _parryBonus;
        private ConfigEntry<bool> _discSpin;

        public override void BindConfig(ConfigFile cfg)
        {
            base.BindConfig(cfg);
            _grain = cfg.Bind(PrefabName, "TextureGrain", 0.5f,
                "How strongly vanilla grain shows: iron on the gold, stone on the disc (0 = off, 1 = full).");
            _blockArmor = cfg.Bind(PrefabName, "BlockArmorMultiplier", 1.05f,
                "Block armor (and its gain per level), as a multiple of the shield it is made from.");
            _blockForce = cfg.Bind(PrefabName, "BlockForceMultiplier", 1f,
                "Block force (knockback on block), as a multiple of the shield it is made from.");
            _parryBonus = cfg.Bind(PrefabName, "ParryBonusMultiplier", 1.1f,
                "Parry bonus, as a multiple of the shield it is made from.");
            _discSpin = cfg.Bind(PrefabName, "DiscSpin", true, "The rune ring and stone face turn slowly in opposite directions.");
        }

        private const int Body = 0, Beams = 1;
        private const int Stone = 0, Runes = 1;
        /// <summary>Where the ring and face sit, in model space; they turn about local Z, the way the shield faces.</summary>
        private static readonly Vector3 DiscCentre = new Vector3(0f, 0.22f, -0.0155f);
        private const float RingSpeed = 15f, FaceSpeed = -8f; // degrees per second
        private static readonly Color BeamColor = new Color(0.55f, 0.8f, 1f);

        public override MaterialSpec[] Materials => new[]
        {
            new MaterialSpec(new Color(0.85f, 0.68f, 0.32f), 0.35f)                        // Titan-forged gold, silver slats
                .WithGrain(VanillaTextures.Iron, 4f, _grain?.Value ?? 0.5f),
            new MaterialSpec(BeamColor, 0.2f, BeamColor * 2.5f),                           // light seams (fallback look)
        };

        private MaterialSpec[] RingMaterials => new[]
        {
            new MaterialSpec(Color.white, 0.25f).WithGrain(VanillaTextures.Stone, 2f, _grain?.Value ?? 0.5f),
            new MaterialSpec(Color.white, 0.4f, BeamColor * 2.5f),                         // runes: texture x glow
        };

        private MaterialSpec[] FaceMaterials => new[]
        {
            new MaterialSpec(Color.white, 0.25f).WithGrain(VanillaTextures.Stone, 2f, _grain?.Value ?? 0.5f),
        };

        private MeshBuilder _ring, _face;

        /// <summary>
        /// Long like a tower shield, so it is held at the Flametal tower shield's grip angle: the round
        /// shield's is turned ~10° differently, invisible on a round shield but off-kilter on a long one,
        /// idle and on the back.
        /// </summary>
        private const string PoseFrom = "ShieldFlametalTower";

        private void CopyGrip(GameObject prefab)
        {
            var tower = FindVanilla(PoseFrom);
            var ours = Find(prefab.transform, "attach");
            var theirs = tower ? Find(tower.transform, "attach") : null;
            if (!ours || !theirs)
            {
                Plugin.Log.LogWarning($"{PrefabName}: no grip from {PoseFrom}, keeping the round shield's");
                return;
            }
            ours.localPosition = theirs.localPosition;
            ours.localRotation = theirs.localRotation;
        }

        private static Transform Find(Transform t, string name)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true))
                if (c.name == name) return c;
            return null;
        }

        protected override void Setup(GameObject prefab, ItemDrop.ItemData.SharedData shared)
        {
            CopyGrip(prefab);
            base.Setup(prefab, shared);

            // Redraw the icon with the disc in it: the base icon only has the body
            var body = ModelUtil.LoadCustomModel(PrefabName, Materials.Length);
            if (body == null || _ring == null || _face == null) return;
            var icon = body.Combine((_ring, DiscCentre), (_face, DiscCentre));
            var specs = new System.Collections.Generic.List<MaterialSpec>();
            specs.AddRange(ModelUtil.WithTextures(Materials, body));
            specs.AddRange(ModelUtil.WithTextures(RingMaterials, _ring));
            specs.AddRange(ModelUtil.WithTextures(FaceMaterials, _face));
            Icon = ModelUtil.RenderIcon(icon, specs.ToArray(), IconAngle);
            shared.m_icons = new[] { Icon };
        }

        protected override void Customize(ItemDrop.ItemData.SharedData shared)
        {
            shared.m_blockPower *= _blockArmor.Value;
            shared.m_blockPowerPerLevel *= _blockArmor.Value;
            shared.m_deflectionForce *= _blockForce.Value;
            shared.m_deflectionForcePerLevel *= _blockForce.Value;
            shared.m_timedBlockBonus *= _parryBonus.Value;
            Plugin.Log.LogInfo($"{PrefabName}: block armor {shared.m_blockPower:0.#} (+{shared.m_blockPowerPerLevel:0.#}/level), " +
                               $"block force {shared.m_deflectionForce:0.#}, parry bonus {shared.m_timedBlockBonus:0.##}x, " +
                               $"durability {shared.m_maxDurability:0}, weight {shared.m_weight:0.#}, " +
                               $"movement {shared.m_movementModifier:0.##}");
        }

        public override void BuildModel(MeshBuilder b)
        {
            // Fallback: a flat kite with a stone disc near the top and a centre seam
            b.Slab(Body, new[]
            {
                new Vector2(0f, -0.5f), new Vector2(0.24f, -0.05f), new Vector2(0.32f, 0.3f),
                new Vector2(0.18f, 0.5f), new Vector2(-0.18f, 0.5f), new Vector2(-0.32f, 0.3f),
                new Vector2(-0.24f, -0.05f)
            }, 0.03f);
            b.Box(Beams, Rect.MinMaxRect(-0.012f, -0.45f, 0.012f, 0.2f), 0.04f);
            var disc = new Vector2[16];
            for (var k = 0; k < disc.Length; k++)
            {
                var a = k * Mathf.PI * 2f / disc.Length;
                disc[k] = new Vector2(Mathf.Cos(a) * 0.14f, DiscCentre.y + Mathf.Sin(a) * 0.14f);
            }
            b.Slab(Beams, disc, 0.06f);
        }

        public override void DecorateModel(GameObject model)
        {
            AddDisc(model);
            AddFlowingBeams(model);
            TameMetal(model);
        }

        private const float Metallic = 0.35f;

        /// <summary>
        /// The Flametal shield's material is full metal, which turns our gold dark and mirror-like:
        /// bring it down so the baked texture reads. Particle materials (the beams) have no such setting.
        /// </summary>
        private void TameMetal(GameObject model)
        {
            foreach (var r in model.GetComponentsInChildren<MeshRenderer>(true))
            foreach (var mat in r.sharedMaterials)
            {
                if (!mat || !mat.HasProperty("_Metallic")) continue;
                Plugin.Log.LogInfo($"{mat.name}: metallic {mat.GetFloat("_Metallic"):0.##} -> {Metallic}" +
                                   (mat.HasProperty("_Glossiness") ? $", gloss {mat.GetFloat("_Glossiness"):0.##}" : ""));
                mat.SetFloat("_Metallic", Metallic);
            }
        }

        /// <summary>Loads the rune ring and stone face as their own objects at the disc centre.</summary>
        private void AddDisc(GameObject model)
        {
            _ring = ModelUtil.LoadCustomModel(PrefabName + "_Ring", RingMaterials.Length);
            _face = ModelUtil.LoadCustomModel(PrefabName + "_Face", FaceMaterials.Length);
            if (_ring == null || _face == null)
            {
                Plugin.Log.LogWarning($"{PrefabName}: no ring or face model, the disc stays empty");
                _ring = _face = null;
                return;
            }
            if (Plugin.TexturedModels.Value)
            {
                AddGrain(_ring, RingMaterials);
                AddGrain(_face, FaceMaterials);
            }
            var ring = ModelUtil.AddChildModel(model, PrefabName + "_ring", _ring,
                ModelUtil.WithTextures(RingMaterials, _ring), DiscCentre, CleanMaterials);
            var face = ModelUtil.AddChildModel(model, PrefabName + "_face", _face,
                ModelUtil.WithTextures(FaceMaterials, _face), DiscCentre, CleanMaterials);
            if (!_discSpin.Value) return;
            ring.AddComponent<SpinAboutZ>().Speed = RingSpeed;
            face.AddComponent<SpinAboutZ>().Speed = FaceSpeed;
        }

        /// <summary>
        /// The beams take Dyrnwyn's see-through flame material, recoloured, with a flow texture scrolled
        /// along their length (V runs out of the disc to the point and the edges). Without it they keep
        /// the plain glowing material.
        /// </summary>
        private void AddFlowingBeams(GameObject model)
        {
            var renderer = model.GetComponent<MeshRenderer>();
            var mats = renderer.sharedMaterials;
            if (mats.Length <= Beams) return;

            Material flame = null;
            var sword = FindVanilla("SwordIronFire");
            if (sword)
                foreach (var ps in sword.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    if (ps.sharedMaterial && ps.sharedMaterial.name.StartsWith("flame")) { flame = ps.sharedMaterial; break; }
            if (!flame)
            {
                Plugin.Log.LogWarning($"{PrefabName}: no flame material from Dyrnwyn, the beams stay solid");
                return;
            }

            var mat = new Material(flame) { name = PrefabName + "_beams" };
            mat.mainTexture = FlowTexture();
            mat.mainTextureScale = new Vector2(1f, FlowRepeatsPerMetre);
            var tint = BeamColor * 1.5f;
            tint.a = 1f;
            foreach (var prop in new[] { "_TintColor", "_Color" })
                if (mat.HasProperty(prop)) mat.SetColor(prop, tint);
            Plugin.Log.LogInfo($"{PrefabName}: beams use '{flame.name}' ({flame.shader.name}); " +
                               $"_TintColor {mat.HasProperty("_TintColor")}, _Color {mat.HasProperty("_Color")}");
            mats[Beams] = mat;
            renderer.sharedMaterials = mats;

            // Particle shaders multiply by vertex colour: give the mesh plain white
            var mesh = model.GetComponent<MeshFilter>().sharedMesh;
            var colors = new Color[mesh.vertexCount];
            for (var i = 0; i < colors.Length; i++) colors[i] = Color.white;
            mesh.colors = colors;

            var scroll = model.AddComponent<ScrollTexture>();
            scroll.MaterialIndex = Beams;
            scroll.Speed = FlowSpeed;
        }

        private const float FlowRepeatsPerMetre = 3f;
        private const float FlowSpeed = 0.5f; // texture repeats per second

        /// <summary>Soft bright pulses along V, a little streaky across U, fading at the ends of each pulse.</summary>
        private static Texture2D FlowTexture()
        {
            const int w = 16, h = 64;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Truthguard_flow", wrapMode = TextureWrapMode.Repeat };
            var px = new Color[w * h];
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                float u = (float)x / w, v = (float)y / h;
                var pulse = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(v * Mathf.PI * 4f), 3f);
                var streak = 0.8f + 0.2f * Mathf.Sin(u * Mathf.PI * 6f + v * Mathf.PI * 2f);
                var value = Mathf.Clamp01((0.3f + 0.7f * pulse) * streak);
                px[y * w + x] = new Color(value, value, value, value);
            }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>Turns its object about local Z. Driven by the clock, so every copy turns in step.</summary>
        private class SpinAboutZ : MonoBehaviour
        {
            public float Speed;
            private void Update() => transform.localRotation = Quaternion.Euler(0f, 0f, Time.time * Speed % 360f);
        }

        /// <summary>Scrolls one material's texture along V. Shared material, set from the clock, so copies agree.</summary>
        private class ScrollTexture : MonoBehaviour
        {
            public int MaterialIndex;
            public float Speed;
            private Material _mat;

            private void Start()
            {
                var mats = GetComponent<MeshRenderer>().sharedMaterials;
                if (MaterialIndex < mats.Length) _mat = mats[MaterialIndex];
            }

            private void Update()
            {
                if (_mat) _mat.mainTextureOffset = new Vector2(0f, -(Time.time * Speed % 1f));
            }
        }
    }
}

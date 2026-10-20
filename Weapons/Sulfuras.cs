using System.IO;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MysticalWeapons.Weapons
{
    /// <summary>
    /// Sulfuras, Hand of Ragnaros (World of Warcraft): the Firelord's two-handed hammer of molten
    /// elementium, crowned with white-hot spikes. Swings like a battleaxe (a three-hit combo) with the
    /// Demolisher's ground slam on the secondary attack, on the Clubs skill. A swing that lands has a chance
    /// to hurl the Staff of Embers' fireball at the creature struck, as the WoW hammer does.
    /// Ships as models/Sulfuras.obj with two textures from tools/blender/make_sulfuras_texture.py: red
    /// elementium with flame swirls and runes, and a heat gradient that whitens towards the spike tips.
    /// The shape below is only the fallback: after changing it, re-export and re-run that script.
    /// </summary>
    internal class Sulfuras : MysticalWeapon
    {
        public override string PrefabName => "Sulfuras";
        public override string[] SourcePrefabs => new[] { "SledgeDemolisher", "SledgeIron" }; // ground slam
        public override string DisplayName => "Sulfuras, Hand of Ragnaros";
        public override string Description =>
            "The hammer of Ragnaros the Firelord, forged of flaming elementium and etched with runes " +
            "that flow like lava. Those it strikes are wreathed in fire.";

        // The Demolisher's material would otherwise show its own textures and metal colour through ours
        protected override bool CleanMaterials => true;
        protected override string DefaultDamage => "blunt:165";
        protected override string DefaultDamagePerLevel => "blunt:6";
        // The consumed sledge stands in for the Sulfuron Hammer, the Surtling cores for the Eye of Sulfuras
        protected override string DefaultRecipe => "SledgeDemolisher:1,FlametalNew:20,SurtlingCore:10";
        protected override string DefaultStation => "blackforge";
        protected override int DefaultStationLevel => 3;

        private ConfigEntry<float> _grain;
        private ConfigEntry<float> _comboStamina;
        private ConfigEntry<float> _slamStamina;
        private ConfigEntry<float> _slamDamage;
        private ConfigEntry<bool> _headFlames;
        internal static ConfigEntry<float> FireballChance;
        internal static ConfigEntry<float> FireballCooldown;
        internal static ConfigEntry<float> FireballDamage;

        public override void BindConfig(ConfigFile cfg)
        {
            base.BindConfig(cfg);
            _grain = cfg.Bind(PrefabName, "TextureGrain", 0.55f,
                "How strongly vanilla flametal's grain shows on the molten body (0 = off, 1 = full).");
            _comboStamina = cfg.Bind(PrefabName, "ComboStamina", 22f, "Stamina used per swing of the primary combo.");
            _slamStamina = cfg.Bind(PrefabName, "SlamStamina", 28f, "Stamina used by the secondary ground slam.");
            _slamDamage = cfg.Bind(PrefabName, "SlamDamageMultiplier", 1f, "Damage of the secondary ground slam, as a multiple of the weapon's damage.");
            FireballChance = cfg.Bind(PrefabName, "FireballChance", 0.2f,
                "Chance (0 to 1) that a swing which hits a creature hurls a fireball at it. One roll per swing, however many it hits.");
            FireballCooldown = cfg.Bind(PrefabName, "FireballCooldown", 3f, "Minimum seconds between fireballs.");
            FireballDamage = cfg.Bind(PrefabName, "FireballDamage", 60f, "Fire damage of the fireball. Fire also sets the target burning.");
            _headFlames = cfg.Bind(PrefabName, "HeadFlames", true, "Small flames licking off the hammer's head.");
        }

        // Late-game battleaxes first: every battleaxe has the same combo, only the stamina differs
        private static readonly string[] ComboSources = { "BattleaxeSkullSplittur", "BattleaxeBlackmetal", "Battleaxe" };

        protected override void Setup(GameObject prefab, ItemDrop.ItemData.SharedData shared)
        {
            base.Setup(prefab, shared);

            ItemDrop.ItemData.SharedData axe = null;
            foreach (var name in ComboSources)
            {
                var go = FindVanilla(name);
                var drop = go ? go.GetComponent<ItemDrop>() : null;
                if (drop) { axe = drop.m_itemData.m_shared; break; }
            }
            if (axe == null)
            {
                Plugin.Log.LogWarning($"{PrefabName}: no battleaxe found, keeping the sledge's single slam");
                return;
            }

            // The sledge's own slam moves to the secondary; the battleaxe combo and pose take the primary
            var slam = shared.m_attack.Clone();
            slam.m_attackStamina = _slamStamina.Value;
            slam.m_damageMultiplier = _slamDamage.Value;
            shared.m_secondaryAttack = slam;

            shared.m_attack = axe.m_attack.Clone();
            shared.m_attack.m_attackStamina = _comboStamina.Value;
            shared.m_animationState = axe.m_animationState;

            // The sledge keeps its blast in item-wide effects that every attack plays; move them onto
            // the slam and give the item the battleaxe's, so the combo hits like a battleaxe
            slam.m_hitEffect = Merge(slam.m_hitEffect, shared.m_hitEffect);
            slam.m_hitTerrainEffect = Merge(slam.m_hitTerrainEffect, shared.m_hitTerrainEffect);
            slam.m_startEffect = Merge(slam.m_startEffect, shared.m_startEffect);
            slam.m_triggerEffect = Merge(slam.m_triggerEffect, shared.m_triggerEffect);
            slam.m_trailStartEffect = Merge(slam.m_trailStartEffect, shared.m_trailStartEffect);
            if (!slam.m_spawnOnHit) slam.m_spawnOnHit = shared.m_spawnOnHit;
            if (shared.m_spawnOnHitTerrain)
                Plugin.Log.LogInfo($"{PrefabName}: dropping the sledge's '{shared.m_spawnOnHitTerrain.name}' on terrain hits");
            Plugin.Log.LogInfo($"{PrefabName}: moved to the slam: {Names(shared.m_hitEffect)} {Names(shared.m_hitTerrainEffect)} " +
                               $"{Names(shared.m_triggerEffect)} {(shared.m_spawnOnHit ? shared.m_spawnOnHit.name : "")}".Trim());
            shared.m_hitEffect = axe.m_hitEffect;
            shared.m_hitTerrainEffect = axe.m_hitTerrainEffect;
            shared.m_startEffect = axe.m_startEffect;
            shared.m_triggerEffect = axe.m_triggerEffect;
            shared.m_trailStartEffect = axe.m_trailStartEffect;
            shared.m_spawnOnHit = axe.m_spawnOnHit;
            shared.m_spawnOnHitTerrain = axe.m_spawnOnHitTerrain;
        }

        private static EffectList Merge(EffectList a, EffectList b)
        {
            var list = new System.Collections.Generic.List<EffectList.EffectData>();
            if (a?.m_effectPrefabs != null) list.AddRange(a.m_effectPrefabs);
            if (b?.m_effectPrefabs != null) list.AddRange(b.m_effectPrefabs);
            return new EffectList { m_effectPrefabs = list.ToArray() };
        }

        private static string Names(EffectList e) => e?.m_effectPrefabs == null ? "" :
            string.Join(" ", System.Array.ConvertAll(e.m_effectPrefabs, d => d.m_prefab ? d.m_prefab.name : "?"));

        private const int Molten = 0, Hot = 1;
        private bool Textured => File.Exists(Path.Combine(ModelUtil.CustomModelDir, PrefabName + ".obj"));
        private static readonly Color HeatColor = new Color(1f, 0.5f, 0.15f);

        public override MaterialSpec[] Materials => new[]
        {
            // With the custom model the textures set the colour and the emission colour scales each
            // texture's glow (bright swirls and spike tips glow most). Without it, flat colours and a dimmer glow.
            new MaterialSpec(new Color(0.42f, 0.07f, 0.03f), 0.45f,
                Textured ? new Color(1f, 0.45f, 0.1f) : new Color(0.12f, 0.02f, 0f))     // molten body
                .WithGrain(VanillaTextures.Flametal, 4f, _grain?.Value ?? 0.55f),        // with Valheim's flametal grain
            new MaterialSpec(new Color(1f, 0.6f, 0.18f), 0.75f,
                Textured ? new Color(0.9f, 0.8f, 0.6f) : new Color(0.55f, 0.28f, 0.05f)), // white-hot spikes, studs, rims
        };

        // Head layout: centred on the haft line at HeadY, long axis along X, thin in Z
        private const float HeadY = 0.93f;

        public override void BuildModel(MeshBuilder b)
        {
            // Pommel: an octagonal knob with a gold ring, a long point and a cross of side spikes
            b.Taper(Molten, -0.26f, -0.24f, 0.03f, 0.04f);
            b.Prism(Molten, -0.24f, -0.14f, 0.04f);
            b.Prism(Hot, -0.14f, -0.12f, 0.036f);
            Spike(b, new Vector3(0f, -0.255f, 0f), Vector3.down, Vector3.right, 0.03f, 0.03f, 0.15f);
            foreach (var r in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                Spike(b, new Vector3(0f, -0.19f, 0f) + r * 0.035f, r + Vector3.down * 0.35f, Vector3.up, 0.022f, 0.022f, 0.11f);

            // Haft
            b.Prism(Molten, -0.12f, 0.34f, 0.026f);

            // Sleeve: gold rim, a short bevel, then a barrel that narrows into a neck under the head
            b.Prism(Hot, 0.34f, 0.36f, 0.055f);
            b.Taper(Molten, 0.36f, 0.41f, 0.055f, 0.09f);
            b.Taper(Molten, 0.41f, 0.74f, 0.09f, 0.082f);
            b.Prism(Molten, 0.74f, 0.78f, 0.06f);

            // Stepped head: wide centre block, bevelled flanges, narrower end blocks
            b.Box(Molten, Rect.MinMaxRect(-0.11f, HeadY - 0.15f, 0.11f, HeadY + 0.15f), 0.08f);
            foreach (var s in new[] { 1f, -1f })
            {
                b.Slab(Molten, Mirror(s, new[]
                {
                    new Vector2(0.11f, HeadY - 0.15f), new Vector2(0.12f, HeadY - 0.17f),
                    new Vector2(0.13f, HeadY - 0.17f), new Vector2(0.14f, HeadY - 0.15f),
                    new Vector2(0.14f, HeadY + 0.15f), new Vector2(0.13f, HeadY + 0.17f),
                    new Vector2(0.12f, HeadY + 0.17f), new Vector2(0.11f, HeadY + 0.15f)
                }), 0.09f);
                b.Box(Molten, s > 0
                    ? Rect.MinMaxRect(0.14f, HeadY - 0.13f, 0.24f, HeadY + 0.13f)
                    : Rect.MinMaxRect(-0.24f, HeadY - 0.13f, -0.14f, HeadY + 0.13f), 0.07f);

                // Big pyramid spike on each striking end
                Spike(b, new Vector3(0.24f * s, HeadY, 0f), Vector3.right * s, Vector3.up, 0.1f, 0.06f, 0.2f);

                // One spike at each outer corner of the flange, angled outwards
                foreach (var t in new[] { 1f, -1f })
                    Spike(b, new Vector3(0.125f * s, HeadY + 0.165f * t, 0f), new Vector3(0.75f * s, t, 0f),
                        Vector3.forward, 0.025f, 0.025f, 0.13f);

                // Low diamond stud on the front and back of the centre block
                Spike(b, new Vector3(0f, HeadY, 0.08f * s), Vector3.forward * s, new Vector3(1f, 1f, 0f),
                    0.034f, 0.034f, 0.05f);
            }

            // Smaller spike on top, along the haft line
            Spike(b, new Vector3(0f, HeadY + 0.15f, 0f), Vector3.up, Vector3.right, 0.05f, 0.05f, 0.14f);
        }

        public override void DecorateModel(GameObject model)
        {
            ModelUtil.AddGlow(model, new Vector3(0f, HeadY, 0f), HeatColor, 3f, 1.2f);
            if (_headFlames.Value) AddHeadFlames(model);
        }

        /// <summary>
        /// Borrows Dyrnwyn's flame and ember particles, each placed straight on the hammer's head. Its
        /// other systems (item sparkle, blade glow, smoke) and its layout along the blade stay behind.
        /// </summary>
        private void AddHeadFlames(GameObject model)
        {
            var sword = FindVanilla("SwordIronFire");
            if (!sword)
            {
                Plugin.Log.LogWarning($"{PrefabName}: no Dyrnwyn (SwordIronFire) found, head stays without flames");
                return;
            }
            var head = new GameObject("flames");
            head.transform.SetParent(model.transform, false);
            head.transform.localPosition = new Vector3(0f, HeadY, 0f);

            foreach (var source in sword.GetComponentsInChildren<ParticleSystem>(true))
            {
                var r = source.GetComponent<ParticleSystemRenderer>();
                var mat = r && r.sharedMaterial ? r.sharedMaterial.name : "";
                if (!mat.StartsWith("flame") && !mat.StartsWith("gnista")) continue; // gnista: embers

                var go = Object.Instantiate(source.gameObject, head.transform, false);
                go.name = source.name;
                foreach (Transform child in go.transform) Object.Destroy(child.gameObject);
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                go.transform.localScale = Vector3.one;
                go.SetActive(true);

                var ps = go.GetComponent<ParticleSystem>();
                var main = ps.main;
                main.playOnAwake = true;
                main.startSizeMultiplier *= FlameSize;
                // Drift up in the world whichever way the hammer points, and trail behind a swing
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.15f);
                main.gravityModifier = -0.25f;
                var velocity = ps.velocityOverLifetime;
                velocity.enabled = false;
                var force = ps.forceOverLifetime;
                force.enabled = false;
                // Come off a shell just outside the head; flames born inside the solid head never show
                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.BoxShell;
                shape.position = Vector3.zero;
                shape.rotation = Vector3.zero;
                shape.scale = new Vector3(0.54f, 0.36f, 0.2f);
                var emission = ps.emission;
                emission.rateOverTimeMultiplier *= FlameSize;
            }
            if (head.transform.childCount == 0)
            {
                Plugin.Log.LogWarning($"{PrefabName}: Dyrnwyn has no flames to borrow");
                Object.Destroy(head);
            }
        }

        /// <summary>How big and how many of Dyrnwyn's flames, relative to the sword's.</summary>
        private const float FlameSize = 0.8f;

        /// <summary>
        /// A four-sided pyramid: rectangular base centred on <paramref name="baseCentre"/>, pointing
        /// along <paramref name="dir"/>. <paramref name="across"/> sets which way the base's first side faces.
        /// </summary>
        private static void Spike(MeshBuilder b, Vector3 baseCentre, Vector3 dir, Vector3 across,
            float halfAcross, float halfOther, float length)
        {
            dir.Normalize();
            var w = Vector3.Cross(dir, across).normalized;
            var u = Vector3.Cross(w, dir).normalized;
            var tip = baseCentre + dir * length;
            var c = new[]
            {
                baseCentre + u * halfAcross + w * halfOther, baseCentre - u * halfAcross + w * halfOther,
                baseCentre - u * halfAcross - w * halfOther, baseCentre + u * halfAcross - w * halfOther
            };
            for (var i = 0; i < 4; i++) b.Tri(Hot, tip, c[i], c[(i + 1) % 4]);
            b.Quad(Hot, c[0], c[1], c[2], c[3]);
        }

        private const string FireballPrefab = "staff_fireball_projectile";
        private const float FireballSpeed = 25f;
        private static float _nextFireball;
        private static int _rolledFrame = -1;

        /// <summary>
        /// When a swing of Sulfuras lands on a creature, maybe hurl a fireball at it. Runs on the
        /// attacker's client. A swing hits everything it touches in the same frame, so one roll per frame.
        /// </summary>
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        private static class FireballPatch
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                if (FireballChance == null) return;
                // The fireball's own hit carries no skill, so it never throws another
                if (hit.m_ranged || hit.m_skill == Skills.SkillType.None) return;
                if (!(hit.GetAttacker() is Player attacker) || attacker != Player.m_localPlayer) return;
                var weapon = attacker.GetCurrentWeapon();
                if (weapon?.m_dropPrefab == null || weapon.m_dropPrefab.name != "Sulfuras") return;

                if (_rolledFrame == Time.frameCount) return;
                _rolledFrame = Time.frameCount;
                if (Time.time < _nextFireball || Random.value >= FireballChance.Value) return;
                if (Hurl(attacker, __instance)) _nextFireball = Time.time + FireballCooldown.Value;
            }
        }

        private static bool Hurl(Player attacker, Character target)
        {
            var prefab = ZNetScene.instance ? ZNetScene.instance.GetPrefab(FireballPrefab) : null;
            if (!prefab)
            {
                Plugin.Log.LogWarning($"Sulfuras: no '{FireballPrefab}' prefab, no fireball");
                return false;
            }
            // From chest height just in front of the wielder, straight at the creature's centre
            var from = attacker.GetCenterPoint() + attacker.transform.forward * 0.4f;
            var dir = (target.GetCenterPoint() - from).normalized;
            var go = Object.Instantiate(prefab, from, Quaternion.LookRotation(dir));
            var projectile = go.GetComponent<Projectile>();
            if (!projectile)
            {
                Object.Destroy(go);
                return false;
            }
            projectile.Setup(attacker, dir * FireballSpeed, -1f, null, null, null);
            projectile.m_damage = new HitData.DamageTypes { m_fire = FireballDamage.Value };
            projectile.m_skill = Skills.SkillType.None;
            return true;
        }

        private static Vector2[] Mirror(float s, Vector2[] pts)
        {
            if (s > 0) return pts;
            var m = new Vector2[pts.Length];
            for (var i = 0; i < pts.Length; i++) m[i] = new Vector2(-pts[pts.Length - 1 - i].x, pts[pts.Length - 1 - i].y);
            return m;
        }
    }
}

using System.IO;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MysticalWeapons.Weapons
{
    /// <summary>
    /// Thunderfury, Blessed Blade of the Windseeker (World of Warcraft): a split, spear-like blade with a
    /// ball of lightning caught at its root. Swings like any one-handed sword; a swing that lands has a
    /// chance to loose vanilla chain lightning at the creature struck, as the WoW blade does.
    /// Cloned from plain Nidhögg so no lightning comes along; its peer, Nidhögg the Thundering, is what the
    /// recipe consumes and lends its sparks to the orb.
    /// Ships as models/Thunderfury.obj from tools/blender/make_thunderfury_texture.py. The shape below is
    /// only the fallback: after changing it, re-run that script.
    /// </summary>
    internal class Thunderfury : MysticalWeapon
    {
        public override string PrefabName => "Thunderfury";
        public override string[] SourcePrefabs => new[] { "SwordNiedhogg" };
        public override string VanillaPeer => "SwordNiedhoggLightning";
        public override string DisplayName => "Thunderfury, Blessed Blade of the Windseeker";
        public override string Description =>
            "Forged from the bindings that held Thunderaan, Prince of Air, the blade still hums with " +
            "his storm. Lightning leaps from it to all who stand against its wielder.";

        protected override bool CleanMaterials => true;
        protected override string DefaultDamage => "slash:145";
        protected override string DefaultDamagePerLevel => "slash:6";
        // The Thunderstones stand in for the two Bindings of the Windseeker
        protected override string DefaultRecipe => "SwordNiedhoggLightning:1,FlametalNew:20,GemstoneBlue:2,Thunderstone:2"; // GemstoneBlue is Iolite
        protected override string DefaultStation => "blackforge";
        protected override int DefaultStationLevel => 4;

        private ConfigEntry<float> _grain;
        private ConfigEntry<bool> _orbCrackle;
        private ConfigEntry<float> _crackleSize;
        internal static ConfigEntry<float> LightningChance;
        internal static ConfigEntry<float> LightningCooldown;
        internal static ConfigEntry<float> LightningDamage;

        public override void BindConfig(ConfigFile cfg)
        {
            base.BindConfig(cfg);
            _grain = cfg.Bind(PrefabName, "TextureGrain", 0.35f,
                "How strongly vanilla iron's grain shows on the blade and hilt (0 = off, 1 = full).");
            LightningChance = cfg.Bind(PrefabName, "LightningChance", 0.25f,
                "Chance (0 to 1) that a swing which hits a creature looses chain lightning at it. One roll per swing, however many it hits.");
            LightningCooldown = cfg.Bind(PrefabName, "LightningCooldown", 2f, "Minimum seconds between chain lightnings.");
            LightningDamage = cfg.Bind(PrefabName, "LightningDamage", 60f,
                "Lightning damage of each strike of the chain. Lightning hurts wet creatures more.");
            _orbCrackle = cfg.Bind(PrefabName, "OrbCrackle", true, "Small lightning sparks crackling around the orb.");
            _crackleSize = cfg.Bind(PrefabName, "OrbCrackleSize", 0.3f,
                "Size and amount of the orb's sparks, relative to Nidhogg the Thundering's.");
        }

        private const int Steel = 0, Orb = 1;
        private bool Textured => File.Exists(Path.Combine(ModelUtil.CustomModelDir, PrefabName + ".obj"));
        private static readonly Color StormColor = new Color(0.65f, 0.85f, 1f);

        public override MaterialSpec[] Materials => new[]
        {
            new MaterialSpec(new Color(0.7f, 0.74f, 0.8f), 0.7f)                         // blade, hilt, spikes
                .WithGrain(VanillaTextures.Iron, 4f, _grain?.Value ?? 0.35f),
            new MaterialSpec(new Color(0.8f, 0.9f, 1f), 0.8f,
                Textured ? StormColor : StormColor * 0.6f),                              // the orb
        };

        /// <summary>Height of the orb's centre above the hand, in the model.</summary>
        private const float OrbY = 0.40f;

        public override void BuildModel(MeshBuilder b)
        {
            // Fallback only: a long tapering blade, a short collar, grip and pommel, and the orb
            b.Blade(Steel,
                new MeshBuilder.Station(0.10f, 0.11f, 0.02f),
                new MeshBuilder.Station(0.30f, 0.07f, 0.016f),
                new MeshBuilder.Station(0.90f, 0.03f, 0.01f),
                new MeshBuilder.Station(1.0f, 0f, 0.002f));
            b.Prism(Steel, 0.02f, 0.10f, 0.03f);
            b.Prism(Steel, -0.18f, 0.02f, 0.018f);
            b.Taper(Steel, -0.29f, -0.18f, 0.005f, 0.03f);
            b.Prism(Orb, OrbY - 0.025f, OrbY + 0.025f, 0.028f);
        }

        public override void DecorateModel(GameObject model)
        {
            ModelUtil.AddGlow(model, new Vector3(0f, OrbY, 0f), StormColor, 1.5f, 1f);
            SilenceSourceEffects(model);
            if (_orbCrackle.Value) AddOrbCrackle(model);
        }

        /// <summary>Turns off any particles or lights the cloned Nidhögg brought along its own blade.</summary>
        private void SilenceSourceEffects(GameObject model)
        {
            var attach = model.transform.parent;
            if (!attach) return;
            foreach (var ps in attach.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps.transform.IsChildOf(model.transform)) continue;
                Plugin.Log.LogInfo($"{PrefabName}: turning off Nidhögg's particles '{ps.name}'");
                ps.gameObject.SetActive(false);
            }
            foreach (var light in attach.GetComponentsInChildren<Light>(true))
            {
                if (light.transform.IsChildOf(model.transform)) continue;
                Plugin.Log.LogInfo($"{PrefabName}: turning off Nidhögg's light '{light.name}'");
                light.enabled = false;
            }
        }

        /// <summary>
        /// Borrows the lightning that runs along Nidhögg the Thundering's blade, copied whole (it draws its
        /// sparks as trails) onto a small sphere around the orb and scaled down as
        /// one piece. Its glow and smoke stay behind: they dwarf our small orb.
        /// </summary>
        private void AddOrbCrackle(GameObject model)
        {
            var sword = FindVanilla("SwordNiedhoggLightning");
            var attach = sword ? FindAttach(sword.transform) : null;
            if (!attach)
            {
                Plugin.Log.LogWarning($"{PrefabName}: no Nidhögg the Thundering found, orb stays without sparks");
                return;
            }

            Transform source = null;
            foreach (var ps in attach.GetComponentsInChildren<ParticleSystem>(true))
                if (ps.name == "Lightning") { source = ps.transform; break; }
            if (!source)
            {
                Plugin.Log.LogWarning($"{PrefabName}: Nidhögg the Thundering has no 'Lightning' to borrow");
                return;
            }

            var size = _crackleSize.Value;
            var go = Object.Instantiate(source.gameObject, model.transform, false);
            go.name = "crackle";
            go.transform.localPosition = new Vector3(0f, OrbY, 0f);
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one * size;
            go.SetActive(true);

            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.gameObject.SetActive(true);
                var main = ps.main;
                // Scale every system, sizes, speeds and shapes, with the copy's transform
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                var r = ps.GetComponent<ParticleSystemRenderer>();
                var mat = r && r.sharedMaterial ? r.sharedMaterial.name : "";
                if (mat.StartsWith("wispysmoke") || mat.StartsWith("DraugrFangGlow"))
                {
                    var off = ps.emission;
                    off.enabled = false;
                    continue;
                }
                if (ps.transform != go.transform) continue;
                // The root emitter: from a sphere around the orb rather than the Thundering's blade
                main.playOnAwake = true;
                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.position = Vector3.zero;
                shape.rotation = Vector3.zero;
                shape.scale = Vector3.one;
                shape.radius = 0.04f / Mathf.Max(size, 0.01f);
            }
            Plugin.Log.LogInfo($"{PrefabName}: orb crackle copied from '{source.name}' at size {size:0.00}");
        }

        private static Transform FindAttach(Transform t)
        {
            if (t.name == "attach") return t;
            foreach (Transform child in t)
            {
                var found = FindAttach(child);
                if (found) return found;
            }
            return null;
        }

        private const string ChainLightningPrefab = "ChainLightning";
        private static float _nextLightning;
        private static int _rolledFrame = -1;

        /// <summary>
        /// When a swing of Thunderfury lands on a creature, maybe loose chain lightning at it. Runs on the
        /// attacker's client. A swing hits everything it touches in the same frame, so one roll per frame.
        /// </summary>
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        private static class ChainLightningPatch
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                if (LightningChance == null) return;
                if (!(hit.GetAttacker() is Player attacker) || attacker != Player.m_localPlayer) return;
                var weapon = attacker.GetCurrentWeapon();
                if (weapon?.m_dropPrefab == null || weapon.m_dropPrefab.name != "Thunderfury") return;
                // TEMP: every lightning hit while holding Thunderfury, to see where the chain lands
                if (hit.m_damage.m_lightning > 0f && hit.m_damage.m_slash <= 0f)
                    Plugin.Log.LogInfo($"Thunderfury: lightning hit {__instance.m_name} for {hit.m_damage.m_lightning:0}");
                // The lightning's own hits carry no slash and no skill, so they never loose more
                if (hit.m_ranged || hit.m_skill == Skills.SkillType.None || hit.m_damage.m_slash <= 0f) return;

                if (_rolledFrame == Time.frameCount) return;
                _rolledFrame = Time.frameCount;
                if (Time.time < _nextLightning || Random.value >= LightningChance.Value) return;
                if (Strike(attacker, __instance, hit)) _nextLightning = Time.time + LightningCooldown.Value;
            }
        }

        private static readonly string[] StrikeEffects = { "fx_chainlightning_hit" };

        /// <summary>
        /// Strikes the creature hit with lightning (with the vanilla flash and crack, so it reads on a lone
        /// target too), then spawns vanilla chain lightning there to leap on. The chain copies the damage of the
        /// hit it is given into every leap, so it gets the bolt, never the sword swing. It only leaps to enemies
        /// (BaseAI.IsEnemy), so never between training dummies.
        /// </summary>
        private static bool Strike(Player attacker, Character target, HitData swing)
        {
            var scene = ZNetScene.instance;
            if (!scene) return false;
            var point = target.GetCenterPoint();

            var bolt = new HitData
            {
                m_damage = new HitData.DamageTypes { m_lightning = LightningDamage.Value },
                m_point = point,
                m_dir = swing.m_dir,
                m_skill = Skills.SkillType.None
            };
            bolt.SetAttacker(attacker);
            target.Damage(bolt);

            foreach (var name in StrikeEffects)
            {
                var fx = scene.GetPrefab(name);
                if (fx) Object.Instantiate(fx, point, Quaternion.identity);
                else Plugin.Log.LogWarning($"Thunderfury: no '{name}' prefab");
            }

            var prefab = scene.GetPrefab(ChainLightningPrefab);
            var go = prefab ? Object.Instantiate(prefab, point, Quaternion.identity) : null;
            var aoe = go ? go.GetComponent<Aoe>() : null;
            if (!aoe)
            {
                Plugin.Log.LogWarning($"Thunderfury: no '{ChainLightningPrefab}' with an Aoe, lightning won't leap");
                if (go) Object.Destroy(go);
                return true;
            }
            aoe.Setup(attacker, Vector3.zero, -1f, bolt, null, null);
            aoe.m_damage = bolt.m_damage;
            aoe.m_skill = Skills.SkillType.None;
            // TEMP: how the vanilla chain is set up
            Plugin.Log.LogInfo($"Thunderfury: TEMP struck {target.m_name}; chain radius {aoe.m_radius}, hits characters {aoe.m_hitCharacters}, " +
                               $"enemies {aoe.m_hitEnemy}, uses attack settings {aoe.m_useAttackSettings}, start chance {aoe.m_chainStartChance}, " +
                               $"per target {aoe.m_chainChancePerTarget}, targets {aoe.m_chainMinTargets}-{aoe.m_chainMaxTargets}, " +
                               $"falloff {aoe.m_chainStartChanceFalloff}, leap '{(aoe.m_chainObj ? aoe.m_chainObj.name : "")}', ttl {aoe.m_ttl}");
            return true;
        }
    }
}

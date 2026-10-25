using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MysticalWeapons.Armor
{
    /// <summary>
    /// Shardplate (The Stormlight Archive): seamless slate-grey plate powered by Stormlight.
    /// Built on Flametal (Ashlands) heavy plate, recoloured, with its fiery seams turned
    /// Stormlight blue. Wearing all three pieces grants the Shardplate set bonus, makes the wearer
    /// a head taller, wraps them in Stormlight and cracks lightning where their blows land.
    /// </summary>
    internal abstract class ShardplatePiece : MysticalArmor
    {
        private const string SetName = "Shardplate";
        private static SE_Stats _setEffect;

        protected override string DefaultStation => "forge";
        protected override int DefaultStationLevel => 5;
        // A little above Padded (26), its Plains peer: the set needs Moder's trophy (see BossTrophies)
        protected override float DefaultArmor => 30f;

        private static ConfigEntry<float> _carryWeight;
        private static ConfigEntry<float> _size;
        private static ConfigEntry<bool> _stormlight;
        private static ConfigEntry<bool> _storm;
        private static ConfigEntry<float> _stormCooldown;
        private static bool? _resetOldLook;

        public override void BindConfig(ConfigFile cfg)
        {
            base.BindConfig(cfg);
            ResetOldLook(cfg);
            if (_carryWeight != null) return; // the set's own settings, once for all three pieces
            _carryWeight = cfg.Bind(SetName, "CarryWeight", 50f, "Full set: extra carry weight. Restart to apply.");
            _size = cfg.Bind(SetName, "Size", 1.25f,
                "Full set: the wearer looks this much bigger (1.25 = 25% taller), for everyone. Only the model grows; " +
                "you still fit through doors.");
            _stormlight = cfg.Bind(SetName, "StormlightGlow", true, "Full set: a soft Stormlight-blue glow surrounds the wearer.");
            _storm = cfg.Bind(SetName, "StormStrikes", true, "Full set: lightning cracks where your melee blows land (looks only, no extra damage).");
            _stormCooldown = cfg.Bind(SetName, "StormStrikeCooldown", 0.4f, "Shortest time between two lightning cracks, in seconds.");
        }

        /// <summary>
        /// Once per config: before the breastplate and greaves had custom models (#20), configs saved their
        /// CustomModelGlow 0 and CustomModelBrightness 1, which now show dark, unlit plates. Put that exact
        /// pair back to this version's defaults; a value a player picks later is kept.
        /// </summary>
        private void ResetOldLook(ConfigFile cfg)
        {
            if (_resetOldLook == null)
            {
                var done = cfg.Bind(SetName, "OldLookReset", false,
                    "Set by the mod once it has updated colour settings saved by an older version. Leave as is.");
                _resetOldLook = !done.Value;
                try { done.Value = true; }
                catch (System.Exception e) { Plugin.Log.LogWarning($"Shardplate: could not save OldLookReset. {e.Message}"); }
            }
            if (_resetOldLook != true) return;
            if (!cfg.TryGetEntry<float>(PrefabName, "CustomModelGlow", out var glow) ||
                !cfg.TryGetEntry<float>(PrefabName, "CustomModelBrightness", out var brightness)) return;
            if (glow.Value != 0f || brightness.Value != 1f) return;
            try
            {
                glow.Value = DefaultCustomGlow;
                brightness.Value = DefaultCustomBrightness;
                Plugin.Log.LogInfo($"{PrefabName}: reset CustomModelGlow and CustomModelBrightness saved by an older " +
                                   $"version to {DefaultCustomGlow} and {DefaultCustomBrightness}");
            }
            catch (System.Exception e)
            {
                // Setting a value saves the config file; runs outside Guard, so a failed save mustn't stop the mod
                Plugin.Log.LogWarning($"{PrefabName}: could not reset the old glow settings. {e.Message}");
            }
        }
        protected override Color DefaultTint => new Color(0.62f, 0.68f, 0.78f);  // slate with a blue sheen
        protected override Color DefaultGlow => new Color(0.25f, 0.55f, 1f);     // Stormlight
        // Custom models start from the Flametal chest's material (metallic, emissive), which gives the
        // plates their sheen; the Drake helmet's own material is matte leather and looked flat
        protected override string CustomModelMaterialSource => "ArmorFlametalChest";
        // Krajkoa's plate is painted near-black; lift it to Stormlit slate-blue and let it glow
        protected override float DefaultCustomBrightness => 2.4f;
        protected override float DefaultCustomGlow => 1f;

        protected override void Customize(ItemDrop.ItemData.SharedData shared)
        {
            shared.m_movementModifier = 0f;  // Stormlight carries the weight
            shared.m_weight = 10f;
            shared.m_maxDurability = 3000f;  // cracks, but regrows
            shared.m_equipStatusEffect = null;
            shared.m_damageModifiers = new List<HitData.DamageModPair>();

            shared.m_setName = SetName;
            shared.m_setSize = 3;
            shared.m_setStatusEffect = GetSetEffect();
        }

        private SE_Stats GetSetEffect()
        {
            if (_setEffect) return _setEffect;

            _setEffect = ScriptableObject.CreateInstance<SE_Stats>();
            _setEffect.name = "SE_Shardplate";
            _setEffect.m_name = "Shardplate";
            _setEffect.m_tooltip = "Stormlight fills the plate: you stand a head taller, carry more, " +
                                   "and crushing blows glance off you.";
            _setEffect.m_icon = Icon;
            _setEffect.m_addMaxCarryWeight = _carryWeight.Value;
            _setEffect.m_staminaRegenMultiplier = 1.25f;
            _setEffect.m_mods = new List<HitData.DamageModPair>
            {
                new HitData.DamageModPair { m_type = HitData.DamageType.Blunt, m_modifier = HitData.DamageModifier.Resistant },
            };
            return _setEffect;
        }

        public override void RegisterExtras(ObjectDB db)
        {
            if (_setEffect && !db.m_StatusEffects.Contains(_setEffect)) db.m_StatusEffects.Add(_setEffect);
        }

        // ------------------------------------------------------------- Stormlight

        private static readonly int HelmHash = "ShardplateHelm".GetStableHashCode();
        private static readonly int ChestHash = "ShardplateBreastplate".GetStableHashCode();
        private static readonly int LegsHash = "ShardplateGreaves".GetStableHashCode();
        private static readonly AccessTools.FieldRef<VisEquipment, int> HelmetItemHash =
            AccessTools.FieldRefAccess<VisEquipment, int>("m_currentHelmetItemHash");
        private static readonly AccessTools.FieldRef<VisEquipment, int> ChestItemHash =
            AccessTools.FieldRefAccess<VisEquipment, int>("m_currentChestItemHash");
        private static readonly AccessTools.FieldRef<VisEquipment, int> LegItemHash =
            AccessTools.FieldRefAccess<VisEquipment, int>("m_currentLegItemHash");

        /// <summary>From the synced equipment visuals, so it reads the same on every client.</summary>
        private static bool WearsFullSet(VisEquipment vis) =>
            vis && HelmetItemHash(vis) == HelmHash && ChestItemHash(vis) == ChestHash && LegItemHash(vis) == LegsHash;

        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> VisualRoot =
            AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_visual");

        [HarmonyPatch(typeof(Player), "Awake")]
        private static class AddStormlightPatch
        {
            private static void Postfix(Player __instance) => __instance.gameObject.AddComponent<Stormlit>();
        }

        /// <summary>
        /// On every player on every client: while they wear the full set, their model grows to <see cref="_size"/>
        /// (only the model, so the collider still fits doors and beds) and a Stormlight glow surrounds them.
        /// </summary>
        private class Stormlit : MonoBehaviour
        {
            private const float Interval = 0.5f;   // seconds between checks of the worn armour
            private const float GrowSpeed = 0.5f;  // scale per second while growing or shrinking

            private VisEquipment _vis;
            private Transform _visual;
            private Vector3 _baseScale = Vector3.one;
            private float _scale = 1f, _target = 1f, _next;
            private Light _glow;

            private void Awake() => _vis = GetComponent<VisEquipment>();

            private void Update()
            {
                if (!_visual && _vis && VisualRoot(_vis))
                {
                    // Looked up here: the game may set it after this component wakes
                    _visual = VisualRoot(_vis).transform;
                    _baseScale = _visual.localScale;
                }
                if (Time.time >= _next)
                {
                    _next = Time.time + Interval;
                    var full = _size != null && WearsFullSet(_vis);
                    _target = full ? Mathf.Max(0.1f, _size.Value) : 1f;
                    SetGlow(full && _stormlight.Value);
                }
                if (!_visual || Mathf.Approximately(_scale, _target)) return;
                _scale = Mathf.MoveTowards(_scale, _target, GrowSpeed * Time.deltaTime);
                _visual.localScale = _baseScale * _scale;
            }

            private void SetGlow(bool on)
            {
                if (!on && !_glow) return;
                if (!_glow)
                {
                    var go = new GameObject("ShardplateStormlight");
                    go.transform.SetParent(transform, false);
                    go.transform.localPosition = new Vector3(0f, 1.3f, 0.3f); // in front of the chest
                    _glow = go.AddComponent<Light>();
                    _glow.type = LightType.Point;
                    _glow.color = new Color(0.45f, 0.7f, 1f);
                    _glow.range = 4f;
                    _glow.intensity = 1.2f;
                    _glow.shadows = LightShadows.None;
                }
                _glow.enabled = on;
            }
        }

        private const string StrikeEffect = "fx_chainlightning_hit"; // the flash and crack Thunderfury uses
        private static float _nextStrike;
        private static bool _loggedStrike;

        /// <summary>
        /// A melee blow from the local player in full Shardplate cracks lightning where it lands. Looks only: the
        /// hit is unchanged. Runs on the attacker's client; spell and lightning hits (no skill) are skipped.
        /// </summary>
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        private static class StormStrikePatch
        {
            private static void Prefix(HitData hit)
            {
                if (_storm == null || !_storm.Value || Time.time < _nextStrike) return;
                if (hit.m_ranged || hit.m_skill == Skills.SkillType.None) return;
                if (!(hit.GetAttacker() is Player attacker) || attacker != Player.m_localPlayer) return;
                if (!WearsFullSet(attacker.GetComponent<VisEquipment>())) return;

                var fx = ZNetScene.instance ? ZNetScene.instance.GetPrefab(StrikeEffect) : null;
                if (!fx)
                {
                    if (!_loggedStrike) Plugin.Log.LogWarning($"Shardplate: no '{StrikeEffect}' prefab, no storm strikes");
                    _loggedStrike = true;
                    return;
                }
                if (!_loggedStrike)
                {
                    // A networked effect shows for everyone; otherwise only the wearer sees it
                    Plugin.Log.LogInfo($"Shardplate: storm strikes use '{StrikeEffect}' (synced: {fx.GetComponent<ZNetView>() != null})");
                    _loggedStrike = true;
                }
                _nextStrike = Time.time + _stormCooldown.Value;
                Object.Instantiate(fx, hit.m_point, Quaternion.identity);
            }
        }
    }

    internal class ShardplateHelm : ShardplatePiece
    {
        public override string PrefabName => "ShardplateHelm";
        // Drakescale helm: its swept-back crest reads more like a Shardhelm than the Flametal helmet
        public override string[] SourcePrefabs => new[] { "HelmetDrake", "HelmetFlametal", "HelmetPadded" };

        protected override void Customize(ItemDrop.ItemData.SharedData shared)
        {
            base.Customize(shared);
            // A closed Shardhelm: hide hair and beard so they don't poke out through the breastplate
            shared.m_helmetHideHair = ItemDrop.ItemData.HelmetHairType.Hidden;
            shared.m_helmetHideBeard = ItemDrop.ItemData.HelmetHairType.Hidden;
        }
        public override string DisplayName => "Shardplate Helm";
        public override string Description =>
            "A seamless helm with a narrow eye-slit. When the visor locks, the world sharpens " +
            "and Stormlight hums in your ears.\n\n<size=80%>Model: \"Shardplate\" by Krajkoa, CC BY 4.0</size>";
        protected override string DefaultRecipe => "Silver:10,BlackMetal:10,Crystal:5";

        // Custom model (models/ShardplateHelm.obj): it faces -Z, Valheim's head faces +Z.
        // Fitted to the Drake helmet's largest dimension (its long crest), so shrink it to head size;
        // the crest also centres it too far back, so move it forward to keep the visor over the face.
        // To line the eye-slit up with the eyes it is tipped chin-down 6° (negative X: the model faces
        // -Z before the 180° turn) and lowered by 11% of its fitted height (0.362), and 4% wider.
        protected override Vector3 CustomModelRotation => new Vector3(-6f, 180f, 0f);
        protected override float CustomModelScale => 0.78f;
        protected override Vector3 CustomModelStretch => new Vector3(1.04f, 1f, 1f);
        protected override Vector3 CustomModelOffset => new Vector3(0f, -0.040f, 0.08f);
        // In front of the visor (model space, before the 180° turn) so it lights the outside of the helm
        protected override Vector3 CustomModelLightPosition => new Vector3(0f, 0.05f, -0.35f);
        protected override float DefaultCustomLight => 0.8f;
    }

    internal class ShardplateBreastplate : ShardplatePiece
    {
        public override string PrefabName => "ShardplateBreastplate";
        public override string[] SourcePrefabs => new[] { "ArmorFlametalChest", "ArmorPaddedCuirass" };
        public override string DisplayName => "Shardplate Breastplate";
        public override string Description =>
            "Overlapping plates that fit together without a seam. Cracks glow blue as Stormlight " +
            "leaks out, then slowly seal themselves.\n\n<size=80%>Model: \"Shardplate\" by Krajkoa, CC BY 4.0</size>";
        protected override string DefaultRecipe => "Silver:20,BlackMetal:20,Crystal:10";
    }

    internal class ShardplateGreaves : ShardplatePiece
    {
        public override string PrefabName => "ShardplateGreaves";
        public override string[] SourcePrefabs => new[] { "ArmorFlametalLegs", "ArmorPaddedGreaves" };
        public override string DisplayName => "Shardplate Greaves";
        public override string Description =>
            "Heavy legplates that feel like nothing. A Shardbearer can leap a wall and land running." +
            "\n\n<size=80%>Model: \"Shardplate\" by Krajkoa, CC BY 4.0</size>";
        protected override string DefaultRecipe => "Silver:15,BlackMetal:15,Crystal:8";
    }
}

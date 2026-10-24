using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MysticalWeapons.Hallows
{
    /// <summary>
    /// The Cloak of Invisibility. While you sneak wearing it, monsters cannot see you
    /// (they can still hear you), and you fade to a dark, see-through shade. Recoloured linen cape.
    /// </summary>
    internal class InvisibilityCloak : MysticalArmor
    {
        private const string Name = "InvisibilityCloak";
        private static readonly int CloakHash = Name.GetStableHashCode();

        public override string PrefabName => Name;
        public override string[] SourcePrefabs => new[] { "CapeLinen", "CapeDeerHide" };
        public override string DisplayName => "The Cloak of Invisibility";
        public override string Description =>
            "Silvery, fluid cloth that flows like water. Not a cloak that merely hides - it hides " +
            "you from Death himself. Crouch beneath it and no creature can see you.";

        protected override float DefaultArmor => 2f;
        protected override float DefaultArmorPerLevel => 1f;
        protected override Color DefaultTint => new Color(0.85f, 0.88f, 0.95f); // silvery
        protected override string DefaultRecipe => "LinenThread:20,Silver:6,Crystal:4";
        protected override string DefaultStation => "piece_workbench";
        protected override int DefaultStationLevel => 4;

        private static ConfigEntry<bool> _requireSneak;
        private static ConfigEntry<bool> _fade;
        private static ConfigEntry<Color> _fadeColor;
        private SE_Stats _cloakEffect;

        public override void BindConfig(ConfigFile cfg)
        {
            base.BindConfig(cfg);
            _requireSneak = cfg.Bind(PrefabName, "RequireSneak", true,
                "Only invisible while sneaking. False = always invisible to monsters (very strong).");
            _fade = cfg.Bind(PrefabName, "FadeWhileHidden", true,
                "While the cloak hides you, you and your gear turn into a dark, see-through shade (everyone sees it).");
            _fadeColor = cfg.Bind(PrefabName, "FadeColor", new Color(0.12f, 0.22f, 0.5f, 0.45f),
                "Colour of the shade (alpha = how solid: 0 unseen, 1 solid). Restart to apply.");
        }

        protected override void Customize(ItemDrop.ItemData.SharedData shared)
        {
            _cloakEffect = DeathlyHallows.MakeEffect("SE_InvisibilityCloak", "Unseen",
                _requireSneak.Value
                    ? "While you sneak, monsters cannot see you. They can still hear you."
                    : "Monsters cannot see you. They can still hear you.", Icon);
            _cloakEffect.m_noiseModifier = -0.5f;
            shared.m_equipStatusEffect = _cloakEffect;
            shared.m_movementModifier = 0f;
            shared.m_weight = 1f;
            DeathlyHallows.JoinSet(shared, Icon);
        }

        public override void RegisterExtras(ObjectDB db) => DeathlyHallows.Register(db, _cloakEffect);

        private static readonly AccessTools.FieldRef<VisEquipment, int> ShoulderHash =
            AccessTools.FieldRefAccess<VisEquipment, int>("m_currentShoulderItemHash");

        /// <summary>
        /// Uses the synced equipment visuals and crouch animation, so this also works when the
        /// monster's AI runs on another player's machine.
        /// </summary>
        private static bool IsCloaked(Player player)
        {
            if (_requireSneak == null) return false;
            var vis = player.GetComponent<VisEquipment>();
            if (!vis || ShoulderHash(vis) != CloakHash) return false;
            return !_requireSneak.Value || player.IsCrouching();
        }

        /// <summary>Monster sight range is multiplied by the stealth factor: 0 means unseen.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.GetStealthFactor))]
        private static class StealthPatch
        {
            private static void Postfix(Player __instance, ref float __result)
            {
                if (IsCloaked(__instance)) __result = 0f;
            }
        }

        [HarmonyPatch(typeof(Player), "Awake")]
        private static class AddFadePatch
        {
            private static void Postfix(Player __instance) => __instance.gameObject.AddComponent<CloakFade>();
        }

        /// <summary>
        /// Swaps every mesh on a player (body and worn gear) for a see-through, tinted copy of its own material
        /// while the cloak hides them, and back after. On every player on every client, from the synced state <see cref="IsCloaked"/> reads.
        /// </summary>
        private class CloakFade : MonoBehaviour
        {
            /// <summary>Seconds between checks; gear put on while hidden fades at the next one.</summary>
            private const float Interval = 0.4f;
            /// <summary>Shade copies of the materials met so far, shared by all players: each is made once.</summary>
            private static readonly Dictionary<Material, Material> Shades = new Dictionary<Material, Material>();

            private Player _player;
            private float _next;
            private bool _faded;
            private readonly Dictionary<Renderer, Material[]> _original = new Dictionary<Renderer, Material[]>();
            private readonly List<Renderer> _renderers = new List<Renderer>();

            private void Awake() => _player = GetComponent<Player>();

            private void Update()
            {
                if (Time.time < _next) return;
                _next = Time.time + Interval;
                var hide = _player && _fade != null && _fade.Value && IsCloaked(_player);
                if (hide) Fade();
                else if (_faded) Restore();
            }

            private void Fade()
            {
                _faded = true;
                GetComponentsInChildren(true, _renderers);
                foreach (var r in _renderers)
                {
                    if (r is ParticleSystemRenderer || _original.ContainsKey(r)) continue;
                    var mats = r.sharedMaterials;
                    _original[r] = mats;
                    r.sharedMaterials = ModelUtil.Spectral(mats, _fadeColor.Value, Name, Shades);
                }
            }

            /// <summary>
            /// Puts each renderer's own materials back. While faded, the game recolours the body, hair and beard each frame
            /// through Renderer.materials, which makes the renderer its own copy of our shade: destroy those copies, which
            /// would otherwise leak one per fade.
            /// </summary>
            private void Restore()
            {
                _faded = false;
                foreach (var pair in _original)
                {
                    if (!pair.Key) continue;
                    foreach (var current in pair.Key.sharedMaterials)
                        if (current && System.Array.IndexOf(pair.Value, current) < 0 && !Shades.ContainsValue(current))
                            Destroy(current);
                    pair.Key.sharedMaterials = pair.Value;
                }
                _original.Clear();
            }
        }
    }
}

using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.Rendering;

namespace MysticalWeapons.Armor
{
    /// <summary>
    /// Eagle Vision: nearby creatures glow, on the wearer's own screen only: a strong silhouette through walls when
    /// hidden, a faint wash when in sight. Each glowing renderer gets one extra material slot, a flat see-through colour
    /// drawn over everything. Hidden or in sight is decided per creature by sight lines, not per pixel: against the
    /// game's depth, grass and the creature's own limbs counted as cover and the glow flickered.
    /// </summary>
    internal static class EagleVision
    {
        private const string Name = "EagleVision";
        /// <summary>Unity's built-in UI shader: it reads its depth and stencil tests from material values.</summary>
        private const string ShaderName = "UI/Default";
        /// <summary>
        /// A stencil bit, set on each pixel the glow covers, so overlapping parts (a hand in front of the body) are
        /// coloured once, not twice. Clear of deferred lighting's bits 5-7.
        /// </summary>
        private const int Mark = 16;
        private static Shader _shader;
        private static bool _lookedUp;
        private static readonly Dictionary<Color, Material> Materials = new Dictionary<Color, Material>();
        /// <summary>What blocks a creature's sight, as the game's own AI uses (grass has no collider, so isn't cover).</summary>
        private static int _viewBlockMask;
        /// <summary>Each glowing creature, its colour, and its renderers with the materials each had before.</summary>
        private static readonly Dictionary<Character, (Color colour, Dictionary<Renderer, Material[]> renderers)> Lit =
            new Dictionary<Character, (Color, Dictionary<Renderer, Material[]>)>();

        /// <summary>A flat colour over the creature, drawn through anything in front of it, once per pixel.</summary>
        private static Material GlowMaterial(Color color)
        {
            if (!_lookedUp)
            {
                _lookedUp = true;
                _shader = Shader.Find(ShaderName);
                if (_shader) Plugin.Log.LogInfo($"{Name}: glows use the '{_shader.name}' shader");
                else Plugin.Log.LogWarning($"{Name}: no '{ShaderName}' shader; creatures won't glow");
            }
            if (!_shader) return null;
            if (Materials.TryGetValue(color, out var mat) && mat) return mat;
            mat = new Material(_shader) { name = Name + "_glow", color = color, renderQueue = 3100 };
            mat.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
            mat.SetInt("_Stencil", Mark);
            mat.SetInt("_StencilComp", (int)CompareFunction.NotEqual);
            mat.SetInt("_StencilOp", (int)StencilOp.Replace);
            mat.SetInt("_StencilReadMask", Mark);
            mat.SetInt("_StencilWriteMask", Mark);
            Materials[color] = mat;
            return mat;
        }

        /// <summary>True when terrain or buildings block both the camera's view of the creature's eyes and its middle.</summary>
        private static bool IsHidden(Vector3 camera, Character c)
        {
            if (_viewBlockMask == 0)
                _viewBlockMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "viewblock", "vehicle");
            return Physics.Linecast(camera, c.GetEyePoint(), _viewBlockMask)
                && Physics.Linecast(camera, c.GetCenterPoint(), _viewBlockMask);
        }

        /// <summary>Lights <paramref name="creatures"/> in their colours and puts out everything else.</summary>
        public static void Show(Dictionary<Character, Color> creatures)
        {
            foreach (var c in new List<Character>(Lit.Keys))
                if (!c || !creatures.TryGetValue(c, out var colour) || colour != Lit[c].colour) Unlight(c);
            foreach (var pair in creatures)
                if (!Lit.ContainsKey(pair.Key)) Light(pair.Key, pair.Value);
        }

        public static void Clear() => Show(new Dictionary<Character, Color>());

        private static void Light(Character c, Color color)
        {
            var mat = GlowMaterial(color);
            var visual = c.GetVisual();
            if (!mat || !visual) return;
            var renderers = new Dictionary<Renderer, Material[]>();
            foreach (var r in visual.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                var old = r.sharedMaterials;
                var lit = new Material[old.Length + 1];
                old.CopyTo(lit, 0);
                lit[old.Length] = mat; // a slot past the last submesh draws that submesh again
                r.sharedMaterials = lit;
                renderers[r] = old;
            }
            Lit[c] = (color, renderers);
        }

        /// <summary>Gives the creature back its own materials (a despawned one needs nothing back).</summary>
        private static void Unlight(Character c)
        {
            if (!Lit.TryGetValue(c, out var lit)) return;
            Lit.Remove(c);
            foreach (var pair in lit.renderers)
                if (pair.Key) pair.Key.sharedMaterials = pair.Value;
        }

        /// <summary>
        /// The set's 3-piece bonus. While its wearer crouches, looks twice a second for creatures hostile to them in
        /// range, and ten times a second for which of those are hidden; passive animals (the deer and boar faction) and
        /// tamed creatures never glow.
        /// </summary>
        internal class Effect : StatusEffect
        {
            private const float FindInterval = 0.5f;
            private const float SightInterval = 0.1f;
            public ConfigEntry<float> Range;
            public ConfigEntry<Color> HostileColour;
            public ConfigEntry<Color> StarredColour;
            public ConfigEntry<float> InSightOpacity;
            public ConfigEntry<bool> Message;
            public ConfigEntry<float> MessageCooldown;
            private readonly List<Character> _targets = new List<Character>();
            private readonly Dictionary<Character, Color> _show = new Dictionary<Character, Color>();
            private float _nextFind, _nextSight, _nextMessage;
            private bool _crouched;

            public override void UpdateStatusEffect(float dt)
            {
                base.UpdateStatusEffect(dt);
                if (m_character != Player.m_localPlayer) return;
                if (!BrotherhoodPiece.Crouched(m_character))
                {
                    if (Lit.Count > 0) Clear();
                    _nextFind = _nextSight = 0f; // light up at once on the next crouch
                    _crouched = false;
                    return;
                }
                // Name it on screen as it comes on, but not every crouch: sneaking is crouching on and off
                if (!_crouched && Message.Value && Time.time >= _nextMessage)
                {
                    m_character.Message(MessageHud.MessageType.Center, m_name);
                    _nextMessage = Time.time + MessageCooldown.Value;
                }
                _crouched = true;
                if (Time.time >= _nextFind)
                {
                    _nextFind = Time.time + FindInterval;
                    Find();
                }
                if (Time.time < _nextSight) return;
                _nextSight = Time.time + SightInterval;

                var camera = Utils.GetMainCamera();
                if (!camera) return;
                var eye = camera.transform.position;
                _show.Clear();
                foreach (var c in _targets)
                {
                    if (!c) continue;
                    var colour = c.GetLevel() > 1 ? StarredColour.Value : HostileColour.Value;
                    if (!IsHidden(eye, c)) colour.a = InSightOpacity.Value;
                    if (colour.a > 0f) _show[c] = colour;
                }
                Show(_show);
            }

            private void Find()
            {
                _targets.Clear();
                var me = m_character.transform.position;
                var range = Range.Value * Range.Value;
                foreach (var c in Character.GetAllCharacters())
                {
                    if (!c || c.IsPlayer() || c.IsTamed() || c.GetFaction() == Character.Faction.AnimalsVeg) continue;
                    if ((c.transform.position - me).sqrMagnitude <= range && BaseAI.IsEnemy(m_character, c)) _targets.Add(c);
                }
            }

            public override void Stop()
            {
                base.Stop();
                if (m_character == Player.m_localPlayer) Clear();
            }
        }
    }
}

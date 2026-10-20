using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace MysticalWeapons.Armor
{
    /// <summary>
    /// While the Shardplate breastplate is worn, the wearer's body turns into a translucent Stormlight
    /// spirit, as if the armor were animated by it: wherever the body shows through the plates (gaps,
    /// the eye-slit, a wrist bending through a vambrace) it reads as glow rather than as skin.
    ///
    /// The real body renderer is left alone (Valheim keeps setting its skin colour and armor paint) and
    /// only stops drawing; a second renderer with the same mesh and bones draws the ghost on top.
    /// </summary>
    internal static class GhostBody
    {
        private static readonly int BreastplateHash = "ShardplateBreastplate".GetStableHashCode();

        /// <summary>How opaque the spirit is: 0 = invisible, 1 = solid.</summary>
        private const float Opacity = 0.35f;

        private static readonly AccessTools.FieldRef<VisEquipment, int> ChestHash =
            AccessTools.FieldRefAccess<VisEquipment, int>("m_currentChestItemHash");

        private static Material _material;
        private static bool _materialFailed;

        /// <summary>The ghost renderer belonging to a character's body.</summary>
        private class Link : MonoBehaviour
        {
            public SkinnedMeshRenderer Ghost;
            // The character's animator only moves the bones while the body renderer draws (culling
            // "based on renderers"), and the hidden body no longer does: keep it animating meanwhile
            public Animator Animator;
            public AnimatorCullingMode Culling;
        }

        /// <summary>
        /// Runs every frame after VisEquipment has applied equipment and the base model (which can
        /// swap the body mesh), so the ghost always matches the body it replaces.
        /// </summary>
        [HarmonyPatch(typeof(VisEquipment), "UpdateVisuals")]
        private static class UpdateVisualsPatch
        {
            private static void Postfix(VisEquipment __instance)
            {
                var body = __instance.m_bodyModel;
                if (!body) return;
                var link = __instance.GetComponent<Link>();
                var wearing = ChestHash(__instance) == BreastplateHash;
                if (!wearing)
                {
                    if (link && link.Ghost && link.Ghost.enabled)
                    {
                        link.Ghost.enabled = false;
                        body.forceRenderingOff = false;
                        if (link.Animator) link.Animator.cullingMode = link.Culling;
                        link.Animator = null;
                    }
                    return;
                }

                if (!link) link = __instance.gameObject.AddComponent<Link>();
                if (!link.Ghost)
                {
                    var mat = GetMaterial();
                    if (!mat) return;
                    link.Ghost = CreateGhost(body, mat);
                }
                if (!link.Animator)
                {
                    link.Animator = __instance.GetComponentInChildren<Animator>();
                    if (link.Animator)
                    {
                        link.Culling = link.Animator.cullingMode;
                        link.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    }
                }
                var ghost = link.Ghost;
                if (ghost.sharedMesh != body.sharedMesh)
                {
                    ghost.sharedMesh = body.sharedMesh;
                    ghost.sharedMaterials = Enumerable.Repeat(_material, Mathf.Max(1, body.sharedMesh ? body.sharedMesh.subMeshCount : 1)).ToArray();
                }
                ghost.enabled = body.enabled;
                body.forceRenderingOff = true;
            }
        }

        private static SkinnedMeshRenderer CreateGhost(SkinnedMeshRenderer body, Material mat)
        {
            var go = new GameObject("ShardplateGhostBody") { layer = body.gameObject.layer };
            go.transform.SetParent(body.transform.parent, false);
            go.transform.localPosition = body.transform.localPosition;
            go.transform.localRotation = body.transform.localRotation;
            go.transform.localScale = body.transform.localScale;
            var ghost = go.AddComponent<SkinnedMeshRenderer>();
            ghost.sharedMesh = body.sharedMesh;
            ghost.bones = body.bones;
            ghost.rootBone = body.rootBone;
            ghost.localBounds = body.localBounds;
            ghost.sharedMaterials = Enumerable.Repeat(mat, Mathf.Max(1, body.sharedMesh ? body.sharedMesh.subMeshCount : 1)).ToArray();
            ghost.shadowCastingMode = ShadowCastingMode.Off;
            ghost.receiveShadows = false;
            return ghost;
        }

        /// <summary>A flat, unlit, see-through Stormlight blue (the Shardplate's Glow colour).</summary>
        private static Material GetMaterial()
        {
            if (_material || _materialFailed) return _material;
            var shader = new[] { "Sprites/Default", "UI/Default" }.Select(Shader.Find).FirstOrDefault(s => s);
            if (!shader)
            {
                _materialFailed = true;
                Plugin.Log.LogWarning("Shardplate ghost body: no transparent shader found, the body stays as it is");
                return null;
            }
            var colour = Plugin.Items.OfType<ShardplateBreastplate>().FirstOrDefault()?.Glow ?? new Color(0.25f, 0.55f, 1f);
            colour.a = Opacity;
            _material = new Material(shader) { name = "ShardplateGhostBody", color = colour };
            if (_material.renderQueue < (int)RenderQueue.Transparent) _material.renderQueue = (int)RenderQueue.Transparent;
            Plugin.Log.LogInfo($"Shardplate ghost body: shader {shader.name}");
            return _material;
        }
    }
}

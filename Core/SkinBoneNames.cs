using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MysticalWeapons
{
    /// <summary>
    /// The bone names a custom skinned armor mesh was weighted to, in the order of its bind poses.
    /// Valheim gives an attached "attach_skin" renderer the player body's bone array; if that array's
    /// order ever differs from ours (e.g. after a game update), <see cref="AttachArmorPatch"/> rebinds by name.
    /// </summary>
    public class SkinBoneNames : MonoBehaviour
    {
        public string[] Names;
    }

    [HarmonyPatch]
    internal static class AttachArmorPatch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(VisEquipment), "AttachArmor");
        private static bool Prepare() => TargetMethod() != null;

        private static readonly HashSet<string> Warned = new HashSet<string>();

        private static void Postfix(VisEquipment __instance, List<GameObject> __result)
        {
            if (__result == null || !__instance.m_bodyModel) return;
            var body = __instance.m_bodyModel;
            foreach (var go in __result)
            {
                if (!go) continue;
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var names = smr.GetComponent<SkinBoneNames>();
                    if (!names || names.Names == null) continue;

                    var byName = new Dictionary<string, Transform>();
                    foreach (var b in body.bones) if (b) byName[b.name] = b;
                    var bones = new Transform[names.Names.Length];
                    for (var i = 0; i < bones.Length; i++)
                        if (!byName.TryGetValue(names.Names[i], out bones[i]))
                        {
                            bones[i] = body.rootBone;
                            if (Warned.Add(names.Names[i]))
                                Plugin.Log.LogWarning($"Armor bone '{names.Names[i]}' not found on the player");
                        }
                    smr.bones = bones;
                    smr.rootBone = body.rootBone;
                    // The armor sits around the body: borrow the body's bounds, padded, so it isn't culled early
                    var bounds = body.localBounds;
                    bounds.Expand(0.5f);
                    smr.localBounds = bounds;
                }
            }
        }
    }
}

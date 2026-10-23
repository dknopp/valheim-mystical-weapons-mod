using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MysticalWeapons
{
    /// <summary>How a replacement model is positioned inside the cloned item.</summary>
    internal struct ModelPlacement
    {
        /// <summary>Rotate model +Y onto the original's longest axis (weapons: grip along the handle).</summary>
        public bool AutoAlign;
        /// <summary>Scale the model to the original's largest dimension and centre it on the original.</summary>
        public bool FitToOriginal;
        public Vector3 Rotation;
        public Vector3 Offset;
        public float Scale;
        /// <summary>Clear everything the cloned item's material had (see <see cref="MysticalWeapon.CleanMaterials"/>).</summary>
        public bool CleanMaterials;
    }

    internal static class ModelUtil
    {
        /// <summary>Folder next to the plugin DLL where edited models can be dropped.</summary>
        public static string CustomModelDir =>
            Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "", "models");

        /// <summary>
        /// Loads "{prefabName}.obj" (and its textures) from <see cref="CustomModelDir"/>.
        /// Returns null if there is none or it fails to load.
        /// </summary>
        public static MeshBuilder LoadCustomModel(string prefabName, int submeshes)
        {
            var path = Path.Combine(CustomModelDir, prefabName + ".obj");
            if (!File.Exists(path)) return null;
            try
            {
                var builder = new MeshBuilder(submeshes);
                var texturePaths = ObjImport.Load(path, builder);
                builder.Textures = new Texture2D[submeshes];
                foreach (var kv in texturePaths)
                {
                    if (!File.Exists(kv.Value))
                    {
                        Plugin.Log.LogWarning($"{prefabName}: texture {kv.Value} not found");
                        continue;
                    }
                    var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = Path.GetFileName(kv.Value) };
                    if (LoadImage(tex, File.ReadAllBytes(kv.Value))) builder.Textures[kv.Key] = tex;
                }
                Plugin.Log.LogInfo($"{prefabName}: using custom model {path} ({builder.Verts.Count / 6} triangles, " +
                                   $"{texturePaths.Count} textures)");
                return builder;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"{prefabName}: could not load {path}, using the default model. {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// ImageConversion.LoadImage(Texture2D, byte[]). Called through reflection because Unity 6 also
        /// has a ReadOnlySpan overload whose type lives in Unity's mscorlib, which the compiler cannot see.
        /// </summary>
        private static bool LoadImage(Texture2D tex, byte[] data)
        {
            var method = typeof(ImageConversion).GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
            return method != null && (bool)method.Invoke(null, new object[] { tex, data });
        }

        /// <summary>
        /// Recolours a (readable) texture in place: each pixel's brightness is multiplied by <paramref name="multiply"/>
        /// (components may exceed 1 to brighten; the source colours are dropped so stray hues like red
        /// scratches take on the tint), then <paramref name="glow"/> is added - a little everywhere
        /// and strongly on bright details such as glyphs and edges - scaled by <paramref name="glowAmount"/>.
        /// </summary>
        public static void GradeTexture(Texture2D tex, Color multiply, Color glow, float glowAmount)
        {
            var px = tex.GetPixels();
            for (var i = 0; i < px.Length; i++)
            {
                var c = px[i];
                var grey = c.grayscale;
                var highlight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.7f, grey));
                var g = glow * (glowAmount * (0.15f + 0.85f * highlight));
                px[i] = new Color(
                    Mathf.Clamp01(grey * multiply.r + g.r),
                    Mathf.Clamp01(grey * multiply.g + g.g),
                    Mathf.Clamp01(grey * multiply.b + g.b),
                    c.a);
            }
            tex.SetPixels(px);
            tex.Apply(true);
        }

        /// <summary>
        /// Textured parts show their texture as-is: their base colour becomes white (borrowed
        /// textures already carry the tint) and they use their textured gloss.
        /// </summary>
        public static MaterialSpec[] WithTextures(MaterialSpec[] specs, MeshBuilder builder)
        {
            var result = (MaterialSpec[])specs.Clone();
            for (var i = 0; i < result.Length; i++)
                if (builder.Textures != null && i < builder.Textures.Length && builder.Textures[i])
                {
                    result[i].Color = Color.white;
                    result[i].Gloss = result[i].TexturedGloss;
                }
            return result;
        }

        /// <summary>Hides the cloned item's renderers and puts our model under its "attach" child.</summary>
        public static GameObject ReplaceModel(GameObject prefab, string name, MeshBuilder builder,
            MaterialSpec[] specs, ModelPlacement place)
        {
            var attach = FindDeep(prefab.transform, "attach") ?? prefab.transform;

            Material baseMat = null;
            var hasBounds = false;
            var bounds = new Bounds();
            foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                if (!baseMat) baseMat = r.sharedMaterial;
                r.enabled = false;

                // Original model bounds in attach space, used to line our grip up with its handle
                var mf = r.GetComponent<MeshFilter>();
                var m = mf ? mf.sharedMesh : (r as SkinnedMeshRenderer)?.sharedMesh;
                if (!m || !r.transform.IsChildOf(attach)) continue;
                var mb = m.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = attach.InverseTransformPoint(r.transform.TransformPoint(corner));
                    if (!hasBounds) { bounds = new Bounds(p, Vector3.zero); hasBounds = true; }
                    else bounds.Encapsulate(p);
                }
            }
            Plugin.Log.LogInfo($"{name}: original model bounds center {bounds.center}, size {bounds.size}");

            var mesh = builder.ToMesh(name);
            var materials = new Material[specs.Length];
            for (var i = 0; i < materials.Length; i++)
                materials[i] = MakeMaterial(baseMat, specs[i], $"{name}_mat{i}",
                    builder.Textures != null && i < builder.Textures.Length ? builder.Textures[i] : null,
                    place.CleanMaterials);

            if (materials.Length > 0)
            {
                var m0 = materials[0];
                Plugin.Log.LogInfo($"{name}: shader {m0.shader.name}; _Color {m0.HasProperty("_Color")}, _MainTex {m0.HasProperty("_MainTex")}, " +
                                   $"_EmissionColor {m0.HasProperty("_EmissionColor")}, _EmissionMap {m0.HasProperty("_EmissionMap")}");
            }

            var model = new GameObject(name + "_model");
            model.layer = attach.gameObject.layer;
            model.transform.SetParent(attach, false);
            model.AddComponent<MeshFilter>().sharedMesh = mesh;
            model.AddComponent<MeshRenderer>().sharedMaterials = materials;

            Fit(name, mesh.bounds, hasBounds ? bounds : (Bounds?)null, place, out var rotation, out var position, out var scale);
            model.transform.localRotation = rotation;
            model.transform.localPosition = position;
            model.transform.localScale = Vector3.one * scale;
            return model;
        }

        /// <summary>Where our model goes against the original's bounds (null = unknown), as <see cref="ModelPlacement"/> asks.</summary>
        private static void Fit(string name, Bounds ours, Bounds? original, ModelPlacement place,
            out Quaternion rotation, out Vector3 position, out float scale)
        {
            rotation = place.AutoAlign && original.HasValue ? AlignToHandle(original.Value) : Quaternion.identity;
            rotation *= Quaternion.Euler(place.Rotation);
            scale = place.Scale;
            position = place.Offset;
            if (!place.FitToOriginal || !original.HasValue) return;
            // Uniformly scale our rotated mesh to the original's largest dimension, centred on it
            var rotated = RotatedBounds(ours, rotation);
            scale *= original.Value.size.MaxComponent() / Mathf.Max(rotated.size.MaxComponent(), 1e-4f);
            position += original.Value.center - rotated.center * scale;
            Plugin.Log.LogInfo($"{name}: fitted custom model at {position}, scale {scale:0.###}");
        }

        /// <summary>
        /// Dual-wield items (Skoll and Hati) show both blades in hand as one skinned mesh under
        /// "attach_skin", bound to the player's hand bones. Replaces that mesh with two copies of
        /// <paramref name="model"/> (from <see cref="ReplaceModel"/>), one per hand, the left one mirrored,
        /// each held with its grip across the fist (see <see cref="GripFrame"/>).
        /// Returns false if the cloned item has no such mesh.
        /// </summary>
        public static bool ReplaceSkinnedModel(GameObject prefab, string name, GameObject model, ModelPlacement place)
        {
            var skin = prefab.transform.Find("attach_skin");
            var renderer = skin ? skin.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.sharedMesh) : null;
            // The vanilla mesh can't be read, so find the hands by name in the skeleton it is bound to
            var bones = renderer ? renderer.bones : new Transform[0];
            var hands = new[]
            {
                System.Array.FindIndex(bones, t => t && t.name.EndsWith("RightHand")),
                System.Array.FindIndex(bones, t => t && t.name.EndsWith("LeftHand"))
            };
            if (hands.Any(h => h < 0))
            {
                Plugin.Log.LogWarning($"{name}: no hand bones under attach_skin, the model only shows on the ground");
                return false;
            }

            // Bind poses map mesh space to bone space, so each blade is built in its bone's space
            var bindposes = new Matrix4x4[bones.Length];
            var ours = model.GetComponent<MeshFilter>().sharedMesh;
            var srcVerts = ours.vertices;
            var srcNormals = ours.normals;
            var srcUvs = ours.uv;
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var weights = new List<BoneWeight>();
            var tris = new List<int>[ours.subMeshCount];
            for (var s = 0; s < tris.Length; s++) tris[s] = new List<int>();
            for (var k = 0; k < hands.Length; k++)
            {
                var bone = hands[k];
                var hand = bones[bone];
                var mirror = k == 1;
                if (!GripFrame(hand, out var grip, out var gripAt))
                    Plugin.Log.LogWarning($"{name}: no finger bones under {hand.name}, the blade follows the bone's own axes");
                Fit(name, ours.bounds, null, place, out var rotation, out var position, out var scale);
                // The grip frame is the same for both hands but turned the other way round, so mirroring
                // across it (flat side, Z) makes the left blade the mirror image of the right
                var flip = mirror ? Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) : Matrix4x4.identity;
                var m = Matrix4x4.TRS(gripAt, grip, Vector3.one) * flip * Matrix4x4.TRS(position, rotation, Vector3.one * scale);

                var first = verts.Count;
                for (var i = 0; i < srcVerts.Length; i++)
                {
                    verts.Add(m.MultiplyPoint3x4(srcVerts[i]));
                    normals.Add(m.MultiplyVector(srcNormals[i]).normalized);
                    uvs.Add(i < srcUvs.Length ? srcUvs[i] : Vector2.zero);
                    weights.Add(new BoneWeight { boneIndex0 = bone, weight0 = 1f });
                }
                for (var s = 0; s < tris.Length; s++)
                {
                    var t = ours.GetTriangles(s);
                    for (var i = 0; i < t.Length; i += 3)
                    {
                        // Mirroring turns triangles inside out unless their winding is reversed too
                        tris[s].Add(first + t[i]);
                        tris[s].Add(first + t[mirror ? i + 2 : i + 1]);
                        tris[s].Add(first + t[mirror ? i + 1 : i + 2]);
                    }
                }
                // The player's bones carry the rig's import scale (a blade built in metres showed up
                // giant), so undo the bone's scale to keep our blade at its real size
                var boneScale = hand.lossyScale;
                bindposes[bone] = Matrix4x4.Scale(new Vector3(1f / boneScale.x, 1f / boneScale.y, 1f / boneScale.z));
                Plugin.Log.LogInfo($"{name}: blade {k + 1} on bone {bone} '{hand.name}' (bone scale {boneScale}), " +
                                   $"grip at {gripAt}{(mirror ? ", mirrored" : "")}");
            }

            var mesh = new Mesh { name = name + "_skinned" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = tris.Length;
            for (var s = 0; s < tris.Length; s++) mesh.SetTriangles(tris[s], s);
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = bindposes;
            mesh.RecalculateBounds();

            renderer.sharedMesh = mesh;
            renderer.sharedMaterials = model.GetComponent<MeshRenderer>().sharedMaterials;
            renderer.enabled = true;
            return true;
        }

        /// <summary>
        /// Holstered, the game shows an item's "attach_back" child (if it has one) on the back joint for its
        /// type, instead of its dropped "attach" model. Adds one with two copies of <paramref name="model"/>
        /// crossed in an X, the second mirrored, laid flat on the back with the tips curving up and out.
        /// <see cref="BackFramePatch"/> places them on the player.
        /// </summary>
        public static void AddCrossedBackModel(GameObject prefab, string name, GameObject model, ModelPlacement place)
        {
            var back = new GameObject("attach_back");
            back.layer = model.layer;
            back.transform.SetParent(prefab.transform, false);
            // The game takes the first child named attach_back or attach, and shows it once instantiated
            back.transform.SetSiblingIndex(0);
            back.SetActive(false);

            var frame = new GameObject(name + BackFrame).transform;
            frame.gameObject.layer = model.layer;
            frame.SetParent(back.transform, false);
            var mesh = model.GetComponent<MeshFilter>().sharedMesh;
            var materials = model.GetComponent<MeshRenderer>().sharedMaterials;
            var s = Mathf.Sin(BackAngle * Mathf.Deg2Rad);
            var c = Mathf.Cos(BackAngle * Mathf.Deg2Rad);
            for (var k = 0; k < 2; k++)
            {
                // Model +Y (tip to tip) leans off vertical, +Z (the way the tips curve) points up and out, +X (the
                // grip side) faces the back so the medallion faces out. The second is the first mirrored left to right.
                var side = k == 0 ? -1f : 1f;
                var glaive = new GameObject($"{name}_back{k + 1}");
                glaive.layer = model.layer;
                glaive.transform.SetParent(frame, false);
                glaive.transform.localRotation = Quaternion.LookRotation(new Vector3(-side * c, s, 0f), new Vector3(side * s, c, 0f));
                glaive.transform.localPosition = new Vector3(0f, 0f, -BackGap * k);
                glaive.transform.localScale = new Vector3(k == 0 ? -place.Scale : place.Scale, place.Scale, place.Scale);
                glaive.AddComponent<MeshFilter>().sharedMesh = mesh;
                glaive.AddComponent<MeshRenderer>().sharedMaterials = materials;
            }
        }

        /// <summary>Name suffix of the body-space frame under "attach_back" (see <see cref="BackFramePatch"/>).</summary>
        private const string BackFrame = "_backframe";

        /// <summary>
        /// The back joints sit at the shoulder or hip with their own axes and the rig's import scale, which
        /// differ between the player prefab and a live player. So once our holstered model is attached, move it
        /// onto the chest bone (it follows the torso, not the hips) and lay its frame out in the player's body
        /// space (X right, Y up, Z forward), one unit to a metre. Body space is taken from the chest bone's rest
        /// pose (the body mesh's bind pose), not its pose right now, which is mid-animation while holstering.
        /// </summary>
        [HarmonyLib.HarmonyPatch(typeof(VisEquipment), "AttachBackItem")]
        private static class BackFramePatch
        {
            private static void Postfix(VisEquipment __instance, GameObject __result)
            {
                if (!__result || !__result.name.StartsWith("attach_back")) return;
                foreach (Transform frame in __result.transform)
                {
                    if (!frame.name.EndsWith(BackFrame)) continue;
                    var body = __instance.transform;
                    var skin = __instance.m_bodyModel;
                    var bones = skin ? skin.bones : new Transform[0];
                    var bindposes = skin && skin.sharedMesh ? skin.sharedMesh.bindposes : new Matrix4x4[0];
                    var i = System.Array.FindIndex(bones, t => t && t.name == "Spine2");
                    var place = Matrix4x4.TRS(BackPosition, Quaternion.Euler(BackTilt, 0f, 0f), Vector3.one);
                    if (i < 0 || i >= bindposes.Length)
                    {
                        // No rest pose to go by: lay it out against the body as it stands now
                        frame.SetPositionAndRotation(body.TransformPoint(BackPosition), body.rotation * Quaternion.Euler(BackTilt, 0f, 0f));
                        var parent = frame.parent.lossyScale;
                        frame.localScale = new Vector3(1f / parent.x, 1f / parent.y, 1f / parent.z);
                        continue;
                    }

                    // The chest bone at rest in body space; the body mesh doesn't move against the player
                    var chestAtRest = body.worldToLocalMatrix * skin.transform.localToWorldMatrix * bindposes[i].inverse;
                    var m = chestAtRest.inverse * place;
                    __result.transform.SetParent(bones[i], false);
                    __result.transform.localPosition = Vector3.zero;
                    __result.transform.localRotation = Quaternion.identity;
                    __result.transform.localScale = Vector3.one;
                    frame.localPosition = m.GetColumn(3);
                    frame.localRotation = m.rotation;
                    frame.localScale = m.lossyScale;
                }
            }
        }

        /// <summary>Degrees each holstered blade leans off vertical.</summary>
        private const float BackAngle = 30f;
        /// <summary>Where the holstered blades cross in body space (metres): between the shoulder blades.</summary>
        private static readonly Vector3 BackPosition = new Vector3(0f, 1.35f, -0.22f);
        /// <summary>Degrees the holstered X tips forward at the top, so its lower half clears the cape.</summary>
        private const float BackTilt = 8f;
        /// <summary>Space between the two crossed blades, so they don't flicker through each other.</summary>
        private const float BackGap = 0.03f;

        /// <summary>
        /// How a weapon sits in a closed hand, in the hand bone's space and in metres: model +Y runs
        /// across the fist from the little finger to the index finger (like a held knife's grip), +X
        /// points along the fingers, and the grip passes just inside the knuckles.
        /// False (and the bone's own axes) if the hand has no finger bones.
        /// </summary>
        private static bool GripFrame(Transform hand, out Quaternion rotation, out Vector3 position)
        {
            rotation = Quaternion.identity;
            position = Vector3.zero;
            var index = FindDeep(hand, hand.name + "Index1");
            var middle = FindDeep(hand, hand.name + "Middle1");
            var pinky = FindDeep(hand, hand.name + "Pinky1");
            if (!index || !middle || !pinky) return false;
            Vector3 Local(Transform t) => Vector3.Scale(hand.InverseTransformPoint(t.position), hand.lossyScale);
            var up = (Local(index) - Local(pinky)).normalized;
            var knuckles = Local(middle);
            var forward = Vector3.ProjectOnPlane(knuckles, up).normalized;
            rotation = Quaternion.LookRotation(Vector3.Cross(forward, up), up);
            position = knuckles * GripDepth;
            return true;
        }

        /// <summary>How far from the wrist to the knuckles the grip sits in a closed hand.</summary>
        private const float GripDepth = 0.9f;

        private static Bounds RotatedBounds(Bounds b, Quaternion rotation)
        {
            var result = new Bounds(rotation * b.center, Vector3.zero);
            for (var i = 0; i < 8; i++)
                result.Encapsulate(rotation * (b.center + Vector3.Scale(b.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            return result;
        }

        private static float MaxComponent(this Vector3 v) => Mathf.Max(v.x, Mathf.Max(v.y, v.z));

        /// <summary>
        /// Model +Y along the original's longest axis (pointing the way most of the weapon extends
        /// from the hand), model +X along the second longest axis.
        /// </summary>
        private static Quaternion AlignToHandle(Bounds b)
        {
            var size = b.size;
            var axes = new List<int> { 0, 1, 2 };
            axes.Sort((a, c) => size[c].CompareTo(size[a]));
            var up = Vector3.zero;
            up[axes[0]] = b.center[axes[0]] < 0f ? -1f : 1f;
            var right = Vector3.zero;
            right[axes[1]] = 1f;
            return Quaternion.LookRotation(Vector3.Cross(right, up), up);
        }

        private static Material MakeMaterial(Material baseMat, MaterialSpec spec, string name, Texture2D texture, bool clean)
        {
            var mat = baseMat ? new Material(baseMat) : new Material(Shader.Find("Standard"));
            mat.name = name;
            if (!clean)
            {
                foreach (var prop in new[] { "_MainTex", "_BumpMap", "_MetallicGlossMap", "_EmissionMap" })
                    if (mat.HasProperty(prop)) mat.SetTexture(prop, null);
                if (texture && mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", texture);
                mat.color = spec.Color;
                if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", spec.Gloss);
                if (spec.Emission.maxColorComponent > 0f && mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", spec.Emission);
                    if (texture && mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", texture);
                }
                return mat;
            }

            // Clear every texture the cloned item had, not only the main ones: e.g. the ice staff's
            // detail map would otherwise show its frost through our texture
            var cleared = new List<string>();
            foreach (var prop in mat.GetTexturePropertyNames())
            {
                if (!mat.GetTexture(prop)) continue;
                mat.SetTexture(prop, null);
                cleared.Add(prop);
            }
            foreach (var keyword in new[] { "_DETAIL_MULX2", "_PARALLAXMAP", "_NORMALMAP", "_METALLICGLOSSMAP" })
                mat.DisableKeyword(keyword);
            if (cleared.Count > 0) Plugin.Log.LogInfo($"{name}: cleared textures {string.Join(", ", cleared)}");
            // Colour settings carry over too: e.g. black metal's metal colour tints our steel green
            var colours = new List<string>();
            foreach (var prop in mat.GetPropertyNames(MaterialPropertyType.Vector))
                colours.Add($"{prop} {mat.GetVector(prop)}");
            if (colours.Count > 0) Plugin.Log.LogInfo($"{name}: inherited colours {string.Join(", ", colours)}");
            if (mat.HasProperty("_MetalColor")) mat.SetColor("_MetalColor", Color.white);
            if (texture && mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", texture);
            mat.color = spec.Color;
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", spec.Gloss);
            if (spec.Emission.maxColorComponent > 0f && mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", spec.Emission);
                // Textured: glow follows the texture, so bright details (glyphs, edges) glow most
                if (texture && mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", texture);
            }
            else if (mat.HasProperty("_EmissionColor"))
            {
                // No glow wanted: switch off any glow the cloned item had (the ice staff glows icy blue)
                var old = mat.GetColor("_EmissionColor");
                if (old.maxColorComponent > 0f) Plugin.Log.LogInfo($"{name}: removed the original glow {old}");
                mat.DisableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
            }
            return mat;
        }

        public static Light AddGlow(GameObject model, Vector3 localPos, Color color, float range, float intensity)
        {
            var go = new GameObject("glow");
            go.transform.SetParent(model.transform, false);
            go.transform.localPosition = localPos;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            return light;
        }

        /// <summary>Vanilla creatures whose see-through body material makes things look like spirits, first found wins.</summary>
        private static readonly string[] GhostSources = { "Ghost", "Wraith" };
        private static Material _ghostBase;

        /// <summary>
        /// A tinted copy of a vanilla ghost's body material, or null if no ghost is loaded. Looks prefabs up in
        /// <paramref name="scene"/>, so call it once a ZNetScene exists.
        /// </summary>
        public static Material GhostMaterial(ZNetScene scene, Color tint, string user)
        {
            if (!_ghostBase && scene)
            {
                foreach (var name in GhostSources)
                {
                    var source = scene.GetPrefab(name);
                    var body = source
                        ? source.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.sharedMaterial)
                        : null;
                    if (!body) continue;
                    _ghostBase = body.sharedMaterial;
                    Plugin.Log.LogInfo($"{user}: ghost material '{_ghostBase.name}' (shader '{_ghostBase.shader.name}') from '{name}'");
                    break;
                }
                if (!_ghostBase) Plugin.Log.LogWarning($"{user}: no ghost material found ({string.Join(", ", GhostSources)})");
            }
            if (!_ghostBase) return null;

            var mat = new Material(_ghostBase) { name = user + "_ghost" };
            foreach (var property in new[] { "_Color", "_TintColor", "_BaseColor" })
                if (mat.HasProperty(property)) mat.SetColor(property, tint);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", tint * 0.5f);
            return mat;
        }

        /// <summary>Gives every mesh under <paramref name="root"/> the same material (particles are left alone).</summary>
        public static void SetAllMaterials(GameObject root, Material mat)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                var mats = r.sharedMaterials;
                for (var i = 0; i < mats.Length; i++) mats[i] = mat;
                r.sharedMaterials = mats;
            }
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform child in t)
            {
                var found = FindDeep(child, name);
                if (found) return found;
            }
            return null;
        }

        // ---------------------------------------------------------------- icon

        /// <summary>Rasterises the model, seen from the front, into an inventory icon.</summary>
        public static Sprite RenderIcon(MeshBuilder b, MaterialSpec[] mats, float angleDeg, int size = 128,
            Quaternion? view = null)
        {
            // view turns the model to face the camera (looking down -Z), angleDeg then spins the icon
            var rot = Quaternion.Euler(0f, 0f, angleDeg) * (view ?? Quaternion.identity);
            var pts = new Vector3[b.Verts.Count];
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (var i = 0; i < pts.Length; i++)
            {
                pts[i] = rot * b.Verts[i];
                min = Vector2.Min(min, pts[i]);
                max = Vector2.Max(max, pts[i]);
            }

            const float margin = 4f;
            var extent = max - min;
            var scale = (size - 2 * margin) / Mathf.Max(extent.x, extent.y);
            var offset = new Vector2(size, size) * 0.5f - (min + extent * 0.5f) * scale;

            var color = new Color[size * size];
            var depth = new float[size * size];
            for (var i = 0; i < depth.Length; i++) depth[i] = float.MinValue;
            var light = new Vector3(-0.4f, 0.6f, 0.7f).normalized;

            for (var sub = 0; sub < b.Tris.Length; sub++)
            {
                var tris = b.Tris[sub];
                var texture = b.Textures != null && sub < b.Textures.Length ? b.Textures[sub] : null;
                for (var t = 0; t < tris.Count; t += 3)
                {
                    var n = rot * (b.Normals[tris[t]] + b.Normals[tris[t + 1]] + b.Normals[tris[t + 2]]).normalized;
                    if (n.z <= 0.001f) continue; // facing away from the camera

                    var shade = 0.55f + 0.45f * Mathf.Max(0f, Vector3.Dot(n, light));
                    var c = mats[sub].Color * shade + mats[sub].Emission * 0.6f;
                    c.a = 1f;

                    Vector3 p0 = pts[tris[t]], p1 = pts[tris[t + 1]], p2 = pts[tris[t + 2]];
                    Vector2 a = (Vector2)p0 * scale + offset, bb = (Vector2)p1 * scale + offset, cc = (Vector2)p2 * scale + offset;
                    var textured = texture ? new TexturedTri
                    {
                        Texture = texture, Ua = b.Uvs[tris[t]], Ub = b.Uvs[tris[t + 1]], Uc = b.Uvs[tris[t + 2]], Tint = c
                    } : (TexturedTri?)null;
                    FillTriangle(a, bb, cc, p0.z, p1.z, p2.z, c, color, depth, size, textured);
                }
            }

            // Dark outline around the silhouette
            var outlined = (Color[])color.Clone();
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                if (color[y * size + x].a > 0f) continue;
                for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= size || ny >= size || color[ny * size + nx].a <= 0f) continue;
                    outlined[y * size + x] = new Color(0.04f, 0.04f, 0.05f, 0.9f);
                }
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.SetPixels(outlined);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        /// <summary>
        /// Copies a vanilla icon out of its (unreadable) atlas via the GPU and recolours it:
        /// greys are multiplied by <paramref name="tint"/>, warm glowing pixels become <paramref name="glow"/>.
        /// Returns null if the copy fails.
        /// </summary>
        public static Sprite RecolorIcon(Sprite source, Color tint, Color glow)
        {
            try
            {
                var src = source.texture;
                var rect = source.textureRect;
                var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
                var previous = RenderTexture.active;
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                var tex = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGBA32, false);
                tex.ReadPixels(rect, 0, 0);
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);

                var pixels = tex.GetPixels();
                for (var i = 0; i < pixels.Length; i++)
                {
                    var c = pixels[i];
                    var max = c.maxColorComponent;
                    var sat = max - Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                    var grey = c.grayscale * 1.25f;
                    var plate = new Color(grey * tint.r, grey * tint.g, grey * tint.b);
                    var glowing = glow.maxColorComponent > 0f && sat > 0.3f && c.r > c.b;
                    var result = glowing ? Color.Lerp(plate, glow * (0.6f + max), Mathf.Clamp01(sat * 1.5f)) : plate;
                    result.a = c.a;
                    pixels[i] = result;
                }
                tex.SetPixels(pixels);
                tex.Apply();
                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"Could not recolour icon {source.name}: {e.Message}");
                return null;
            }
        }

        private struct TexturedTri
        {
            public Texture2D Texture;
            public Vector2 Ua, Ub, Uc;
            public Color Tint;
        }

        private static void FillTriangle(Vector2 a, Vector2 b, Vector2 c, float za, float zb, float zc,
            Color col, Color[] color, float[] depth, int size, TexturedTri? tex = null)
        {
            var area = (b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y);
            if (Mathf.Abs(area) < 1e-6f) return;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))));
            int x1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))));
            int y1 = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))));

            for (var y = y0; y <= y1; y++)
            for (var x = x0; x <= x1; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                var w0 = ((b.x - p.x) * (c.y - p.y) - (c.x - p.x) * (b.y - p.y)) / area;
                var w1 = ((c.x - p.x) * (a.y - p.y) - (a.x - p.x) * (c.y - p.y)) / area;
                var w2 = 1f - w0 - w1;
                if (w0 < -0.02f || w1 < -0.02f || w2 < -0.02f) continue;
                var z = w0 * za + w1 * zb + w2 * zc;
                var i = y * size + x;
                if (z <= depth[i]) continue;
                depth[i] = z;
                if (tex is TexturedTri t)
                {
                    var uv = t.Ua * w0 + t.Ub * w1 + t.Uc * w2;
                    var texel = t.Texture.GetPixelBilinear(uv.x, uv.y) * t.Tint;
                    texel.a = 1f;
                    color[i] = texel;
                }
                else color[i] = col;
            }
        }
    }
}

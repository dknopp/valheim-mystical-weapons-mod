using System.Globalization;
using System.IO;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace MysticalWeapons
{
    /// <summary>
    /// Development helpers, inactive for players. Creating an empty file named "dump-skeleton" next to
    /// the plugin DLL makes the mod write player_skeleton.json there on startup: the player's bones in
    /// their rest pose, in the space of the player's "Visual" object. Body armor is fitted to it offline
    /// (tools/blender), then attached per bone.
    /// </summary>
    internal static class DevTools
    {
        private static string PluginDir => Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "";

        /// <summary>"mw_reload": re-read the config and custom models from disk and re-equip, no restart.</summary>
        [HarmonyPatch(typeof(Terminal), "InitTerminal")]
        private static class ReloadCommandPatch
        {
            private static void Postfix()
            {
                new Terminal.ConsoleCommand("mw_reload",
                    "Mystical Weapons: reload custom models, textures and colour settings from disk",
                    args =>
                    {
                        var n = ReloadModels();
                        args.Context?.AddString($"Mystical Weapons: reloaded {n} custom models. Re-equipped your armor.");
                    }, isCheat: true);
            }
        }

        private static readonly AccessTools.FieldRef<VisEquipment, int> ChestHash =
            AccessTools.FieldRefAccess<VisEquipment, int>("m_currentChestItemHash");
        private static readonly AccessTools.FieldRef<VisEquipment, int> LegHash =
            AccessTools.FieldRefAccess<VisEquipment, int>("m_currentLegItemHash");
        private static readonly AccessTools.FieldRef<VisEquipment, int> HelmetHash =
            AccessTools.FieldRefAccess<VisEquipment, int>("m_currentHelmetItemHash");

        internal static int ReloadModels()
        {
            Plugin.Instance?.Config.Reload();
            ModelUtil.ClearTextureCache();
            Armor.BodyMask.Clear();
            var n = 0;
            foreach (var item in Plugin.Items)
            {
                if (!item.Prefab || !(item is MysticalArmor)) continue;
                try { item.ReloadCustomModel(); n++; }
                catch (System.Exception e) { Plugin.Log.LogError($"Reloading {item.PrefabName} failed: {e}"); }
            }
            // Forget what the local player is wearing so VisEquipment re-attaches it from the new prefabs
            var vis = Player.m_localPlayer ? Player.m_localPlayer.GetComponent<VisEquipment>() : null;
            if (vis)
            {
                ChestHash(vis) = 0; LegHash(vis) = 0; HelmetHash(vis) = 0;
            }
            return n;
        }

        /// <summary>Main menu: the character preview uses the same Player prefab.</summary>
        [HarmonyPatch(typeof(FejdStartup), "Start")]
        private static class DumpSkeletonAtMenuPatch
        {
            private static void Postfix(FejdStartup __instance) => TryDump(__instance.m_playerPrefab);
        }

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class DumpSkeletonInWorldPatch
        {
            private static void Postfix(ZNetScene __instance) =>
                TryDump(__instance.m_prefabs.Find(go => go && go.name == "Player"));
        }

        private static void TryDump(GameObject player)
        {
            if (!File.Exists(Path.Combine(PluginDir, "dump-skeleton"))) return;
            try { DumpPlayerSkeleton(player); }
            catch (System.Exception e) { Plugin.Log.LogError($"Skeleton dump failed: {e}"); }
        }

        private static void DumpPlayerSkeleton(GameObject player)
        {
            var vis = player ? player.GetComponent<VisEquipment>() : null;
            if (!vis || !vis.m_bodyModel) { Plugin.Log.LogWarning("Skeleton dump: no Player prefab"); return; }

            var visual = player.transform.Find("Visual") ?? player.transform;
            var toVisual = visual.worldToLocalMatrix;
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            string V(Vector3 v) => string.Format(inv, "[{0:0.#####},{1:0.#####},{2:0.#####}]", v.x, v.y, v.z);
            string Q(Quaternion q) => string.Format(inv, "[{0:0.#####},{1:0.#####},{2:0.#####},{3:0.#####}]", q.x, q.y, q.z, q.w);

            sb.Append("{\n  \"space\": \"Player/Visual local (Unity, left-handed, Y up, +Z forward)\",\n");
            sb.Append($"  \"rootBone\": \"{vis.m_bodyModel.rootBone?.name}\",\n  \"bones\": [\n");
            var bones = vis.m_bodyModel.bones;
            for (var i = 0; i < bones.Length; i++)
            {
                var b = bones[i];
                if (!b) continue;
                var m = toVisual * b.localToWorldMatrix;
                sb.Append($"    {{\"name\": \"{b.name}\", \"parent\": \"{b.parent?.name}\", ")
                  .Append($"\"position\": {V(m.GetColumn(3))}, \"rotation\": {Q(m.rotation)}, \"scale\": {V(m.lossyScale)}}}")
                  .Append(i < bones.Length - 1 ? ",\n" : "\n");
            }
            sb.Append("  ],\n");

            // The body surface, if the mesh is readable, so armor can be fitted around it
            var mesh = vis.m_bodyModel.sharedMesh;
            sb.Append($"  \"bodyMeshReadable\": {(mesh && mesh.isReadable ? "true" : "false")},\n  \"body\": [");
            if (mesh && mesh.isReadable)
            {
                var toVisualBody = toVisual * vis.m_bodyModel.transform.localToWorldMatrix;
                var verts = mesh.vertices;
                for (var i = 0; i < verts.Length; i += 4)
                    sb.Append(i > 0 ? "," : "").Append(V(toVisualBody.MultiplyPoint3x4(verts[i])));
            }
            sb.Append("]\n}\n");

            var path = Path.Combine(PluginDir, "player_skeleton.json");
            File.WriteAllText(path, sb.ToString());
            Plugin.Log.LogInfo($"Wrote player skeleton ({bones.Length} bones) to {path}");
        }
    }
}

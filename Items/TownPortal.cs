using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MysticalWeapons.Items
{
    /// <summary>
    /// The portal a <see cref="PortalScroll"/> opens: vanilla's portal swirl, stretched into a tall oval and
    /// recoloured D2 blue, with no frame. Each end keeps its destination in its ZDO, so it works when the other end
    /// isn't loaded, and sends whoever steps in there with the game's own teleport. Not a <c>TeleportWorld</c>: tag
    /// pairing could link strangers' portals to ours and it shows a "Set tag" prompt.
    /// </summary>
    internal static class TownPortal
    {
        public const string PrefabName = "PortalScroll_Portal";
        private const string SourcePrefab = "portal_wood";
        private const string SourceEffect = "_target_found_red";

        // ZDO keys
        internal const string TargetKey = "mw_portal_target";
        internal const string TargetRotKey = "mw_portal_target_rot";
        internal const string PairKey = "mw_portal_pair";
        internal const string OwnerKey = "mw_portal_owner";
        internal const string SnapKey = "mw_portal_snap";

        /// <summary>How far in front of the reader the field end opens.</summary>
        private const float FieldDistance = 2f;

        private static GameObject _prefab;

        public static GameObject GetOrCreatePrefab(Func<string, GameObject> findVanilla)
        {
            if (_prefab) return _prefab;
            var source = findVanilla(SourcePrefab);
            var effect = source ? source.transform.Find(SourceEffect) : null;
            if (!effect)
            {
                Plugin.Log.LogError($"{PrefabName}: vanilla '{SourcePrefab}/{SourceEffect}' not found");
                return null;
            }

            var root = MysticalItem.NewPrefab(PrefabName);
            var nview = root.AddComponent<ZNetView>();
            nview.m_persistent = true;
            root.AddComponent<PortalEnd>();

            var swirl = Object.Instantiate(effect.gameObject, root.transform);
            swirl.name = "swirl";
            swirl.SetActive(true);
            // EffectFade fades the swirl in only once a vanilla portal is linked; ours always shows
            Object.DestroyImmediate(swirl.GetComponent<EffectFade>());
            swirl.transform.localPosition = Vector3.up * 0.5f;
            swirl.transform.localRotation = Quaternion.identity;
            swirl.transform.localScale = new Vector3(0.75f, 1.2f, 0.75f); // every ring is a circle: stretch to an oval
            Recolour(swirl);

            _prefab = root;
            Plugin.Log.LogInfo($"{PrefabName}: swirl from {SourcePrefab}/{SourceEffect}, recoloured blue");
            return _prefab;
        }

        /// <summary>D2's white-hot blue to blue-violet, from the look test (references/town-portal.md).</summary>
        private static void Recolour(GameObject go)
        {
            var blue = new Color(0.30f, 0.42f, 1f);
            var violet = new Color(0.55f, 0.45f, 1f);
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy; // so the oval stretch reaches the particles
                var dark = main.startColor.colorMax.maxColorComponent < 0.1f; // Black_suck makes the void: keep it
                if (!dark) main.startColor = new ParticleSystem.MinMaxGradient(blue, violet);
                var col = ps.colorOverLifetime;
                if (col.enabled && !dark)
                {
                    var white = new Gradient();
                    white.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                        (col.color.gradient ?? new Gradient()).alphaKeys);
                    col.color = white;
                }
                var trails = ps.trails;
                if (trails.enabled)
                {
                    trails.colorOverLifetime = new ParticleSystem.MinMaxGradient(new Color(0.6f, 0.7f, 1f));
                    trails.inheritParticleColor = true;
                }
                // The flame ring's colour is not in its material: the gradient-mapped shader reads Custom2 (HDR)
                var cd = ps.customData;
                if (cd.enabled && cd.GetMode(ParticleSystemCustomData.Custom2) == ParticleSystemCustomDataMode.Color)
                {
                    var g = new Gradient();
                    g.SetKeys(new[] { new GradientColorKey(new Color(1.6f, 1.8f, 3.0f), 0f), new GradientColorKey(new Color(0.35f, 0.4f, 1.8f), 1f) },
                        new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
                    cd.SetColor(ParticleSystemCustomData.Custom2, new ParticleSystem.MinMaxGradient(g));
                    main.startSpeed = 0.35f; // tighter rim
                }
            }
            foreach (var light in go.GetComponentsInChildren<Light>(true))
            {
                light.color = new Color(0.35f, 0.45f, 1f);
                light.intensity = 3f;
            }
        }

        /// <summary>Opens a field end in front of the player and a home end at their respawn point.</summary>
        public static void OpenPair(Player player)
        {
            if (!_prefab || !ZNetScene.instance || ZDOMan.instance == null) return;
            if (!HomePoint(out var home))
            {
                Plugin.Log.LogWarning($"{PrefabName}: no bed and no world start found; no portal opened");
                return;
            }

            var facing = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up);
            facing = facing.sqrMagnitude > 0.01f ? facing.normalized : Vector3.forward;
            var fieldPos = player.transform.position + facing * FieldDistance;
            if (ZoneSystem.instance.FindFloor(fieldPos + Vector3.up * 2f, out var floor)) fieldPos.y = floor;
            // Faces the reader, so people coming from home step out towards where they stood
            var fieldRot = Quaternion.LookRotation(-facing);
            var homeRot = Quaternion.LookRotation(facing);

            var field = Object.Instantiate(_prefab, fieldPos, fieldRot).GetComponent<ZNetView>().GetZDO();

            // The home end is only a ZDO for now: the game builds it when someone is near, and it finds its floor then
            var hash = PrefabName.GetStableHashCode();
            var homeZdo = ZDOMan.instance.CreateNewZDO(home, hash);
            homeZdo.SetPrefab(hash);
            homeZdo.Persistent = true;
            homeZdo.SetRotation(homeRot);
            homeZdo.Set(SnapKey, true);

            Link(field, homeZdo, home, homeRot, player.GetPlayerID());
            Link(homeZdo, field, fieldPos, fieldRot, player.GetPlayerID());
            Plugin.Log.LogInfo($"{PrefabName}: opened {fieldPos} <-> {home} for {player.GetPlayerName()}");
        }

        private static void Link(ZDO end, ZDO other, Vector3 otherPos, Quaternion otherRot, long owner)
        {
            end.Set(TargetKey, otherPos);
            end.Set(TargetRotKey, otherRot);
            end.Set(PairKey, other.m_uid);
            end.Set(OwnerKey, owner);
        }

        /// <summary>Where the game would respawn the player: their bed, else the world start (as <c>Game.FindSpawnPoint</c>).</summary>
        private static bool HomePoint(out Vector3 point)
        {
            var profile = Game.instance ? Game.instance.GetPlayerProfile() : null;
            if (profile != null && profile.HaveCustomSpawnPoint())
            {
                point = profile.GetCustomSpawnPoint();
                return true;
            }
            if (ZoneSystem.instance.GetLocationIcon(Game.instance.m_StartLocation, out point))
            {
                point += Vector3.up * 2f;
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// One end of a town portal. Sends the local player through when they step into the ring: entering counts, not
    /// standing there, so someone who arrives or respawns inside it isn't sent straight back.
    /// </summary>
    internal class PortalEnd : MonoBehaviour
    {
        /// <summary>How far in front of the other end you arrive.</summary>
        private const float ExitDistance = 1.5f;
        /// <summary>Seconds after arriving before any portal fires again.</summary>
        private const float ArrivalGrace = 3f;

        private static float _graceUntil;

        private ZNetView _nview;
        private bool _seen;
        private bool _inside;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            if (_nview.GetZDO() == null) enabled = false;
        }

        private void Update()
        {
            if (!_nview.IsValid()) return;
            var zdo = _nview.GetZDO();
            if (_nview.IsOwner() && zdo.GetBool(TownPortal.SnapKey)) Snap(zdo);

            var player = Player.m_localPlayer;
            if (!player) return;
            var inside = Inside(player.transform.position);
            var entered = _seen && inside && !_inside;
            _seen = true;
            _inside = inside;
            if (player.IsTeleporting()) _graceUntil = Time.time + ArrivalGrace;
            if (!entered || Time.time < _graceUntil || player.IsDead()) return;
            Send(player, zdo);
        }

        /// <summary>Inside the ring: the oval's width, a step either side of it, from the ground to above head height.</summary>
        private bool Inside(Vector3 pos)
        {
            var local = transform.InverseTransformPoint(pos);
            return Mathf.Abs(local.x) < 0.8f && Mathf.Abs(local.z) < 0.6f && local.y > -0.5f && local.y < 2.5f;
        }

        private static void Send(Player player, ZDO zdo)
        {
            if (ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoPortals))
            {
                player.Message(MessageHud.MessageType.Center, "$msg_blocked");
                return;
            }
            // Ore and metal, unless the world allows them (Inventory.IsTeleportable reads the world setting)
            if (!player.IsTeleportable(false))
            {
                player.Message(MessageHud.MessageType.Center, "$msg_noteleport");
                return;
            }
            var target = zdo.GetVec3(TownPortal.TargetKey, Vector3.zero);
            if (target == Vector3.zero) return;
            var rot = zdo.GetQuaternion(TownPortal.TargetRotKey, Quaternion.identity);
            player.TeleportTo(target + rot * Vector3.forward * ExitDistance + Vector3.up, rot, true);
            _graceUntil = Time.time + ArrivalGrace;
        }

        /// <summary>The home end was placed at the bed's spawn point from afar; stand it on the floor once its area loads.</summary>
        private void Snap(ZDO zdo)
        {
            var pos = transform.position;
            if (!ZNetScene.instance.IsAreaReady(pos)) return;
            if (ZoneSystem.instance.FindFloor(pos + Vector3.up, out var floor))
            {
                pos.y = floor;
                transform.position = pos;
                zdo.SetPosition(pos);
            }
            zdo.Set(TownPortal.SnapKey, false);
        }
    }
}

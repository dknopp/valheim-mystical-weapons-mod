using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MysticalWeapons.Items
{
    /// <summary>
    /// The portal a <see cref="PortalScroll"/> opens: vanilla's portal swirl, stretched into a tall oval and
    /// recoloured D2 blue, with no frame. Each end keeps its destination in its ZDO, so it works when the other end
    /// isn't loaded, and sends whoever steps in there with the game's own teleport. Not a <c>TeleportWorld</c>: tag
    /// pairing could link strangers' portals to ours and it shows a "Set tag" prompt.
    /// Closing: every end records who read the scroll and when. Closing a pair (the reader coming back through the
    /// home end, or reading another scroll) tells every peer "this reader's ends opened before T are closed"; each
    /// removes the ends it holds, and an end that loads later checks the same cutoff, and the timeout, before it shows.
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
        internal const string HomeKey = "mw_portal_home";
        internal const string AtBedKey = "mw_portal_at_bed";
        internal const string OpenedKey = "mw_portal_opened";
        internal const string ReadyKey = "mw_portal_ready";

        /// <summary>Player custom data: the reader's bed as last seen loaded (its spawn point and yaw), as the bed may be far away when they read.</summary>
        private const string BedDataKey = "mw_portal_bed";
        /// <summary>Both vanilla beds put their spawn point this high above their base, facing the foot.</summary>
        private const float BedSpawnHeight = 0.44f;
        /// <summary>The home end stands at the foot of the mattress, this far towards the foot from the spawn point (both beds end 1.7 to 1.9 m out).</summary>
        private const float BedFootOffset = 1.5f;

        private const string CloseRpc = "MysticalWeapons_PortalScroll_Close";

        /// <summary>Per reader: their ends opened before this (world time, ticks) are closed. Kept for this world session only.</summary>
        private static readonly Dictionary<long, long> Cutoffs = new Dictionary<long, long>();

        /// <summary>How far in front of the reader the field end opens.</summary>
        private const float FieldDistance = 2f;
        /// <summary>A forming end still unlinked this long after it should have formed is left over from a cast that never finished.</summary>
        private const float AbandonedSeconds = 10f;

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
            // Every ring is a circle (r 1.26, centred 1.37 m up in the effect): stretch to an oval 2.5 m tall, its
            // bottom 0.63 m off the ground, and squash its depth so the particles don't bow out behind it
            swirl.transform.localPosition = Vector3.up * 0.52f;
            swirl.transform.localRotation = Quaternion.identity;
            swirl.transform.localScale = new Vector3(0.75f, 1.2f, 0.25f) * 0.82f;
            // The dark void keeps its old depth: squashed, too few of its puffs overlap and it turns see-through
            foreach (var ps in swirl.GetComponentsInChildren<ParticleSystem>(true))
                if (ps.transform != swirl.transform && IsVoid(ps))
                    ps.transform.localScale = Vector3.Scale(ps.transform.localScale, new Vector3(1f, 1f, 3f));
            Recolour(swirl);
            AddDemister(root, findVanilla);

            _prefab = root;
            Plugin.Log.LogInfo($"{PrefabName}: swirl from {SourcePrefab}/{SourceEffect}, recoloured blue");
            return _prefab;
        }

        private const string WispSource = "Demister"; // the Wisplight

        /// <summary>Copies the Wisplight ball's mist-clearing part (its Demister and force field), so the Mistlands mist parts round each end.</summary>
        private static void AddDemister(GameObject root, Func<string, GameObject> findVanilla)
        {
            var radius = PortalScroll.MistClearRadius.Value;
            if (radius <= 0f) return;
            var wisplight = findVanilla(WispSource);
            var se = wisplight ? wisplight.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_equipStatusEffect as SE_Demister : null;
            // The Demister finds its force field on waking; on the prefab, look on the same object
            var demister = se && se.m_ballPrefab ? se.m_ballPrefab.GetComponentInChildren<Demister>(true) : null;
            if (!demister || !demister.GetComponent<ParticleSystemForceField>())
            {
                Plugin.Log.LogWarning($"{PrefabName}: no '{WispSource}' demister to copy; portals won't clear mist");
                return;
            }
            // That object holds only the Demister and its force field: the ball's look and motion stay behind
            var copy = Object.Instantiate(demister.gameObject, root.transform);
            copy.name = "demister";
            copy.transform.localPosition = Vector3.up;
            copy.transform.localRotation = Quaternion.identity;
            var field = copy.GetComponent<ParticleSystemForceField>();
            var wispRange = field.endRange;
            field.endRange = radius;
            Plugin.Log.LogInfo($"{PrefabName}: clears mist like '{wisplight.name}' ({demister.name}, its radius {wispRange:0.#} m, ours {radius:0.#} m)");
        }

        /// <summary>D2's white-hot blue to blue-violet, from the look test (references/town-portal.md).</summary>
        /// <summary>Black_suck, the dark particles that make the portal's void.</summary>
        private static bool IsVoid(ParticleSystem ps) => ps.main.startColor.colorMax.maxColorComponent < 0.1f;

        private static void Recolour(GameObject go)
        {
            var blue = new Color(0.30f, 0.42f, 1f);
            var violet = new Color(0.55f, 0.45f, 1f);
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy; // so the oval stretch reaches the particles
                var dark = IsVoid(ps); // keep the void dark
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

        /// <summary>
        /// Starts the field end growing in front of where the player stood (<paramref name="from"/>, facing
        /// <paramref name="facing"/>), over <paramref name="formSeconds"/> on every peer. Until <see cref="FinishOpen"/> it
        /// has no destination and no reader, so it leads nowhere and closes nothing. Null if it can't open.
        /// </summary>
        public static ZDO BeginOpen(Vector3 from, Vector3 facing, float formSeconds)
        {
            if (!_prefab || !ZNetScene.instance || ZDOMan.instance == null) return null;
            facing = facing.sqrMagnitude > 0.01f ? facing.normalized : Vector3.forward;
            var fieldPos = from + facing * FieldDistance;
            if (ZoneSystem.instance.FindFloor(fieldPos + Vector3.up * 2f, out var floor)) fieldPos.y = floor;
            // Faces the reader, so people coming from home step out towards where they stood
            var field = Object.Instantiate(_prefab, fieldPos, Quaternion.LookRotation(-facing)).GetComponent<ZNetView>().GetZDO();
            var now = ZNet.instance.GetTime().Ticks;
            field.Set(OpenedKey, now);
            field.Set(ReadyKey, now + (long)(formSeconds * TimeSpan.TicksPerSecond));
            return field;
        }

        /// <summary>
        /// The cast completed: makes the home end at the player's respawn point (growing in over
        /// <paramref name="formSeconds"/>), links it with the formed <paramref name="field"/> end and closes the player's
        /// older pair. False, and the field end removed, if there's nowhere to go.
        /// </summary>
        public static bool FinishOpen(Player player, ZDO field, float formSeconds)
        {
            if (!HomePoint(out var home))
            {
                Plugin.Log.LogWarning($"{PrefabName}: no bed and no world start found; no portal opened");
                Remove(field);
                return false;
            }
            var fieldPos = field.GetPosition();
            var fieldRot = field.GetRotation();
            var homeRot = Quaternion.LookRotation(fieldRot * Vector3.back);
            // At a bed it's the same every time: at the foot of the mattress, facing out past the foot (nothing stands
            // on a bed, where in front of it a chest might)
            var atBed = BedFacing(home, out var bedRot);
            if (atBed)
            {
                homeRot = bedRot;
                home += Vector3.down * BedSpawnHeight + bedRot * Vector3.forward * BedFootOffset;
            }

            // A new scroll closes your old pair
            var owner = player.GetPlayerID();
            var opened = ZNet.instance.GetTime().Ticks;
            Close(owner, opened);

            // The home end is only a ZDO for now: the game builds it when someone is near, and it finds its floor then
            var hash = PrefabName.GetStableHashCode();
            var homeZdo = ZDOMan.instance.CreateNewZDO(home, hash);
            homeZdo.SetPrefab(hash);
            homeZdo.Persistent = true;
            homeZdo.SetRotation(homeRot);
            homeZdo.Set(SnapKey, !atBed); // at a bed its height is known; the floor there would be the mattress
            homeZdo.Set(HomeKey, true);
            homeZdo.Set(AtBedKey, atBed);

            Link(field, homeZdo, home, homeRot, owner, opened, opened); // already formed
            Link(homeZdo, field, fieldPos, fieldRot, owner, opened, opened + (long)(formSeconds * TimeSpan.TicksPerSecond));
            Plugin.Log.LogInfo($"{PrefabName}: opened {fieldPos} <-> {home}{(atBed ? " (over the bed)" : "")} for {player.GetPlayerName()}");
            return true;
        }

        private static void Link(ZDO end, ZDO other, Vector3 otherPos, Quaternion otherRot, long owner, long opened, long ready)
        {
            end.Set(TargetKey, otherPos);
            end.Set(TargetRotKey, otherRot);
            end.Set(PairKey, other.m_uid);
            end.Set(OwnerKey, owner);
            end.Set(OpenedKey, opened);
            end.Set(ReadyKey, ready);
        }

        /// <summary>Closes every end <paramref name="owner"/> opened before <paramref name="before"/>, on every peer.</summary>
        public static void Close(long owner, long before)
        {
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, CloseRpc, owner, before);
        }

        private static void RPC_Close(long sender, long owner, long before)
        {
            Cutoff(owner, before);
            var held = new List<ZDO>();
            var index = 0;
            while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(PrefabName, held, ref index)) { }
            foreach (var zdo in held)
                if (IsClosed(zdo)) Remove(zdo);
        }

        /// <summary>Only the newest pair per reader stays open: seeing one end closes anything older of theirs.</summary>
        internal static void Seen(ZDO zdo)
        {
            var owner = zdo.GetLong(OwnerKey);
            if (owner != 0) Cutoff(owner, zdo.GetLong(OpenedKey)); // still forming: not anyone's pair yet
        }

        private static void Cutoff(long owner, long before)
        {
            if (!Cutoffs.TryGetValue(owner, out var cutoff) || before > cutoff) Cutoffs[owner] = before;
        }

        /// <summary>
        /// Closed by its reader (a later cutoff), or open longer than the timeout, or a forming end whose cast never
        /// finished (the reader logged out or crashed mid-cast).
        /// </summary>
        internal static bool IsClosed(ZDO zdo)
        {
            if (zdo.GetLong(OwnerKey) == 0)
                return ZNet.instance.GetTime().Ticks - zdo.GetLong(ReadyKey) > (long)(AbandonedSeconds * TimeSpan.TicksPerSecond);
            var opened = zdo.GetLong(OpenedKey);
            if (Cutoffs.TryGetValue(zdo.GetLong(OwnerKey), out var cutoff) && opened < cutoff) return true;
            var timeout = PortalScroll.TimeoutMinutes?.Value ?? 0f;
            return timeout > 0f && ZNet.instance.GetTime().Ticks - opened > (long)(timeout * TimeSpan.TicksPerMinute);
        }

        /// <summary>Removes an end whoever owns it: claim it first, as vanilla's <c>WearNTear.Remove</c> does.</summary>
        internal static void Remove(ZDO zdo)
        {
            var view = ZNetScene.instance.FindInstance(zdo);
            if (view)
            {
                view.ClaimOwnership();
                ZNetScene.instance.Destroy(view.gameObject);
                return;
            }
            zdo.SetOwner(ZDOMan.GetSessionID());
            ZDOMan.instance.DestroyZDO(zdo);
        }

        [HarmonyPatch(typeof(Game), "Start")]
        private static class RegisterClose
        {
            private static void Postfix()
            {
                Cutoffs.Clear(); // world times differ between worlds
                ZRoutedRpc.instance.Register<long, long>(CloseRpc, RPC_Close);
            }
        }

        /// <summary>The facing of the reader's bed, if it's the bed at <paramref name="spawn"/> (their spawn point now).</summary>
        private static bool BedFacing(Vector3 spawn, out Quaternion rot)
        {
            rot = Quaternion.identity;
            var player = Player.m_localPlayer;
            if (!player || !player.m_customData.TryGetValue(BedDataKey, out var data)) return false;
            var f = data.Split(' ');
            if (f.Length != 4) return false;
            var v = new float[4];
            for (var i = 0; i < 4; i++)
                if (!float.TryParse(f[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return false;
            if ((new Vector3(v[0], v[1], v[2]) - spawn).sqrMagnitude > 0.01f) return false; // a different bed since
            rot = Quaternion.Euler(0f, v[3], 0f);
            return true;
        }

        /// <summary>Notes the reader's bed (where it is, which way it faces) whenever it's loaded or used.</summary>
        [HarmonyPatch(typeof(Bed))]
        private static class RememberBed
        {
            [HarmonyPostfix, HarmonyPatch("Awake")]
            private static void Loaded(Bed __instance) => Remember(__instance);

            [HarmonyPostfix, HarmonyPatch(nameof(Bed.Interact))]
            private static void Used(Bed __instance) => Remember(__instance);

            private static void Remember(Bed bed)
            {
                var player = Player.m_localPlayer;
                var profile = Game.instance ? Game.instance.GetPlayerProfile() : null;
                if (!player || profile == null || !profile.HaveCustomSpawnPoint() || !bed.m_spawnPoint) return;
                var spawn = bed.m_spawnPoint;
                if ((profile.GetCustomSpawnPoint() - spawn.position).sqrMagnitude > 0.01f) return;
                player.m_customData[BedDataKey] = string.Format(CultureInfo.InvariantCulture, "{0:R} {1:R} {2:R} {3:R}",
                    spawn.position.x, spawn.position.y, spawn.position.z, spawn.eulerAngles.y);
            }
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
        /// <summary>At a bed the ring reaches past the foot, so you can step in from the floor without climbing on, but not to where you lie.</summary>
        private const float BedReach = 0.9f;
        /// <summary>Seconds after arriving before any portal fires again.</summary>
        private const float ArrivalGrace = 3f;
        /// <summary>The same after getting out of bed or up from a seat, or respawning: getting up from sleep alone takes ~3 s.</summary>
        private const float GetUpGrace = 10f;

        private static float _graceUntil;
        private static Player _lastPlayer;

        private ZNetView _nview;
        private bool _seen;
        private bool _inside;

        // Growing in: the swirl's full scale, emission, light and hum, scaled up from nothing until the ready time
        private bool _formed;
        private Transform _swirl;
        private Vector3 _swirlScale;
        private ParticleSystem[] _systems;
        private float[] _rates;
        private Light[] _lights;
        private float[] _intensities;
        private AudioSource[] _hums;
        private float[] _volumes;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            if (_nview.GetZDO() == null) enabled = false;
            else TownPortal.Seen(_nview.GetZDO());
        }

        private void Update()
        {
            if (!_nview.IsValid()) return;
            var zdo = _nview.GetZDO();
            if (!_formed) Form(zdo);
            if (_nview.IsOwner())
            {
                // Closed or timed out while this area was unloaded
                if (TownPortal.IsClosed(zdo))
                {
                    TownPortal.Remove(zdo);
                    return;
                }
                if (zdo.GetBool(TownPortal.SnapKey)) Snap(zdo);
            }

            var player = Player.m_localPlayer;
            if (!player) return;
            var inside = _formed && Inside(player.transform.position, zdo.GetBool(TownPortal.AtBedKey) ? BedReach : 0.6f);
            var entered = _seen && inside && !_inside;
            _seen = true;
            _inside = inside;
            // Arriving, lying in bed or sitting, or a new body after respawning: getting up next to a portal at your bed
            // isn't stepping into it
            if (player.InBed() || player.IsAttached() || player != _lastPlayer) _graceUntil = Time.time + GetUpGrace;
            else if (player.IsTeleporting()) _graceUntil = Mathf.Max(_graceUntil, Time.time + ArrivalGrace);
            _lastPlayer = player;
            if (!entered || player.IsDead()) return;
            if (Time.time < _graceUntil)
            {
                player.Message(MessageHud.MessageType.Center, "The portal is still settling");
                return;
            }
            Send(player, zdo);
        }

        /// <summary>
        /// Grows the swirl in from a spark to full size between the open and ready times in the ZDO (world time, so every
        /// peer shows the same stage). Ends from before this existed have no ready time and show at once.
        /// </summary>
        private void Form(ZDO zdo)
        {
            if (!_swirl)
            {
                // Read on the first frame, not in Awake: the hum's own setup may set its volume in its Awake
                _swirl = transform.Find("swirl");
                if (!_swirl) { _formed = true; return; }
                _swirlScale = _swirl.localScale;
                _systems = _swirl.GetComponentsInChildren<ParticleSystem>(true);
                _rates = Array.ConvertAll(_systems, s => s.emission.rateOverTimeMultiplier);
                _lights = _swirl.GetComponentsInChildren<Light>(true);
                _intensities = Array.ConvertAll(_lights, l => l.intensity);
                _hums = _swirl.GetComponentsInChildren<AudioSource>(true);
                _volumes = Array.ConvertAll(_hums, a => a.volume);
            }

            var opened = zdo.GetLong(TownPortal.OpenedKey);
            var ready = zdo.GetLong(TownPortal.ReadyKey);
            var t = ready > opened ? Mathf.Clamp01((float)(ZNet.instance.GetTime().Ticks - opened) / (ready - opened)) : 1f;
            _formed = t >= 1f;
            var k = t * t * (3f - 2f * t); // ease in and out
            _swirl.localScale = Vector3.Lerp(_swirlScale * 0.07f, _swirlScale, k);
            for (var i = 0; i < _systems.Length; i++)
            {
                var emission = _systems[i].emission;
                emission.rateOverTimeMultiplier = _rates[i] * Mathf.Max(0.15f, k);
            }
            for (var i = 0; i < _lights.Length; i++) _lights[i].intensity = _intensities[i] * k;
            for (var i = 0; i < _hums.Length; i++) _hums[i].volume = _volumes[i] * k;
        }

        /// <summary>Inside the ring: the oval's width, <paramref name="reach"/> either side of it, from the ground to above head height.</summary>
        private bool Inside(Vector3 pos, float reach)
        {
            var local = transform.InverseTransformPoint(pos);
            return Mathf.Abs(local.x) < 0.8f && Mathf.Abs(local.z) < reach && local.y > -0.5f && local.y < 2.5f;
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
            // The reader heading back out through the home end closes the pair (D2's rule); anyone else leaves it open
            var owner = zdo.GetLong(TownPortal.OwnerKey);
            if (zdo.GetBool(TownPortal.HomeKey) && owner == player.GetPlayerID())
                TownPortal.Close(owner, zdo.GetLong(TownPortal.OpenedKey) + 1);
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

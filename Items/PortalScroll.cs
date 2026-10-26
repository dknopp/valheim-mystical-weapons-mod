using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MysticalWeapons.Items
{
    /// <summary>
    /// Diablo's Scroll of Town Portal, named Hearth Portal here: it goes to your bed, and Valheim has no towns. Reading it opens a pair of portals: one in front of you, one at your bed
    /// (the world start if you have none). Anyone can step through either end. Mechanics decisions are in
    /// references/town-portal.md.
    /// </summary>
    internal class PortalScroll : MysticalWeapon
    {
        public override string PrefabName => "PortalScroll";
        public override string[] SourcePrefabs => new[] { "MeadHealthMinor", "MeadStaminaMinor" };
        public override string DisplayName => "Scroll of Hearth Portal";
        public override string Description =>
            "Read it to open a gateway home, and another back to where you stood. Ore and metal still refuse the road.";

        protected override bool HasDamage => false;
        protected override bool CleanMaterials => true;
        protected override float ModelScale => 1.75f; // a real-size scroll vanishes on the ground; it's never held
        protected override bool AutoAlign => false;
        // Haldor sells it from the Black Forest on (see SellAtHaldor); crafting waits for the Mistlands
        protected override string DefaultRecipe => "Eitr:2,LinenThread:2,GreydwarfEye:4";
        protected override string DefaultStation => "piece_magetable";
        protected override int DefaultStationLevel => 1;

        // Submeshes of models/PortalScroll.obj (tools/blender/make_portal_scroll_texture.py)
        private const int Parchment = 0, Cord = 2; // 1 is the wax seal

        public override MaterialSpec[] Materials => new[]
        {
            new MaterialSpec(new Color(0.86f, 0.78f, 0.6f), 0.2f),  // parchment
            new MaterialSpec(new Color(0.62f, 0.1f, 0.1f), 0.6f),   // wax seal
            new MaterialSpec(new Color(0.2f, 0.3f, 0.85f), 0.3f),   // cord, Diablo II blue
        };

        /// <summary>Fallback when models/PortalScroll.obj is missing: a plain roll with the cord band.</summary>
        public override void BuildModel(MeshBuilder b)
        {
            b.Prism(Parchment, -0.105f, 0.105f, 0.017f, 12);
            b.Prism(Cord, -0.023f, 0.023f, 0.019f, 12);
        }

        // Lies on its side with the seal up: length along X, the seal (+Z in the model) facing +Y
        protected override Vector3 ModelRotation => new Vector3(-90f, -90f, 0f);

        /// <summary>
        /// The mead's colliders are two upright boxes (bottle and neck) with their base at the origin, so the
        /// scroll stood on end, half through the floor. Fits one box to the scroll instead and drops the rest.
        /// </summary>
        public override void DecorateModel(GameObject model)
        {
            var root = model.GetComponentInParent<ItemDrop>(true).transform;
            BoxCollider box = null;
            foreach (var c in root.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger) continue;
                if (!box && c is BoxCollider b) box = b;
                else UnityEngine.Object.DestroyImmediate(c);
            }
            if (!box) box = root.gameObject.AddComponent<BoxCollider>();
            if (box.transform != root)
            {
                box.transform.localPosition = Vector3.zero;
                box.transform.localRotation = Quaternion.identity;
                box.transform.localScale = Vector3.one;
            }

            var mb = model.GetComponent<MeshFilter>().sharedMesh.bounds;
            var fit = new Bounds();
            for (var i = 0; i < 8; i++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = box.transform.InverseTransformPoint(model.transform.TransformPoint(corner));
                if (i == 0) fit = new Bounds(p, Vector3.zero);
                else fit.Encapsulate(p);
            }
            box.center = fit.center;
            box.size = fit.size;
            // Floating measures the item by a collider it holds, which may have been one of those just removed
            var floating = root.GetComponent<Floating>();
            if (floating) AccessTools.Field(typeof(Floating), "m_collider")?.SetValue(floating, box);
            Plugin.Log.LogInfo($"{PrefabName}: collider fitted to the scroll, centre {fit.center}, size {fit.size}");
        }

        internal enum CastPose { Point, Kneel }

        /// <summary>The portal grows in over the last this-many seconds of the cast.</summary>
        private const float FormSeconds = 1f;
        /// <summary>Seconds from the pose starting to the point's arm reaching out; the pose starts this long before the portal forms.</summary>
        private const float PoseLead = 1.5f;
        /// <summary>Walking further than this from where you started reading cancels the cast.</summary>
        private const float MoveTolerance = 0.3f;

        private ConfigEntry<int> _stackSize;
        private ConfigEntry<float> _castSeconds;
        private ConfigEntry<CastPose> _castAnimation;
        internal static ConfigEntry<float> TimeoutMinutes;
        internal static ConfigEntry<float> MistClearRadius;
        private ConfigEntry<int> _traderPrice;
        private ConfigEntry<string> _traderKey;

        public override void BindConfig(ConfigFile cfg)
        {
            base.BindConfig(cfg);
            _traderPrice = cfg.Bind(PrefabName, "HaldorPrice", 250, "Coins Haldor asks for one scroll. 0 = he doesn't sell it.");
            _traderKey = cfg.Bind(PrefabName, "HaldorRequiredKey", "defeated_gdking",
                "World key needed before Haldor offers it (defeated_gdking = after The Elder). Empty = from the start.");
            _stackSize = cfg.Bind(PrefabName, "StackSize", 20, "How many scrolls fit in one inventory slot.");
            _castSeconds = cfg.Bind(PrefabName, "CastSeconds", 3f,
                "Seconds between reading the scroll and the portal opening; it forms over the last second. Moving or getting hit before it opens cancels it and keeps the scroll. 0 = instant.");
            _castAnimation = cfg.Bind(PrefabName, "CastAnimation", CastPose.Point, "What your character does while reading the scroll.");
            TimeoutMinutes = cfg.Bind(PrefabName, "TimeoutMinutes", 60f,
                "Minutes before an unused pair of portals closes by itself. 0 = they stay until you come back through the home end or read another scroll.");
            MistClearRadius = cfg.Bind(PrefabName, "MistClearRadius", 15f,
                "Metres of Mistlands mist each portal clears around itself (a Wisplight clears 15). 0 = none. Restart to apply.");
        }

        private static OpenEffect _effect;

        protected override void Customize(ItemDrop.ItemData.SharedData shared)
        {
            shared.m_maxStackSize = _stackSize.Value;
            shared.m_weight = 0.1f;
            shared.m_teleportable = true;

            _effect = ScriptableObject.CreateInstance<OpenEffect>();
            _effect.name = "SE_PortalScroll";
            _effect.m_name = DisplayName;
            _effect.m_tooltip = "The gateway is opening.";
            _effect.m_icon = Icon;
            _effect.m_hidden = true; // the cast shows on the action bar instead (CastBar below)
            CastBar.Action.m_progressText = "Reading " + DisplayName;
            // The effect's countdown is the cast. Instant still lasts a second, so one press can't read two scrolls
            // (a ttl of 0 would never end).
            var cast = Mathf.Max(0f, _castSeconds.Value);
            _effect.m_ttl = cast > 0f ? cast : 1f;
            _effect.FormSeconds = Mathf.Min(FormSeconds, cast);
            _effect.OpenAt = cast - _effect.FormSeconds;
            _effect.Pose = _castAnimation.Value;
            // Makes the game treat the scroll as usable; Read below takes over from there
            shared.m_consumeStatusEffect = _effect;
        }

        /// <summary>Starts reading, unless a scroll is already being read. The scroll is spent when the portal starts forming.</summary>
        private static void StartCast(Player player, Inventory inventory, ItemDrop.ItemData scroll)
        {
            var seman = player.GetSEMan();
            if (!_effect || seman.HaveStatusEffect(_effect.NameHash())) return;
            if (seman.AddStatusEffect(_effect) is OpenEffect cast)
            {
                cast.Inventory = inventory;
                cast.Scroll = scroll;
            }
        }

        [HarmonyPatch(typeof(Player), nameof(Player.ConsumeItem))]
        private static class Read
        {
            // Skips the original for the scroll: it would spend the scroll now and play the drinking animation and
            // sound (Humanoid.UseItem plays those when this returns true). Ours is spent only if the cast completes.
            private static bool Prefix(Player __instance, Inventory inventory, ItemDrop.ItemData item, ref bool __result)
            {
                if (item?.m_dropPrefab == null || item.m_dropPrefab.name != "PortalScroll") return true;
                StartCast(__instance, inventory, item);
                __result = false;
                return false;
            }
        }

        /// <summary>
        /// Shows the cast on the game's own timed-action bar (the one "Equipping..." uses), when nothing real is queued.
        /// Only the HUD reads this.
        /// </summary>
        [HarmonyPatch(typeof(Player), nameof(Player.GetActionProgress),
            new[] { typeof(string), typeof(float), typeof(Player.MinorActionData) },
            new[] { ArgumentType.Out, ArgumentType.Out, ArgumentType.Out })]
        private static class CastBar
        {
            internal static readonly Player.MinorActionData Action = new Player.MinorActionData();

            private static void Postfix(Player __instance, ref string name, ref float progress, ref Player.MinorActionData data)
            {
                if (name != null || __instance != Player.m_localPlayer || !_effect) return;
                if (!(__instance.GetSEMan().GetStatusEffect(_effect.NameHash()) is OpenEffect cast) || cast.m_ttl <= 0f) return;
                if (cast.OpenAt <= 0f && cast.FormSeconds <= 0f) return; // instant: nothing to wait for
                Action.m_duration = cast.m_ttl;
                Action.m_time = cast.GetDuration();
                data = Action;
                name = Action.m_progressText;
                progress = Mathf.Clamp01(Action.m_time / Action.m_duration);
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
        private static class Interrupt
        {
            private static void Postfix(Character __instance, HitData hit)
            {
                if (__instance != Player.m_localPlayer || !_effect || hit.GetTotalDamage() <= 0.1f) return;
                // Burning, poison and the like tick while you stand there; they aren't a hit
                switch (hit.m_hitType)
                {
                    case HitData.HitType.Burning: case HitData.HitType.Freezing: case HitData.HitType.Poisoned:
                    case HitData.HitType.Smoke: case HitData.HitType.Drowning: case HitData.HitType.Water:
                        return;
                }
                (__instance.GetSEMan().GetStatusEffect(_effect.NameHash()) as OpenEffect)?.Cancel($"hit, {hit.m_hitType}");
            }
        }

        public override void RegisterExtras(ObjectDB db)
        {
            if (_effect && !db.m_StatusEffects.Contains(_effect)) db.m_StatusEffects.Add(_effect);
        }

        public override void RegisterNetExtras(ZNetScene scene)
        {
            var portal = TownPortal.GetOrCreatePrefab(name => scene.m_prefabs.Find(go => go && go.name == name));
            if (portal && !RegisterNetPrefab(scene, portal))
                Plugin.Log.LogWarning($"{PrefabName}: another mod already registers '{portal.name}'; scrolls won't open portals");
            SellAtHaldor(scene);
        }

        private static Trader.TradeItem _tradeItem;

        /// <summary>Adds the scroll to Haldor's stock on his prefab; <see cref="HaldorStock"/> covers the Haldors that aren't made from it.</summary>
        private void SellAtHaldor(ZNetScene scene)
        {
            if (_traderPrice.Value <= 0 || !Prefab) return;
            var trader = scene.m_prefabs.Find(go => go && go.name == "Haldor")?.GetComponent<Trader>();
            if (!trader)
            {
                Plugin.Log.LogWarning($"{PrefabName}: Haldor not found; he won't sell scrolls");
                return;
            }
            // The shop reads m_tooltip.Length and the buy effects without null checks; Unity fills them on vanilla entries
            _tradeItem = new Trader.TradeItem
            {
                m_prefab = Prefab.GetComponent<ItemDrop>(), m_stack = 1, m_price = _traderPrice.Value, m_requiredGlobalKey = _traderKey.Value.Trim(),
                m_name = "", m_tooltip = "", m_buyKey = "", m_buyPlayerEffects = new EffectList()
            };
            if (!AddToStock(trader)) return;
            Plugin.Log.LogInfo($"{PrefabName}: sold by Haldor for {_traderPrice.Value} coins" +
                               (_traderKey.Value.Trim().Length > 0 ? $" once '{_traderKey.Value.Trim()}' is set" : ""));
        }

        private static bool AddToStock(Trader trader)
        {
            if (_tradeItem == null || trader.m_items.Exists(t => t.m_prefab == _tradeItem.m_prefab)) return false;
            trader.m_items.Add(_tradeItem);
            return true;
        }

        /// <summary>
        /// A freshly placed camp builds Haldor from its own copy inside the location, not from his prefab, so he
        /// wouldn't sell the scroll until his area reloads. Adds it to every Haldor as he starts.
        /// </summary>
        [HarmonyPatch(typeof(Trader), "Start")]
        private static class HaldorStock
        {
            private static void Postfix(Trader __instance)
            {
                if (Utils.GetPrefabName(__instance.gameObject) == "Haldor") AddToStock(__instance);
            }
        }

        /// <summary>
        /// The cast. Reading the scroll applies this (only on the reader's machine; the emote and the portal are what
        /// others see). The portal's spot is fixed when reading starts; it starts growing in <see cref="FormSeconds"/>
        /// before the end. Only when the cast completes is the scroll spent, the pair linked and the old pair closed, so
        /// moving or a hit at any point cancels it and keeps both; so does death or logging out.
        /// </summary>
        internal class OpenEffect : StatusEffect
        {
            public float OpenAt;
            public float FormSeconds;
            public CastPose Pose;

            // Per cast, on the copy SEMan makes
            internal Inventory Inventory;
            internal ItemDrop.ItemData Scroll;
            private Vector3 _start, _facing;
            private ZDOID _forming = ZDOID.None; // an ID, not the ZDO: the game pools and reuses removed ones
            private bool _posed, _opened, _cancelled;

            private static readonly Action<Character> StopEmote =
                AccessTools.MethodDelegate<Action<Character>>(AccessTools.Method(typeof(Character), "StopEmote"));

            public override void Setup(Character character)
            {
                base.Setup(character);
                _start = character.transform.position;
                _facing = Vector3.ProjectOnPlane(character.transform.forward, Vector3.up);
            }

            public override void UpdateStatusEffect(float dt)
            {
                base.UpdateStatusEffect(dt);
                if (_opened || _cancelled) return;
                if (Moved()) { Cancel("moved"); return; }
                // Timed so the arm reaches out as the portal starts forming
                if (!_posed && m_time >= OpenAt - PoseLead)
                {
                    _posed = true;
                    if (m_character is Player player) player.StartEmote(Pose.ToString().ToLowerInvariant());
                }
                if (_forming.IsNone() && m_time >= OpenAt) BeginForming();
                if (!_forming.IsNone() && m_time >= OpenAt + FormSeconds) Open();
            }

            public override bool IsDone() => _cancelled || base.IsDone();

            public override void Stop()
            {
                base.Stop();
                // Removed before completing (death, or anything else): the half-formed portal goes too
                if (!_opened) RemoveForming();
            }

            private void BeginForming()
            {
                if (!(m_character is Player player) || player != Player.m_localPlayer) { _cancelled = true; return; }
                var field = TownPortal.BeginOpen(_start, _facing, FormSeconds);
                if (field == null) _cancelled = true;
                else _forming = field.m_uid;
            }

            private void RemoveForming()
            {
                var field = _forming.IsNone() ? null : ZDOMan.instance?.GetZDO(_forming);
                if (field != null) TownPortal.Remove(field);
                _forming = ZDOID.None;
            }

            private bool Moved()
            {
                return m_character.GetMoveDir().sqrMagnitude > 0.01f || m_character.InDodge() ||
                       Vector3.ProjectOnPlane(m_character.transform.position - _start, Vector3.up).magnitude > MoveTolerance;
            }

            /// <summary>Ends the cast without a portal, removing it if it was forming. SEMan removes the effect next update.</summary>
            internal void Cancel(string why)
            {
                if (_opened || _cancelled) return;
                _cancelled = true;
                Plugin.Log.LogInfo($"PortalScroll: cast cancelled ({why}) after {m_time:0.0}s");
                RemoveForming();
                StopEmote(m_character);
            }

            /// <summary>The cast completed: spend the scroll and link the formed end to a new home end.</summary>
            private void Open()
            {
                // The read scroll, or another one if that stack was moved or split
                var scroll = Inventory != null && Inventory.ContainsItem(Scroll) ? Scroll : Inventory?.GetItem(Scroll?.m_shared.m_name);
                if (scroll == null)
                {
                    Cancel("no scroll left to spend");
                    return;
                }
                var field = ZDOMan.instance.GetZDO(_forming);
                if (field == null)
                {
                    Cancel("the forming portal is gone");
                    return;
                }
                _opened = true;
                if (TownPortal.FinishOpen((Player)m_character, field, FormSeconds)) Inventory.RemoveOneItem(scroll);
            }
        }
    }
}

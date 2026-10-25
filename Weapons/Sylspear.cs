using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MysticalWeapons.Weapons
{
    /// <summary>
    /// Kaladin Stormblessed's Shardspear (The Stormlight Archive): Syl in spear form.
    /// Glows with Stormlight and grants Windrunner lashings while held. Sheathed, Syl leaves the
    /// spear shape and follows the wielder as a spren of light (the Wisplight's ball, recoloured).
    /// </summary>
    internal class Sylspear : MysticalWeapon
    {
        private const string Name = "Sylspear";

        public override string PrefabName => Name;
        // Carapace spear first: same spear animations, and the Mistlands peer it is compared with
        public override string[] SourcePrefabs => new[] { "SpearCarapace", "SpearWolfFang", "SpearElderbark", "SpearBronze" };
        public override string VanillaPeer => "SpearCarapace";
        public override string DisplayName => "Sylspear";
        public override string Description =>
            "A silver Shardspear that hums with Stormlight - the honorspren Syl, bonded to a " +
            "Windrunner. Whoever wields it feels the winds lift them. Put it away and she rides " +
            "the air beside you as a ribbon of light. Life before death.";

        // Mistlands, beside the Carapace spear (115 pierce)
        protected override string DefaultDamage => "pierce:110,lightning:30,chop:100,pickaxe:80";
        protected override string DefaultDamagePerLevel => "pierce:6,lightning:3";
        protected override string DefaultRecipe => "Silver:10,Eitr:10,Wisp:3"; // wisps, the nearest thing to spren
        protected override string DefaultStation => "blackforge";
        protected override int DefaultStationLevel => 2;

        private ConfigEntry<float> _fallSpeed;
        private ConfigEntry<float> _jump;
        private static ConfigEntry<bool> _spren;
        private ConfigEntry<bool> _sprenClearsMist;
        private ConfigEntry<Color> _sprenColor;

        public override void BindConfig(ConfigFile cfg)
        {
            base.BindConfig(cfg);
            _fallSpeed = cfg.Bind(PrefabName, "MaxFallSpeed", 3f,
                "Windrunner: fastest you fall with the spear held or sheathed, in m/s (vanilla falling is far faster; the Feather cape is 5).");
            _jump = cfg.Bind(PrefabName, "JumpBoost", 0.3f, "Windrunner: extra jump height with the spear held or sheathed (0.3 = 30% higher).");
            _fallDamage = cfg.Bind(PrefabName, "FallDamage", 0f,
                "Windrunner: share of fall damage you still take with the spear held or sheathed (0 = none, 1 = all). Restart to apply.");
            _spren = cfg.Bind(PrefabName, "SprenWhenSheathed", true,
                "Sheathed (R), the spear disappears from your back and Syl follows you as a glowing spren. Restart to apply.");
            _sprenClearsMist = cfg.Bind(PrefabName, "SprenClearsMist", true,
                "The spren clears the Mistlands mist around you like a Wisplight: the Sylspear doubles as one. Restart to apply.");
            _sprenColor = cfg.Bind(PrefabName, "SprenColor", new Color(0.55f, 0.8f, 1f), "Colour of the spren's light. Restart to apply.");
        }

        private const int Shaft = 0, Head = 1, Accent = 2;

        public override MaterialSpec[] Materials => new[]
        {
            new MaterialSpec(new Color(0.82f, 0.88f, 0.95f), 0.8f, new Color(0.08f, 0.16f, 0.26f)), // shaft
            new MaterialSpec(new Color(0.92f, 0.96f, 1f), 0.9f, new Color(0.35f, 0.6f, 0.9f)),      // head
            new MaterialSpec(new Color(0.68f, 0.78f, 0.9f), 0.8f, new Color(0.18f, 0.32f, 0.5f)),   // accents
        };

        private ConfigEntry<float> _fallDamage;
        private SE_Stats _windrunner;
        /// <summary>
        /// Windrunner while the spear is sheathed: sheathing unequips it, which ends the equip effect. With the spren on
        /// it is hidden behind Syl's icon, so sheathed you have one effect, Syl: Windrunner's perks and the wisp.
        /// </summary>
        private static SE_SheathedWindrunner _sheathedWindrunner;

        protected override void Customize(ItemDrop.ItemData.SharedData shared)
        {
            shared.m_useDurability = false; // a spren does not wear out
            shared.m_weight = 0f;           // summoned from Syl, not carried
            shared.m_toolTier = 4;          // like a Shardblade: fells any tree and breaks any rock
            shared.m_attackForce = 40f;

            _windrunner = MakeWindrunner<SE_Stats>("SE_Windrunner");
            shared.m_equipStatusEffect = _windrunner;
            _sheathedWindrunner = MakeWindrunner<SE_SheathedWindrunner>("SE_WindrunnerSheathed");
            if (_sprenEffect)
            {
                _sheathedWindrunner.m_hidden = true; // the HUD lists Syl alone
                _sprenEffect.m_tooltip = "Syl rides the wind beside you, lighting the way. Her Stormlight still lashes you: " +
                                         "you jump higher, fall gently and land unhurt, and your stamina returns faster.";
            }
        }

        private T MakeWindrunner<T>(string id) where T : SE_Stats
        {
            var se = ScriptableObject.CreateInstance<T>();
            se.name = id;
            se.m_name = "Windrunner";
            se.m_tooltip = "Stormlight lashes you to the sky: you jump higher, fall gently and land unhurt, " +
                           "and your stamina returns faster.";
            se.m_icon = Icon;
            se.m_maxMaxFallSpeed = _fallSpeed.Value;
            se.m_jumpModifier = new Vector3(0f, _jump.Value, 0f);
            se.m_fallDamageModifier = Mathf.Clamp01(_fallDamage.Value) - 1f; // added to 1: -1 takes it all away
            se.m_staminaRegenMultiplier = 1.5f;
            se.m_runStaminaDrainModifier = -0.25f;
            return se;
        }

        public override void RegisterExtras(ObjectDB db)
        {
            foreach (var se in new StatusEffect[] { _windrunner, _sheathedWindrunner, _sprenEffect })
                if (se && !db.m_StatusEffects.Contains(se)) db.m_StatusEffects.Add(se);
        }

        public override void BuildModel(MeshBuilder b)
        {
            // Hand at the origin, shaft runs from y=-1.0 to the head at y=1.0+
            b.Prism(Shaft, -1.0f, 1.0f, 0.018f);
            b.Blade(Accent,                                   // butt spike
                new MeshBuilder.Station(-1.0f, 0.02f, 0.02f),
                new MeshBuilder.Station(-1.12f, 0f, 0.002f));
            b.Prism(Accent, 0.97f, 1.05f, 0.026f);            // collar
            b.Prism(Accent, -0.32f, -0.28f, 0.022f);          // grip rings
            b.Prism(Accent, 0.28f, 0.32f, 0.022f);

            b.Blade(Head,                                     // leaf-shaped spearhead
                new MeshBuilder.Station(1.04f, 0.015f, 0.012f),
                new MeshBuilder.Station(1.14f, 0.055f, 0.015f),
                new MeshBuilder.Station(1.30f, 0.045f, 0.012f),
                new MeshBuilder.Station(1.52f, 0f, 0.003f));

            // Swept-back wings at the base of the head, like wind-swept spren ribbons
            b.Slab(Accent, new[] { new Vector2(0.02f, 1.03f), new Vector2(0.13f, 0.95f), new Vector2(0.035f, 1.11f) }, 0.008f);
            b.Slab(Accent, new[] { new Vector2(-0.02f, 1.03f), new Vector2(-0.035f, 1.11f), new Vector2(-0.13f, 0.95f) }, 0.008f);
        }

        public override void DecorateModel(GameObject model)
        {
            ModelUtil.AddGlow(model, new Vector3(0f, 1.25f, 0f), new Color(0.55f, 0.8f, 1f), 3f, 1.5f);
        }

        // ------------------------------------------------------------- Syl as a spren

        private const string WispSource = "Demister"; // the Wisplight
        private const string SprenName = "SylSpren";
        private static SE_Spren _sprenEffect;
        private static readonly string[] LiveFields =
            { "m_nameHash", "m_character", "m_time", "m_startEffectInstances", "m_msgTimer", "m_isNew", "m_ballInstance", "m_ballVel" };
        private GameObject _sprenBall;

        protected override void Setup(GameObject prefab, ItemDrop.ItemData.SharedData shared)
        {
            base.Setup(prefab, shared);
            if (_spren.Value) SetupSpren(prefab);
        }

        /// <summary>
        /// Copies the Wisplight's status effect and the ball it spawns, recoloured, as the spren. An empty
        /// "attach_back" child keeps the holstered spear off the player's back (the game shows that child
        /// instead of the model), for every player, since back items are synced.
        /// </summary>
        private void SetupSpren(GameObject prefab)
        {
            var wisplight = FindVanilla(WispSource);
            var source = wisplight ? wisplight.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_equipStatusEffect as SE_Demister : null;
            if (!source || !source.m_ballPrefab)
            {
                Plugin.Log.LogWarning($"{PrefabName}: no '{WispSource}' wisp to copy; the spear stays on your back when sheathed");
                return;
            }

            _sprenBall = CloneVanilla(source.m_ballPrefab, SprenName);
            var color = _sprenColor.Value;
            foreach (var light in _sprenBall.GetComponentsInChildren<Light>(true)) light.color = color;
            foreach (var ps in _sprenBall.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.startColor = color;
            }
            if (!_sprenClearsMist.Value)
                foreach (var field in _sprenBall.GetComponentsInChildren<ParticleSystemForceField>(true))
                    Object.DestroyImmediate(field);

            // Our subclass of the Wisplight's effect, with every setting copied from it (ball motion, sounds...)
            // but not its live state, nor its cached name hash, which would make the game take it for the Wisplight
            _sprenEffect = ScriptableObject.CreateInstance<SE_Spren>();
            for (var t = typeof(SE_Demister); t != typeof(ScriptableObject); t = t.BaseType)
                foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    if (System.Array.IndexOf(LiveFields, field.Name) < 0) field.SetValue(_sprenEffect, field.GetValue(source));
            _sprenEffect.name = "SE_SylSpren";
            _sprenEffect.m_name = "Syl";
            _sprenEffect.m_icon = Icon;
            // Keep the Wisplight's sound, not its "You release the wisp" message
            _sprenEffect.m_startMessage = "";
            _sprenEffect.m_stopMessage = "";
            _sprenEffect.m_ballPrefab = _sprenBall;
            _sprenEffect.m_ttl = 0f;

            var back = new GameObject("attach_back");
            back.transform.SetParent(prefab.transform, false);
            back.transform.SetSiblingIndex(0); // the game takes the first child named attach_back or attach
            back.SetActive(false);
            Plugin.Log.LogInfo($"{PrefabName}: spren copied from '{wisplight.name}' (ball '{source.m_ballPrefab.name}', " +
                               $"clears mist: {_sprenClearsMist.Value})");
        }

        public override void RegisterNetExtras(ZNetScene scene)
        {
            if (_sprenBall && !RegisterNetPrefab(scene, _sprenBall))
                Plugin.Log.LogWarning($"{PrefabName}: another mod already adds a prefab named '{SprenName}'");
        }

        private static readonly AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> HiddenRightItem =
            AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_hiddenRightItem");

        private static bool Sheathed(Character character)
        {
            var hidden = character is Humanoid h ? HiddenRightItem(h) : null;
            return hidden?.m_dropPrefab != null && hidden.m_dropPrefab.name == Name;
        }

        /// <summary>The Wisplight's follow-the-wielder ball, which ends itself once the spear is drawn or gone.</summary>
        private class SE_Spren : SE_Demister
        {
            public override void UpdateStatusEffect(float dt)
            {
                base.UpdateStatusEffect(dt);
                if (Sheathed(m_character)) return;
                m_ttl = 0.01f; // done: the status effect manager removes it, and the ball with it
                m_time = 1f;
            }
        }

        /// <summary>Windrunner that stays while the spear is sheathed and ends itself once it is drawn or gone.</summary>
        private class SE_SheathedWindrunner : SE_Stats
        {
            public override void UpdateStatusEffect(float dt)
            {
                base.UpdateStatusEffect(dt);
                if (Sheathed(m_character)) return;
                m_ttl = 0.01f; // done; drawing the spear gives the equip Windrunner back
                m_time = 1f;
            }
        }

        /// <summary>
        /// Sheathing unequips the spear, which ends its Windrunner: give it back for as long as the spear stays
        /// sheathed, and call the spren. Runs on the owner's client.
        /// </summary>
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.HideHandItems))]
        private static class SheathePatch
        {
            private static void Postfix(Humanoid __instance)
            {
                if (__instance != Player.m_localPlayer || !Sheathed(__instance)) return;
                var seman = __instance.GetSEMan();
                foreach (var se in new StatusEffect[] { _sheathedWindrunner, _sprenEffect })
                    if (se && !seman.HaveStatusEffect(se.NameHash())) seman.AddStatusEffect(se, true);
            }
        }
    }
}

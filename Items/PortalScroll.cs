using BepInEx.Configuration;
using UnityEngine;

namespace MysticalWeapons.Items
{
    /// <summary>
    /// Scroll of Town Portal (Diablo II). Reading it opens a pair of portals: one in front of you, one at your bed
    /// (the world start if you have none). Anyone can step through either end. Mechanics decisions are in
    /// references/town-portal.md.
    /// </summary>
    internal class PortalScroll : MysticalWeapon
    {
        public override string PrefabName => "PortalScroll";
        public override string[] SourcePrefabs => new[] { "MeadHealthMinor", "MeadStaminaMinor" };
        public override string DisplayName => "Scroll of Town Portal";
        public override string Description =>
            "Read it to open a gateway home, and another back to where you stood. Ore and metal still refuse the road.";

        protected override bool HasDamage => false;
        protected override bool AutoAlign => false;
        // Placeholder until chunk (d): Haldor sells it, then the Galdr table
        protected override string DefaultRecipe => "LeatherScraps:2,GreydwarfEye:2";
        protected override string DefaultStation => "piece_workbench";
        protected override int DefaultStationLevel => 1;

        private const int Parchment = 0, Rod = 1, Ribbon = 2;

        public override MaterialSpec[] Materials => new[]
        {
            new MaterialSpec(new Color(0.86f, 0.78f, 0.6f), 0.2f),  // parchment
            new MaterialSpec(new Color(0.3f, 0.19f, 0.1f), 0.4f),   // wooden rods
            new MaterialSpec(new Color(0.2f, 0.3f, 0.85f), 0.5f),   // ribbon
        };

        public override void BuildModel(MeshBuilder b)
        {
            // A rolled scroll along Y: parchment, rod ends poking out, a ribbon round the middle
            b.Prism(Parchment, -0.1f, 0.1f, 0.025f, 12);
            b.Prism(Rod, -0.125f, -0.1f, 0.012f);
            b.Prism(Rod, 0.1f, 0.125f, 0.012f);
            b.Prism(Ribbon, -0.012f, 0.012f, 0.027f, 12);
        }

        private ConfigEntry<int> _stackSize;

        public override void BindConfig(ConfigFile cfg)
        {
            base.BindConfig(cfg);
            _stackSize = cfg.Bind(PrefabName, "StackSize", 20, "How many scrolls fit in one inventory slot.");
        }

        private OpenEffect _effect;

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
            // Short: it only keeps a second scroll from being read straight after the first
            _effect.m_ttl = 3f;
            shared.m_consumeStatusEffect = _effect;
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
        }

        /// <summary>Reading the scroll (the game's mead path) applies this; adding it opens the pair.</summary>
        internal class OpenEffect : StatusEffect
        {
            public override void Setup(Character character)
            {
                base.Setup(character);
                if (character is Player player && player == Player.m_localPlayer) TownPortal.OpenPair(player);
            }
        }
    }
}

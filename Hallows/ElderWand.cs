using BepInEx.Configuration;
using UnityEngine;

namespace MysticalWeapons.Hallows
{
    /// <summary>
    /// The Elder Wand, the Deathstick. Built on a Mistlands staff so it casts spell bolts,
    /// but by default it runs on stamina instead of eitr so anyone can wield it.
    /// </summary>
    internal class ElderWand : MysticalWeapon
    {
        public override string PrefabName => "ElderWand";
        public override string[] SourcePrefabs => new[] { "StaffIceShards", "StaffFireball" };
        public override string DisplayName => "The Elder Wand";
        public override string Description =>
            "Fifteen inches of elder wood, knotted like a string of beads. The Deathstick, the " +
            "Wand of Destiny - unbeatable, they say, in the hand of its true master.";

        protected override string DefaultDamage => "pierce:16,lightning:22";
        protected override string DefaultDamagePerLevel => "pierce:2,lightning:3";
        protected override string DefaultRecipe => "ElderBark:10,Thunderstone:2,Crystal:6";
        protected override string DefaultStation => "piece_workbench";
        protected override int DefaultStationLevel => 4;

        private ConfigEntry<float> _eitrCost;
        private ConfigEntry<float> _staminaCost;

        public override void BindConfig(ConfigFile cfg)
        {
            base.BindConfig(cfg);
            _eitrCost = cfg.Bind(PrefabName, "EitrCost", 0f, "Eitr used per spell (vanilla staffs use eitr).");
            _staminaCost = cfg.Bind(PrefabName, "StaminaCost", 4f, "Stamina used per spell.");
        }

        private const int Wood = 0, Knot = 1;

        public override MaterialSpec[] Materials => new[]
        {
            new MaterialSpec(new Color(0.62f, 0.56f, 0.46f), 0.3f), // pale elder wood
            new MaterialSpec(new Color(0.4f, 0.34f, 0.27f), 0.25f), // darker knots
        };

        private SE_Stats _wandEffect;

        protected override void Customize(ItemDrop.ItemData.SharedData shared)
        {
            foreach (var attack in new[] { shared.m_attack, shared.m_secondaryAttack })
            {
                if (attack == null) continue;
                attack.m_attackEitr = _eitrCost.Value;
                attack.m_attackStamina = _staminaCost.Value;
            }
            shared.m_useDurability = false; // the wand of destiny does not wear out
            shared.m_weight = 0.5f;

            _wandEffect = DeathlyHallows.MakeEffect("SE_ElderWand", "The Elder Wand",
                "The Deathstick answers to you.", Icon);
            DeathlyHallows.JoinSet(shared, Icon);
        }

        public override void RegisterExtras(ObjectDB db) => DeathlyHallows.Register(db, _wandEffect);

        private static float ShaftRadius(float y) => Mathf.Lerp(0.0095f, 0.0045f, Mathf.InverseLerp(0.04f, 0.33f, y));

        public override void BuildModel(MeshBuilder b)
        {
            // Hand at the origin: knobbly handle below, tapering shaft above
            b.Taper(Wood, -0.10f, 0.04f, 0.012f, 0.011f, 7);
            foreach (var y in new[] { -0.095f, -0.06f, -0.025f, 0.035f })
                b.Taper(Knot, y - 0.007f, y + 0.007f, 0.0155f, 0.0155f, 7);
            b.Taper(Knot, -0.11f, -0.10f, 0.009f, 0.012f, 7); // rounded butt

            b.Taper(Wood, 0.04f, 0.33f, ShaftRadius(0.04f), ShaftRadius(0.33f), 7);
            foreach (var y in new[] { 0.09f, 0.15f, 0.21f, 0.27f })
            {
                var r = ShaftRadius(y) + 0.003f;
                b.Taper(Knot, y - 0.006f, y, r - 0.002f, r, 7);
                b.Taper(Knot, y, y + 0.006f, r, r - 0.002f, 7);
            }
            b.Taper(Wood, 0.33f, 0.345f, ShaftRadius(0.33f), 0.001f, 7); // tip
        }
    }
}

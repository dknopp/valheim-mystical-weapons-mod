using UnityEngine;

namespace MysticalWeapons.Weapons
{
    /// <summary>
    /// Kaladin Stormblessed's Shardspear (The Stormlight Archive): Syl in spear form.
    /// Glows with Stormlight and grants Windrunner lashings while held.
    /// </summary>
    internal class Sylspear : MysticalWeapon
    {
        public override string PrefabName => "Sylspear";
        public override string[] SourcePrefabs => new[] { "SpearWolfFang", "SpearElderbark", "SpearBronze" };
        public override string DisplayName => "Sylspear";
        public override string Description =>
            "A silver Shardspear that hums with Stormlight - the honorspren Syl, bonded to a " +
            "Windrunner. Whoever wields it feels the winds lift them. Life before death.";

        protected override string DefaultDamage => "pierce:70,lightning:25";
        protected override string DefaultDamagePerLevel => "pierce:6,lightning:3";
        protected override string DefaultRecipe => "Silver:12,FineWood:6,Crystal:6";
        protected override int DefaultStationLevel => 3;

        private const int Shaft = 0, Head = 1, Accent = 2;

        public override MaterialSpec[] Materials => new[]
        {
            new MaterialSpec(new Color(0.82f, 0.88f, 0.95f), 0.8f, new Color(0.08f, 0.16f, 0.26f)), // shaft
            new MaterialSpec(new Color(0.92f, 0.96f, 1f), 0.9f, new Color(0.35f, 0.6f, 0.9f)),      // head
            new MaterialSpec(new Color(0.68f, 0.78f, 0.9f), 0.8f, new Color(0.18f, 0.32f, 0.5f)),   // accents
        };

        private SE_Stats _windrunner;

        protected override void Customize(ItemDrop.ItemData.SharedData shared)
        {
            shared.m_useDurability = false; // a spren does not wear out
            shared.m_attackForce = 40f;

            _windrunner = ScriptableObject.CreateInstance<SE_Stats>();
            _windrunner.name = "SE_Windrunner";
            _windrunner.m_name = "Windrunner";
            _windrunner.m_tooltip = "Stormlight lashes you to the sky: you fall gently, " +
                                    "and your stamina returns faster.";
            _windrunner.m_icon = Icon;
            _windrunner.m_maxMaxFallSpeed = 5f;
            _windrunner.m_staminaRegenMultiplier = 1.5f;
            _windrunner.m_runStaminaDrainModifier = -0.25f;
            shared.m_equipStatusEffect = _windrunner;
        }

        public override void RegisterExtras(ObjectDB db)
        {
            if (_windrunner && !db.m_StatusEffects.Contains(_windrunner)) db.m_StatusEffects.Add(_windrunner);
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
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace MysticalWeapons.Armor
{
    /// <summary>
    /// Shardplate (The Stormlight Archive): seamless slate-grey plate powered by Stormlight.
    /// Built on Flametal (Ashlands) heavy plate, recoloured, with its fiery seams turned
    /// Stormlight blue. Wearing all three pieces grants the Shardplate set bonus.
    /// </summary>
    internal abstract class ShardplatePiece : MysticalArmor
    {
        private const string SetName = "Shardplate";
        private static SE_Stats _setEffect;

        protected override string DefaultStation => "forge";
        protected override int DefaultStationLevel => 5;
        protected override float DefaultArmor => 40f;
        protected override Color DefaultTint => new Color(0.62f, 0.68f, 0.78f);  // slate with a blue sheen
        protected override Color DefaultGlow => new Color(0.25f, 0.55f, 1f);     // Stormlight

        protected override void Customize(ItemDrop.ItemData.SharedData shared)
        {
            shared.m_movementModifier = 0f;  // Stormlight carries the weight
            shared.m_weight = 10f;
            shared.m_maxDurability = 3000f;  // cracks, but regrows
            shared.m_equipStatusEffect = null;
            shared.m_damageModifiers = new List<HitData.DamageModPair>();

            shared.m_setName = SetName;
            shared.m_setSize = 3;
            shared.m_setStatusEffect = GetSetEffect();
        }

        private SE_Stats GetSetEffect()
        {
            if (_setEffect) return _setEffect;

            _setEffect = ScriptableObject.CreateInstance<SE_Stats>();
            _setEffect.name = "SE_Shardplate";
            _setEffect.m_name = "Shardplate";
            _setEffect.m_tooltip = "Stormlight fills the plate: you are strong enough to carry a boulder " +
                                   "and blows glance off you.";
            _setEffect.m_icon = Icon;
            _setEffect.m_addMaxCarryWeight = 150f;
            _setEffect.m_staminaRegenMultiplier = 1.25f;
            _setEffect.m_mods = new List<HitData.DamageModPair>
            {
                new HitData.DamageModPair { m_type = HitData.DamageType.Blunt, m_modifier = HitData.DamageModifier.Resistant },
                new HitData.DamageModPair { m_type = HitData.DamageType.Slash, m_modifier = HitData.DamageModifier.Resistant },
                new HitData.DamageModPair { m_type = HitData.DamageType.Pierce, m_modifier = HitData.DamageModifier.Resistant },
            };
            return _setEffect;
        }

        public override void RegisterExtras(ObjectDB db)
        {
            if (_setEffect && !db.m_StatusEffects.Contains(_setEffect)) db.m_StatusEffects.Add(_setEffect);
        }
    }

    internal class ShardplateHelm : ShardplatePiece
    {
        public override string PrefabName => "ShardplateHelm";
        // Drakescale helm: its swept-back crest reads more like a Shardhelm than the Flametal helmet
        public override string[] SourcePrefabs => new[] { "HelmetDrake", "HelmetFlametal", "HelmetPadded" };
        public override string DisplayName => "Shardplate Helm";
        public override string Description =>
            "A seamless helm with a narrow eye-slit. When the visor locks, the world sharpens " +
            "and Stormlight hums in your ears.\n\n<size=80%>Model: \"Shardplate\" by Krajkoa, CC BY 4.0</size>";
        protected override string DefaultRecipe => "Silver:10,BlackMetal:10,Crystal:5";

        // Custom model (models/ShardplateHelm.obj): it faces -Z, Valheim's head faces +Z.
        // Fitted to the Drake helmet, whose swept-back crest centres it too far back: nudge it forward.
        protected override Vector3 CustomModelRotation => new Vector3(0f, 180f, 0f);
        protected override Vector3 CustomModelOffset => new Vector3(0f, 0f, 0.03f);
        // In front of the visor (model space, before the 180° turn) so it lights the outside of the helm
        protected override Vector3 CustomModelLightPosition => new Vector3(0f, 0.05f, -0.35f);
        // Krajkoa's plate is painted near-black; lift it to Stormlit slate-blue and let it glow
        protected override float DefaultCustomBrightness => 2.4f;
        protected override float DefaultCustomGlow => 1f;
        protected override float DefaultCustomLight => 0.8f;
    }

    internal class ShardplateBreastplate : ShardplatePiece
    {
        public override string PrefabName => "ShardplateBreastplate";
        public override string[] SourcePrefabs => new[] { "ArmorFlametalChest", "ArmorPaddedCuirass" };
        public override string DisplayName => "Shardplate Breastplate";
        public override string Description =>
            "Overlapping plates that fit together without a seam. Cracks glow blue as Stormlight " +
            "leaks out, then slowly seal themselves.";
        protected override string DefaultRecipe => "Silver:20,BlackMetal:20,Crystal:10";
    }

    internal class ShardplateGreaves : ShardplatePiece
    {
        public override string PrefabName => "ShardplateGreaves";
        public override string[] SourcePrefabs => new[] { "ArmorFlametalLegs", "ArmorPaddedGreaves" };
        public override string DisplayName => "Shardplate Greaves";
        public override string Description =>
            "Heavy legplates that feel like nothing. A Shardbearer can leap a wall and land running.";
        protected override string DefaultRecipe => "Silver:15,BlackMetal:15,Crystal:8";
    }
}

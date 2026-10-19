using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MysticalWeapons.Hallows
{
    /// <summary>
    /// The Cloak of Invisibility. While you sneak wearing it, monsters cannot see you
    /// (they can still hear you). Recoloured linen cape.
    /// </summary>
    internal class InvisibilityCloak : MysticalArmor
    {
        private const string Name = "InvisibilityCloak";
        private static readonly int CloakHash = Name.GetStableHashCode();

        public override string PrefabName => Name;
        public override string[] SourcePrefabs => new[] { "CapeLinen", "CapeDeerHide" };
        public override string DisplayName => "The Cloak of Invisibility";
        public override string Description =>
            "Silvery, fluid cloth that flows like water. Not a cloak that merely hides - it hides " +
            "you from Death himself. Crouch beneath it and no creature can see you.";

        protected override float DefaultArmor => 2f;
        protected override float DefaultArmorPerLevel => 1f;
        protected override Color DefaultTint => new Color(0.85f, 0.88f, 0.95f); // silvery
        protected override string DefaultRecipe => "LinenThread:20,Silver:6,Crystal:4";
        protected override string DefaultStation => "piece_workbench";
        protected override int DefaultStationLevel => 4;

        private static ConfigEntry<bool> _requireSneak;
        private SE_Stats _cloakEffect;

        public override void BindConfig(ConfigFile cfg)
        {
            base.BindConfig(cfg);
            _requireSneak = cfg.Bind(PrefabName, "RequireSneak", true,
                "Only invisible while sneaking. False = always invisible to monsters (very strong).");
        }

        protected override void Customize(ItemDrop.ItemData.SharedData shared)
        {
            _cloakEffect = DeathlyHallows.MakeEffect("SE_InvisibilityCloak", "Unseen",
                _requireSneak.Value
                    ? "While you sneak, monsters cannot see you. They can still hear you."
                    : "Monsters cannot see you. They can still hear you.", Icon);
            _cloakEffect.m_noiseModifier = -0.5f;
            shared.m_equipStatusEffect = _cloakEffect;
            shared.m_movementModifier = 0f;
            shared.m_weight = 1f;
            DeathlyHallows.JoinSet(shared, Icon);
        }

        public override void RegisterExtras(ObjectDB db) => DeathlyHallows.Register(db, _cloakEffect);

        private static readonly AccessTools.FieldRef<VisEquipment, int> ShoulderHash =
            AccessTools.FieldRefAccess<VisEquipment, int>("m_currentShoulderItemHash");

        /// <summary>
        /// Uses the synced equipment visuals and crouch animation, so this also works when the
        /// monster's AI runs on another player's machine.
        /// </summary>
        private static bool IsCloaked(Player player)
        {
            if (_requireSneak == null) return false;
            var vis = player.GetComponent<VisEquipment>();
            if (!vis || ShoulderHash(vis) != CloakHash) return false;
            return !_requireSneak.Value || player.IsCrouching();
        }

        /// <summary>Monster sight range is multiplied by the stealth factor: 0 means unseen.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.GetStealthFactor))]
        private static class StealthPatch
        {
            private static void Postfix(Player __instance, ref float __result)
            {
                if (IsCloaked(__instance)) __result = 0f;
            }
        }
    }
}

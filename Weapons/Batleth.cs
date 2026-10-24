using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace MysticalWeapons.Weapons
{
    /// <summary>
    /// Klingon bat'leth (Star Trek). A crescent blade held by a grip on its back. Battle excites its
    /// wielder: it carries its own adrenaline bar (like a trinket), filled by hits, kills, blocks, parries and
    /// dodges, and when full the Klingon fights on with "A good day to die".
    /// </summary>
    internal class Batleth : MysticalWeapon
    {
        private const string Name = "Batleth";
        private static readonly int BatlethHash = Name.GetStableHashCode();

        public override string PrefabName => Name;
        public override string[] SourcePrefabs => new[] { "Battleaxe" }; // wide two-handed swings
        public override string DisplayName => "Bat'leth";
        public override string Description =>
            "The sword of honor. A curved Klingon blade with four points, forged for a warrior " +
            "who intends to die well - just not today.";

        protected override string DefaultDamage => "slash:80,pierce:20";
        protected override string DefaultDamagePerLevel => "slash:6";
        // Reforged from the Battleaxe it swings like, plus most of its cost again (it is Iron 35, Ancient bark 30, Leather 4):
        // more than its peer for its adrenaline bar. Bloodbags for the warrior's blood
        protected override string DefaultRecipe => "Battleaxe:1,Iron:30,FineWood:10,Bloodbag:5";
        protected override int DefaultStationLevel => 2;
        protected override float ModelScale => 0.9f;
        public override float IconAngle => 90f; // cutting edge up

        public override MaterialSpec[] Materials => new[]
        {
            new MaterialSpec(new Color(0.72f, 0.72f, 0.76f), 0.75f), // 0 metal
            new MaterialSpec(new Color(0.22f, 0.13f, 0.07f), 0.2f),  // 1 leather
        };

        private ConfigEntry<bool> _fury;
        private ConfigEntry<float> _maxAdrenaline;
        private ConfigEntry<float> _hitAdrenaline;
        private static ConfigEntry<float> _killAdrenaline;
        private ConfigEntry<float> _furyDuration;
        private ConfigEntry<float> _blockAdrenaline;
        private ConfigEntry<float> _parryAdrenaline;
        private static ConfigEntry<float> _dodgeAdrenaline;
        private ConfigEntry<float> _furyStamina;
        private ConfigEntry<float> _furyDamage;

        public override void BindConfig(ConfigFile cfg)
        {
            base.BindConfig(cfg);
            _fury = cfg.Bind(PrefabName, "GoodDayToDie", true,
                "Holding the bat'leth gives you an adrenaline bar, filled by its hits and kills and by blocking, parrying and dodging; when full you get " +
                "\"A good day to die\". It shares the bar with a worn trinket. Restart to apply.");
            _maxAdrenaline = cfg.Bind(PrefabName, "MaxAdrenaline", 20f, "Adrenaline needed for \"A good day to die\". Restart to apply.");
            _hitAdrenaline = cfg.Bind(PrefabName, "HitAdrenaline", 3f, "Adrenaline per hit with the bat'leth. Restart to apply.");
            _killAdrenaline = cfg.Bind(PrefabName, "KillAdrenaline", 10f,
                "Extra adrenaline when the bat'leth's blow kills a creature. The kill is counted on the game that controls the " +
                "creature, which uses its own value, so keep this the same for every player on a server.");
            _furyDuration = cfg.Bind(PrefabName, "GoodDayToDieDuration", 30f, "Seconds \"A good day to die\" lasts. Restart to apply.");
            _blockAdrenaline = cfg.Bind(PrefabName, "BlockAdrenaline", 8f, "Adrenaline when you block a blow with the bat'leth. Restart to apply.");
            _parryAdrenaline = cfg.Bind(PrefabName, "ParryAdrenaline", 12f,
                "Adrenaline when you parry a blow with the bat'leth (a block just as it lands). Restart to apply.");
            _dodgeAdrenaline = cfg.Bind(PrefabName, "DodgeAdrenaline", 7f,
                "Extra adrenaline when you dodge through a blow with the bat'leth in hand (a plain roll gives none, as in vanilla).");
            _furyStamina = cfg.Bind(PrefabName, "GoodDayToDieStamina", 50f,
                "Stamina restored at once when \"A good day to die\" starts. Restart to apply.");
            _furyDamage = cfg.Bind(PrefabName, "GoodDayToDieDamage", 0.2f,
                "Extra damage with axes during \"A good day to die\" (0.2 = +20%); attacks also cost half the stamina. Restart to apply.");
        }

        private SE_Stats _goodDay;

        protected override void Customize(ItemDrop.ItemData.SharedData shared)
        {
            shared.m_attackForce = 90f;
            shared.m_backstabBonus = 4f;
            if (!_fury.Value) return;

            _goodDay = ScriptableObject.CreateInstance<SE_Stats>();
            _goodDay.name = "SE_GoodDayToDie";
            _goodDay.m_name = "A good day to die";
            _goodDay.m_tooltip = "Today is a good day to die! The blood sings: your strength returns, and every swing " +
                                 "costs less and cuts deeper.";
            _goodDay.m_icon = Icon;
            _goodDay.m_ttl = _furyDuration.Value;
            _goodDay.m_staminaUpFront = _furyStamina.Value;
            _goodDay.m_attackStaminaUseModifier = -0.5f;
            _goodDay.m_modifyAttackSkill = shared.m_skillType;
            _goodDay.m_damageModifier = 1f + _furyDamage.Value;
            _goodDay.m_startMessageType = MessageHud.MessageType.Center;
            _goodDay.m_startMessage = "Today is a good day to die!";

            // Like a trinket: equipped, it adds to your maximum adrenaline, and the game gives every equipped
            // item's "full adrenaline" effect when the bar fills
            shared.m_maxAdrenaline = _maxAdrenaline.Value;
            shared.m_fullAdrenalineSE = _goodDay;
            // The game reads these from the item that blocks, which for a two-hander is the weapon itself
            shared.m_blockAdrenaline = _blockAdrenaline.Value;
            shared.m_perfectBlockAdrenaline = _parryAdrenaline.Value;
            foreach (var attack in new[] { shared.m_attack, shared.m_secondaryAttack })
                if (attack != null) attack.m_attackAdrenaline = _hitAdrenaline.Value;
        }

        public override void RegisterExtras(ObjectDB db)
        {
            if (_goodDay && !db.m_StatusEffects.Contains(_goodDay)) db.m_StatusEffects.Add(_goodDay);
        }

        private static readonly AccessTools.FieldRef<Character, HitData> LastHit =
            AccessTools.FieldRefAccess<Character, HitData>("m_lastHit");
        private static readonly AccessTools.FieldRef<VisEquipment, int> RightItemHash =
            AccessTools.FieldRefAccess<VisEquipment, int>("m_currentRightItemHash");

        /// <summary>
        /// Dodging through a blow ("hit while dodging", vanilla's perfect dodge) with the bat'leth in hand adds its
        /// own adrenaline on top of the game's. The game sends this RPC to the dodging player's client.
        /// </summary>
        [HarmonyPatch(typeof(Player), "RPC_HitWhileDodging")]
        private static class DodgePatch
        {
            private static void Postfix(Player __instance)
            {
                if (_dodgeAdrenaline == null || _dodgeAdrenaline.Value <= 0f || __instance != Player.m_localPlayer) return;
                var weapon = __instance.GetCurrentWeapon();
                if (weapon?.m_dropPrefab == null || weapon.m_dropPrefab.name != Name) return;
                __instance.AddAdrenaline(_dodgeAdrenaline.Value);
            }
        }

        /// <summary>
        /// A creature killed by a player holding the bat'leth sends that player extra adrenaline. Runs where the
        /// creature dies (its owner's client), so the weapon comes from the attacker's synced equipment, the amount from
        /// that client's KillAdrenaline, and the adrenaline goes over the same RPC the game uses for staggering an enemy.
        /// </summary>
        [HarmonyPatch(typeof(Character), "OnDeath")]
        private static class KillPatch
        {
            private static void Postfix(Character __instance)
            {
                if (_killAdrenaline == null || _killAdrenaline.Value <= 0f || __instance.IsPlayer()) return;
                var hit = LastHit(__instance);
                if (hit == null || hit.m_ranged || !(hit.GetAttacker() is Player attacker)) return;
                var vis = attacker.GetComponent<VisEquipment>();
                if (!vis || RightItemHash(vis) != BatlethHash) return;
                attacker.GetComponent<ZNetView>()?.InvokeRPC("RPC_AddAdrenaline", _killAdrenaline.Value);
            }
        }

        // Shape in the XY plane: Y along the grip, +X towards the cutting edge
        private static readonly Vector2 Center = new Vector2(-0.45f, 0f);
        private const float OuterRadius = 0.75f;
        private const float BladeWidth = 0.12f;
        private const float HalfAngle = 62f * Mathf.Deg2Rad;
        private const float BladeHalfThickness = 0.012f;

        private static float InnerRadius(float angle)
        {
            var t = Mathf.Abs(angle) / HalfAngle; // taper to a point at each tip
            return OuterRadius - BladeWidth * (1f - t * t * t);
        }

        private static Vector2 Polar(float angle, float radius) =>
            Center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;

        /// <summary>Back prong: a spike on the inner edge pointing back at the wielder.</summary>
        private static Vector2[] Prong(float sign)
        {
            float a0 = 36f * Mathf.Deg2Rad * sign, a1 = 48f * Mathf.Deg2Rad * sign;
            var p0 = Polar(a0, InnerRadius(a0) + 0.02f);
            var p1 = Polar(a1, InnerRadius(a1) + 0.02f);
            return new[] { p0, p1, (p0 + p1) * 0.5f + new Vector2(-0.17f, -0.03f * sign) };
        }

        public override void BuildModel(MeshBuilder b)
        {
            const int metal = 0, leather = 1;
            const int segments = 48;

            // Crescent blade with a sharp (zero-thickness) outer edge
            for (var i = 0; i < segments; i++)
            {
                var a0 = Mathf.Lerp(-HalfAngle, HalfAngle, i / (float)segments);
                var a1 = Mathf.Lerp(-HalfAngle, HalfAngle, (i + 1) / (float)segments);
                var in0 = Polar(a0, InnerRadius(a0));
                var in1 = Polar(a1, InnerRadius(a1));
                var out0 = MeshBuilder.V(Polar(a0, OuterRadius), 0f);
                var out1 = MeshBuilder.V(Polar(a1, OuterRadius), 0f);
                var h0 = BladeHalfThickness * (1f - Mathf.Abs(a0) / HalfAngle * 0.7f);
                var h1 = BladeHalfThickness * (1f - Mathf.Abs(a1) / HalfAngle * 0.7f);

                b.Quad(metal, MeshBuilder.V(in0, h0), MeshBuilder.V(in1, h1), out1, out0);
                b.Quad(metal, MeshBuilder.V(in0, -h0), MeshBuilder.V(in1, -h1), out1, out0);
                b.Quad(metal, MeshBuilder.V(in0, h0), MeshBuilder.V(in1, h1),
                    MeshBuilder.V(in1, -h1), MeshBuilder.V(in0, -h0));
            }

            b.Slab(metal, Prong(1f), BladeHalfThickness);
            b.Slab(metal, Prong(-1f), BladeHalfThickness);
            b.Box(metal, Rect.MinMaxRect(0f, 0.11f, 0.2f, 0.15f), BladeHalfThickness * 1.2f);   // struts
            b.Box(metal, Rect.MinMaxRect(0f, -0.15f, 0.2f, -0.11f), BladeHalfThickness * 1.2f);
            b.Box(leather, Rect.MinMaxRect(-0.02f, -0.15f, 0.02f, 0.15f), 0.022f);               // grip
        }
    }
}

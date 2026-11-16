using UnityEngine;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// A weapon as data (Combat_Architecture.md, "Silah verisi"). The prefab keeps the model, the muzzle and the left
    /// hand grip; the numbers live here. A new weapon is a new asset, no code.
    /// The WeaponController copies these values when it wakes up, so its own fields are only the fallback for
    /// prefabs without a definition. Assets: Assets/Data/Weapons (made by Endless Survival > Dövüş > Silah Tanımlarını Oluştur).
    /// </summary>
    [CreateAssetMenu(menuName = "Endless Survival/Combat/Weapon Definition", fileName = "WD_NewWeapon")]
    public class WeaponDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "Silah";
        [Tooltip("Rifle and Pistol shoot; Melee uses the melee numbers below")]
        public WeaponType type = WeaponType.Rifle;

        [Header("Holding")]
        [Tooltip("Body yaw offset while aiming (pistols). Rifles use the PlayerShooter value.")]
        [Range(-90f, 90f)] public float aimYawOffset;
        [Tooltip("Lock the left hand to the LeftHandGrip point (two handed long guns)")]
        public bool useLeftHandGrip = true;

        [Header("Firing (firearms)")]
        public bool automatic = true;
        [Tooltip("Damage per pellet")]
        public float damage = 25f;
        [Tooltip("Seconds between shots (or shots per second when above 1)")]
        public float fireInterval = 0.1f;
        public float range = 200f;
        [Tooltip("Pellets per shot (1 = one bullet; shotguns more, each with the damage above)")]
        [Min(1)] public int pellets = 1;
        [Tooltip("Spread of the pellets around the aim line (degrees). Added to the holder's own spread.")]
        [Min(0f)] public float pelletSpread;

        [Header("Ammunition")]
        public int magazine = 30;
        public float reloadTime = 2.2f;
        [Tooltip("None = chosen from the name (pistol / smg = pistol rounds, hunting / shotgun = shells, else rifle)")]
        public AmmoKind ammo = AmmoKind.None;

        [Header("Melee (type Melee)")]
        public MeleeStats melee = MeleeStats.Default;

        [Header("Draw / holster (Combat_Action_Plan.md 4)")]
        [Tooltip("Seconds from the holster to ready in the hand. 0 = by weapon kind (pistol 0.35, long gun 0.75, knife 0.25, club 0.45)")]
        [Min(0f)] public float drawTime;
        [Tooltip("Seconds to put it back in its holster. 0 = by weapon kind (a bit shorter than the draw)")]
        [Min(0f)] public float holsterTime;

        public bool IsFirearm => type == WeaponType.Rifle || type == WeaponType.Pistol;

        // ------------------------------------------------------------------ draw times (tunable defaults by kind)

        public static float DrawPistol = 0.35f, DrawLongGun = 0.75f, DrawKnife = 0.25f, DrawMelee = 0.45f,
                            DrawShield = 0.5f, DrawThrowable = 0.3f, HolsterFactor = 0.85f;
        private static bool _tuningRegistered;

        public static void RegisterTuning()
        {
            if (_tuningRegistered) return;
            _tuningRegistered = true;
            const string P = "Dövüş";
            Tuning.TuningPanel.Register(P, "Ele alma: tabanca (sn)", () => DrawPistol, v => DrawPistol = v, 0f, 2f, 0.05f);
            Tuning.TuningPanel.Register(P, "Ele alma: uzun namlu (sn)", () => DrawLongGun, v => DrawLongGun = v, 0f, 2f, 0.05f);
            Tuning.TuningPanel.Register(P, "Ele alma: bıçak (sn)", () => DrawKnife, v => DrawKnife = v, 0f, 2f, 0.05f);
            Tuning.TuningPanel.Register(P, "Ele alma: sopa (sn)", () => DrawMelee, v => DrawMelee = v, 0f, 2f, 0.05f);
            Tuning.TuningPanel.Register(P, "Ele alma: kalkan (sn)", () => DrawShield, v => DrawShield = v, 0f, 2f, 0.05f);
            Tuning.TuningPanel.Register(P, "Ele alma: taş / bomba (sn)", () => DrawThrowable, v => DrawThrowable = v, 0f, 2f, 0.05f);
            Tuning.TuningPanel.Register(P, "Kınına koyma / ele alma oranı", () => HolsterFactor, v => HolsterFactor = v, 0.2f, 2f, 0.05f);
        }

        /// <summary>Seconds from the holster to ready in the hand (null weapon = fists: 0).</summary>
        public static float DrawTimeOf(WeaponController weapon)
        {
            if (weapon == null) return 0f;
            WeaponDefinition d = weapon.Definition;
            if (d != null && d.drawTime > 0f) return d.drawTime;
            switch (weapon.Type)
            {
                case WeaponType.Pistol: return DrawPistol;
                case WeaponType.Rifle: return DrawLongGun;
                case WeaponType.Melee:
                    var melee = weapon.GetComponent<MeleeWeapon>();
                    MeleeCategory c = melee != null ? melee.stats.ResolveCategory(false) : MeleeCategory.OneHand;
                    return c == MeleeCategory.Short || c == MeleeCategory.Fists ? DrawKnife : DrawMelee;
                default: return 0f;
            }
        }

        /// <summary>Seconds to put the weapon back in its holster.</summary>
        public static float HolsterTimeOf(WeaponController weapon)
        {
            if (weapon == null) return 0f;
            WeaponDefinition d = weapon.Definition;
            if (d != null && d.holsterTime > 0f) return d.holsterTime;
            return DrawTimeOf(weapon) * HolsterFactor;
        }
    }
}

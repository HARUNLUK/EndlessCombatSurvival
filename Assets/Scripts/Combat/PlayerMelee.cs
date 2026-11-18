using UnityEngine;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// Player melee and shield input (Core_Feel_Plan stage 2, 2026-11-14; Combat_Action_Plan.md 6, 2026-11-16):
    ///   RB / left click          one quick strike with what is in the hand: fists or the melee weapon; with a firearm
    ///                            a gun butt / pistol whip (little damage, the enemy staggers). No combo.
    ///   RT (melee weapon / fists) one heavy strike: slower, more damage, longer stagger, more stamina (CombatBody.Heavy*)
    ///                            The character keeps walking during the strike (slower).
    ///   LT / right button        with a shield on the arm: shield up (pistol behind it aims as usual, RT fires)
    ///   B, Y                     nothing (dodge and grip switch were removed)
    /// Damage by weapon: fists &lt; knife &lt; club &lt; bat (the weapons' MeleeWeapon stats). A knife from behind into an
    /// enemy that has not noticed the player kills at once (CombatBody.ReceiveMelee).
    /// When a strike may start and what LT means come from PlayerCombatState (rules table, input buffer);
    /// the rest poses are drawn by PlayerCombatAnimation.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerMelee : MonoBehaviour
    {
        [Header("Gun butt push (firearm in the hand)")]
        public float buttDamage = 8f;
        public float buttStagger = 0.8f;
        public float buttKnockback = 3.5f;
        public float buttRange = 1.3f;
        public float buttRadius = 0.6f;
        public float buttStamina = 8f;

        private PlayerShooter _shooter;
        private PlayerCombatState _state;
        private Character.PlayerInputState _inputs;
        private CombatBody _body;
        private ShieldHolder _shield;
        private Camera _camera;
        private EndlessSurvival.Inventory.PlayerLoadout _loadout;

        public CombatBody Body => _body;
        public bool IsAttacking => _body != null && _body.State == ActorState.Attacking;

        /// <summary>A knife (short blade) is in the hand: RB can stealth kill.</summary>
        public bool HoldsKnife
        {
            get
            {
                if (_shooter == null || _shooter.IsHolstered || _shooter.HandType != WeaponType.Melee) return false;
                CurrentStats(out MeleeCategory category);
                return category == MeleeCategory.Short;
            }
        }

        private void Awake()
        {
            _shooter = GetComponent<PlayerShooter>();
            _state = PlayerCombatState.GetOrAdd(gameObject);
            _inputs = GetComponent<Character.PlayerInputState>();
            _shield = ShieldHolder.GetOrAdd(gameObject);
            _body = CombatBody.GetOrAdd(gameObject, CombatTeam.Player);
            _body.team = CombatTeam.Player;
            _body.ExternalSpend = cost => CombatHooks.TrySpendStamina(cost);
            _body.AimDirection = AimDirection;
            _body.SwingFinished += OnSwingFinished;
            _body.RefreshShield();

            const string P = "Karakter";
            Tuning.TuningPanel.Register(P, "Dipçik hasarı", () => buttDamage, v => buttDamage = v, 0f, 50f, 1f, this);
            Tuning.TuningPanel.Register(P, "Dipçik sendeletme (sn)", () => buttStagger, v => buttStagger = v, 0f, 3f, 0.05f, this);
            Tuning.TuningPanel.Register(P, "Dipçik itme", () => buttKnockback, v => buttKnockback = v, 0f, 10f, 0.25f, this);
        }

        private void OnDestroy()
        {
            if (_body != null) _body.SwingFinished -= OnSwingFinished;
        }

        private void Update()
        {
            if (_shooter == null) return;
            if (_camera == null) _camera = Camera.main;
            if (_state == null) _state = PlayerCombatState.GetOrAdd(gameObject);
            if (_loadout == null) _loadout = GetComponent<EndlessSurvival.Inventory.PlayerLoadout>();

            WeaponType hand = _shooter.HandType;
            bool holstered = _shooter.IsHolstered;
            bool meleeHand = holstered || hand == WeaponType.Unarmed || hand == WeaponType.Melee;

            // Without the loadout (old scenes): shield on the arm unless a rifle is in the hand or everything is stowed
            if (_loadout == null && _shield != null && _shield.HasShield)
                _shield.SetOnBack(holstered || hand == WeaponType.Rifle);

            // Shield: what LT means is decided by the combat state (usable shield, no long gun, not throwing / staggered)
            _body.SetGuard(_state.WantsGuard);

            // Strike: RB / left click (quick) or RT with a melee weapon / the fists (heavy), from the input buffer.
            // The rules table decides (a reload is cut short, a strike pressed during the last one comes out when it
            // ends, both wait while a weapon comes to the hand, nothing during a hit reaction).
            bool quick = _state.TryConsume(CombatIntent.Strike);
            bool heavy = !quick && meleeHand && _state.TryConsume(CombatIntent.Heavy);
            if (!quick && !heavy) return;

            if (meleeHand)
            {
                MeleeStats stats = CurrentStats(out MeleeCategory category);
                stats.damage *= CombatHooks.PlayerMeleeDamageMultiplier();
                if (heavy) stats.staminaCost *= CombatBody.HeavyStamina;
                _body.attackSpeed = 1f / Mathf.Max(0.3f, CombatHooks.PlayerMeleeIntervalMultiplier());
                _body.TryAttack(stats, category, false, heavy);
            }
            else
            {
                // Firearm in the hand: gun butt push
                MeleeStats butt = MeleeStats.Fists;
                butt.damage = buttDamage * CombatHooks.PlayerMeleeDamageMultiplier();
                butt.staggerTime = buttStagger;
                butt.knockback = buttKnockback;
                butt.range = buttRange;
                butt.radius = buttRadius;
                butt.staminaCost = buttStamina;
                _body.attackSpeed = 1f;
                _body.TryAttack(butt, MeleeCategory.Fists, butt: true);
            }
        }

        private MeleeStats CurrentStats(out MeleeCategory category)
        {
            if (_shooter.HandType == WeaponType.Melee && !_shooter.IsHolstered && _shooter.CurrentWeapon != null)
            {
                var melee = _shooter.CurrentWeapon.GetComponent<MeleeWeapon>();
                if (melee != null)
                {
                    category = melee.stats.ResolveCategory(false);
                    return melee.stats;
                }
            }
            category = MeleeCategory.Fists;
            return CombatContent.Instance.fists;
        }

        private void OnSwingFinished(int hits)
        {
            CombatHooks.RaisePlayerMeleeLanded(hits);
            MeleeStats stats = CurrentStats(out _);
            float noise = hits > 0 ? stats.noiseRadius : stats.noiseRadius * 0.5f;
            if (noise > 0.1f) PlayerStealthController.Instance?.AlertNearbyEnemies(transform.position, noise);
        }

        /// <summary>Camera relative move input on the ground plane (zero when not moving).</summary>
        private Vector3 MoveDirection()
        {
            if (_inputs == null || _inputs.move.sqrMagnitude < 0.01f) return Vector3.zero;
            Vector3 fwd = _camera != null ? _camera.transform.forward : transform.forward;
            Vector3 right = _camera != null ? _camera.transform.right : transform.right;
            fwd.y = 0f;
            right.y = 0f;
            Vector3 dir = fwd.normalized * _inputs.move.y + right.normalized * _inputs.move.x;
            return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.zero;
        }

        /// <summary>Strike facing: where the camera looks while aiming, else the move direction, else the camera.</summary>
        private Vector3 AimDirection()
        {
            bool aiming = _shooter != null && _shooter.IsAiming;
            if (!aiming)
            {
                Vector3 move = MoveDirection();
                if (move != Vector3.zero) return move;
            }
            if (_camera == null) return transform.forward;
            Vector3 f = _camera.transform.forward;
            f.y = 0f;
            return f;
        }
    }
}

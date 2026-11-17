using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// A weapon object in the world (prefab): model, muzzle, left hand grip, magazine state, firing.
    /// The numbers come from its WeaponDefinition when one is set (Combat_Architecture.md, "Silah verisi");
    /// the fields below are then overwritten at wake up and only stay as the fallback for old prefabs.
    /// Used the same way by the player (PlayerShooter) and the enemies (EnemyController).
    /// </summary>
    public class WeaponController : MonoBehaviour
    {
        [Header("Data (overrides the numbers below)")]
        [SerializeField] private WeaponDefinition definition;

        [Header("Weapon Identity & Type")]
        [SerializeField] private string weaponName = "Assault Rifle";
        [Tooltip("Rifle and Pistol shoot; Melee weapons need a MeleeWeapon component on the same object")]
        [SerializeField] private WeaponType weaponType = WeaponType.Rifle;
        [SerializeField] private bool isAutomatic = true;

        [Header("Holding (non-rifle weapons)")]
        [Tooltip("Body yaw offset while aiming. Rifles use the PlayerShooter value; pistols use this one.")]
        [Range(-90f, 90f)]
        [SerializeField] private float aimYawOffset = 0f;
        [Tooltip("Lock the left hand to the LeftHandGrip point (two handed rifles). Off for pistols and melee weapons.")]
        [SerializeField] private bool useLeftHandGrip = true;

        [Header("Ballistics & Damage")]
        [SerializeField] private float damage = 25f;
        [Tooltip("Shot interval in seconds (e.g. 0.1 for 10 shots/sec) or rate if > 1")]
        [SerializeField] private float fireRate = 0.1f;
        [SerializeField] private float range = 200f;

        [Header("Ammunition")]
        [SerializeField] private bool infiniteAmmo = true;
        [SerializeField] private int magazineCapacity = 30;
        [SerializeField] private int currentAmmo = 30;
        [SerializeField] private int reserveAmmo = 90;
        [SerializeField] private float reloadDuration = 2.2f;
        [Tooltip("Rounds this weapon fires. None = chosen from the name: Pistol / Smg = pistol rounds, Hunting / Shotgun = shells, other firearms = rifle rounds")]
        [SerializeField] private AmmoKind ammoKind = AmmoKind.None;

        [Header("Visual & Audio References")]
        [SerializeField] private Transform muzzlePoint;
        [SerializeField] private Transform leftHandGrip;
        [SerializeField] private ParticleSystem muzzleFlash;
        [SerializeField] private GameObject impactPrefab;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip shootSound;
        [SerializeField] private AudioClip reloadSound;
        [SerializeField] private AudioClip emptySound;

        [Header("Events")]
        public UnityEvent<int, int> onAmmoChanged;
        public UnityEvent onShoot;
        public UnityEvent onReloadStart;
        public UnityEvent onReloadComplete;

        private float nextFireTime;
        private bool isReloading;
        private Coroutine reloadCoroutine;
        private int pellets = 1;
        private float pelletSpread;
        private bool definitionApplied;

        /// <summary>A shot left the barrel (after the ammo check). The animation driver and the HUD listen.</summary>
        public event Action Fired;
        /// <summary>A reload began / finished / was cancelled.</summary>
        public event Action ReloadStarted, ReloadFinished, ReloadCancelled;

        public WeaponDefinition Definition => definition;
        public string WeaponName => weaponName;
        public float BaseDamage => damage;
        public float ShotInterval => GetShotInterval();
        public int Pellets => pellets;
        public WeaponType Type => weaponType;
        public bool IsFirearm => weaponType == WeaponType.Rifle || weaponType == WeaponType.Pistol;
        public float AimYawOffset => aimYawOffset;
        public bool IsAutomatic => isAutomatic;
        public int CurrentAmmo => currentAmmo;
        public int ReserveAmmo => Reserve;
        public int MagazineCapacity => magazineCapacity;
        public bool IsReloading => isReloading;
        public Transform MuzzlePoint => muzzlePoint != null ? muzzlePoint : transform;
        public Transform LeftHandGrip => useLeftHandGrip ? leftHandGrip : null;
        public bool InfiniteAmmo => infiniteAmmo;

        /// <summary>Player weapons reload from this shared pool instead of their own reserve (set by the loadout).</summary>
        public IAmmoPool AmmoPool { get; set; }

        /// <summary>Damage multiplier of the holder (enemies hit softer than the player with the same gun).</summary>
        public float DamageMultiplier { get; set; } = 1f;

        /// <summary>Slower reload when handled one handed (set by the loadout).</summary>
        public float ReloadTimeMultiplier { get; set; } = 1f;

        public AmmoKind AmmoKind
        {
            get
            {
                if (!IsFirearm) return AmmoKind.None;
                if (ammoKind != AmmoKind.None) return ammoKind;
                string n = (weaponName + " " + name).ToLowerInvariant();
                if (n.Contains("pistol") || n.Contains("smg") || n.Contains("tabanca")) return AmmoKind.Pistol;
                if (n.Contains("hunting") || n.Contains("shotgun") || n.Contains("av ")) return AmmoKind.Shotgun;
                return AmmoKind.Rifle;
            }
        }

        /// <summary>Reserve rounds: the shared pool when one is set, else this weapon's own reserve.</summary>
        private int Reserve => AmmoPool != null ? AmmoPool.Count(AmmoKind) : reserveAmmo;

        /// <summary>Raises onAmmoChanged again (the shared pool changed).</summary>
        public void RefreshAmmo() => NotifyAmmoChanged();

        /// <summary>Copies the definition's numbers into this weapon (once; enemy ammo limits are applied after).</summary>
        public void ApplyDefinition()
        {
            if (definitionApplied || definition == null) return;
            definitionApplied = true;
            var d = definition;
            if (!string.IsNullOrEmpty(d.displayName)) weaponName = d.displayName;
            weaponType = d.type;
            isAutomatic = d.automatic;
            aimYawOffset = d.aimYawOffset;
            useLeftHandGrip = d.useLeftHandGrip;
            damage = d.damage;
            fireRate = d.fireInterval;
            range = d.range;
            pellets = Mathf.Max(1, d.pellets);
            pelletSpread = Mathf.Max(0f, d.pelletSpread);
            magazineCapacity = d.IsFirearm ? Mathf.Max(1, d.magazine) : Mathf.Max(0, d.magazine);
            currentAmmo = Mathf.Min(currentAmmo, magazineCapacity);
            reloadDuration = Mathf.Max(0.05f, d.reloadTime);
            ammoKind = d.ammo;
        }

        private void Awake()
        {
            ApplyDefinition();
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null)
                {
                    audioSource = gameObject.AddComponent<AudioSource>();
                    audioSource.playOnAwake = false;
                    audioSource.spatialBlend = 0.5f;
                }
            }

            if (muzzlePoint == null)
            {
                Transform foundMuzzle = transform.Find("Muzzle");
                if (foundMuzzle != null)
                {
                    muzzlePoint = foundMuzzle;
                }
                else
                {
                    muzzlePoint = transform;
                }
            }

            if (muzzleFlash == null && muzzlePoint != null)
            {
                muzzleFlash = muzzlePoint.GetComponentInChildren<ParticleSystem>();
            }

            if (leftHandGrip == null && useLeftHandGrip)
            {
                Transform foundGrip = transform.Find("LeftHandGrip");
                if (foundGrip != null)
                {
                    leftHandGrip = foundGrip;
                }
                else
                {
                    GameObject gripObj = new GameObject("LeftHandGrip");
                    gripObj.transform.SetParent(transform, false);
                    gripObj.transform.localPosition = new Vector3(0f, 0.04f, 0.35f);
                    gripObj.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                    leftHandGrip = gripObj.transform;
                }
            }

            NotifyAmmoChanged();
        }

        private float GetShotInterval()
        {
            if (fireRate <= 0.0001f) return 0.1f;
            // If value is small (e.g. <= 1.0), it represents delay in seconds (0.1 = 100ms)
            // If greater than 1, it represents rounds per second (e.g. 10 = 0.1s interval)
            return fireRate <= 1.0f ? fireRate : (1.0f / fireRate);
        }

        public bool CanShoot()
        {
            if (!infiniteAmmo && isReloading) return false;
            if (Time.time < nextFireTime) return false;
            return true;
        }

        /// <param name="aimAtPoint">Shoot from the muzzle straight toward aimTargetPoint instead of along the muzzle's forward (used by AI).</param>
        /// <param name="spreadDegrees">Random cone half-angle applied to the shot direction.</param>
        /// <param name="ignoreRoot">Colliders under this transform are skipped by the bullet ray (the shooter itself).</param>
        /// <param name="traceOrigin">Start of the bullet ray when it must not start at the muzzle (target closer than the barrel). The tracer is still drawn from the muzzle.</param>
        public bool TryFire(Vector3 aimTargetPoint, LayerMask hitMask, out RaycastHit hitInfo,
            bool aimAtPoint = false, float spreadDegrees = 0f, Transform ignoreRoot = null, Vector3? traceOrigin = null)
        {
            hitInfo = default;

            if (!CanShoot()) return false;

            if (!infiniteAmmo && currentAmmo <= 0)
            {
                PlaySound(emptySound);
                nextFireTime = Time.time + GetShotInterval();
                return false;
            }

            nextFireTime = Time.time + GetShotInterval();

            if (!infiniteAmmo)
            {
                currentAmmo--;
                NotifyAmmoChanged();
            }

            // Muzzle flash
            if (muzzleFlash != null)
            {
                muzzleFlash.Play();
            }

            // Audio
            PlaySound(shootSound);

            Vector3 muzzlePos = MuzzlePoint.position;
            Vector3 startPos = traceOrigin ?? muzzlePos;
            // Bullet trajectory travels directly out of the weapon barrel
            Vector3 aimDirection = MuzzlePoint.forward;

            if (aimAtPoint || aimDirection.sqrMagnitude < 0.001f)
            {
                aimDirection = (aimTargetPoint - startPos).normalized;
            }

            float spread = spreadDegrees + (pellets > 1 ? pelletSpread : 0f);
            bool anyHit = false;
            for (int p = 0; p < pellets; p++)
            {
                RaycastHit pelletHit = FirePellet(startPos, muzzlePos, aimDirection, aimTargetPoint, spread, hitMask, ignoreRoot, out bool hit);
                if (hit && !anyHit)
                {
                    anyHit = true;
                    hitInfo = pelletHit;
                }
            }

            if (IsPlayerWeapon) CombatHooks.RaisePlayerFired();
            CombatHooks.RaiseWeaponFired(transform.position, IsPlayerWeapon);
            EndlessCombat.AI.AIWeaponHooks.OnFired(this, muzzlePos, ignoreRoot);
            onShoot?.Invoke();
            Fired?.Invoke();
            return true;
        }

        /// <summary>One bullet / pellet: ray, tracer, damage through Damage.Deal, impact effect.</summary>
        private RaycastHit FirePellet(Vector3 startPos, Vector3 muzzlePos, Vector3 aimDirection, Vector3 aimTargetPoint, float spreadDegrees,
            LayerMask hitMask, Transform ignoreRoot, out bool hasHit)
        {
            RaycastHit hitInfo;
            Vector3 shootDirection = aimDirection;
            if (spreadDegrees > 0f)
            {
                shootDirection = (shootDirection + UnityEngine.Random.insideUnitSphere * Mathf.Tan(spreadDegrees * Mathf.Deg2Rad)).normalized;
            }

            // Raycast towards targeted point
            if (ignoreRoot != null)
            {
                hasHit = false;
                hitInfo = default;
                float best = float.MaxValue;
                foreach (var h in Physics.RaycastAll(startPos, shootDirection, range, hitMask, QueryTriggerInteraction.Ignore))
                {
                    if (h.collider.transform.IsChildOf(ignoreRoot)) continue;
                    if (h.distance < best)
                    {
                        best = h.distance;
                        hitInfo = h;
                        hasHit = true;
                    }
                }
            }
            else
            {
                hasHit = Physics.Raycast(startPos, shootDirection, out hitInfo, range, hitMask, QueryTriggerInteraction.Ignore);
            }

            Vector3 endPoint = hasHit ? hitInfo.point : (startPos + shootDirection * range);
#if UNITY_EDITOR
            DebugShot?.Invoke($"{name}: from {startPos:F2} dir {shootDirection:F2} aimPoint {aimTargetPoint:F2} muzzle {muzzlePos:F2} hit={(hasHit ? hitInfo.collider.name + " @" + hitInfo.distance.ToString("0.00") : "none")}");
#endif

            // Draw visible bullet tracer line
            StartCoroutine(DrawTracerLine(muzzlePos, endPoint));
            EndlessCombat.AI.AIWeaponHooks.OnPellet(this, startPos, endPoint, hasHit, hasHit ? hitInfo.collider : null);

            if (hasHit)
            {
                IDamageable damageable = hitInfo.collider.GetComponentInParent<IDamageable>();
                if (damageable != null && !damageable.IsDead)
                {
                    var info = new DamageInfo(damage * Mathf.Max(0f, DamageMultiplier), DamageType.Bullet, hitInfo.point, hitInfo.normal)
                    {
                        SourceDirection = -shootDirection,
                        Source = ignoreRoot != null ? ignoreRoot.gameObject : gameObject,
                        Region = EndlessCombat.Combat.Damage.RegionOf(hitInfo.collider, hitInfo.point),
                        ByPlayer = IsPlayerWeapon
                    };
                    float dealt = EndlessCombat.Combat.Damage.Deal(damageable, info);
                    if (IsPlayerWeapon) CombatHooks.RaisePlayerFirearmHit(damageable, dealt);
                }

                // Impact particle effect
                SpawnImpactEffect(hitInfo.point, hitInfo.normal);
            }
            return hitInfo;
        }

#if UNITY_EDITOR
        /// <summary>Editor play tests: one line per shot (ray, aim point, what was hit).</summary>
        public static System.Action<string> DebugShot;
#endif

        private int _playerOwned = -1;

        /// <summary>True when the weapon is held by the player (cached; weapons never change owner).</summary>
        public bool IsPlayerWeapon
        {
            get
            {
                if (_playerOwned < 0) _playerOwned = GetComponentInParent<PlayerShooter>() != null ? 1 : 0;
                return _playerOwned == 1;
            }
        }

        /// <summary>Removes all magazine and reserve rounds and returns the total.</summary>
        public int TakeAllAmmo()
        {
            // With a shared pool the reserve already belongs to the player: only the magazine comes out
            int total = currentAmmo + (AmmoPool != null ? 0 : reserveAmmo);
            currentAmmo = 0;
            reserveAmmo = 0;
            NotifyAmmoChanged();
            return total;
        }

        /// <summary>Puts rounds back into the magazine (up to its capacity) and returns the rest.</summary>
        public int RestoreMagazine(int rounds)
        {
            if (rounds <= 0) return 0;
            int put = Mathf.Min(rounds, Mathf.Max(0, magazineCapacity - currentAmmo));
            currentAmmo += put;
            NotifyAmmoChanged();
            return rounds - put;
        }

        /// <summary>Old behaviour: never runs dry (target dummies, debug).</summary>
        public void MakeInfiniteAmmo()
        {
            infiniteAmmo = true;
        }

        /// <summary>
        /// Enemy weapons: a small magazine and a small reserve of their own. When both are gone the enemy puts the
        /// gun away and fights hand to hand (EnemyController).
        /// </summary>
        public void MakeLimitedAmmo(int magazine, int reserve)
        {
            infiniteAmmo = false;
            AmmoPool = null;
            magazineCapacity = Mathf.Max(1, magazine);
            currentAmmo = magazineCapacity;
            reserveAmmo = Mathf.Max(0, reserve);
            NotifyAmmoChanged();
        }

        /// <summary>Magazine and reserve are both empty (never true with infinite ammo).</summary>
        public bool OutOfAmmo => !infiniteAmmo && currentAmmo <= 0 && Reserve <= 0;

        /// <summary>The magazine is empty but there are rounds left to reload.</summary>
        public bool NeedsReload => !infiniteAmmo && currentAmmo <= 0 && Reserve > 0;

        public void AddReserveAmmo(int amount)
        {
            if (amount <= 0) return;
            if (AmmoPool != null) AmmoPool.Add(AmmoKind, amount);
            else reserveAmmo += amount;
            NotifyAmmoChanged();
        }

        public bool TryReload()
        {
            if (isReloading) return false;
            if (currentAmmo >= magazineCapacity) return false;
            if (Reserve <= 0) return false;

            reloadCoroutine = StartCoroutine(ReloadRoutine());
            return true;
        }

        public void CancelReload()
        {
            if (isReloading && reloadCoroutine != null)
            {
                StopCoroutine(reloadCoroutine);
                isReloading = false;
                reloadCoroutine = null;
                ReloadCancelled?.Invoke();
            }
        }

        private IEnumerator ReloadRoutine()
        {
            isReloading = true;
            onReloadStart?.Invoke();
            ReloadStarted?.Invoke();
            PlaySound(reloadSound);

            yield return new WaitForSeconds(reloadDuration * Mathf.Max(0.1f, ReloadTimeMultiplier));

            int neededAmmo = magazineCapacity - currentAmmo;
            int ammoToAdd;
            if (AmmoPool != null) ammoToAdd = AmmoPool.Take(AmmoKind, neededAmmo);
            else
            {
                ammoToAdd = Mathf.Min(neededAmmo, reserveAmmo);
                reserveAmmo -= ammoToAdd;
            }

            currentAmmo += ammoToAdd;

            isReloading = false;
            reloadCoroutine = null;

            NotifyAmmoChanged();
            onReloadComplete?.Invoke();
            ReloadFinished?.Invoke();
        }

        private void SpawnImpactEffect(Vector3 position, Vector3 normal)
        {
            if (impactPrefab != null)
            {
                GameObject impact = Instantiate(impactPrefab, position + (normal * 0.01f), Quaternion.LookRotation(normal));
                Destroy(impact, 2f);
            }
        }

        private IEnumerator DrawTracerLine(Vector3 start, Vector3 end)
        {
            GameObject tracerObj = new GameObject("BulletTracer");
            LineRenderer lr = tracerObj.AddComponent<LineRenderer>();
            lr.startWidth = 0.04f;
            lr.endWidth = 0.02f;
            lr.positionCount = 2;
            lr.SetPosition(0, start);
            lr.SetPosition(1, end);

            // Unlit yellowish tracer material
            Shader unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlitShader == null) unlitShader = Shader.Find("Sprites/Default");
            Material tracerMat = new Material(unlitShader);
            tracerMat.color = new Color(1f, 0.85f, 0.4f, 0.8f);
            lr.material = tracerMat;

            yield return new WaitForSeconds(0.04f);

            Destroy(tracerMat);
            Destroy(tracerObj);
        }

        private void PlaySound(AudioClip clip)
        {
            if (audioSource != null && clip != null)
            {
                audioSource.PlayOneShot(clip);
            }
        }

        private void NotifyAmmoChanged()
        {
            onAmmoChanged?.Invoke(currentAmmo, Reserve);
        }
    }
}

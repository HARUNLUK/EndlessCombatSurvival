using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace EndlessCombat.Combat
{
    public class WeaponController : MonoBehaviour
    {
        [Header("Weapon Identity & Type")]
        [SerializeField] private string weaponName = "Assault Rifle";
        [SerializeField] private bool isAutomatic = true;

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

        public string WeaponName => weaponName;
        public bool IsAutomatic => isAutomatic;
        public int CurrentAmmo => currentAmmo;
        public int ReserveAmmo => reserveAmmo;
        public int MagazineCapacity => magazineCapacity;
        public bool IsReloading => isReloading;
        public Transform MuzzlePoint => muzzlePoint != null ? muzzlePoint : transform;
        public Transform LeftHandGrip => leftHandGrip;
        public bool InfiniteAmmo => infiniteAmmo;

        private void Awake()
        {
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

            if (leftHandGrip == null)
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

        public bool TryFire(Vector3 aimTargetPoint, LayerMask hitMask, out RaycastHit hitInfo)
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

            Vector3 startPos = MuzzlePoint.position;
            // Bullet trajectory travels directly out of the weapon barrel
            Vector3 shootDirection = MuzzlePoint.forward;

            if (shootDirection.sqrMagnitude < 0.001f)
            {
                shootDirection = (aimTargetPoint - startPos).normalized;
            }

            // Raycast towards targeted point
            bool hasHit = Physics.Raycast(startPos, shootDirection, out hitInfo, range, hitMask, QueryTriggerInteraction.Ignore);

            Vector3 endPoint = hasHit ? hitInfo.point : (startPos + shootDirection * range);

            // Draw visible bullet tracer line
            StartCoroutine(DrawTracerLine(startPos, endPoint));

            if (hasHit)
            {
                // Check damageable
                IDamageable damageable = hitInfo.collider.GetComponentInParent<IDamageable>();
                if (damageable != null && !damageable.IsDead)
                {
                    damageable.TakeDamage(damage, hitInfo.point, hitInfo.normal);
                }

                // Impact particle effect
                SpawnImpactEffect(hitInfo.point, hitInfo.normal);
            }

            onShoot?.Invoke();
            return true;
        }

        public bool TryReload()
        {
            if (isReloading) return false;
            if (currentAmmo >= magazineCapacity) return false;
            if (reserveAmmo <= 0) return false;

            reloadCoroutine = StartCoroutine(ReloadRoutine());
            return true;
        }

        public void CancelReload()
        {
            if (isReloading && reloadCoroutine != null)
            {
                StopCoroutine(reloadCoroutine);
                isReloading = false;
            }
        }

        private IEnumerator ReloadRoutine()
        {
            isReloading = true;
            onReloadStart?.Invoke();
            PlaySound(reloadSound);

            yield return new WaitForSeconds(reloadDuration);

            int neededAmmo = magazineCapacity - currentAmmo;
            int ammoToAdd = Mathf.Min(neededAmmo, reserveAmmo);

            currentAmmo += ammoToAdd;
            reserveAmmo -= ammoToAdd;

            isReloading = false;
            reloadCoroutine = null;

            NotifyAmmoChanged();
            onReloadComplete?.Invoke();
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
            onAmmoChanged?.Invoke(currentAmmo, reserveAmmo);
        }
    }
}

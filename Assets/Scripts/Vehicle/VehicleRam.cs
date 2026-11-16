using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using EndlessCombat.AI;
using EndlessSurvival.Survival;

namespace EndlessSurvival.Vehicle
{
    /// <summary>
    /// Running things over. A box in front of (or behind) the vehicle body is swept every physics step while the
    /// vehicle is fast; people and animals found in it are hit, a closed roadblock gate is smashed at speed. The hit
    /// does not rely on collision callbacks (a CharacterController does not reliably raise them): the target's
    /// colliders are ignored by the vehicle instead, so the car is not stopped dead by a capsule.
    /// Added to every vehicle by <see cref="VehicleController"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class VehicleRam : MonoBehaviour
    {
        // ---- settings
        [Header("Ram")]
        [Tooltip("Below this speed (km/h) nothing is hit")]
        public float minSpeedKmh = 20f;
        [Tooltip("At and above this speed (km/h) a person dies at once")]
        public float lethalSpeedKmh = 50f;
        [Tooltip("Same target is not hit twice within this time (s)")]
        public float rehitSeconds = 1f;
        [Tooltip("A living target is solid for the vehicle again after this time (s)")]
        public float ignoreSeconds = 1f;
        [Tooltip("Speed (km/h) from which a closed roadblock gate is smashed")]
        public float barricadeBreakKmh = RoadBarricade.BreakSpeedKmh;
        [Tooltip("Vehicle speed multiplier after smashing a roadblock gate")]
        public float barricadeSpeedFactor = 0.4f;
        [Tooltip("Vehicle damage multiplier with a bullbar")]
        public float bullbarDamageFactor = 0.3f;

        private const int BufferSize = 48;

        private VehicleController _vehicle;
        private VehicleModifications _mods;
        private Rigidbody _rb;
        private BoxCollider _body;
        private readonly Collider[] _buffer = new Collider[BufferSize];
        private readonly Dictionary<Object, float> _lastHit = new Dictionary<Object, float>();
        private readonly List<Object> _expired = new List<Object>();
        private float _nextCleanup;

        private void Awake()
        {
            _vehicle = GetComponent<VehicleController>();
            _rb = GetComponent<Rigidbody>();
            _mods = GetComponent<VehicleModifications>();
            _body = GetComponent<BoxCollider>();
        }

        private void FixedUpdate()
        {
            if (_rb == null || _vehicle == null) return;
            Vector3 velocity = _rb.linearVelocity;
            float speedMs = velocity.magnitude;
            float speedKmh = speedMs * 3.6f;
            if (speedKmh < minSpeedKmh) return;

            float sign = Vector3.Dot(velocity, transform.forward) >= 0f ? 1f : -1f;

            // Body box from the vehicle's own collider (local space)
            Vector3 size = _body != null ? _body.size : new Vector3(1.7f, 1.1f, 2.9f);
            Vector3 center = _body != null ? _body.center : new Vector3(0f, 1.05f, 0f);
            Vector3 scale = transform.lossyScale;
            size = Vector3.Scale(size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
            float depth = Mathf.Max(0.6f, speedMs * Time.fixedDeltaTime * 2f);

            Vector3 localCenter = center + Vector3.forward * sign * (size.z * 0.5f + depth * 0.5f) / Mathf.Max(0.01f, Mathf.Abs(scale.z));
            Vector3 worldCenter = transform.TransformPoint(localCenter);
            Vector3 half = new Vector3(size.x * 0.5f, size.y * 0.5f, depth * 0.5f);
            int count = Physics.OverlapBoxNonAlloc(worldCenter, half, _buffer, transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            if (count <= 0) return;

            Vector3 flatDir = new Vector3(velocity.x, 0f, velocity.z);
            flatDir = flatDir.sqrMagnitude > 0.01f ? flatDir.normalized : transform.forward * sign;
            Vector3 frontPoint = transform.TransformPoint(center + Vector3.forward * sign * size.z * 0.5f / Mathf.Max(0.01f, Mathf.Abs(scale.z)));

            for (int i = 0; i < count; i++)
            {
                Collider col = _buffer[i];
                if (col == null || col.transform.IsChildOf(transform)) continue;
                if (col.CompareTag("Player") || col.transform.root.CompareTag("Player")) continue;

                var enemy = col.GetComponentInParent<EnemyController>();
                if (enemy != null)
                {
                    if (!enemy.IsDead && CanHit(enemy)) HitPerson(enemy, speedMs, speedKmh, flatDir, frontPoint);
                    continue;
                }
                var animal = col.GetComponentInParent<AnimalController>();
                if (animal != null)
                {
                    if (!animal.IsDead && CanHit(animal)) HitAnimal(animal, speedMs, speedKmh, flatDir, frontPoint);
                    continue;
                }
                var gate = col.GetComponentInParent<RoadBarricade>();
                if (gate != null && !gate.IsOpen && speedKmh >= barricadeBreakKmh && CanHit(gate))
                    HitBarricade(gate, speedMs, flatDir, frontPoint);
            }
        }

        private bool CanHit(Object target)
        {
            float now = Time.time;
            if (_lastHit.TryGetValue(target, out float last) && now - last < rehitSeconds) return false;
            _lastHit[target] = now;
            if (now >= _nextCleanup)
            {
                _nextCleanup = now + 10f;
                _expired.Clear();
                foreach (var pair in _lastHit)
                    if (pair.Key == null || now - pair.Value > 5f) _expired.Add(pair.Key);
                foreach (var key in _expired) _lastHit.Remove(key);
            }
            return true;
        }

        // ------------------------------------------------------------------ people

        private void HitPerson(EnemyController enemy, float speedMs, float speedKmh, Vector3 dir, Vector3 point)
        {
            bool lethal = speedKmh >= lethalSpeedKmh;
            float damage = HitDamage(speedKmh);
            Vector3 impulse = dir * (speedMs * 0.6f) + Vector3.up * 2.5f;
            var colliders = enemy.GetComponentsInChildren<Collider>();
            enemy.HitByVehicle(damage, impulse, lethal);
            IgnoreTarget(colliders, enemy.IsDead);
            HurtVehicle(speedKmh, speedMs, point, 1f, 0.10f);
        }

        // ------------------------------------------------------------------ animals

        private void HitAnimal(AnimalController animal, float speedMs, float speedKmh, Vector3 dir, Vector3 point)
        {
            float damage = speedKmh >= lethalSpeedKmh ? 100f + (speedKmh - lethalSpeedKmh) * 4f : HitDamage(speedKmh);
            Vector3 push = dir * (speedMs * 0.6f) + Vector3.up * 2.5f;
            var colliders = animal.GetComponentsInChildren<Collider>();
            animal.HitByVehicle(damage, push);
            IgnoreTarget(colliders, animal.IsDead);

            float factor, slow;
            switch (animal.kind)
            {
                case AnimalKind.Rabbit: factor = 0f; slow = 0f; break;
                case AnimalKind.Wolf: factor = 0.7f; slow = 0.08f; break;
                case AnimalKind.Deer: factor = 1.5f; slow = 0.20f; break;
                default: factor = 3f; slow = 0.45f; break; // bear
            }
            HurtVehicle(speedKmh, speedMs, point, factor, slow);
        }

        /// <summary>Damage dealt to a living target: 25 at the minimum speed up to 90 at 50 km/h.</summary>
        private float HitDamage(float speedKmh) => Mathf.Lerp(25f, 90f, Mathf.InverseLerp(minSpeedKmh, lethalSpeedKmh, speedKmh));

        // ------------------------------------------------------------------ vehicle side

        private void HurtVehicle(float speedKmh, float speedMs, Vector3 point, float targetFactor, float slowdown)
        {
            if (targetFactor <= 0f) return;
            float amount = (4f + (speedKmh - minSpeedKmh) * 0.12f) * targetFactor;
            if (_mods == null) _mods = GetComponent<VehicleModifications>();
            if (_mods != null && _mods.IsInstalled(VehicleModType.Bullbar)) amount *= bullbarDamageFactor;
            _vehicle.ApplyImpact(amount, point, speedMs);
            if (slowdown > 0f) _rb.linearVelocity *= 1f - slowdown;
        }

        // ------------------------------------------------------------------ roadblock

        private void HitBarricade(RoadBarricade gate, float speedMs, Vector3 dir, Vector3 point)
        {
            // Proportional to the speed, no lower limit (the general collision formula has a 14 m/s threshold, this does not).
            // collisionDamageScale also carries the bullbar reduction.
            float amount = speedMs * _vehicle.collisionDamageScale;
            gate.BreakByVehicle(dir);
            _vehicle.ApplyImpact(amount, point, speedMs);
            _rb.linearVelocity *= barricadeSpeedFactor;
        }

        // ------------------------------------------------------------------ no physical snag

        private void IgnoreTarget(Collider[] targetColliders, bool permanent)
        {
            var own = GetComponentsInChildren<Collider>();
            SetIgnore(own, targetColliders, true);
            if (!permanent) StartCoroutine(RestoreCollision(own, targetColliders));
        }

        private IEnumerator RestoreCollision(Collider[] own, Collider[] targetColliders)
        {
            yield return new WaitForSeconds(ignoreSeconds);
            SetIgnore(own, targetColliders, false);
        }

        private static void SetIgnore(Collider[] own, Collider[] targets, bool ignore)
        {
            foreach (var a in own)
            {
                if (a == null || a is WheelCollider) continue;
                foreach (var b in targets)
                    if (b != null && b != a) Physics.IgnoreCollision(a, b, ignore);
            }
        }
    }
}

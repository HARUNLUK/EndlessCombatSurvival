using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using EndlessCombat.AI;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// A thrown rock or grenade (placeholder sphere with a Rigidbody).
    /// Rock: on its first hard landing it makes a noise; enemies that hear it walk to the spot to look (stealth distraction).
    /// Grenade: explodes after the fuse; damage falls off with distance, enemies far around hear the blast.
    /// </summary>
    public class ThrownProjectile : MonoBehaviour
    {
        public enum Kind { Rock, Grenade }

        private Kind _kind;
        private Transform _thrower;
        private bool _landed;
        private float _spawnTime;
        private static readonly Collider[] Buffer = new Collider[128];

        public static ThrownProjectile Launch(Kind kind, Vector3 origin, Vector3 velocity, Transform thrower, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = kind == Kind.Rock ? "Thrown_Rock" : "Thrown_Grenade";
            go.transform.position = origin;
            go.transform.localScale = Vector3.one * (kind == Kind.Rock ? 0.14f : 0.16f);

            var renderer = go.GetComponent<Renderer>();
            var block = new MaterialPropertyBlock();
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = kind == Kind.Rock ? 0.4f : 0.5f;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.5f;
            rb.linearVelocity = velocity;

            var projectile = go.AddComponent<ThrownProjectile>();
            projectile._kind = kind;
            projectile._thrower = thrower;
            projectile._spawnTime = Time.time;

            // Do not collide with the thrower
            if (thrower != null)
            {
                var own = go.GetComponent<Collider>();
                foreach (var c in thrower.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(own, c, true);
            }

            if (kind == Kind.Grenade) projectile.StartCoroutine(projectile.FuseRoutine());
            else Destroy(go, 30f);
            return projectile;
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_kind != Kind.Rock || _landed) return;
            if (Time.time - _spawnTime < 0.05f) return;

            _landed = true;
            float radius = CombatContent.Instance.rockNoiseRadius;
            Vector3 spot = transform.position;

            // A rock that hits an enemy directly alerts that enemy to the thrower instead
            var enemy = collision.collider.GetComponentInParent<EnemyController>();
            if (enemy != null && !enemy.IsDead && _thrower != null)
            {
                enemy.HearSound(_thrower.position, radius);
            }

            AlertEnemies(spot, radius);
        }

        private IEnumerator FuseRoutine()
        {
            yield return new WaitForSeconds(CombatContent.Instance.grenadeFuse);
            Explode();
        }

        private void Explode()
        {
            var content = CombatContent.Instance;
            Vector3 center = transform.position;
            float radius = content.grenadeRadius;

            int count = Physics.OverlapSphereNonAlloc(center, radius, Buffer, ~0, QueryTriggerInteraction.Ignore);
            var damaged = new HashSet<IDamageable>();
            var pushed = new HashSet<Rigidbody>();

            for (int i = 0; i < count; i++)
            {
                Collider col = Buffer[i];
                if (col == null || col.gameObject == gameObject) continue;

                IDamageable target = col.GetComponentInParent<IDamageable>();
                if (target != null && !target.IsDead && damaged.Add(target))
                {
                    Vector3 point = col.ClosestPoint(center);
                    float falloff = 1f - Mathf.Clamp01(Vector3.Distance(center, point) / radius);
                    float dmg = content.grenadeDamage * falloff;
                    if (dmg > 0.5f) target.TakeDamage(dmg, point, (point - center).normalized);
                }

                Rigidbody rb = col.attachedRigidbody;
                if (rb != null && !rb.isKinematic && pushed.Add(rb))
                {
                    rb.AddExplosionForce(600f, center, radius, 1f);
                }
            }

            // Ragdolls of the enemies that just died get pushed in the frame after they switch to ragdoll
            StaticCoroutine.Run(PushRagdollsNextFrame(center, radius));

            AlertEnemies(center, content.grenadeNoiseRadius);
            SpawnBlastVisual(center, radius);
            Destroy(gameObject);
        }

        private static IEnumerator PushRagdollsNextFrame(Vector3 center, float radius)
        {
            yield return new WaitForFixedUpdate();
            int count = Physics.OverlapSphereNonAlloc(center, radius, Buffer, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Rigidbody rb = Buffer[i] != null ? Buffer[i].attachedRigidbody : null;
                if (rb != null && !rb.isKinematic) rb.AddExplosionForce(250f, center, radius, 1f);
            }
        }

        private static void AlertEnemies(Vector3 origin, float radius)
        {
            var stealth = PlayerStealthController.Instance;
            if (stealth != null)
            {
                stealth.AlertNearbyEnemies(origin, radius);
                return;
            }

            foreach (var hit in Physics.OverlapSphere(origin, radius))
            {
                var enemy = hit.GetComponent<EnemyController>();
                if (enemy != null && !enemy.IsDead) enemy.HearSound(origin, radius);
            }
        }

        private static void SpawnBlastVisual(Vector3 center, float radius)
        {
            var flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.name = "GrenadeBlast";
            Destroy(flash.GetComponent<Collider>());
            flash.transform.position = center;

            var renderer = flash.GetComponent<Renderer>();
            Shader unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit == null) unlit = Shader.Find("Sprites/Default");
            var mat = new Material(unlit) { color = new Color(1f, 0.6f, 0.15f, 1f) };
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var light = flash.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.65f, 0.3f);
            light.range = radius * 3f;
            light.intensity = 8f;

            var fx = flash.AddComponent<BlastFade>();
            fx.maxScale = radius * 1.2f;
            fx.material = mat;
        }

        private class BlastFade : MonoBehaviour
        {
            public float maxScale = 6f;
            public Material material;
            private float _t;
            private Light _light;

            private void Start()
            {
                _light = GetComponent<Light>();
            }

            private void Update()
            {
                _t += Time.deltaTime / 0.45f;
                float s = Mathf.Lerp(0.5f, maxScale, Mathf.Sqrt(Mathf.Clamp01(_t)));
                transform.localScale = Vector3.one * s;
                if (_light != null) _light.intensity = Mathf.Lerp(8f, 0f, _t);
                if (material != null)
                {
                    material.color = Color.Lerp(new Color(1f, 0.6f, 0.15f), new Color(0.25f, 0.22f, 0.2f), _t);
                }
                if (_t >= 1f)
                {
                    if (material != null) Destroy(material);
                    Destroy(gameObject);
                }
            }
        }
    }

    /// <summary>Runs a coroutine on a hidden helper object (for objects that destroy themselves).</summary>
    internal class StaticCoroutine : MonoBehaviour
    {
        private static StaticCoroutine _host;

        public static void Run(IEnumerator routine)
        {
            if (_host == null)
            {
                var go = new GameObject("CoroutineHost") { hideFlags = HideFlags.HideAndDontSave };
                _host = go.AddComponent<StaticCoroutine>();
            }
            _host.StartCoroutine(routine);
        }
    }
}

using UnityEngine;
using EndlessSurvival.Inventory;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// A flying arrow (PlayerBow, 2026-11-17): a real projectile that falls with gravity, stepped with ray casts.
    /// Silent (no gunfire noise). A head hit does more damage. It sticks where it lands; on the ground or in a wall
    /// (and sometimes in a body) it can be picked up again as an arrow item, otherwise it broke.
    /// </summary>
    public class ArrowProjectile : MonoBehaviour
    {
        private Vector3 _velocity;
        private float _damage, _headMultiplier, _recoverChance;
        private GameObject _owner;
        private ItemDefinition _item;
        private float _dieAt;
        private bool _stuck;

        public static ArrowProjectile Launch(Vector3 origin, Vector3 velocity, float damage, float headMultiplier, float recoverChance,
                                             GameObject owner, ItemDefinition arrowItem)
        {
            Transform model = arrowItem != null && arrowItem.heldModel != null ? arrowItem.heldModel.transform.Find("ArrowModel") : null;
            GameObject go;
            if (model != null)
            {
                go = Instantiate(model.gameObject);
                go.SetActive(true);
            }
            else
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.transform.localScale = new Vector3(0.012f, 0.012f, 0.75f);
            }
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            go.name = "Arrow";
            go.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(velocity.normalized));
            var a = go.AddComponent<ArrowProjectile>();
            a._velocity = velocity;
            a._damage = damage;
            a._headMultiplier = headMultiplier;
            a._recoverChance = recoverChance;
            a._owner = owner;
            a._item = arrowItem;
            a._dieAt = Time.time + 8f;
            return a;
        }

        private void Update()
        {
            if (_stuck) return;
            if (Time.time > _dieAt) { Destroy(gameObject); return; }

            float dt = Time.deltaTime;
            Vector3 from = transform.position;
            _velocity += Physics.gravity * dt;
            Vector3 step = _velocity * dt;
            float len = step.magnitude;
            if (len < 0.0001f) return;

            RaycastHit best = default;
            bool hit = false;
            foreach (var h in Physics.RaycastAll(from, step / len, len + 0.35f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (_owner != null && h.collider.transform.IsChildOf(_owner.transform)) continue;
                if (!hit || h.distance < best.distance) { best = h; hit = true; }
            }
            if (hit) { Hit(best, step / len); return; }

            transform.position = from + step;
            transform.rotation = Quaternion.LookRotation(_velocity.normalized);
        }

        private void Hit(RaycastHit h, Vector3 dir)
        {
            _stuck = true;
            var surface = h.collider != null ? h.collider.sharedMaterial : null;
            EndlessCombat.Audio.GameSound.Play(EndlessCombat.Audio.SoundId.ArrowImpact, h.point, 1f, surface != null ? surface.name : null, true);
            // The tip goes a little in
            transform.SetPositionAndRotation(h.point - transform.forward * 0.3f + dir * 0.08f, Quaternion.LookRotation(dir));

            IDamageable target = h.collider.GetComponentInParent<IDamageable>();
            bool body = target != null && !target.IsDead;
            if (body)
            {
                BodyRegion region = Damage.RegionOf(h.collider, h.point);
                float amount = _damage * (region == BodyRegion.Head ? _headMultiplier : region == BodyRegion.Limb ? 0.75f : 1f);
                var info = new DamageInfo(amount, DamageType.Other, h.point, -dir)
                {
                    Source = _owner, SourceDirection = -dir, Region = region, ByPlayer = true
                };
                Damage.Deal(target, info);
                if (region == BodyRegion.Head) EndlessSurvival.Survival.SurvivalNotify.Show("Kafadan vuruş!", 1f);
            }

            // Stuck in whatever it hit (moves with a body), gone after a while
            transform.SetParent(h.collider.transform, true);
            Destroy(gameObject, body ? 12f : 25f);

            // Can it be used again?
            if (_item == null || Random.value > _recoverChance) return;
            var inv = _owner != null ? _owner.GetComponent<PlayerInventory>() : null;
            if (inv == null || inv.dropPickupPrefab == null) return;
            Vector3 at = h.point + h.normal * 0.15f + Vector3.up * 0.1f;
            if (body) at = (h.collider.transform.position + Vector3.up * 0.3f);
            WorldPickup.Spawn(inv.dropPickupPrefab, _item, 1, at, null);
        }
    }
}

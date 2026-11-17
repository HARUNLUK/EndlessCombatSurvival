using System.Collections.Generic;
using UnityEngine;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// Body part colliders (ragdoll bones, held weapons, shields) must not push CharacterControllers around.
    /// During a swing or a stab an arm bone sweeps into the opponent's capsule; the capsule then gets pushed out
    /// on top of the arm and the character pops a metre into the air until the attack ends.
    /// Every character (the player and each enemy) gets this component. It turns off the collision between its own
    /// body parts and every character capsule, and between its capsule and everybody else's body parts.
    /// Bullets and melee hits are not affected: they use raycasts and overlap checks, not collisions.
    /// New children (a weapon taken into the hand) are picked up by a rescan once a second.
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterBodyColliders : MonoBehaviour
    {
        private static readonly List<CharacterBodyColliders> All = new List<CharacterBodyColliders>();

        private CharacterController _capsule;
        private readonly HashSet<Collider> _parts = new HashSet<Collider>();
        private readonly List<Collider> _scan = new List<Collider>();
        private float _nextScan;

        public static CharacterBodyColliders GetOrAdd(GameObject owner)
        {
            var c = owner.GetComponent<CharacterBodyColliders>();
            return c != null ? c : owner.AddComponent<CharacterBodyColliders>();
        }

        private void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            _nextScan = 0f;
            _parts.Clear();
        }

        private void OnDisable() => All.Remove(this);

        private void Update()
        {
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + 0.5f;
            if (_capsule == null) _capsule = GetComponent<CharacterController>();

            // New body parts of this character against every capsule
            GetComponentsInChildren(true, _scan);
            foreach (Collider col in _scan)
            {
                if (col == null || col is CharacterController || col.isTrigger) continue;
                _parts.Add(col);
            }
            _parts.RemoveWhere(c => c == null);

            for (int i = All.Count - 1; i >= 0; i--)
            {
                CharacterBodyColliders other = All[i];
                if (other == null) { All.RemoveAt(i); continue; }
                CharacterController capsule = other._capsule != null ? other._capsule : other.GetComponent<CharacterController>();
                if (capsule == null) continue;
                // Set again on every scan: Unity forgets an ignore when one of the two colliders is turned off and on
                // (getting in the vehicle, a capsule disabled for a teleport). A few hundred cheap calls a second.
                if (!capsule.enabled || !capsule.gameObject.activeInHierarchy) continue;
                foreach (Collider part in _parts)
                    if (part != null && part.enabled && part.gameObject.activeInHierarchy)
                        Physics.IgnoreCollision(part, capsule, true);
            }

            IgnoreVehicles();
        }

        /// <summary>Vehicles nearer than this get their body contacts with our body parts turned off.</summary>
        public static float VehicleIgnoreRange = 40f;
        /// <summary>Off only for the AI playtest's before/after comparison.</summary>
        public static bool IgnoreVehicleContacts = true;
        private static readonly List<Collider> s_vehicleCols = new List<Collider>();

        /// <summary>
        /// 17 Nov (user report: men beating on the truck threw it into the air and onto its side): the arm and weapon
        /// colliders are kinematic bodies moved by the animation; a swing into the truck body pushed the 1800 kg truck
        /// with unlimited force, several at once launched it. Body parts never touch a vehicle now. The capsule
        /// (CharacterController) still does, so nobody walks through the truck, and the capsule does not push it.
        /// Running people over is not affected (VehicleRam uses an overlap box, not contacts).
        /// </summary>
        private void IgnoreVehicles()
        {
            var all = EndlessSurvival.Vehicle.VehicleController.All;
            if (!IgnoreVehicleContacts || _released || all.Count == 0 || _parts.Count == 0) return;
            if (_capsule == null) _capsule = GetComponent<CharacterController>();
            bool withCapsule = CapsulePassesVehicles && _capsule != null && _capsule.enabled;
            float r2 = VehicleIgnoreRange * VehicleIgnoreRange;
            Vector3 me = transform.position;
            for (int v = all.Count - 1; v >= 0; v--)
            {
                var vehicle = all[v];
                if (vehicle == null || (vehicle.transform.position - me).sqrMagnitude > r2) continue;
                if (transform.IsChildOf(vehicle.transform)) continue; // the driver sits inside
                vehicle.GetComponentsInChildren(false, s_vehicleCols);
                foreach (Collider vc in s_vehicleCols)
                {
                    if (vc == null || vc.isTrigger || vc is WheelCollider || vc is CharacterController || !vc.enabled) continue;
                    if (vc.GetComponentInParent<CharacterBodyColliders>() != null) continue; // a character inside the vehicle
                    foreach (Collider part in _parts)
                        if (part != null && part.enabled && part.gameObject.activeInHierarchy)
                            Physics.IgnoreCollision(part, vc, true);
                    if (withCapsule) Physics.IgnoreCollision(_capsule, vc, true);
                }
            }
        }

        /// <summary>
        /// Enemies (set by EnemyController): the capsule does not touch vehicles either. A CharacterController is an
        /// immovable body for PhysX; the AI playtest measured up to 6700 N·s per step from capsules hugging the truck
        /// (it slid, tipped, or jumped 4 m when it moved into them). The enemy's own movement keeps him out of the
        /// body box instead (EnemyController.KeepOutOfVehicles), with no force on the truck.
        /// </summary>
        public bool CapsulePassesVehicles;
        private bool _released;

        /// <summary>Died and went limp: the ragdoll touches vehicles again (no falling through the truck).</summary>
        public void ReleaseVehicles()
        {
            if (_released) return;
            _released = true;
            var all = EndlessSurvival.Vehicle.VehicleController.All;
            Vector3 me = transform.position;
            float r2 = VehicleIgnoreRange * VehicleIgnoreRange;
            foreach (var vehicle in all)
            {
                if (vehicle == null || (vehicle.transform.position - me).sqrMagnitude > r2) continue;
                vehicle.GetComponentsInChildren(false, s_vehicleCols);
                foreach (Collider vc in s_vehicleCols)
                {
                    if (vc == null || vc.isTrigger || vc is WheelCollider || vc is CharacterController) continue;
                    foreach (Collider part in _parts)
                        if (part != null) Physics.IgnoreCollision(part, vc, false);
                }
            }
        }
    }
}

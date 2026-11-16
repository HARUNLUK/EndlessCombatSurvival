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
        }
    }
}

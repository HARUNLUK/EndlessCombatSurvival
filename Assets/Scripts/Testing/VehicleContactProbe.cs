using System.Collections.Generic;
using UnityEngine;

namespace EndlessSurvival.Testing
{
    /// <summary>
    /// Debug (AI playtest only): records the collision impulses on a vehicle each physics step, so a test can tell
    /// which collider threw the vehicle. Added by AIPlaytest at runtime; never in a scene.
    /// </summary>
    public class VehicleContactProbe : MonoBehaviour
    {
        public struct Entry
        {
            public float time;
            public string other;
            public float impulse;
            public Vector3 normal;
            public Vector3 localPoint;
            public float velY;
        }

        public static readonly List<Entry> Entries = new List<Entry>();
        private Rigidbody _rb;

        private void Awake() => _rb = GetComponent<Rigidbody>();

        private void OnCollisionStay(Collision c) => Record(c);
        private void OnCollisionEnter(Collision c) => Record(c);

        private void Record(Collision c)
        {
            float imp = c.impulse.magnitude;
            if (imp < 50f) return;
            if (Entries.Count > 4000) Entries.RemoveAt(0);
            string root = c.collider.transform.root != null ? c.collider.transform.root.name : "";
            Vector3 n = c.contactCount > 0 ? c.GetContact(0).normal : Vector3.zero;
            Vector3 p = c.contactCount > 0 ? transform.InverseTransformPoint(c.GetContact(0).point) : Vector3.zero;
            Entries.Add(new Entry
            {
                time = Time.time, other = $"{root}/{c.collider.name} ({c.collider.GetType().Name}{(c.rigidbody != null ? (c.rigidbody.isKinematic ? ", kinematik" : ", dinamik") : "")})",
                impulse = imp, normal = n, localPoint = p, velY = _rb != null ? _rb.linearVelocity.y : 0f
            });
        }
    }
}

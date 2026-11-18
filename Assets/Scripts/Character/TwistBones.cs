using UnityEngine;

namespace EndlessCombat.Character
{
    /// <summary>
    /// Drives the arm twist bones of the Survivor body (2026-11-18, Art/Characters/rig_upgrade.py), like AAA rigs:
    ///   upperarm_twist_01_*: shoulder half of the upper arm, turns back part of the upper arm's roll so the shoulder
    ///                        skin does not wring when the arm rotates,
    ///   lowerarm_twist_01_*: wrist half of the forearm, follows part of the hand's roll so the wrist does not
    ///                        collapse into a candy wrapper.
    /// They are not humanoid bones, so the Animator never touches them; this sets them every frame after
    /// animation and IK (LateUpdate, late execution order), always from the rest pose, so nothing drifts.
    /// Bodies without twist bones (HumanM) make this component do nothing.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class TwistBones : MonoBehaviour
    {
        [Range(0f, 1f), Tooltip("How much of the upper arm's roll the shoulder half turns back.")]
        public float upperArmCounter = 0.5f;
        [Range(0f, 1f), Tooltip("How much of the hand's roll the wrist half of the forearm follows.")]
        public float forearmFollow = 0.5f;

        private struct Link
        {
            public Transform twist, driver;   // driver: upper arm (its own roll) or hand (roll in forearm space)
            public Quaternion twistRest, driverRest;
            public Vector3 axis;              // bone length direction (upper arm: own space; hand: forearm space)
            public bool counter;
        }

        private Link[] links = new Link[0];

        private void Awake()
        {
            var list = new System.Collections.Generic.List<Link>();
            foreach (string s in new[] { "l", "r" })
            {
                Transform ua = Find("upperarm_" + s), la = Find("lowerarm_" + s), hand = Find("hand_" + s);
                Transform uaT = Find("upperarm_twist_01_" + s), laT = Find("lowerarm_twist_01_" + s);
                if (ua != null && la != null && uaT != null)
                    list.Add(new Link { twist = uaT, driver = ua, twistRest = uaT.localRotation, driverRest = ua.localRotation,
                                        axis = la.localPosition.normalized, counter = true });
                if (la != null && hand != null && laT != null)
                    list.Add(new Link { twist = laT, driver = hand, twistRest = laT.localRotation, driverRest = hand.localRotation,
                                        axis = hand.localPosition.normalized, counter = false });
            }
            links = list.ToArray();
            if (links.Length == 0) enabled = false;
        }

        private void LateUpdate()
        {
            for (int i = 0; i < links.Length; i++)
            {
                Link k = links[i];
                if (k.twist == null || k.driver == null) continue;
                Quaternion tw;
                if (k.counter)
                {
                    // upper arm roll in its own rest frame; turn the shoulder half back by part of it
                    Quaternion delta = Quaternion.Inverse(k.driverRest) * k.driver.localRotation;
                    tw = Quaternion.Inverse(Quaternion.Slerp(Quaternion.identity, TwistAround(delta, k.axis), upperArmCounter));
                }
                else
                {
                    // hand roll in forearm space; the wrist half of the forearm follows part of it
                    Quaternion delta = k.driver.localRotation * Quaternion.Inverse(k.driverRest);
                    tw = Quaternion.Slerp(Quaternion.identity, TwistAround(delta, k.axis), forearmFollow);
                }
                k.twist.localRotation = tw * k.twistRest;
            }
        }

        /// <summary>Twist part of a swing-twist split of q around axis (unit).</summary>
        private static Quaternion TwistAround(Quaternion q, Vector3 axis)
        {
            Vector3 p = Vector3.Project(new Vector3(q.x, q.y, q.z), axis);
            var t = new Quaternion(p.x, p.y, p.z, q.w);
            float m = Mathf.Sqrt(t.x * t.x + t.y * t.y + t.z * t.z + t.w * t.w);
            if (m < 1e-6f) return Quaternion.identity;
            return new Quaternion(t.x / m, t.y / m, t.z / m, t.w / m);
        }

        private Transform Find(string bone)
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
                if (t.name == bone) return t;
            return null;
        }
    }
}

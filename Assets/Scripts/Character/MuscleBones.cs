using UnityEngine;

namespace EndlessCombat.Character
{
    /// <summary>
    /// Muscle helper bones of the Survivor body (2026-11-18, Art/Characters/muscle_helpers.py), the game version of
    /// muscle simulation (what most current games ship instead of a real muscle solver):
    ///   biceps_*   swells across the arm and shortens a little as the elbow bends,
    ///   deltoid_*  turns half way with the upper arm, so the shoulder cap keeps its volume when the arm rises,
    ///   pectoral_* turns a quarter of the way with the upper arm, so the chest stretches toward a raised arm.
    /// Not humanoid bones, so the Animator never touches them; set every frame after animation and IK, always from
    /// the rest pose (no drift). Bodies without these bones (HumanM) make this component do nothing.
    /// </summary>
    [DefaultExecutionOrder(999)] // before TwistBones (1000): the shoulder lift changes the upper arm's local rotation
    public class MuscleBones : MonoBehaviour
    {
        [Range(0f, 0.6f), Tooltip("Biceps swell across the arm at a full elbow bend.")]
        public float bicepsBulge = 0.3f;
        [Range(0f, 1f)] public float deltoidFollow = 0.5f;
        [Range(0f, 1f)] public float pectoralFollow = 0.25f;

        [Header("Automatic shoulder lift (off until a clip needs it)")]
        [Tooltip("Lifts the collarbone when the arm goes above liftStart and the animation itself did not lift it " +
                 "enough (shoulder rhythm: past ~90 degrees the shoulder girdle carries about a third of the raise). " +
                 "Only the missing part is added, so clips that already lift the shoulder are left as they are.")]
        public bool autoShoulderLift = false;
        [Range(30f, 120f), Tooltip("Arm angle from hanging down (0) where the lift starts; 90 = arm level.")]
        public float liftStart = 70f;
        [Range(0f, 0.6f), Tooltip("Collarbone lift per degree of arm raise past liftStart.")]
        public float liftRatio = 0.35f;
        [Range(0f, 60f)] public float maxLift = 35f;

        private class Side
        {
            public Transform ua, la, hand, spine, clav, biceps, deltoid, pec;
            public float clavRestElevation;   // collarbone angle above horizontal at rest (spine space)
            public Quaternion uaRest, deltoidRest, pecRest, uaInSpineRest;
            public Vector3 bicepsScale;
            public int bicepsLengthAxis;
            public float restBend;
        }

        private Side[] sides = new Side[0];

        private void Awake()
        {
            var list = new System.Collections.Generic.List<Side>();
            Transform spine = Find("spine_03");
            foreach (string s in new[] { "l", "r" })
            {
                var d = new Side
                {
                    ua = Find("upperarm_" + s), la = Find("lowerarm_" + s), hand = Find("hand_" + s), spine = spine,
                    clav = Find("clavicle_" + s),
                    biceps = Find("biceps_" + s), deltoid = Find("deltoid_" + s), pec = Find("pectoral_" + s),
                };
                if (d.ua == null || d.la == null || d.hand == null) continue;
                if (d.biceps == null && d.deltoid == null && d.pec == null) continue;
                d.uaRest = d.ua.localRotation;
                d.restBend = Bend(d);
                if (d.clav != null) d.clavRestElevation = ClavElevation(d);
                if (d.biceps != null)
                {
                    d.bicepsScale = d.biceps.localScale;
                    Vector3 along = d.biceps.InverseTransformDirection(d.la.position - d.ua.position);
                    Vector3 a = new Vector3(Mathf.Abs(along.x), Mathf.Abs(along.y), Mathf.Abs(along.z));
                    d.bicepsLengthAxis = a.x >= a.y && a.x >= a.z ? 0 : (a.y >= a.z ? 1 : 2);
                }
                if (d.deltoid != null) d.deltoidRest = d.deltoid.localRotation;
                if (d.pec != null && spine != null)
                {
                    d.pecRest = d.pec.localRotation;
                    d.uaInSpineRest = Quaternion.Inverse(spine.rotation) * d.ua.rotation;
                }
                list.Add(d);
            }
            sides = list.ToArray();
            if (sides.Length == 0) enabled = false;
        }

        private static float Bend(Side d) =>
            Vector3.Angle(d.la.position - d.ua.position, d.hand.position - d.la.position);

        /// <summary>Collarbone angle above the character's horizontal plane, degrees.</summary>
        private float ClavElevation(Side d)
        {
            Vector3 dir = (d.ua.position - d.clav.position).normalized;
            return Mathf.Asin(Mathf.Clamp(Vector3.Dot(dir, transform.up), -1f, 1f)) * Mathf.Rad2Deg;
        }

        /// <summary>Adds the collarbone lift the clip is missing; the upper arm keeps its world rotation.</summary>
        private void ShoulderLift(Side d)
        {
            float raise = Vector3.Angle(d.la.position - d.ua.position, -transform.up); // 0 hanging, 180 overhead
            float want = Mathf.Min(maxLift, Mathf.Max(0f, raise - liftStart) * liftRatio);
            float have = ClavElevation(d) - d.clavRestElevation;
            float add = want - Mathf.Max(0f, have);
            if (add <= 0.01f) return;
            Vector3 dir = d.ua.position - d.clav.position;
            Vector3 axis = Vector3.Cross(dir, transform.up);
            if (axis.sqrMagnitude < 1e-8f) return;
            Quaternion uaWorld = d.ua.rotation;
            d.clav.rotation = Quaternion.AngleAxis(add, axis.normalized) * d.clav.rotation;
            d.ua.rotation = uaWorld;
        }

        private void LateUpdate()
        {
            foreach (Side d in sides)
            {
                if (d.ua == null) continue;
                if (autoShoulderLift && d.clav != null) ShoulderLift(d);
                if (d.biceps != null)
                {
                    float k = Mathf.Clamp01((Bend(d) - d.restBend) / 110f);
                    Vector3 sc = d.bicepsScale * (1f + bicepsBulge * k);
                    sc[d.bicepsLengthAxis] = d.bicepsScale[d.bicepsLengthAxis] * (1f - 0.1f * k);
                    d.biceps.localScale = sc;
                }
                if (d.deltoid != null)
                {
                    Quaternion delta = d.ua.localRotation * Quaternion.Inverse(d.uaRest); // clavicle space
                    d.deltoid.localRotation = Quaternion.Slerp(Quaternion.identity, delta, deltoidFollow) * d.deltoidRest;
                }
                if (d.pec != null && d.spine != null)
                {
                    Quaternion now = Quaternion.Inverse(d.spine.rotation) * d.ua.rotation;
                    Quaternion delta = now * Quaternion.Inverse(d.uaInSpineRest);           // spine_03 space
                    d.pec.localRotation = Quaternion.Slerp(Quaternion.identity, delta, pectoralFollow) * d.pecRest;
                }
            }
        }

        private Transform Find(string bone)
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
                if (t.name == bone) return t;
            return null;
        }
    }
}

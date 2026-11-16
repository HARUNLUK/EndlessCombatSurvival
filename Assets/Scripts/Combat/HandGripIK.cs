using EndlessSurvival.Inventory;
using UnityEngine;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// The one place that puts the player's LEFT hand on the weapon (2026-11-12 rewrite). It replaces the old
    /// animator IK + "teleport the hand bone to the grip" pair in PlayerShooter, which tore the wrist off the arm.
    /// A real two bone solve (shoulder, elbow, wrist) runs after every animation and aim correction of the frame:
    ///   two handed long gun   the weapon's LeftHandGrip point, at the hip and while aiming
    ///   two handed pistol     support hand next to the right hand, while aiming
    ///   two handed melee      on the handle next to the right hand, also through the whole swing, so one handed
    ///                         swing clips read as two handed swings
    /// No clip is authored and no pose is invented: only the arm is solved onto the weapon the right hand holds.
    /// </summary>
    [DefaultExecutionOrder(500)]
    [DisallowMultipleComponent]
    public class HandGripIK : MonoBehaviour
    {
        [Tooltip("Left hand on the handguard also when the rifle is not aimed")]
        public bool gripLongGunAtHip = false;
        [Tooltip("Distance between the two hands on a melee handle (m)")]
        public float meleeHandSpacing = 0.11f;
        [Tooltip("Support hand offset from the right palm for the two handed pistol (character space: right, up, forward)")]
        public Vector3 pistolSupportOffset = new Vector3(-0.045f, -0.025f, 0.01f);
        [Tooltip("Pull the left arm onto the long gun's grip point with a two bone solve. Off (2026-11-13): the gun is laid through both palms instead, no bone is moved")]
        public bool lockLeftHandOnLongGun = false;
        [Tooltip("Lay the two handed long gun through both palms every frame (only the gun moves, never a bone)")]
        public bool seatLongGunInHands = true;
        [Tooltip("Where the long gun's pistol grip is along the gun, from the stock (0) to the muzzle (1)")]
        [Range(0f, 1f)] public float longGunGripAlong = 0.45f;
        [Tooltip("Where that grip is from the top of the gun (0) to the bottom (1)")]
        [Range(0f, 1f)] public float longGunGripDown = 0.72f;
        [Tooltip("Seat the pistol in the right palm every frame (grip in the palm, barrel along the hand)")]
        public bool seatPistolInPalm = true;
        [Tooltip("Pull the left hand onto a two handed melee weapon / next to a two handed pistol. Off = the clips alone (2026-11-13: the solved hand twisted the wrist)")]
        public bool gripMeleeAndPistol = false;
        [Tooltip("Where the pistol's grip is along the gun, from the back (0) to the muzzle (1)")]
        [Range(0f, 1f)] public float pistolGripAlong = 0.2f;
        [Tooltip("Where the grip is from the top of the gun (0) to the bottom (1)")]
        [Range(0f, 1f)] public float pistolGripDown = 0.6f;
        [Tooltip("Blend speed (1 / s)")]
        public float blendSpeed = 9f;

        private Animator _animator;
        private PlayerShooter _shooter;
        private PlayerLoadout _loadout;
        private PlayerThrower _thrower;
        private Transform _upper, _lower, _hand, _rightHand;
        private Transform _lMid, _lIndex, _lLittle, _rMid, _rIndex, _rLittle;
        private float _weight;
        private Vector3 _lastPos;
        private Quaternion _lastRot = Quaternion.identity;

        // Handle axis of the current melee weapon (weapon local space), measured once per weapon
        private WeaponController _axisWeapon;
        private Vector3 _axisCenter, _axisDir;
        private float _axisHalf;

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            _shooter = GetComponent<PlayerShooter>();
            if (_animator == null || !_animator.isHuman) { enabled = false; return; }
            _upper = _animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _lower = _animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _hand = _animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            _lMid = _animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            _lIndex = _animator.GetBoneTransform(HumanBodyBones.LeftIndexProximal);
            _lLittle = _animator.GetBoneTransform(HumanBodyBones.LeftLittleProximal);
            _rMid = _animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            _rIndex = _animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            _rLittle = _animator.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            if (_upper == null || _lower == null || _hand == null || _rightHand == null) enabled = false;
        }

        private void LateUpdate()
        {
            if (_shooter == null) return;
            if (_loadout == null) _loadout = GetComponent<PlayerLoadout>();
            if (_thrower == null) _thrower = GetComponent<PlayerThrower>();

            SeatPistol();
            SeatLongGun();
            bool has = Target(out Vector3 pos, out Quaternion rot);
            if (has) { _lastPos = _hand.parent.InverseTransformPoint(pos); _lastRot = Quaternion.Inverse(_rightHand.rotation) * rot; }
            _weight = Mathf.MoveTowards(_weight, has ? 1f : 0f, Time.deltaTime * blendSpeed);
            if (_weight <= 0.001f) return;
            if (!has)
            {
                // Letting go: fade out from where the hand was, relative to the body
                pos = _hand.parent.TransformPoint(_lastPos);
                rot = _rightHand.rotation * _lastRot;
            }
            Solve(pos, rot, _weight);
        }

        // Pistol in the palm
        private WeaponController _seatWeapon;
        private Vector3 _seatGrip, _seatForward, _seatUp;
        private float _aimBlend;

        /// <summary>
        /// Puts the pistol into the right palm after the animation: the grip in the palm, the barrel along the
        /// hand (wrist to knuckles), the sights towards the thumb. While aiming the barrel turns onto the camera's
        /// aim line. The fixed offset under the hand socket only fitted one pose and left the gun beside the fist.
        /// </summary>
        private void SeatPistol()
        {
            WeaponController weapon = _shooter.CurrentWeapon;
            bool pistol = seatPistolInPalm && weapon != null && weapon.Type == WeaponType.Pistol && !_shooter.IsHolstered
                          && weapon.MuzzlePoint != null && weapon.transform.IsChildOf(_rightHand) && _rMid != null && _rIndex != null && _rLittle != null;
            _aimBlend = Mathf.MoveTowards(_aimBlend, pistol && _shooter.IsAiming ? 1f : 0f, Time.deltaTime * 7f);
            if (!pistol) return;

            Transform w = weapon.transform;
            if (_seatWeapon != weapon)
            {
                _seatWeapon = weapon;
                _seatForward = w.InverseTransformDirection(weapon.MuzzlePoint.forward).normalized;
                _seatUp = Vector3.ProjectOnPlane(Vector3.up, _seatForward);
                if (_seatUp.sqrMagnitude < 0.01f) _seatUp = Vector3.ProjectOnPlane(Vector3.forward, _seatForward);
                _seatUp.Normalize();
                // The grip: at the back of the gun, below the barrel
                Bounds b = LocalBounds(weapon, out bool any);
                if (!any) { _seatWeapon = null; return; }
                float fMin = float.MaxValue, fMax = float.MinValue, uMin = float.MaxValue, uMax = float.MinValue;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1f : 1f, (k & 2) == 0 ? -1f : 1f, (k & 4) == 0 ? -1f : 1f));
                    float f = Vector3.Dot(c, _seatForward), u = Vector3.Dot(c, _seatUp);
                    fMin = Mathf.Min(fMin, f); fMax = Mathf.Max(fMax, f);
                    uMin = Mathf.Min(uMin, u); uMax = Mathf.Max(uMax, u);
                }
                Vector3 side = Vector3.Cross(_seatUp, _seatForward);
                _seatGrip = _seatForward * Mathf.Lerp(fMin, fMax, pistolGripAlong) + _seatUp * Mathf.Lerp(uMax, uMin, pistolGripDown)
                            + side * Vector3.Dot(b.center, side);
            }

            Vector3 palm = PalmCenter(_rightHand, _rMid, _rIndex, _rLittle, false, out Vector3 fingers, out _);
            Vector3 thumbSide = (_rIndex.position - _rLittle.position).normalized;
            Vector3 barrel = fingers;
            Camera cam = Camera.main;
            if (_aimBlend > 0.001f && cam != null)
            {
                Vector3 aim = cam.transform.position + cam.transform.forward * 40f - palm;
                barrel = Vector3.Slerp(fingers, aim.normalized, _aimBlend);
            }
            Vector3 up = Vector3.ProjectOnPlane(thumbSide, barrel);
            if (up.sqrMagnitude < 0.0001f) return;
            w.rotation = Quaternion.LookRotation(barrel, up.normalized) * Quaternion.Inverse(Quaternion.LookRotation(_seatForward, _seatUp));
            w.position += palm - w.TransformPoint(_seatGrip);
        }

        private WeaponController _longWeapon;
        private Vector3 _longGrip, _longUp;
        private Quaternion _longInHand = Quaternion.identity;
        private bool _longHasPose;

        /// <summary>
        /// Two handed long gun: the gun is laid through both hands of the clip that is playing. Its pistol grip
        /// sits in the right palm and its handguard point (LeftHandGrip) lies towards the left palm, kept upright.
        /// Only the gun object moves. While the left hand is away (reload) it keeps its last pose in the right hand.
        /// </summary>
        private void SeatLongGun()
        {
            WeaponController weapon = _shooter.CurrentWeapon;
            if (!seatLongGunInHands || weapon == null || weapon.Type != WeaponType.Rifle || _shooter.OneHandLongGun || _shooter.IsHolstered
                || weapon.MuzzlePoint == null || !weapon.transform.IsChildOf(_rightHand) || _rMid == null || _rIndex == null || _rLittle == null)
            {
                _longHasPose = false;
                return;
            }
            Transform w = weapon.transform;
            if (_longWeapon != weapon)
            {
                _longWeapon = weapon;
                _longHasPose = false;
                Vector3 fwd = w.InverseTransformDirection(weapon.MuzzlePoint.forward).normalized;
                _longUp = Vector3.ProjectOnPlane(Vector3.up, fwd);
                if (_longUp.sqrMagnitude < 0.01f) _longUp = Vector3.ProjectOnPlane(Vector3.forward, fwd);
                _longUp.Normalize();
                Bounds b = LocalBounds(weapon, out bool any);
                if (!any) { _longWeapon = null; return; }
                float fMin = float.MaxValue, fMax = float.MinValue, uMin = float.MaxValue, uMax = float.MinValue;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 c = b.center + Vector3.Scale(b.extents, new Vector3((k & 1) == 0 ? -1f : 1f, (k & 2) == 0 ? -1f : 1f, (k & 4) == 0 ? -1f : 1f));
                    float f = Vector3.Dot(c, fwd), u = Vector3.Dot(c, _longUp);
                    fMin = Mathf.Min(fMin, f); fMax = Mathf.Max(fMax, f);
                    uMin = Mathf.Min(uMin, u); uMax = Mathf.Max(uMax, u);
                }
                Vector3 side = Vector3.Cross(_longUp, fwd);
                _longGrip = fwd * Mathf.Lerp(fMin, fMax, longGunGripAlong) + _longUp * Mathf.Lerp(uMax, uMin, longGunGripDown) + side * Vector3.Dot(b.center, side);
            }

            Vector3 rightPalm = PalmCenter(_rightHand, _rMid, _rIndex, _rLittle, false, out _, out _);
            Vector3 leftPalm = PalmCenter(_hand, _lMid, _lIndex, _lLittle, true, out _, out _);
            Vector3 between = leftPalm - rightPalm;
            Transform front = weapon.LeftHandGrip;
            bool bothHands = front != null && !weapon.IsReloading && between.magnitude > 0.14f && between.magnitude < 0.75f
                             && Vector3.Dot(between, transform.forward) > -0.05f;
            if (bothHands)
            {
                Vector3 localDir = (w.InverseTransformPoint(front.position) - _longGrip).normalized;
                Vector3 dir = between.normalized;
                Vector3 up = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.95f ? transform.forward : Vector3.up;
                Quaternion target = Quaternion.LookRotation(dir, up) * Quaternion.Inverse(Quaternion.LookRotation(localDir, _longUp));
                w.rotation = _longHasPose ? Quaternion.Slerp(_rightHand.rotation * _longInHand, target, Time.deltaTime * 18f) : target;
                _longInHand = Quaternion.Inverse(_rightHand.rotation) * w.rotation;
                _longHasPose = true;
            }
            else if (_longHasPose) w.rotation = _rightHand.rotation * _longInHand;
            else return;
            w.position += rightPalm - w.TransformPoint(_longGrip);
        }

        /// <summary>Where the left hand bone belongs this frame. False = the hand is free (animation only).</summary>
        private bool Target(out Vector3 pos, out Quaternion rot)
        {
            pos = default;
            rot = default;
            WeaponController weapon = _shooter.CurrentWeapon;
            if (weapon == null || _shooter.IsHolstered || weapon.IsReloading) return false;
            if (_thrower != null && _thrower.IsBusy) return false;

            if (weapon.Type == WeaponType.Rifle)
            {
                if (_shooter.OneHandLongGun) return false;
                if (!lockLeftHandOnLongGun || (!_shooter.IsAiming && !gripLongGunAtHip)) return false;
                Transform grip = weapon.LeftHandGrip;
                if (grip == null) return false;
                pos = grip.position;
                rot = grip.rotation;
                return true;
            }

            if (weapon.Type == WeaponType.Pistol)
            {
                if (!gripMeleeAndPistol || !_shooter.PistolTwoHand || !_shooter.IsAiming) return false;
                Vector3 palm = PalmCenter(_rightHand, _rMid, _rIndex, _rLittle, false, out _, out _);
                Vector3 fwd = weapon.MuzzlePoint != null ? weapon.MuzzlePoint.forward : transform.forward;
                Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
                Vector3 up = Vector3.Cross(fwd, right);
                Vector3 grip = palm + right * pistolSupportOffset.x + up * pistolSupportOffset.y + fwd * pistolSupportOffset.z;
                // Support hand: fingers wrap forward and to the right around the gun hand, palm faces the gun
                Vector3 fingers = (fwd * 0.75f + right * 0.6f + up * 0.25f).normalized;
                return PalmOnto(grip, fingers, right, out pos, out rot);
            }

            if (weapon.Type == WeaponType.Melee)
            {
                if (!gripMeleeAndPistol || _loadout == null || _loadout.Active != PlayerLoadout.Slot.Melee || !_loadout.TwoHanded(PlayerLoadout.Slot.Melee)) return false;
                if (weapon.GetComponent<MeleeWeapon>() == null) return false;
                MeasureAxis(weapon);
                if (_axisHalf < 0.12f) return false; // too short for a second hand

                Transform w = weapon.transform;
                Vector3 center = w.TransformPoint(_axisCenter);
                Vector3 axis = w.TransformDirection(_axisDir).normalized;
                Vector3 rightPalm = PalmCenter(_rightHand, _rMid, _rIndex, _rLittle, false, out _, out Vector3 rightNormal);
                float along = Vector3.Dot(rightPalm - center, axis);
                // The tip is the far end, the pommel the near one
                Vector3 tip = along <= 0f ? axis : -axis;
                float toPommel = _axisHalf - Mathf.Abs(along);
                Vector3 onAxis = center + axis * along;
                Vector3 grip = onAxis + tip * (toPommel >= meleeHandSpacing + 0.04f ? -meleeHandSpacing : meleeHandSpacing);

                // The left hand wraps the handle from the other side: palm against the right palm's direction,
                // thumb towards the tip like the right hand
                Vector3 normal = Vector3.ProjectOnPlane(-rightNormal, tip);
                if (normal.sqrMagnitude < 0.0001f) normal = Vector3.ProjectOnPlane(transform.right, tip);
                normal.Normalize();
                Vector3 fingers = Vector3.Cross(normal, tip);
                return PalmOnto(grip, fingers, normal, out pos, out rot);
            }
            return false;
        }

        /// <summary>
        /// Palm centre of a hand from its own bones (works on any humanoid rig, no bone axis assumptions).
        /// fingers = wrist to middle knuckle, normal = the way the palm faces.
        /// </summary>
        private static Vector3 PalmCenter(Transform hand, Transform mid, Transform index, Transform little, bool left, out Vector3 fingers, out Vector3 normal)
        {
            if (mid == null || index == null || little == null)
            {
                fingers = hand.forward;
                normal = -hand.up;
                return hand.position;
            }
            Vector3 toMid = mid.position - hand.position;
            fingers = toMid.normalized;
            Vector3 knuckles = (index.position - little.position).normalized; // little finger to index finger
            normal = Vector3.Cross(fingers, knuckles).normalized;
            if (left) normal = -normal;
            return hand.position + toMid * 0.6f + normal * 0.025f;
        }

        /// <summary>Left hand bone pose that puts its palm centre on a point, with the given finger and palm directions.</summary>
        private bool PalmOnto(Vector3 point, Vector3 fingers, Vector3 palmNormal, out Vector3 pos, out Quaternion rot)
        {
            Vector3 palm = PalmCenter(_hand, _lMid, _lIndex, _lLittle, true, out Vector3 curFingers, out Vector3 curNormal);
            Quaternion delta = Quaternion.LookRotation(fingers, palmNormal) * Quaternion.Inverse(Quaternion.LookRotation(curFingers, curNormal));
            rot = delta * _hand.rotation;
            pos = point - delta * (palm - _hand.position);
            return true;
        }

        /// <summary>Long axis of the weapon's mesh in the weapon's own space (the handle runs along it).</summary>
        private void MeasureAxis(WeaponController weapon)
        {
            if (_axisWeapon == weapon) return;
            _axisWeapon = weapon;
            _axisHalf = 0f;
            Transform w = weapon.transform;
            Bounds b = LocalBounds(weapon, out bool any);
            if (!any) return;
            Vector3 e = b.extents;
            _axisDir = e.x >= e.y && e.x >= e.z ? Vector3.right : (e.y >= e.z ? Vector3.up : Vector3.forward);
            _axisCenter = b.center;
            // Half length in metres (the weapon may be scaled)
            _axisHalf = Mathf.Max(e.x, e.y, e.z) * w.TransformVector(_axisDir).magnitude;
        }

        /// <summary>Bounds of all the weapon's meshes in the weapon's own space.</summary>
        private static Bounds LocalBounds(WeaponController weapon, out bool any)
        {
            Transform w = weapon.transform;
            any = false;
            Bounds b = default;
            foreach (MeshFilter mf in weapon.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Bounds mb = mf.sharedMesh.bounds;
                for (int k = 0; k < 8; k++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((k & 1) == 0 ? -1f : 1f, (k & 2) == 0 ? -1f : 1f, (k & 4) == 0 ? -1f : 1f));
                    Vector3 p = w.InverseTransformPoint(mf.transform.TransformPoint(corner));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            return b;
        }

        /// <summary>Two bone solve: the elbow keeps bending the way the animation bent it, pulled a little down.</summary>
        private void Solve(Vector3 target, Quaternion targetRot, float weight)
        {
            Vector3 a = _upper.position, b = _lower.position, c = _hand.position;
            target = Vector3.Lerp(c, target, weight);
            float lab = Vector3.Distance(a, b), lcb = Vector3.Distance(b, c);
            Vector3 toTarget = target - a;
            float lat = Mathf.Clamp(toTarget.magnitude, 0.02f, (lab + lcb) * 0.999f);
            Vector3 dir = toTarget.sqrMagnitude > 0.000001f ? toTarget.normalized : transform.forward;

            Vector3 pole = Vector3.ProjectOnPlane((b - a) + (-transform.up - transform.right * 0.4f) * 0.08f, dir);
            if (pole.sqrMagnitude < 0.000001f) pole = Vector3.ProjectOnPlane(-transform.up, dir);
            pole.Normalize();

            float cosA = Mathf.Clamp((lab * lab + lat * lat - lcb * lcb) / (2f * lab * lat), -1f, 1f);
            Vector3 elbow = a + dir * (lab * cosA) + pole * (lab * Mathf.Sqrt(1f - cosA * cosA));

            _upper.rotation = Quaternion.FromToRotation(b - a, elbow - a) * _upper.rotation;
            _lower.rotation = Quaternion.FromToRotation(_hand.position - _lower.position, target - _lower.position) * _lower.rotation;
            _hand.rotation = Quaternion.Slerp(_hand.rotation, targetRot, weight);
        }
    }
}

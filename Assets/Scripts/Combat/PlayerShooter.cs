using UnityEngine;
using StarterAssets;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EndlessCombat.Combat
{
    public class PlayerShooter : MonoBehaviour
    {
        [Header("Weapon Setup")]
        [SerializeField] private WeaponController currentWeapon;
        [SerializeField] private LayerMask aimColliderMask = ~0; // Everything by default

        [Header("Aiming Settings")]
        [SerializeField] private float aimRotateSpeed = 20f;
        [SerializeField] private bool allowHipFire = false;
        [Tooltip("Adjust this yaw angle so the character's rifle points straight forward with the camera crosshair")]
        [Range(-90f, 90f)]
        [SerializeField] private float aimBodyYawOffset = -45f;

        [Header("Dynamic Weapon Aiming")]
        [SerializeField] private bool dynamicAiming = true;
        [Range(0f, 1f)]
        [SerializeField] private float aimWeight = 1f;
        [SerializeField] private float maxAimAngle = 85f;

        [Header("IK Setup")]
        [SerializeField] private bool useLeftHandIK = true;
        [Range(0f, 1f)]
        [SerializeField] private float leftHandIKWeight = 1f;

        [Header("Animation Setup")]
        [SerializeField] private Animator animator;
        [SerializeField] private string aimingParam = "IsAiming";
        [SerializeField] private string fireTriggerParam = "Fire";
        [SerializeField] private string reloadTriggerParam = "Reload";

        private Camera mainCamera;
        private StarterAssets.ThirdPersonController tpController;
        private bool isAiming;
        private bool isShooting;
        private float currentIKWeight = 0f;
        private int animAimingHash;
        private int animFireHash;
        private int animReloadHash;

        public bool IsAiming => isAiming;
        public bool IsShooting => isShooting;
        public float AimBodyYawOffset => aimBodyYawOffset;
        public WeaponController CurrentWeapon => currentWeapon;

        [Header("Holster Settings")]
        [SerializeField] private bool isHolstered = false;
        [Tooltip("Pre-defined HolsterSocket GameObject in character hierarchy (e.g. under Chest/Spine). Position and rotate the socket directly in the scene.")]
        [SerializeField] private Transform holsterSocket;
        [Tooltip("Hold R for this many seconds to holster or unholster the weapon")]
        [SerializeField] private float holsterHoldDuration = 0.4f;

        private Transform handSocket;
        private Vector3 handLocalPos;
        private Quaternion handLocalRot;
        private float currentUpperBodyLayerWeight = 1f;
        private float rKeyPressStartTime = -1f;
        private bool holsterTriggeredThisPress = false;

        public bool IsHolstered => isHolstered;
        public Transform HolsterSocket => holsterSocket;

        private struct BonePose
        {
            public Transform bone;
            public Quaternion localRotation;
        }
        private readonly System.Collections.Generic.List<BonePose> leftFingerPoses = new System.Collections.Generic.List<BonePose>();
        private bool hasCachedFingerPoses = false;
        private float currentAimWeight = 0f;

        private void Awake()
        {
            mainCamera = Camera.main;
            tpController = GetComponent<StarterAssets.ThirdPersonController>();

            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }

            if (currentWeapon == null)
            {
                currentWeapon = GetComponentInChildren<WeaponController>();
            }

            // Exclude Player layer from aim raycast to avoid hitting self
            int playerLayer = gameObject.layer;
            aimColliderMask &= ~(1 << playerLayer);

            animAimingHash = Animator.StringToHash(aimingParam);
            animFireHash = Animator.StringToHash(fireTriggerParam);
            animReloadHash = Animator.StringToHash(reloadTriggerParam);

            if (currentWeapon != null)
            {
                handSocket = currentWeapon.transform.parent;
                handLocalPos = currentWeapon.transform.localPosition;
                handLocalRot = currentWeapon.transform.localRotation;
            }

            FindHolsterSocket();

            if (isHolstered)
            {
                SetHolstered(true);
            }
        }

        private void Start()
        {
            CacheLeftFingerPoses();
        }

        private void CacheLeftFingerPoses()
        {
            if (animator == null) return;
            Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (leftHand == null) return;

            leftFingerPoses.Clear();
            foreach (Transform child in leftHand.GetComponentsInChildren<Transform>(true))
            {
                if (child != leftHand)
                {
                    leftFingerPoses.Add(new BonePose { bone = child, localRotation = child.localRotation });
                }
            }
            hasCachedFingerPoses = leftFingerPoses.Count > 0;
        }

        private void FindHolsterSocket()
        {
            if (holsterSocket != null) return;

            // 1. Search existing pre-defined socket in character hierarchy
            Transform[] children = transform.root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name.Equals("HolsterSocket", System.StringComparison.OrdinalIgnoreCase))
                {
                    holsterSocket = children[i];
                    return;
                }
            }

            // 2. Search on Chest/Spine bone
            Transform spine = null;
            if (animator != null)
            {
                spine = animator.GetBoneTransform(HumanBodyBones.Chest);
                if (spine == null) spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            }

            if (spine != null)
            {
                Transform existing = spine.Find("HolsterSocket");
                if (existing != null)
                {
                    holsterSocket = existing;
                    return;
                }

                // 3. Fallback: Create on chest so holstering always works even if socket wasn't placed in editor yet
                GameObject socketObj = new GameObject("HolsterSocket");
                socketObj.transform.SetParent(spine, false);
                socketObj.transform.localPosition = new Vector3(0.18f, 0.02f, -0.15f);
                socketObj.transform.localRotation = Quaternion.Euler(25f, 155f, -35f);
                holsterSocket = socketObj.transform;
            }
        }

        public void ToggleHolster()
        {
            SetHolstered(!isHolstered);
        }

        public void SetHolstered(bool holstered)
        {
            isHolstered = holstered;

            if (currentWeapon != null)
            {
                if (isHolstered)
                {
                    isAiming = false;
                    isShooting = false;

                    if (animator != null)
                    {
                        animator.SetBool(animAimingHash, false);
                    }

                    if (tpController != null)
                    {
                        tpController.IsAiming = false;
                    }

                    FindHolsterSocket();
                    if (holsterSocket != null)
                    {
                        currentWeapon.transform.SetParent(holsterSocket, false);
                        currentWeapon.transform.localPosition = Vector3.zero;
                        currentWeapon.transform.localRotation = Quaternion.identity;
                    }
                    else
                    {
                        Debug.LogWarning("[PlayerShooter] HolsterSocket bulunamadi! Lutfen karaktere 'HolsterSocket' GameObject'i ekleyin veya PlayerShooter inspector'ina atayin.", this);
                    }
                }
                else
                {
                    if (handSocket != null)
                    {
                        currentWeapon.transform.SetParent(handSocket, false);
                        currentWeapon.transform.localPosition = handLocalPos;
                        currentWeapon.transform.localRotation = handLocalRot;
                    }
                }
            }
        }

        private void Update()
        {
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
                if (mainCamera == null) return;
            }

            UpdateUpperBodyLayerWeight();
            HandleRKeyInput();
            HandleAimInput();
            HandleShootInput();
        }

        private void UpdateUpperBodyLayerWeight()
        {
            if (animator == null) return;

            // When holstered, layer weight blends to 0, returning character to unarmed locomotion and idle
            float targetLayerWeight = isHolstered ? 0f : 1f;
            currentUpperBodyLayerWeight = Mathf.MoveTowards(currentUpperBodyLayerWeight, targetLayerWeight, Time.deltaTime * 6f);
            animator.SetLayerWeight(1, currentUpperBodyLayerWeight);
        }

        private void HandleRKeyInput()
        {
            bool rIsPressed = false;

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                rIsPressed = Keyboard.current.rKey.isPressed;
            }
#else
            rIsPressed = Input.GetKey(KeyCode.R);
#endif

            if (rIsPressed)
            {
                if (rKeyPressStartTime < 0f)
                {
                    rKeyPressStartTime = Time.time;
                    holsterTriggeredThisPress = false;
                }
                else if (!holsterTriggeredThisPress && (Time.time - rKeyPressStartTime >= holsterHoldDuration))
                {
                    // Held R long enough -> Toggle Holster!
                    ToggleHolster();
                    holsterTriggeredThisPress = true;
                }
            }
            else
            {
                if (rKeyPressStartTime > 0f)
                {
                    float holdDuration = Time.time - rKeyPressStartTime;
                    rKeyPressStartTime = -1f;

                    // Tap R (< holsterHoldDuration) -> Reload!
                    if (!holsterTriggeredThisPress && holdDuration < holsterHoldDuration && !isHolstered)
                    {
                        TriggerReload();
                    }
                }
            }
        }

        private void TriggerReload()
        {
            if (currentWeapon == null || isHolstered) return;

            if (currentWeapon.TryReload())
            {
                if (animator != null)
                {
                    animator.SetTrigger(animReloadHash);
                }
            }
        }

        private void LateUpdate()
        {
            if (isHolstered) return;

            // Smoothly blend aim weight to prevent instantaneous backward pitch snapping
            float targetAimWeight = isAiming ? aimWeight : 0f;
            float blendSpeed = isAiming ? 5.5f : 8f; // ~0.18s blend in, ~0.12s blend out
            currentAimWeight = Mathf.MoveTowards(currentAimWeight, targetAimWeight, Time.deltaTime * blendSpeed);

            if (currentAimWeight <= 0.001f) return;

            // Fallback character body rotation if tpController isn't doing it
            if (tpController == null && mainCamera != null && isAiming)
            {
                Vector3 cameraForward = mainCamera.transform.forward;
                cameraForward.y = 0f;

                if (cameraForward.sqrMagnitude > 0.001f)
                {
                    Quaternion yawOffset = Quaternion.Euler(0f, -aimBodyYawOffset, 0f);
                    Vector3 adjustedForward = yawOffset * cameraForward.normalized;

                    Quaternion targetRot = Quaternion.LookRotation(adjustedForward);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, aimRotateSpeed * Time.deltaTime);
                }
            }

            AlignWeaponToAim(currentAimWeight);

            // Lock left hand and fingers solidly to weapon grip
            LockLeftHandGrip(currentAimWeight);
        }

        private void LockLeftHandGrip(float weight = 1f)
        {
            if (currentWeapon == null || animator == null) return;

            Transform leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform grip = currentWeapon.LeftHandGrip;
            if (leftHand != null && grip != null)
            {
                if (weight < 0.999f)
                {
                    leftHand.position = Vector3.Lerp(leftHand.position, grip.position, weight);
                    leftHand.rotation = Quaternion.Slerp(leftHand.rotation, grip.rotation, weight);
                }
                else
                {
                    leftHand.position = grip.position;
                    leftHand.rotation = grip.rotation;
                }
            }

            if (!hasCachedFingerPoses)
            {
                CacheLeftFingerPoses();
            }

            if (hasCachedFingerPoses)
            {
                for (int i = 0; i < leftFingerPoses.Count; i++)
                {
                    if (leftFingerPoses[i].bone != null)
                    {
                        if (weight < 0.999f)
                        {
                            leftFingerPoses[i].bone.localRotation = Quaternion.Slerp(leftFingerPoses[i].bone.localRotation, leftFingerPoses[i].localRotation, weight);
                        }
                        else
                        {
                            leftFingerPoses[i].bone.localRotation = leftFingerPoses[i].localRotation;
                        }
                    }
                }
            }
        }

        public void AlignWeaponToAim(float weight = 1f)
        {
            if (!dynamicAiming || currentWeapon == null || animator == null) return;

            Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Spine);

            Transform rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform muzzle = currentWeapon.MuzzlePoint;
            if (muzzle == null) return;

            Vector3 aimTargetPoint = GetAimPoint();

            // Parallax safety check: if desired direction diverges too far from camera forward (e.g. wall right next to barrel),
            // clamp to camera forward so the character aims forward cleanly without contorting.
            Vector3 desiredMuzzleDir = (aimTargetPoint - muzzle.position).normalized;
            if (mainCamera != null && Vector3.Angle(mainCamera.transform.forward, desiredMuzzleDir) > 20f)
            {
                desiredMuzzleDir = mainCamera.transform.forward;
                aimTargetPoint = muzzle.position + desiredMuzzleDir * 50f;
            }

            // Pass 1: Upper body / Chest aiming towards the target point
            if (chest != null)
            {
                Vector3 currentMuzzleDir = muzzle.forward;

                float angleToForward = Vector3.Angle(transform.forward, desiredMuzzleDir);
                if (angleToForward <= 85f && desiredMuzzleDir.sqrMagnitude > 0.001f && currentMuzzleDir.sqrMagnitude > 0.001f)
                {
                    Quaternion aimCorrection = Quaternion.FromToRotation(currentMuzzleDir, desiredMuzzleDir);
                    float corrAngle = Quaternion.Angle(Quaternion.identity, aimCorrection);
                    if (corrAngle > maxAimAngle)
                    {
                        aimCorrection = Quaternion.Slerp(Quaternion.identity, aimCorrection, maxAimAngle / corrAngle);
                    }

                    // Smoothly blend in correction to eliminate backward fling on transition
                    aimCorrection = Quaternion.Slerp(Quaternion.identity, aimCorrection, weight);
                    chest.rotation = aimCorrection * chest.rotation;
                }
            }

            // Pass 2: Precision Right Hand / Weapon alignment
            // Rotates the weapon holding bone directly so muzzle.forward is 100% pointing at aimTargetPoint
            Transform weaponBone = rightHand != null ? rightHand : currentWeapon.transform.parent;
            if (weaponBone != null)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    Vector3 desiredDir = (aimTargetPoint - muzzle.position).normalized;
                    if (mainCamera != null && Vector3.Angle(mainCamera.transform.forward, desiredDir) > 20f)
                    {
                        desiredDir = mainCamera.transform.forward;
                    }

                    Vector3 currentDir = muzzle.forward;

                    if (desiredDir.sqrMagnitude > 0.001f && currentDir.sqrMagnitude > 0.001f)
                    {
                        Quaternion handCorrection = Quaternion.FromToRotation(currentDir, desiredDir);
                        handCorrection = Quaternion.Slerp(Quaternion.identity, handCorrection, weight);
                        weaponBone.rotation = handCorrection * weaponBone.rotation;
                    }
                }
            }
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (animator == null || isHolstered) return;

            // Two-hand weapon holding IK (only active when aiming, smoothly releases when released)
            float targetWeight = (useLeftHandIK && isAiming) ? leftHandIKWeight : 0f;
            currentIKWeight = Mathf.MoveTowards(currentIKWeight, targetWeight, Time.deltaTime * 8f);

            if (currentIKWeight > 0.001f && currentWeapon != null)
            {
                Transform grip = currentWeapon.LeftHandGrip;
                if (grip != null)
                {
                    animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, currentIKWeight);
                    animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, currentIKWeight);
                    animator.SetIKPosition(AvatarIKGoal.LeftHand, grip.position);
                    animator.SetIKRotation(AvatarIKGoal.LeftHand, grip.rotation);
                }
            }
            else
            {
                animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, 0f);
                animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, 0f);
            }
        }

        private void HandleAimInput()
        {
            if (isHolstered)
            {
                if (isAiming)
                {
                    isAiming = false;
                    if (animator != null) animator.SetBool(animAimingHash, false);
                    if (tpController != null) tpController.IsAiming = false;
                }
                return;
            }

            bool aimPressed = false;

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                aimPressed = Mouse.current.rightButton.isPressed;
            }
#else
            aimPressed = Input.GetMouseButton(1);
#endif

            if (aimPressed != isAiming)
            {
                isAiming = aimPressed;

                if (animator != null)
                {
                    animator.SetBool(animAimingHash, isAiming);
                }

                if (tpController != null)
                {
                    tpController.IsAiming = isAiming;
                    tpController.AimYawOffset = aimBodyYawOffset;
                }
            }
            else if (isAiming && tpController != null)
            {
                tpController.AimYawOffset = aimBodyYawOffset;
            }
        }

        private void HandleShootInput()
        {
            if (currentWeapon == null || isHolstered)
            {
                isShooting = false;
                return;
            }

            // Only allow shooting when aiming
            if (!isAiming && !allowHipFire)
            {
                isShooting = false;
                return;
            }

            bool shootTriggered = false;

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                isShooting = Mouse.current.leftButton.isPressed;
                shootTriggered = currentWeapon.IsAutomatic
                    ? isShooting
                    : Mouse.current.leftButton.wasPressedThisFrame;
            }
#else
            isShooting = Input.GetMouseButton(0);
            shootTriggered = currentWeapon.IsAutomatic
                ? isShooting
                : Input.GetMouseButtonDown(0);
#endif

            if (shootTriggered)
            {
                if (isAiming)
                {
                    // Snap aim weight to full so bullet hits target with 100% accuracy immediately
                    currentAimWeight = 1f;
                    AlignWeaponToAim(1f);
                }

                Vector3 aimTargetPoint = GetAimPoint();

                if (currentWeapon.TryFire(aimTargetPoint, aimColliderMask, out _))
                {
                    // Only trigger fire recoil animation when actively aiming and not already in firing transition
                    if (isAiming && animator != null)
                    {
                        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(1);
                        if (!stateInfo.IsName("Firing Rifle"))
                        {
                            animator.SetTrigger(animFireHash);
                        }
                    }
                }
            }
        }

        public Vector3 GetAimPoint()
        {
            if (mainCamera == null)
            {
                return transform.position + transform.forward * 50f;
            }

            Ray ray = mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            RaycastHit[] hits = Physics.RaycastAll(ray, 300f, aimColliderMask, QueryTriggerInteraction.Ignore);

            if (hits != null && hits.Length > 0)
            {
                System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

                foreach (var hit in hits)
                {
                    if (hit.collider == null) continue;

                    // Ignore character and any attached child colliders (weapon, socket, etc.)
                    if (hit.collider.transform.IsChildOf(transform) || hit.collider.transform.root == transform.root)
                        continue;

                    // Minimum safe distance in front of the character (prevents extreme parallax singularity near walls)
                    float forwardDistance = Vector3.Dot(transform.forward, hit.point - transform.position);
                    if (hit.distance < 1.0f || forwardDistance < 2.5f)
                        continue;

                    // Return the exact point under the crosshair
                    return hit.point;
                }
            }

            return ray.GetPoint(150f);
        }

        public void SetWeapon(WeaponController newWeapon)
        {
            currentWeapon = newWeapon;
        }
    }
}

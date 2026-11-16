using UnityEngine;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// Aiming: the LT stance (PlayerCombatState.WantsAim decides), where the crosshair points (aim point, shot point)
    /// and the procedural rifle pose bend toward it after the animation.
    /// </summary>
    public partial class PlayerShooter
    {
        private float currentAimWeight = 0f;
        private bool _propAligned;

        private void HandleAimInput()
        {
            if (isHolstered)
            {
                if (isAiming) SetAiming(false);
                return;
            }

            // Only firearms aim; melee weapons and fists use LT for the shield (PlayerMelee)
            bool want = _state.WantsAim || (ForceAim && currentWeapon != null && currentWeapon.IsFirearm);

            if (want != isAiming) SetAiming(want);
            else if (isAiming && tpController != null) tpController.AimYawOffset = CurrentAimYawOffset;
        }

        private void SetAiming(bool aiming)
        {
            isAiming = aiming;
            if (!aiming) isShooting = false;
            if (tpController != null)
            {
                tpController.IsAiming = aiming;
                tpController.AimYawOffset = CurrentAimYawOffset;
            }
        }

        /// <summary>
        /// Long gun held with one hand (shield on the other arm): the arm plays the pistol clips, but the rifle sits
        /// in the hand at the angle tuned for the two handed rifle clips and would point across the body. While
        /// aiming, the rifle itself (the prop, not a bone) is turned in the hand so its barrel points at the
        /// crosshair; when the aim is released it goes back to its normal place in the hand.
        /// </summary>
        private void AlignOneHandLongGun()
        {
            bool want = !isHolstered && currentWeapon != null && OneHandLongGun && currentWeapon.Type == WeaponType.Rifle
                        && handSocket != null && currentWeapon.transform.parent == handSocket;
            if (!want)
            {
                _propAligned = false;
                return;
            }
            // Start every frame from the normal hand pose (the animation has moved the hand since)
            currentWeapon.transform.localRotation = handLocalRot;
            _propAligned = currentAimWeight > 0.001f;
            if (!_propAligned || mainCamera == null) return;

            Transform muzzle = currentWeapon.MuzzlePoint;
            Vector3 desired = GetAimPoint() - muzzle.position;
            if (desired.sqrMagnitude < 0.01f || Vector3.Angle(mainCamera.transform.forward, desired) > 25f) desired = mainCamera.transform.forward;
            Quaternion turn = Quaternion.FromToRotation(muzzle.forward, desired.normalized);
            // Keep the rifle upright (no roll): the up of the gun follows the world up as far as it can
            Quaternion target = turn * currentWeapon.transform.rotation;
            Vector3 fwd = desired.normalized;
            Quaternion upright = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(muzzle.parent == currentWeapon.transform ? currentWeapon.transform.InverseTransformDirection(muzzle.forward) : Vector3.forward, Vector3.up));
            if (muzzle.parent == currentWeapon.transform || muzzle == currentWeapon.transform) target = upright;
            currentWeapon.transform.rotation = Quaternion.Slerp(currentWeapon.transform.rotation, target, currentAimWeight);
        }

        private void LateUpdate()
        {
            if (isHolstered) return;

            // Smoothly blend aim weight to prevent instantaneous backward pitch snapping
            float targetAimWeight = isAiming ? aimWeight : 0f;
            float blendSpeed = isAiming ? 5.5f : 8f; // ~0.18s blend in, ~0.12s blend out
            currentAimWeight = Mathf.MoveTowards(currentAimWeight, targetAimWeight, Time.deltaTime * blendSpeed);

            AlignOneHandLongGun();

            if (currentAimWeight <= 0.001f) return;

            // Procedural bone bending (chest / right hand / left hand grip) is tuned for the rifle only.
            // Other weapons play their animation clips untouched.
            if (currentWeapon == null || EffectiveType != WeaponType.Rifle) return;

            // Fallback character body rotation if tpController isn't doing it
            if (tpController == null && mainCamera != null && isAiming)
            {
                Vector3 cameraForward = mainCamera.transform.forward;
                cameraForward.y = 0f;

                if (cameraForward.sqrMagnitude > 0.001f)
                {
                    Quaternion yawOffset = Quaternion.Euler(0f, -CurrentAimYawOffset, 0f);
                    Vector3 adjustedForward = yawOffset * cameraForward.normalized;

                    Quaternion targetRot = Quaternion.LookRotation(adjustedForward);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, aimRotateSpeed * Time.deltaTime);
                }
            }

            AlignWeaponToAim(currentAimWeight);
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
            if (wristAimCorrection && weaponBone != null)
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

        /// <summary>
        /// Where a shot lands: the first thing under the crosshair that is not the player and not between the camera
        /// and the player. traceOrigin is set when the target is so close that a ray from the muzzle would start
        /// beside or behind it; then the bullet is traced along the camera ray from the player's depth.
        /// </summary>
        public Vector3 GetShotPoint(out Vector3? traceOrigin)
        {
            traceOrigin = null;
            if (mainCamera == null) return transform.position + transform.forward * 50f;

            Ray ray = mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            // Depth of the player along the camera ray: nothing nearer than this can be shot (it is behind the character)
            float playerDepth = Mathf.Max(0f, Vector3.Dot(transform.position + Vector3.up * 1.2f - ray.origin, ray.direction));
            Vector3 point = ray.GetPoint(150f);
            float best = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(ray, 300f, aimColliderMask, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider == null) continue;
                if (hit.collider.transform.IsChildOf(transform) || hit.collider.transform.root == transform.root) continue;
                if (hit.distance < playerDepth - 0.2f) continue;
                if (hit.distance < best)
                {
                    best = hit.distance;
                    point = hit.point;
                }
            }

            Vector3 muzzle = currentWeapon != null ? currentWeapon.MuzzlePoint.position : transform.position + Vector3.up * 1.4f;
            Vector3 toPoint = point - muzzle;
            if (toPoint.magnitude < 1.2f || Vector3.Dot(toPoint.normalized, ray.direction) < 0.6f)
                traceOrigin = ray.GetPoint(Mathf.Max(0f, playerDepth - 0.1f));
            return point;
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
    }
}

using UnityEngine;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// The weapon in the hand and the ones on the body: holster socket, body spots (through the loadout's
    /// ParkWeapon), spawning carried weapons, taking one into the hand.
    /// </summary>
    public partial class PlayerShooter
    {
        private Transform handSocket;
        private Vector3 handLocalPos;
        private Quaternion handLocalRot;

        private readonly System.Collections.Generic.Dictionary<WeaponController, Pose> handPoses =
            new System.Collections.Generic.Dictionary<WeaponController, Pose>();

        /// <summary>
        /// Set by the loadout: puts a weapon that is not in the hand on its own spot on the body (back, hip).
        /// Returns false to use the default holster socket.
        /// </summary>
        public System.Func<WeaponController, bool> ParkWeapon { get; set; }

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
                    currentWeapon.CancelReload();
                    SetAiming(false);
                    isShooting = false;

                    if (!Park(currentWeapon))
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

        /// <summary>Puts a weapon that is not in the hand on its spot on the body. False when there is no spot.</summary>
        private bool Park(WeaponController weapon)
        {
            if (weapon == null) return true;
            if (ParkWeapon != null && ParkWeapon(weapon)) return true;
            FindHolsterSocket();
            if (holsterSocket == null) return false;
            weapon.transform.SetParent(holsterSocket, false);
            weapon.transform.localPosition = Vector3.zero;
            weapon.transform.localRotation = Quaternion.identity;
            return true;
        }

        /// <summary>
        /// Loadout: spawns a carried weapon (not taken into the hand). Its hand pose is remembered and it is parked
        /// on the body. Returns the instance.
        /// </summary>
        public WeaponController SpawnWeapon(WeaponController weaponPrefab)
        {
            if (weaponPrefab == null) return null;
            if (handSocket == null) handSocket = FindHandSocket();
            if (handSocket == null) return null;
            WeaponController instance = Instantiate(weaponPrefab, handSocket);
            handPoses[instance] = new Pose(instance.transform.localPosition, instance.transform.localRotation);
            Park(instance);
            return instance;
        }

        /// <summary>Loadout: remembers the hand pose of a weapon that is already in the hand (scene start weapon).</summary>
        public void RegisterHeldWeapon(WeaponController weapon)
        {
            if (weapon == null) return;
            if (weapon == currentWeapon && handSocket != null) handPoses[weapon] = new Pose(handLocalPos, handLocalRot);
            else handPoses[weapon] = new Pose(weapon.transform.localPosition, weapon.transform.localRotation);
        }

        /// <summary>
        /// Loadout: makes the weapon the active one (null = fists). The previous one goes to its spot on the body.
        /// drawn = in the hand; false = active but stowed (stays on the body until drawn).
        /// Taking a weapon into the hand starts its draw time (Combat_Action_Plan.md 4).
        /// </summary>
        public void SetActiveWeapon(WeaponController weapon, bool drawn)
        {
            bool changed = currentWeapon != weapon || (drawn && isHolstered);
            if (currentWeapon != null && currentWeapon != weapon)
            {
                currentWeapon.CancelReload();
                Park(currentWeapon);
            }
            isShooting = false;
            currentWeapon = weapon;
            if (weapon != null)
            {
                if (handSocket == null) handSocket = FindHandSocket();
                if (handPoses.TryGetValue(weapon, out Pose pose))
                {
                    handLocalPos = pose.position;
                    handLocalRot = pose.rotation;
                }
            }
            SetHolstered(!drawn);
            if (drawn) _autoReloadAt = Time.time + 0.35f;
            if (drawn && changed && _state != null) _state.NoteDraw(WeaponDefinition.DrawTimeOf(weapon)); // shots and strikes wait for it
        }

        /// <summary>Loadout: removes a carried weapon for good.</summary>
        public void DestroyWeapon(WeaponController weapon)
        {
            if (weapon == null) return;
            weapon.CancelReload();
            if (weapon == currentWeapon)
            {
                currentWeapon = null;
                isShooting = false;
            }
            handPoses.Remove(weapon);
            Destroy(weapon.gameObject);
        }

        public void SetWeapon(WeaponController newWeapon)
        {
            currentWeapon = newWeapon;
        }

        /// <summary>
        /// Destroys the held weapon and, if a prefab is given, spawns it in the hand socket
        /// (or holster socket while holstered). Returns the new weapon instance, or null.
        /// </summary>
        public WeaponController ReplaceWeapon(WeaponController weaponPrefab)
        {
            if (currentWeapon != null)
            {
                currentWeapon.CancelReload();
                Destroy(currentWeapon.gameObject);
                currentWeapon = null;
            }

            isShooting = false;
            if (weaponPrefab == null) return null;

            if (handSocket == null) handSocket = FindHandSocket();
            if (handSocket == null)
            {
                Debug.LogWarning("[PlayerShooter] WeaponSocket not found; cannot equip weapon.", this);
                return null;
            }

            WeaponController instance = Instantiate(weaponPrefab, handSocket);
            handLocalPos = instance.transform.localPosition;
            handLocalRot = instance.transform.localRotation;
            currentWeapon = instance;

            SetHolstered(isHolstered);
            return instance;
        }

        private Transform FindHandSocket()
        {
            foreach (Transform child in transform.GetComponentsInChildren<Transform>(true))
            {
                if (child.name.Equals("WeaponSocket", System.StringComparison.OrdinalIgnoreCase)) return child;
            }
            return animator != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;
        }
    }
}

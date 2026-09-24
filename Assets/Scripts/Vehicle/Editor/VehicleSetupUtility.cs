#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using EndlessSurvival.Vehicle;
using Unity.Cinemachine;

namespace EndlessSurvival.Vehicle.Editor
{
    public static class VehicleSetupUtility
    {
        [InitializeOnLoadMethod]
        private static void AutoSetupIfMissing()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isPlaying) return;
                var pickup = GameObject.Find("pickup");
                if (pickup != null && (pickup.GetComponent<BoxCollider>() == null || pickup.GetComponent<VehicleInteraction>() == null))
                {
                    Debug.Log("[VehicleSetupUtility] Auto-running vehicle setup on unconfigured pickup...");
                    SetupPickupInActiveScene();
                }
            };
        }

        [MenuItem("Endless Survival/Setup Vehicle in Scene")]
        public static void SetupPickupInActiveScene()
        {
            Debug.Log("[VehicleSetupUtility] Starting vehicle setup in active scene...");

            // 1. Locate pickup in the scene
            GameObject pickupGo = GameObject.Find("pickup");
            if (pickupGo == null)
            {
                // Try finding any GameObject with name containing pickup
                foreach (var go in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
                {
                    if (go.name.ToLower().Contains("pickup"))
                    {
                        pickupGo = go;
                        break;
                    }
                }
            }

            if (pickupGo == null)
            {
                Debug.LogError("[VehicleSetupUtility] Could not find 'pickup' GameObject in active scene!");
                return;
            }

            if (PrefabUtility.IsPartOfAnyPrefab(pickupGo))
            {
                PrefabUtility.UnpackPrefabInstance(pickupGo, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }

            Undo.RegisterFullObjectHierarchyUndo(pickupGo, "Setup Vehicle");

            // 2. Log hierarchy and transforms for debugging
            Debug.Log($"[VehicleSetupUtility] Found '{pickupGo.name}' at pos={pickupGo.transform.position}, scale={pickupGo.transform.localScale}");
            foreach (Transform child in pickupGo.GetComponentsInChildren<Transform>(true))
            {
                Debug.Log($"  Child: {child.name}, localPos={child.localPosition}, localRot={child.localRotation.eulerAngles}, scale={child.localScale}");
            }

            // 3. Configure Rigidbody
            Rigidbody rb = pickupGo.GetComponent<Rigidbody>();
            if (rb == null) rb = pickupGo.AddComponent<Rigidbody>();
            rb.mass = 1650f;
            rb.linearDamping = 0.08f;
            rb.angularDamping = 2.0f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            // 4. Configure BoxCollider on body so character cannot walk through it
            BoxCollider bodyCollider = pickupGo.GetComponent<BoxCollider>();
            if (bodyCollider == null) bodyCollider = pickupGo.AddComponent<BoxCollider>();

            // Calculate bounds from child renderers (excluding wheels if possible)
            Renderer[] renderers = pickupGo.GetComponentsInChildren<Renderer>(true);
            Bounds combinedBounds = new Bounds();
            bool hasBounds = false;

            foreach (var rend in renderers)
            {
                string rName = rend.name.ToLower();
                if (rName.Contains("wheel")) continue;

                if (!hasBounds)
                {
                    combinedBounds = rend.bounds;
                    hasBounds = true;
                }
                else
                {
                    combinedBounds.Encapsulate(rend.bounds);
                }
            }

            if (hasBounds)
            {
                // Convert world bounds to local space of pickupGo
                Vector3 localCenter = pickupGo.transform.InverseTransformPoint(combinedBounds.center);
                Vector3 localSize = new Vector3(
                    combinedBounds.size.x / pickupGo.transform.lossyScale.x,
                    combinedBounds.size.y / pickupGo.transform.lossyScale.y,
                    combinedBounds.size.z / pickupGo.transform.lossyScale.z
                );
                // Keep collider slightly above ground so wheels touch first
                localCenter.y += 0.15f;
                localSize.y = Mathf.Max(localSize.y - 0.25f, 0.8f);

                bodyCollider.center = localCenter;
                bodyCollider.size = localSize;
                Debug.Log($"[VehicleSetupUtility] Calculated BoxCollider: center={localCenter}, size={localSize}");
            }
            else
            {
                bodyCollider.center = new Vector3(0f, 0.9f, 0f);
                bodyCollider.size = new Vector3(2.0f, 1.4f, 4.4f);
            }

            // 5. Locate visual wheel transforms
            Transform flMesh = null, frMesh = null, blMesh = null, brMesh = null;
            foreach (Transform t in pickupGo.GetComponentsInChildren<Transform>(true))
            {
                string lower = t.name.ToLower();
                if (lower == "fl_wheel" || lower.Contains("frontleft")) flMesh = t;
                else if (lower == "fr_wheel" || lower.Contains("frontright")) frMesh = t;
                else if (lower == "bl_wheel" || lower.Contains("backleft")) blMesh = t;
                else if (lower == "br_wheel" || lower.Contains("backright")) brMesh = t;
            }

            Debug.Log($"[VehicleSetupUtility] Visual wheels found: FL={flMesh?.name}, FR={frMesh?.name}, BL={blMesh?.name}, BR={brMesh?.name}");

            // 6. Setup WheelColliders holder
            Transform collidersHolder = pickupGo.transform.Find("WheelColliders");
            if (collidersHolder == null)
            {
                GameObject holderGo = new GameObject("WheelColliders");
                holderGo.transform.SetParent(pickupGo.transform, false);
                collidersHolder = holderGo.transform;
            }

            // Helper to get or create WheelCollider
            WheelCollider flCol = SetupWheelCollider(collidersHolder, "FL_Collider", flMesh, pickupGo.transform);
            WheelCollider frCol = SetupWheelCollider(collidersHolder, "FR_Collider", frMesh, pickupGo.transform);
            WheelCollider blCol = SetupWheelCollider(collidersHolder, "BL_Collider", blMesh, pickupGo.transform);
            WheelCollider brCol = SetupWheelCollider(collidersHolder, "BR_Collider", brMesh, pickupGo.transform);

            // 7. Setup VehicleController
            VehicleController vc = pickupGo.GetComponent<VehicleController>();
            if (vc == null) vc = pickupGo.AddComponent<VehicleController>();

            vc.frontLeftCollider = flCol;
            vc.frontRightCollider = frCol;
            vc.rearLeftCollider = blCol;
            vc.rearRightCollider = brCol;

            vc.frontLeftMesh = flMesh;
            vc.frontRightMesh = frMesh;
            vc.rearLeftMesh = blMesh;
            vc.rearRightMesh = brMesh;

            vc.motorForce = 2200f;
            vc.brakeForce = 4000f;
            vc.maxSteerAngle = 35f;
            vc.steerSmoothSpeed = 8f;
            vc.centerOfMassOffset = new Vector3(0f, -0.25f, 0.15f);
            vc.antiRollForce = 4500f;
            vc.forwardGrip = 2.8f;
            vc.sidewaysGrip = 3.4f;
            vc.tractionAssist = 0.70f;
            vc.downforce = 140f;
            vc.coastBrakeTorque = 150f;
            vc.currentFuel = 100f;
            vc.maxFuel = 100f;

            // 8. Setup ExitPoint
            Transform exitPoint = pickupGo.transform.Find("ExitPoint");
            if (exitPoint == null)
            {
                GameObject exitGo = new GameObject("ExitPoint");
                exitGo.transform.SetParent(pickupGo.transform, false);
                exitPoint = exitGo.transform;
            }
            exitPoint.localPosition = new Vector3(-2.4f, 0.2f, 0.3f);

            // 9. Setup VehicleCameraTarget with VehicleCameraOrbit
            Transform camTarget = pickupGo.transform.Find("VehicleCameraTarget");
            if (camTarget == null)
            {
                GameObject camTargetGo = new GameObject("VehicleCameraTarget");
                camTargetGo.transform.SetParent(pickupGo.transform, false);
                camTarget = camTargetGo.transform;
            }
            camTarget.localPosition = new Vector3(0f, 1.4f, -0.5f);

            VehicleCameraOrbit orbit = camTarget.GetComponent<VehicleCameraOrbit>();
            if (orbit == null) orbit = camTarget.gameObject.AddComponent<VehicleCameraOrbit>();
            orbit.parentVehicle = vc;
            orbit.followRotationSpeed = 4.5f;

            // 10. Setup VehicleFollowCamera in scene
            GameObject vehicleCameraGo = GameObject.Find("VehicleFollowCamera");
            if (vehicleCameraGo == null)
            {
                // Try finding PlayerFollowCamera to clone Cinemachine settings
                GameObject playerCamGo = GameObject.Find("PlayerFollowCamera");
                if (playerCamGo != null)
                {
                    vehicleCameraGo = UnityEngine.Object.Instantiate(playerCamGo);
                    vehicleCameraGo.name = "VehicleFollowCamera";
                    Undo.RegisterCreatedObjectUndo(vehicleCameraGo, "Create VehicleFollowCamera");
                }
                else
                {
                    string vcamPrefabPath = "Assets/Starter Assets/Runtime/ThirdPersonController/Prefabs/PlayerFollowCamera.prefab";
                    GameObject vcamPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(vcamPrefabPath);
                    if (vcamPrefab != null)
                    {
                        vehicleCameraGo = (GameObject)PrefabUtility.InstantiatePrefab(vcamPrefab);
                        vehicleCameraGo.name = "VehicleFollowCamera";
                        Undo.RegisterCreatedObjectUndo(vehicleCameraGo, "Create VehicleFollowCamera");
                    }
                }
            }

            if (vehicleCameraGo != null)
            {
                CinemachineCamera cmCam = vehicleCameraGo.GetComponent<CinemachineCamera>();
                if (cmCam != null)
                {
                    cmCam.Target.TrackingTarget = camTarget;

                    var thirdPerson = vehicleCameraGo.GetComponent<CinemachineThirdPersonFollow>();
                    if (thirdPerson != null)
                    {
                        thirdPerson.CameraDistance = 7.0f;
                        thirdPerson.ShoulderOffset = new Vector3(0f, 0.45f, 0f);
                        thirdPerson.VerticalArmLength = 0.25f;
                        thirdPerson.CameraSide = 1.0f; // Centered directly behind the vehicle
                        thirdPerson.Damping = new Vector3(0.15f, 0.15f, 0.15f);
                    }
                }
                vehicleCameraGo.SetActive(false);
            }

            // 11. Setup VehicleInteraction
            VehicleInteraction vi = pickupGo.GetComponent<VehicleInteraction>();
            if (vi == null) vi = pickupGo.AddComponent<VehicleInteraction>();

            vi.vehicleController = vc;
            vi.exitPoint = exitPoint;
            vi.vehicleCamera = vehicleCameraGo;
            vi.playerCamera = GameObject.Find("PlayerFollowCamera");
            vi.interactionDistance = 3.5f;
            vi.legacyInteractKey = KeyCode.F;

            // 12. Adjust pickup position so it rests nicely on the road surface
            Vector3 pos = pickupGo.transform.position;
            if (pos.y < 0.5f)
            {
                pos.y = 0.65f;
                pickupGo.transform.position = pos;
            }

            string prefabPath = "Assets/Prefabs/Vehicle_Jeep.prefab";
            if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            {
                AssetDatabase.CreateFolder("Assets", "Prefabs");
            }
            PrefabUtility.SaveAsPrefabAssetAndConnect(pickupGo, prefabPath, InteractionMode.AutomatedAction);

            EditorUtility.SetDirty(pickupGo);
            if (vehicleCameraGo != null) EditorUtility.SetDirty(vehicleCameraGo);

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveOpenScenes();

            Debug.Log("[VehicleSetupUtility] Successfully set up, saved prefab, and saved active scene!");
        }

        private static WheelCollider SetupWheelCollider(Transform parent, string name, Transform referenceMesh, Transform vehicleRoot)
        {
            Transform existing = parent.Find(name);
            WheelCollider wc;
            if (existing != null)
            {
                wc = existing.GetComponent<WheelCollider>();
            }
            else
            {
                GameObject go = new GameObject(name);
                go.transform.SetParent(parent, false);
                wc = go.AddComponent<WheelCollider>();
            }

            if (referenceMesh != null)
            {
                // Position wheel collider at the reference mesh center in local coordinates
                Vector3 worldPos = referenceMesh.position;
                wc.transform.position = worldPos;
                wc.transform.rotation = vehicleRoot.rotation;
            }

            // Wheel physics parameters tailored for responsive arcade driving with weight and grip
            wc.mass = 45f;
            wc.radius = 0.42f;
            wc.wheelDampingRate = 0.8f;
            wc.suspensionDistance = 0.25f;

            JointSpring spring = wc.suspensionSpring;
            spring.spring = 26000f; // Softened spring to give visible body roll and weight pitch
            spring.damper = 2800f;
            spring.targetPosition = 0.45f;
            wc.suspensionSpring = spring;

            WheelFrictionCurve fFriction = wc.forwardFriction;
            fFriction.extremumSlip = 0.35f;
            fFriction.extremumValue = 1.25f;
            fFriction.asymptoteSlip = 0.80f;
            fFriction.asymptoteValue = 1.0f;
            fFriction.stiffness = 2.8f; // High traction
            wc.forwardFriction = fFriction;

            WheelFrictionCurve sFriction = wc.sidewaysFriction;
            sFriction.extremumSlip = 0.25f;
            sFriction.extremumValue = 1.35f;
            sFriction.asymptoteSlip = 0.65f;
            sFriction.asymptoteValue = 1.15f;
            sFriction.stiffness = 3.4f; // Solid lateral grip
            wc.sidewaysFriction = sFriction;

            return wc;
        }
    }
}
#endif

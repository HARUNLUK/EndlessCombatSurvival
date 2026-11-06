using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using EndlessCombat.AI;
using EndlessSurvival.Inventory;
using EndlessSurvival.World.POI;

namespace EndlessCombat.Combat.Editor
{
    /// <summary>
    /// Builds everything Package 1 (combat) needs as assets. Safe to run again: existing assets are updated in place.
    ///   - Animator: PistolUpperBody and Actions layers + triggers on StarterAssetsThirdPerson.controller
    ///   - Items: Pistol, Knife, Wooden Club, Baseball Bat, Rock, Grenade (+ ItemDatabase, loot tables)
    ///   - Weapon prefabs: Weapon_Pistol, Weapon_Knife, Weapon_WoodenClub, Weapon_BaseballBat (primitive shapes)
    ///   - Enemy variants: Enemy_Pistol, Enemy_Melee_Bat, Enemy_Melee_Knife and the mix in the camp prefabs
    ///   - Resources/CombatContent (throwables, fists, footsteps, hearing focus settings)
    /// Menu: Tools > Combat > Setup Package 1. Batch: -executeMethod EndlessCombat.Combat.Editor.Package1Setup.RunBatch
    /// </summary>
    public static class Package1Setup
    {
        private const string ControllerPath = "Assets/Starter Assets/Runtime/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";
        private const string WeaponMaskPath = "Assets/Animations/WeaponUpperBodyMask.mask";
        private const string Ual1Path = "Assets/_AssetPacks/Universal Animation Library[Standard]/Universal Animation Library[Standard]/Unity/UAL1_Standard.fbx";
        private const string Ual2Path = "Assets/_AssetPacks/Universal Animation Library 2[Standard]/Universal Animation Library 2[Standard]/Unity/UAL2_Standard.fbx";

        private const string ItemsDir = "Assets/Data/Items";
        private const string LootDir = "Assets/Data/Loot";
        private const string DatabasePath = "Assets/Data/ItemDatabase.asset";
        private const string WeaponsDir = "Assets/Prefabs/Weapons";
        private const string RiflePrefabPath = "Assets/Prefabs/Weapons/Weapon_AssaultRifle.prefab";
        private const string EnemyPath = "Assets/Prefabs/Enemies/Enemy.prefab";
        private const string ContentPath = "Assets/Resources/CombatContent.asset";
        private const string FootstepDir = "Assets/Starter Assets/Runtime/ThirdPersonController/Character/Sfx";

        private static readonly string[] CampPrefabs =
        {
            "Assets/Prefabs/PointsOfInterests/EnemyCamp_Campfire.prefab",
            "Assets/Prefabs/PointsOfInterests/EnemyCamp_Medium.prefab",
            "Assets/Prefabs/PointsOfInterests/EnemyCamp_Outpost.prefab"
        };

        private static readonly List<string> Log = new List<string>();

        [MenuItem("Tools/Combat/Setup Package 1 (weapons, melee, throwables, enemies)")]
        public static void RunMenu()
        {
            Run();
            EditorUtility.DisplayDialog("Package 1", string.Join("\n", Log), "OK");
        }

        public static void RunBatch()
        {
            Run();
            foreach (var line in Log) Debug.Log("[Package1Setup] " + line);
        }

        private static void Run()
        {
            Log.Clear();
            SetupAnimator();

            var items = CreateItems();
            CreateWeaponPrefabs(items);
            UpdateDatabase(items.Values);
            UpdateLootTables(items);
            var variants = CreateEnemyVariants(items);
            UpdateCamps(variants);
            CreateContent(items);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Log.Add("Done.");
        }

        // ================================================================== Animator

        private static void SetupAnimator()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null) { Log.Add("ERROR animator controller not found"); return; }
            // Weapon / action layers use their own mask WITH the waist and spine (Body), so a clip plays from the
            // waist up exactly as authored. UpperBodyMask (rifle layer) has Body turned off by the old crouch tools
            // and the rifle compensates with procedural chest bending; it is left as it is.
            var mask = GetOrCreateWeaponMask();

            var clips = LoadClips(Ual1Path).Concat(LoadClips(Ual2Path)).ToList();
            AnimationClip Clip(string name)
            {
                var c = clips.FirstOrDefault(x => x.name == name || x.name.EndsWith("|" + name));
                if (c == null) Log.Add("WARNING clip missing: " + name);
                return c;
            }

            string[] triggers =
            {
                CombatAnimator.PunchJab, CombatAnimator.PunchCross, CombatAnimator.MeleeSwing, CombatAnimator.MeleeStab,
                CombatAnimator.Throw, CombatAnimator.HitReact, CombatAnimator.PistolFire, CombatAnimator.PistolReload
            };
            foreach (var t in triggers)
            {
                if (!controller.parameters.Any(p => p.name == t))
                    controller.AddParameter(t, AnimatorControllerParameterType.Trigger);
            }

            if (!controller.parameters.Any(p => p.name == CombatAnimator.AimPitchParam))
                controller.AddParameter(CombatAnimator.AimPitchParam, AnimatorControllerParameterType.Float);

            RemoveLayer(controller, CombatAnimator.PistolLayerName);
            RemoveLayer(controller, CombatAnimator.MeleeLayerName);
            RemoveLayer(controller, CombatAnimator.ActionLayerName);

            // ---------------- Pistol upper body (only the pack's clips; no bone bending in code)
            var pistol = AddLayer(controller, CombatAnimator.PistolLayerName, mask);
            var sm = pistol.stateMachine;
            var idle = AddState(sm, "Pistol Idle", Clip("Pistol_Idle_Loop"), new Vector3(300, 0));

            // Aim: Pistol_Aim_Down / Neutral / Up blended by camera pitch (AimPitch -1..1)
            foreach (var old in AssetDatabase.LoadAllAssetsAtPath(ControllerPath).OfType<BlendTree>().Where(b => b.name == "Pistol Aim Pitch").ToList())
                Object.DestroyImmediate(old, true);
            var aimTree = new BlendTree
            {
                name = "Pistol Aim Pitch",
                blendType = BlendTreeType.Simple1D,
                blendParameter = CombatAnimator.AimPitchParam,
                useAutomaticThresholds = false,
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(aimTree, controller);
            aimTree.AddChild(Clip("Pistol_Aim_Down"), -1f);
            aimTree.AddChild(Clip("Pistol_Aim_Neutral"), 0f);
            aimTree.AddChild(Clip("Pistol_Aim_Up"), 1f);
            var aim = AddState(sm, "Pistol Aim", aimTree, new Vector3(300, 100));
            var shoot = AddState(sm, "Pistol Shoot", Clip("Pistol_Shoot"), new Vector3(560, 100));
            var reload = AddState(sm, "Pistol Reload", Clip("Pistol_Reload"), new Vector3(560, 0));
            sm.defaultState = idle;

            Transition(idle, aim, 0.12f).AddCondition(AnimatorConditionMode.If, 0, "IsAiming");
            Transition(aim, idle, 0.15f).AddCondition(AnimatorConditionMode.IfNot, 0, "IsAiming");
            Transition(aim, shoot, 0.04f).AddCondition(AnimatorConditionMode.If, 0, CombatAnimator.PistolFire);
            var shootBack = Transition(shoot, aim, 0.1f);
            shootBack.hasExitTime = true;
            shootBack.exitTime = 0.8f;
            Transition(shoot, shoot, 0.03f).AddCondition(AnimatorConditionMode.If, 0, CombatAnimator.PistolFire);
            var toReload = sm.AddAnyStateTransition(reload);
            toReload.AddCondition(AnimatorConditionMode.If, 0, CombatAnimator.PistolReload);
            toReload.duration = 0.1f;
            toReload.canTransitionToSelf = false;
            var reloadBack = Transition(reload, idle, 0.15f);
            reloadBack.hasExitTime = true;
            reloadBack.exitTime = 0.9f;

            // ---------------- Melee weapon hold: last frame of Sword_Block (both arms low, fist closed); swings play on the Actions layer above it
            var meleeLayer = AddLayer(controller, CombatAnimator.MeleeLayerName, mask);
            var hold = AddState(meleeLayer.stateMachine, "Melee Hold", Clip("Sword_Block"), new Vector3(300, 0));
            hold.speed = 0f;
            hold.cycleOffset = 0.98f;
            meleeLayer.stateMachine.defaultState = hold;

            // ---------------- One shot actions
            var actions = AddLayer(controller, CombatAnimator.ActionLayerName, mask);
            var asm = actions.stateMachine;
            var empty = asm.AddState("Empty", new Vector3(300, -100));
            empty.writeDefaultValues = false;
            asm.defaultState = empty;

            AddAction(asm, empty, "Punch Jab", Clip("Punch_Jab"), CombatAnimator.PunchJab, 0);
            AddAction(asm, empty, "Punch Cross", Clip("Punch_Cross"), CombatAnimator.PunchCross, 1);
            AddAction(asm, empty, "Melee Swing", Clip("Sword_Regular_A"), CombatAnimator.MeleeSwing, 2);
            AddAction(asm, empty, "Melee Stab", Clip("Punch_Cross"), CombatAnimator.MeleeStab, 3); // right hand thrust (knife is in the right hand)
            AddAction(asm, empty, "Throw", Clip("OverhandThrow"), CombatAnimator.Throw, 4);
            AddAction(asm, empty, "Hit React", Clip("Hit_Chest"), CombatAnimator.HitReact, 5);

            EditorUtility.SetDirty(controller);
            Log.Add("Animator: PistolUpperBody, MeleeUpperBody and Actions layers built.");
        }

        private static IEnumerable<AnimationClip> LoadClips(string path)
        {
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__"));
        }

        private static void RemoveLayer(AnimatorController controller, string name)
        {
            for (int i = controller.layers.Length - 1; i >= 0; i--)
            {
                if (controller.layers[i].name == name) controller.RemoveLayer(i);
            }
        }

        private static AvatarMask GetOrCreateWeaponMask()
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(WeaponMaskPath);
            if (mask == null)
            {
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, WeaponMaskPath);
            }
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                var part = (AvatarMaskBodyPart)i;
                bool on = part == AvatarMaskBodyPart.Body || part == AvatarMaskBodyPart.Head
                          || part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.RightArm
                          || part == AvatarMaskBodyPart.LeftFingers || part == AvatarMaskBodyPart.RightFingers
                          || part == AvatarMaskBodyPart.LeftHandIK || part == AvatarMaskBodyPart.RightHandIK;
                mask.SetHumanoidBodyPartActive(part, on);
            }
            EditorUtility.SetDirty(mask);
            Log.Add("Mask: WeaponUpperBodyMask (waist + spine + head + arms + fingers).");
            return mask;
        }

        private static AnimatorControllerLayer AddLayer(AnimatorController controller, string name, AvatarMask mask)
        {
            controller.AddLayer(name);
            var layers = controller.layers;
            int index = layers.Length - 1;
            layers[index].avatarMask = mask;
            layers[index].defaultWeight = 0f;
            layers[index].blendingMode = AnimatorLayerBlendingMode.Override;
            layers[index].iKPass = false;
            controller.layers = layers;
            return controller.layers[index];
        }

        private static AnimatorState AddState(AnimatorStateMachine sm, string name, Motion motion, Vector3 position)
        {
            var state = sm.AddState(name, position);
            state.motion = motion;
            return state;
        }

        private static AnimatorStateTransition Transition(AnimatorState from, AnimatorState to, float duration)
        {
            var t = from.AddTransition(to);
            t.hasExitTime = false;
            t.duration = duration;
            return t;
        }

        private static void AddAction(AnimatorStateMachine sm, AnimatorState empty, string name, Motion clip, string trigger, int row)
        {
            var state = AddState(sm, name, clip, new Vector3(600, row * 70 - 100));
            var enter = sm.AddAnyStateTransition(state);
            enter.AddCondition(AnimatorConditionMode.If, 0, trigger);
            enter.duration = 0.06f;
            enter.canTransitionToSelf = true;
            var back = state.AddTransition(empty);
            back.hasExitTime = true;
            back.exitTime = 0.88f;
            back.duration = 0.12f;
        }

        // ================================================================== Items

        private static Dictionary<string, ItemDefinition> CreateItems()
        {
            var items = new Dictionary<string, ItemDefinition>();
            //               key            name           category                  kg    val  color                              scale
            items["Pistol"] = Item("Pistol", "Pistol", ItemCategory.Weapon, 1.0f, 90, new Color(0.18f, 0.18f, 0.2f), new Vector3(0.25f, 0.15f, 0.06f), "Light handgun. Weaker than a rifle, quick to draw.");
            items["Knife"] = Item("Knife", "Knife", ItemCategory.Weapon, 0.3f, 25, new Color(0.7f, 0.72f, 0.75f), new Vector3(0.3f, 0.04f, 0.06f), "Fast, short reach.");
            items["WoodenClub"] = Item("WoodenClub", "Wooden Club", ItemCategory.Weapon, 1.0f, 8, new Color(0.45f, 0.3f, 0.15f), new Vector3(0.7f, 0.07f, 0.07f), "A heavy stick. Better than fists.");
            items["BaseballBat"] = Item("BaseballBat", "Baseball Bat", ItemCategory.Weapon, 1.1f, 30, new Color(0.75f, 0.6f, 0.4f), new Vector3(0.85f, 0.08f, 0.08f), "Slow swing, hits hard.");
            items["Rock"] = Item("Rock", "Rock", ItemCategory.Throwable, 0.3f, 0, new Color(0.5f, 0.5f, 0.48f), new Vector3(0.14f, 0.12f, 0.14f), "Throw it [T] to make a noise somewhere else.");
            items["Grenade"] = Item("Grenade", "Grenade", ItemCategory.Throwable, 0.4f, 60, new Color(0.25f, 0.35f, 0.2f), new Vector3(0.12f, 0.15f, 0.12f), "Throw it [G]. Explodes after three seconds.");

            foreach (var key in new[] { "Pistol", "Knife", "WoodenClub", "BaseballBat" })
            {
                items[key].equipSlot = EquipSlot.Weapon;
                EditorUtility.SetDirty(items[key]);
            }
            Log.Add("Items: Pistol, Knife, Wooden Club, Baseball Bat, Rock, Grenade.");
            return items;
        }

        private static ItemDefinition Item(string key, string displayName, ItemCategory category, float weight, int value,
            Color color, Vector3 scale, string description)
        {
            string path = $"{ItemsDir}/Item_{key}.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            bool isNew = item == null;
            if (isNew) item = ScriptableObject.CreateInstance<ItemDefinition>();

            item.displayName = displayName;
            item.category = category;
            item.weight = weight;
            item.value = value;
            item.useAmount = 1;
            item.worldColor = color;
            item.worldScale = scale;
            item.description = description;

            if (isNew) AssetDatabase.CreateAsset(item, path);
            else EditorUtility.SetDirty(item);
            return item;
        }

        // ================================================================== Weapon prefabs

        private struct HandFrame
        {
            public Vector3 localPosition;   // root transform relative to the hand socket
            public Quaternion localRotation;
            public Vector3 localScale;
            public Vector3 hand;            // hand point in root space
            public Quaternion forward;      // barrel direction in root space
            public float scale;             // root uniform scale (children compensate)
        }

        private static void CreateWeaponPrefabs(Dictionary<string, ItemDefinition> items)
        {
            var rifleGo = AssetDatabase.LoadAssetAtPath<GameObject>(RiflePrefabPath);
            var rifle = rifleGo != null ? rifleGo.GetComponent<WeaponController>() : null;
            if (rifle == null) { Log.Add("ERROR rifle prefab not found: " + RiflePrefabPath); return; }

            HandFrame frame = ReadHandFrame(rifle);

            items["Pistol"].weaponPrefab = BuildPistol(frame, rifle);
            items["Knife"].weaponPrefab = BuildMelee("Knife", "Knife", frame, 0.32f, 0.018f, true, new Color(0.7f, 0.72f, 0.75f),
                new MeleeStats { damage = 25f, range = 1.15f, radius = 0.5f, attackInterval = 0.45f, hitDelay = 0.18f, staminaCost = 8f, noiseRadius = 3f, knockback = 1f, staggerTime = 0.3f, stab = true });
            items["WoodenClub"].weaponPrefab = BuildMelee("WoodenClub", "Wooden Club", frame, 0.7f, 0.03f, false, new Color(0.45f, 0.3f, 0.15f),
                new MeleeStats { damage = 20f, range = 1.5f, radius = 0.6f, attackInterval = 0.7f, hitDelay = 0.32f, staminaCost = 12f, noiseRadius = 6f, knockback = 2.5f, staggerTime = 0.5f, stab = false });
            items["BaseballBat"].weaponPrefab = BuildMelee("BaseballBat", "Baseball Bat", frame, 0.85f, 0.035f, false, new Color(0.75f, 0.6f, 0.4f),
                new MeleeStats { damage = 35f, range = 1.6f, radius = 0.65f, attackInterval = 0.9f, hitDelay = 0.38f, staminaCost = 16f, noiseRadius = 7f, knockback = 3.5f, staggerTime = 0.7f, stab = false });

            foreach (var key in new[] { "Pistol", "Knife", "WoodenClub", "BaseballBat" }) EditorUtility.SetDirty(items[key]);
            Log.Add("Weapon prefabs: Weapon_Pistol, Weapon_Knife, Weapon_WoodenClub, Weapon_BaseballBat.");
        }

        private static HandFrame ReadHandFrame(WeaponController rifle)
        {
            Transform root = rifle.transform;
            var f = new HandFrame
            {
                localPosition = root.localPosition,
                localRotation = root.localRotation,
                localScale = root.localScale,
                scale = Mathf.Max(0.0001f, root.localScale.x)
            };

            // The prefab root is stored relative to the hand socket: the socket origin in root space is the hand
            f.hand = Quaternion.Inverse(root.localRotation) * (-root.localPosition) / f.scale;

            Transform muzzle = rifle.MuzzlePoint;
            Quaternion muzzleWorld = muzzle != null ? muzzle.rotation : root.rotation;
            f.forward = Quaternion.Inverse(root.rotation) * muzzleWorld;
            return f;
        }

        private struct FistGrip
        {
            public Vector3 axis;    // shaft direction in weapon root space (out of the thumb side)
            public Vector3 center;  // point inside the fist in weapon root space
            public Vector3 fingers; // finger direction in weapon root space
        }

        private static FistGrip? _fistGrip;

        /// <summary>
        /// Measures the right fist on the enemy prefab (same skeleton and WeaponSocket as the player):
        /// knuckle line little -> index proximal = handle axis, grip centre a bit towards the palm and fingertips.
        /// Falls back to the rifle grip frame when the bones are missing.
        /// </summary>
        private static FistGrip ReadFistGrip(HandFrame f)
        {
            if (_fistGrip.HasValue) return _fistGrip.Value;
            var fallback = new FistGrip
            {
                axis = (f.forward * Quaternion.Euler(-90f, 0f, 0f)) * Vector3.forward,
                center = f.hand,
                fingers = f.forward * Vector3.forward
            };

            const string enemyPath = "Assets/Prefabs/Enemies/Enemy.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(enemyPath) == null) { _fistGrip = fallback; return fallback; }
            var contents = PrefabUtility.LoadPrefabContents(enemyPath);
            try
            {
                var animator = contents.GetComponentInChildren<Animator>();
                Transform socket = contents.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.Trim() == "WeaponSocket");
                Transform B(HumanBodyBones b) => animator != null && animator.isHuman ? animator.GetBoneTransform(b) : null;
                Transform hand = B(HumanBodyBones.RightHand), index = B(HumanBodyBones.RightIndexProximal),
                    middle = B(HumanBodyBones.RightMiddleProximal), little = B(HumanBodyBones.RightLittleProximal),
                    thumb = B(HumanBodyBones.RightThumbProximal);
                if (socket == null || hand == null || index == null || middle == null || little == null)
                {
                    Log.Add("Fist grip: finger bones or WeaponSocket missing, using rifle grip frame.");
                    _fistGrip = fallback;
                    return fallback;
                }

                // Close the fist with a pack clip that grips a handle, then take the hinge axis the middle finger curls
                // around (= the handle axis) and the centre of the curl (= where the handle passes).
                Vector3 axisW, centerW;
                Vector3 fingersW = (middle.position - hand.position).normalized;
                Transform midI = B(HumanBodyBones.RightMiddleIntermediate), midD = B(HumanBodyBones.RightMiddleDistal);
                var gripClip = LoadClips(Ual1Path).FirstOrDefault(c => c.name == "Armature|Sword_Idle");
                if (gripClip != null && midI != null && midD != null)
                {
                    gripClip.SampleAnimation(animator.gameObject, gripClip.length * 0.5f);
                    Vector3 a = midI.position - middle.position, b = midD.position - midI.position;
                    axisW = Vector3.Cross(a, b).normalized;
                    if (Vector3.Dot(axisW, index.position - little.position) < 0f) axisW = -axisW; // out of the thumb side
                    Vector3 tip = midD.position + b.normalized * 0.02f;
                    centerW = (middle.position + midI.position + midD.position + tip) / 4f;
                    fingersW = Vector3.ProjectOnPlane(middle.position - hand.position, axisW).normalized;
                }
                else
                {
                    axisW = Vector3.ProjectOnPlane(index.position - little.position, fingersW).normalized;
                    centerW = (index.position + middle.position + little.position) / 3f + fingersW * 0.03f;
                }

                // world -> socket space -> weapon root space (root sits at f.localPosition/localRotation/scale under the socket)
                Quaternion inv = Quaternion.Inverse(f.localRotation);
                Vector3 ToRootDir(Vector3 w) => (inv * socket.InverseTransformDirection(w)).normalized;
                Vector3 ToRootPoint(Vector3 w) => inv * (socket.InverseTransformPoint(w) - f.localPosition) / f.scale;
                var g = new FistGrip { axis = ToRootDir(axisW), center = ToRootPoint(centerW), fingers = ToRootDir(fingersW) };
                Log.Add($"Fist grip measured: axis {g.axis}, centre {g.center}.");
                _fistGrip = g;
                return g;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static GameObject NewRoot(string name, HandFrame f)
        {
            var root = new GameObject(name);
            root.transform.localPosition = f.localPosition;
            root.transform.localRotation = f.localRotation;
            root.transform.localScale = f.localScale;
            return root;
        }

        /// <summary>Adds a collider-less primitive in root space, sized in meters (compensates the root scale).</summary>
        private static GameObject Part(GameObject root, PrimitiveType type, string name, Vector3 localPos, Quaternion localRot, Vector3 meters,
            Color color, float rootScale)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = meters / rootScale;

            var mat = GetMaterial(name, color);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private static Material GetMaterial(string name, Color color)
        {
            string path = $"{WeaponsDir}/Mat_Weapon_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.color = color;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static WeaponController BuildPistol(HandFrame f, WeaponController rifle)
        {
            string path = $"{WeaponsDir}/Weapon_Pistol.prefab";
            var root = NewRoot("Weapon_Pistol", f);
            float s = f.scale;
            Vector3 fwd = f.forward * Vector3.forward;
            Vector3 up = f.forward * Vector3.up;
            var dark = new Color(0.18f, 0.18f, 0.2f);

            Part(root, PrimitiveType.Cube, "PistolSlide", f.hand + (fwd * 0.07f + up * 0.05f) / s, f.forward, new Vector3(0.035f, 0.045f, 0.2f), dark, s);
            Part(root, PrimitiveType.Cube, "PistolGrip", f.hand, f.forward * Quaternion.Euler(-15f, 0f, 0f), new Vector3(0.03f, 0.11f, 0.045f), new Color(0.12f, 0.12f, 0.12f), s);

            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(root.transform, false);
            muzzle.transform.localPosition = f.hand + (fwd * 0.18f + up * 0.05f) / s;
            muzzle.transform.localRotation = f.forward;

            // Borrow the rifle's muzzle flash, sounds and impact effect
            var rifleSo = new SerializedObject(rifle);
            var flash = rifleSo.FindProperty("muzzleFlash").objectReferenceValue as ParticleSystem;
            ParticleSystem flashCopy = null;
            if (flash != null)
            {
                var copy = Object.Instantiate(flash.gameObject, muzzle.transform, false);
                copy.name = "MuzzleFlash";
                copy.transform.localPosition = Vector3.zero;
                copy.transform.localRotation = Quaternion.identity;
                copy.transform.localScale = Vector3.one * 0.6f / s;
                flashCopy = copy.GetComponent<ParticleSystem>();
            }

            var weapon = root.AddComponent<WeaponController>();
            var so = new SerializedObject(weapon);
            so.FindProperty("weaponName").stringValue = "Pistol";
            so.FindProperty("weaponType").enumValueIndex = (int)WeaponType.Pistol;
            so.FindProperty("isAutomatic").boolValue = false;
            so.FindProperty("aimYawOffset").floatValue = 0f;
            so.FindProperty("useLeftHandGrip").boolValue = false;
            so.FindProperty("damage").floatValue = 18f;
            so.FindProperty("fireRate").floatValue = 0.25f;
            so.FindProperty("range").floatValue = 80f;
            so.FindProperty("infiniteAmmo").boolValue = false;
            so.FindProperty("magazineCapacity").intValue = 12;
            so.FindProperty("currentAmmo").intValue = 0;
            so.FindProperty("reserveAmmo").intValue = 0;
            so.FindProperty("reloadDuration").floatValue = 1.6f;
            so.FindProperty("muzzlePoint").objectReferenceValue = muzzle.transform;
            so.FindProperty("muzzleFlash").objectReferenceValue = flashCopy;
            foreach (var prop in new[] { "impactPrefab", "shootSound", "reloadSound", "emptySound" })
                so.FindProperty(prop).objectReferenceValue = rifleSo.FindProperty(prop).objectReferenceValue;
            so.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<WeaponController>();
        }

        private static WeaponController BuildMelee(string key, string displayName, HandFrame f, float length, float radius, bool blade,
            Color color, MeleeStats stats)
        {
            string path = $"{WeaponsDir}/Weapon_{key}.prefab";
            var root = NewRoot($"Weapon_{key}", f);
            float s = f.scale;

            // The handle runs through the closed fist along the knuckle line (little finger -> index finger), measured
            // on the character's real finger bones, and the weapon sticks out of the thumb side, like holding a bat.
            FistGrip g = ReadFistGrip(f);
            Vector3 dir = g.axis;
            Vector3 grip = g.center;
            Quaternion along = Quaternion.LookRotation(dir, g.fingers); // local forward -> shaft, local up -> fingers
            const float handle = 0.09f;
            if (blade)
            {
                Part(root, PrimitiveType.Cube, $"{key}Handle", grip + dir * (0.02f / s), along, new Vector3(0.025f, 0.03f, 0.11f), new Color(0.15f, 0.1f, 0.08f), s);
                Part(root, PrimitiveType.Cube, $"{key}Blade", grip + dir * ((handle + (length - handle) * 0.5f) / s), along,
                    new Vector3(0.006f, 0.03f, length - handle), color, s);
            }
            else
            {
                Quaternion cyl = Quaternion.FromToRotation(Vector3.up, dir); // cylinder Y axis -> shaft
                Part(root, PrimitiveType.Cylinder, $"{key}Body", grip + dir * ((length * 0.5f - handle) / s), cyl,
                    new Vector3(radius * 2f, length * 0.5f, radius * 2f), color, s);
            }

            var weapon = root.AddComponent<WeaponController>();
            var so = new SerializedObject(weapon);
            so.FindProperty("weaponName").stringValue = displayName;
            so.FindProperty("weaponType").enumValueIndex = (int)WeaponType.Melee;
            so.FindProperty("isAutomatic").boolValue = false;
            so.FindProperty("useLeftHandGrip").boolValue = false;
            so.FindProperty("infiniteAmmo").boolValue = true;
            so.FindProperty("magazineCapacity").intValue = 0;
            so.FindProperty("currentAmmo").intValue = 0;
            so.FindProperty("reserveAmmo").intValue = 0;
            so.FindProperty("damage").floatValue = stats.damage;
            so.ApplyModifiedPropertiesWithoutUndo();

            var melee = root.AddComponent<MeleeWeapon>();
            melee.stats = stats;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<WeaponController>();
        }

        // ================================================================== Database and loot

        private static void UpdateDatabase(IEnumerable<ItemDefinition> newItems)
        {
            var db = AssetDatabase.LoadAssetAtPath<ItemDatabase>(DatabasePath);
            if (db == null) { Log.Add("ERROR ItemDatabase not found"); return; }

            var list = new List<ItemDefinition>(db.items ?? new ItemDefinition[0]);
            foreach (var item in newItems)
                if (!list.Contains(item)) list.Add(item);
            db.items = list.ToArray();
            EditorUtility.SetDirty(db);
        }

        private static void UpdateLootTables(Dictionary<string, ItemDefinition> items)
        {
            AddLoot("Loot_Roadside", items, ("Pistol", 1.2f, 1, 1), ("Knife", 2f, 1, 1), ("WoodenClub", 3f, 1, 1),
                ("BaseballBat", 1.5f, 1, 1), ("Rock", 6f, 1, 3), ("Grenade", 0.6f, 1, 1));
            AddLoot("Loot_Chest", items, ("Pistol", 4f, 1, 1), ("Knife", 3f, 1, 1), ("BaseballBat", 2f, 1, 1),
                ("Grenade", 3f, 1, 2), ("Rock", 2f, 1, 3));
            // Enemies drop the weapon they carry; grenades come from them now and then
            AddLoot("Loot_EnemyDrop", items, ("Grenade", 2.5f, 1, 1));
            Log.Add("Loot: roadside, chest and enemy drop tables extended.");
        }

        private static void AddLoot(string table, Dictionary<string, ItemDefinition> items, params (string key, float weight, int min, int max)[] entries)
        {
            var t = AssetDatabase.LoadAssetAtPath<LootTable>($"{LootDir}/{table}.asset");
            if (t == null) { Log.Add("WARNING loot table missing: " + table); return; }
            foreach (var e in entries)
            {
                var item = items[e.key];
                if (t.entries.Exists(x => x.item == item)) continue;
                t.entries.Add(new LootEntry { item = item, weight = e.weight, minAmount = e.min, maxAmount = e.max });
            }
            EditorUtility.SetDirty(t);
        }

        // ================================================================== Enemies

        private static List<GameObject> CreateEnemyVariants(Dictionary<string, ItemDefinition> items)
        {
            var result = new List<GameObject>();
            var baseEnemy = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPath);
            if (baseEnemy == null) { Log.Add("ERROR enemy prefab not found"); return result; }

            result.Add(Variant(baseEnemy, "Enemy_Pistol", items["Pistol"], e =>
            {
                e.maxHealth = 90f;
                e.attackRange = 12f;
                e.attackCooldown = 0.9f;
                e.aimSpreadClose = 1f;
                e.aimSpreadFar = 3.5f;
            }));
            result.Add(Variant(baseEnemy, "Enemy_Melee_Bat", items["BaseballBat"], e =>
            {
                e.maxHealth = 120f;
                e.moveSpeed = 3.6f;
                e.attackCooldown = 1.2f;
                e.meleeRange = 1.5f;
                e.meleeChargeSpeedMultiplier = 1.6f;
            }));
            result.Add(Variant(baseEnemy, "Enemy_Melee_Knife", items["Knife"], e =>
            {
                e.maxHealth = 100f;
                e.moveSpeed = 3.8f;
                e.attackCooldown = 0.8f;
                e.meleeRange = 1.2f;
                e.meleeChargeSpeedMultiplier = 1.7f;
            }));
            Log.Add("Enemy variants: Enemy_Pistol, Enemy_Melee_Bat, Enemy_Melee_Knife.");
            return result;
        }

        private static GameObject Variant(GameObject baseEnemy, string name, ItemDefinition weaponItem, System.Action<EnemyController> configure)
        {
            string path = $"Assets/Prefabs/Enemies/{name}.prefab";

            void Configure(GameObject go)
            {
                var enemy = go.GetComponent<EnemyController>();
                enemy.heldWeaponItem = weaponItem;
                enemy.equipHeldWeaponOnSpawn = true;
                configure(enemy);
                EditorUtility.SetDirty(enemy);
            }

            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            {
                var contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    Configure(contents);
                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }

            // New variant of the base enemy (keeps model, dialogue, loot and all later changes of Enemy.prefab)
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(baseEnemy);
            instance.name = name;
            Configure(instance);
            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, path);
            Object.DestroyImmediate(instance);
            return prefab;
        }

        private static void UpdateCamps(List<GameObject> variants)
        {
            var baseEnemy = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPath);
            if (baseEnemy == null || variants.Count < 3) return;

            // Weights by repetition (seeded rng.Pick): rifle 3, pistol 2, bat 1, knife 1
            var mix = new[] { baseEnemy, baseEnemy, baseEnemy, variants[0], variants[0], variants[1], variants[2] };

            foreach (var path in CampPrefabs)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var camp = root.GetComponentInChildren<EnemyCampPOI>(true);
                    if (camp != null)
                    {
                        camp.enemyPrefabs = mix;
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            Log.Add("Camps: enemy mix rifle 3 / pistol 2 / bat 1 / knife 1.");
        }

        // ================================================================== Content asset

        private static void CreateContent(Dictionary<string, ItemDefinition> items)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");

            var content = AssetDatabase.LoadAssetAtPath<CombatContent>(ContentPath);
            if (content == null)
            {
                content = ScriptableObject.CreateInstance<CombatContent>();
                AssetDatabase.CreateAsset(content, ContentPath);
            }

            content.rockItem = items["Rock"];
            content.grenadeItem = items["Grenade"];
            content.testItems = new[] { items["Pistol"], items["Knife"], items["WoodenClub"], items["BaseballBat"] };

            var steps = new List<AudioClip>();
            for (int i = 1; i <= 10; i++)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{FootstepDir}/Player_Footstep_{i:00}.wav");
                if (clip != null) steps.Add(clip);
            }
            content.footstepClips = steps.ToArray();
            content.landingClip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{FootstepDir}/Player_Land.wav");

            EditorUtility.SetDirty(content);
            Log.Add($"Resources/CombatContent: {steps.Count} footstep clips.");
        }
    }
}

using System.Collections.Generic;
using System.IO;
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
    /// Souls-like melee setup (see Docs/Melee_Souls_Combat.md). Safe to run again.
    ///   Setup:           animation sockets asset (empty sockets get UAL placeholders), Mixamo import settings for
    ///                    Assets/Animations/Combat, animator layers on every humanoid controller, shield items + loot,
    ///                    weapon categories, shield enemy variants in the camps.
    ///   Rebuild Animator: only the layers (after dropping new clips into the sockets).
    /// Batch: -executeMethod EndlessCombat.Combat.Editor.MeleeCombatSetup.RunBatch / RebuildBatch
    /// </summary>
    public static class MeleeCombatSetup
    {
        private const string PlayerControllerPath = "Assets/Animations/Character/CharacterAnimator.controller";
        private const string SetPath = "Assets/Resources/MeleeAnimationSet.asset";
        private const string WeaponMaskPath = "Assets/Animations/WeaponUpperBodyMask.mask";
        private const string ShieldMaskPath = "Assets/Animations/ShieldArmMask.mask";
        private const string ClipDir = "Assets/Animations/Combat";
        private const string HitReactionPath = "Assets/Animations/Survival/Hit Reaction.fbx";
        private const string Ual1Path = "Assets/_AssetPacks/Universal Animation Library[Standard]/Universal Animation Library[Standard]/Unity/UAL1_Standard.fbx";
        private const string Ual2Path = "Assets/_AssetPacks/Universal Animation Library 2[Standard]/Universal Animation Library 2[Standard]/Unity/UAL2_Standard.fbx";
        private const string ItemsDir = "Assets/Data/Items";
        private const string LootDir = "Assets/Data/Loot";
        private const string DatabasePath = "Assets/Data/ItemDatabase.asset";
        private const string ContentPath = "Assets/Resources/CombatContent.asset";
        private const string EnemyPath = "Assets/Prefabs/Enemies/Enemy.prefab";

        private static readonly string[] CampPrefabs =
        {
            "Assets/Prefabs/PointsOfInterests/EnemyCamp_Campfire.prefab",
            "Assets/Prefabs/PointsOfInterests/EnemyCamp_Medium.prefab",
            "Assets/Prefabs/PointsOfInterests/EnemyCamp_Outpost.prefab"
        };

        private static readonly List<string> Log = new List<string>();

        [MenuItem("Tools/Combat/Melee/Setup (sockets, animator, shields, enemies)")]
        public static void RunMenu()
        {
            Run(true);
            EditorUtility.DisplayDialog("Melee Setup", string.Join("\n", Log), "OK");
        }

        [MenuItem("Tools/Combat/Melee/Rebuild Animator (after changing clips)")]
        public static void RebuildMenu()
        {
            Run(false);
            EditorUtility.DisplayDialog("Melee Animator", string.Join("\n", Log), "OK");
        }

        [MenuItem("Tools/Combat/Melee/Select Animation Sockets")]
        public static void SelectSockets()
        {
            var set = AssetDatabase.LoadAssetAtPath<MeleeAnimationSet>(SetPath);
            if (set == null) { EditorUtility.DisplayDialog("Melee", "Run Tools > Combat > Melee > Setup first.", "OK"); return; }
            Selection.activeObject = set;
            EditorGUIUtility.PingObject(set);
        }

        public static void RunBatch()
        {
            Run(true);
            foreach (var line in Log) Debug.Log("[MeleeCombatSetup] " + line);
        }

        public static void RebuildBatch()
        {
            Run(false);
            foreach (var line in Log) Debug.Log("[MeleeCombatSetup] " + line);
        }

        private static void Run(bool full)
        {
            Log.Clear();
            PrepareClipFolder();
            var set = GetOrCreateSet(full);
            UseProjectHitReaction(set);
            Validate(set);
            foreach (var controller in HumanoidControllers()) BuildLayers(controller, set);

            if (full)
            {
                var shields = CreateShieldItems();
                UpdateDatabase(shields.Values);
                AddLoot("Loot_Roadside", (shields["WoodenShield"], 1.2f));
                AddLoot("Loot_Chest", (shields["WoodenShield"], 1.5f), (shields["RiotShield"], 0.8f));
                AddTestItems(shields.Values);
                SetWeaponCategories();
                var variants = CreateEnemyVariants(shields);
                AddToCamps(variants);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Log.Add("Done.");
        }

        // ================================================================== Mixamo clip folder

        private static void PrepareClipFolder()
        {
            if (!AssetDatabase.IsValidFolder(ClipDir)) AssetDatabase.CreateFolder("Assets/Animations", "Combat");

            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ClipDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                if (importer == null || importer.animationType == ModelImporterAnimationType.Human) continue;

                // First import only: later hand edits in the import settings are kept
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                importer.SaveAndReimport();

                string file = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                bool loop = file.Contains("idle") || file.Contains("loop") || file.Contains("guard") || file.Contains("block idle");
                var clips = importer.defaultClipAnimations;
                foreach (var c in clips)
                {
                    c.loopTime = loop;
                    c.loopPose = loop;
                    c.lockRootRotation = true;
                    c.keepOriginalOrientation = true;
                    c.lockRootHeightY = true;
                    c.keepOriginalPositionY = true;
                    c.lockRootPositionXZ = false; // XZ goes to root motion, which is switched off: the code moves the body
                }
                importer.clipAnimations = clips;
                importer.SaveAndReimport();
                Log.Add($"Mixamo import set: {Path.GetFileName(path)}{(loop ? " (loop)" : "")}");
            }
        }

        // ================================================================== sockets asset

        private static MeleeAnimationSet GetOrCreateSet(bool fillPlaceholders)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            var set = AssetDatabase.LoadAssetAtPath<MeleeAnimationSet>(SetPath);
            bool isNew = set == null;
            if (isNew)
            {
                set = ScriptableObject.CreateInstance<MeleeAnimationSet>();
                set.twoHand.light1.speed = set.twoHand.light2.speed = set.twoHand.light3.speed = 0.85f;
                set.twoHand.heavy.speed = 0.8f;
                set.twoHand.light1.hyperArmor = true;
                AssetDatabase.CreateAsset(set, SetPath);
                Log.Add("Created " + SetPath);
            }
            if (!fillPlaceholders && !isNew) return set;

            var clips = LoadClips(Ual1Path).Concat(LoadClips(Ual2Path)).ToList();
            AnimationClip Clip(string name)
            {
                var c = clips.FirstOrDefault(x => x.name == name || x.name.EndsWith("|" + name));
                if (c == null) Log.Add("WARNING placeholder clip missing: " + name);
                return c;
            }
            int filled = 0;
            void Fill(ref AnimationClip slot, string name)
            {
                if (slot != null || name == null) return;
                slot = Clip(name);
                if (slot != null) filled++;
            }

            Fill(ref set.fists.light1.clip, "Punch_Jab");
            Fill(ref set.fists.light2.clip, "Punch_Cross");
            Fill(ref set.fists.heavy.clip, "Melee_Hook");
            Fill(ref set.shortBlade.light1.clip, "Punch_Cross");
            Fill(ref set.shortBlade.light2.clip, "Sword_Regular_A");
            Fill(ref set.shortBlade.heavy.clip, "Sword_Attack");
            Fill(ref set.oneHand.light1.clip, "Sword_Regular_A");
            Fill(ref set.oneHand.light2.clip, "Sword_Regular_B");
            Fill(ref set.oneHand.light3.clip, "Sword_Regular_C");
            Fill(ref set.oneHand.heavy.clip, "Sword_Attack");
            Fill(ref set.twoHand.light1.clip, "Sword_Regular_C");
            Fill(ref set.twoHand.light2.clip, "Sword_Regular_A");
            Fill(ref set.twoHand.heavy.clip, "Sword_Attack");
            Fill(ref set.hitLight.clip, "Hit_Chest");
            Fill(ref set.hitHeavy.clip, "Hit_Knockback");
            Fill(ref set.guardBreak.clip, "Idle_Shield_Break");
            Fill(ref set.rebound.clip, "Hit_Head");
            Fill(ref set.shieldGuard.clip, "Idle_Shield_Loop");
            Fill(ref set.weaponGuard.clip, "Sword_Block");
            if (filled > 0) Log.Add($"Sockets: {filled} empty sockets got UAL placeholder clips (replace them with Mixamo clips).");

            EditorUtility.SetDirty(set);
            return set;
        }

        /// <summary>
        /// The light hit reaction uses the user's Mixamo "Hit Reaction" clip (Npc_Hit, retarget fixed with the real
        /// Mixamo rest pose by Setup Survival Animations), the same clip enemies played before the souls-like combat.
        /// Only a UAL placeholder or an empty socket is replaced; a clip the user dropped in by hand is kept.
        /// </summary>
        private static void UseProjectHitReaction(MeleeAnimationSet set)
        {
            var current = set.hitLight.clip;
            if (current != null)
            {
                string path = AssetDatabase.GetAssetPath(current);
                if (path != Ual1Path && path != Ual2Path) return;
            }
            var hit = AssetDatabase.LoadAllAssetsAtPath(HitReactionPath).OfType<AnimationClip>()
                .FirstOrDefault(c => c.name == EndlessSurvival.Survival.SurvivalAnimClips.NpcHit);
            if (hit == null)
            {
                Log.Add("WARNING " + EndlessSurvival.Survival.SurvivalAnimClips.NpcHit + " not found in " + HitReactionPath + " (run Endless Survival > Setup Survival Animations first). Hit Light keeps its placeholder.");
                return;
            }
            set.hitLight.clip = hit;
            EditorUtility.SetDirty(set);
            Log.Add("Hit Light socket: " + hit.name + " (Mixamo Hit Reaction, same clip as the enemies' old hit react).");
        }

        private static void Validate(MeleeAnimationSet set)
        {
            void Pose(PoseSlot p, string name)
            {
                if (p.clip != null && p.holdAt < 0f && !p.clip.isLooping)
                    Log.Add($"WARNING {name}: clip '{p.clip.name}' has no Loop Time. Tick Loop Time in its import settings or set Hold At.");
            }
            Pose(set.shieldGuard, "Shield Guard");
            Pose(set.shieldCarry, "Shield Carry");
            Pose(set.weaponGuard, "Weapon Guard");
            // No rolls (2026-11-09): side steps / dash / hop back. Empty sockets still move, without an animation.
            if (set.dodgeLeft.clip == null || set.dodgeRight.clip == null || set.dodgeForward.clip == null || set.backstep.clip == null)
                Log.Add("Dodge sockets: some are empty (Mixamo Standing Dodge Left / Right / Forward / Backward); those dodges move without an animation.");
        }

        private static IEnumerable<AnimationClip> LoadClips(string path)
        {
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__"));
        }

        // ================================================================== animator

        private static IEnumerable<AnimatorController> HumanoidControllers()
        {
            var set = new HashSet<AnimatorController>();
            var player = AssetDatabase.LoadAssetAtPath<AnimatorController>(PlayerControllerPath);
            if (player != null) set.Add(player);
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Enemies" }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab == null) continue;
                foreach (var a in prefab.GetComponentsInChildren<Animator>(true))
                    if (a.runtimeAnimatorController is AnimatorController ac) set.Add(ac);
            }
            return set;
        }

        private static void BuildLayers(AnimatorController controller, MeleeAnimationSet set)
        {
            if (!controller.parameters.Any(p => p.name == MeleeAnimationSet.SpeedParam))
            {
                controller.AddParameter(new AnimatorControllerParameter
                {
                    name = MeleeAnimationSet.SpeedParam,
                    type = AnimatorControllerParameterType.Float,
                    defaultFloat = 1f
                });
            }

            // Only the three Combat* layers are rebuilt. Every other layer (base locomotion, rifle/pistol/melee upper
            // body, Actions with the Mixamo hit react, SurvivalFullBody) must stay exactly as Setup Survival Animations
            // and the earlier setups left it: snapshot before, compare after.
            string before = OtherLayersSnapshot(controller);

            RemoveLayer(controller, MeleeAnimationSet.LayerShield);
            RemoveLayer(controller, MeleeAnimationSet.LayerGuard);
            RemoveLayer(controller, MeleeAnimationSet.LayerAttack);
            RemoveLayer(controller, MeleeAnimationSet.LayerFullBody);

            // ---------------- left arm: shield carry / raised
            var shieldLayer = AddLayer(controller, MeleeAnimationSet.LayerShield, GetOrCreateShieldMask());
            var ssm = shieldLayer.stateMachine;
            ssm.defaultState = Empty(ssm);
            AddPose(ssm, MeleeAnimationSet.StateShieldCarry, set.shieldCarry, 0);
            AddPose(ssm, MeleeAnimationSet.StateShieldGuard, set.shieldGuard, 1);

            // ---------------- upper body: guard with the weapon
            var guardLayer = AddLayer(controller, MeleeAnimationSet.LayerGuard, AssetDatabase.LoadAssetAtPath<AvatarMask>(WeaponMaskPath));
            var gsm = guardLayer.stateMachine;
            gsm.defaultState = Empty(gsm);
            AddPose(gsm, MeleeAnimationSet.StateWeaponGuard, set.weaponGuard, 0);
            if (set.twoHandCarry.clip != null) AddPose(gsm, MeleeAnimationSet.StateTwoHandCarry, set.twoHandCarry, 1);

            // ---------------- upper body: attacks (the legs keep the base stance, no jumping feet)
            var attackLayer = AddLayer(controller, MeleeAnimationSet.LayerAttack, AssetDatabase.LoadAssetAtPath<AvatarMask>(WeaponMaskPath));
            var asm = attackLayer.stateMachine;
            asm.defaultState = Empty(asm);
            int row = 0, count = 0;
            foreach (var cat in new[] { MeleeCategory.Fists, MeleeCategory.Short, MeleeCategory.OneHand, MeleeCategory.TwoHand })
            {
                var slots = set.Category(cat);
                count += AddAction(asm, MeleeAnimationSet.AttackState(cat, MeleeAnimationSet.MoveLight1), slots.light1.clip, row++);
                count += AddAction(asm, MeleeAnimationSet.AttackState(cat, MeleeAnimationSet.MoveLight2), slots.light2.clip, row++);
                count += AddAction(asm, MeleeAnimationSet.AttackState(cat, MeleeAnimationSet.MoveLight3), slots.light3.clip, row++);
                count += AddAction(asm, MeleeAnimationSet.AttackState(cat, MeleeAnimationSet.MoveHeavy), slots.heavy.clip, row++);
            }

            // ---------------- full body (top layer): dodges, reactions
            var fullLayer = AddLayer(controller, MeleeAnimationSet.LayerFullBody, null);
            var fsm = fullLayer.stateMachine;
            fsm.defaultState = Empty(fsm);
            row = 0;
            count += AddAction(fsm, MeleeAnimationSet.StateDodgeLeft, set.dodgeLeft.clip, row++);
            count += AddAction(fsm, MeleeAnimationSet.StateDodgeRight, set.dodgeRight.clip, row++);
            count += AddAction(fsm, MeleeAnimationSet.StateDodgeForward, set.dodgeForward.clip, row++);
            count += AddAction(fsm, MeleeAnimationSet.StateBackstep, set.backstep.clip, row++);
            count += AddAction(fsm, MeleeAnimationSet.StateHitLight, set.hitLight.clip, row++);
            count += AddAction(fsm, MeleeAnimationSet.StateHitHeavy, set.hitHeavy.clip, row++);
            count += AddAction(fsm, MeleeAnimationSet.StateGuardHit, set.guardHit.clip, row++);
            count += AddAction(fsm, MeleeAnimationSet.StateGuardBreak, set.guardBreak.clip, row++);
            count += AddAction(fsm, MeleeAnimationSet.StateRebound, set.rebound.clip, row++);

            EditorUtility.SetDirty(controller);
            Log.Add($"Animator {controller.name}: combat layers built ({count} full body states).");

            string after = OtherLayersSnapshot(controller);
            Log.Add(before == after
                ? $"Animator {controller.name}: other layers unchanged."
                : $"WARNING Animator {controller.name}: a non-combat layer changed!\nBEFORE:\n{before}\nAFTER:\n{after}");
        }

        private static bool IsCombatLayer(string name) =>
            name == MeleeAnimationSet.LayerShield || name == MeleeAnimationSet.LayerGuard || name == MeleeAnimationSet.LayerFullBody
            || name == MeleeAnimationSet.LayerAttack;

        /// <summary>Name, weight, mask, blending, IK and every state's motion / write defaults of the non-combat layers.</summary>
        private static string OtherLayersSnapshot(AnimatorController controller)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var layer in controller.layers)
            {
                if (IsCombatLayer(layer.name)) continue;
                sb.Append(layer.name).Append(" w=").Append(layer.defaultWeight).Append(" mask=")
                  .Append(layer.avatarMask != null ? layer.avatarMask.name : "-").Append(" blend=").Append(layer.blendingMode)
                  .Append(" ik=").Append(layer.iKPass).Append(" sync=").Append(layer.syncedLayerIndex).AppendLine();
                if (layer.stateMachine == null) continue;
                foreach (var child in layer.stateMachine.states.OrderBy(s => s.state.name))
                    sb.Append("  ").Append(child.state.name).Append(" = ")
                      .Append(child.state.motion != null ? child.state.motion.name : "-")
                      .Append(" wd=").Append(child.state.writeDefaultValues)
                      .Append(" tr=").Append(child.state.transitions.Length).AppendLine();
            }
            return sb.ToString();
        }

        private static AnimatorState Empty(AnimatorStateMachine sm)
        {
            var empty = sm.AddState(MeleeAnimationSet.EmptyState, new Vector3(300, -120));
            empty.writeDefaultValues = false;
            return empty;
        }

        private static int AddAction(AnimatorStateMachine sm, string name, AnimationClip clip, int row)
        {
            if (clip == null) return 0;
            var state = sm.AddState(name, new Vector3(600 + (row / 16) * 260, (row % 16) * 55 - 120));
            state.motion = clip;
            state.writeDefaultValues = false;
            state.speedParameterActive = true;
            state.speedParameter = MeleeAnimationSet.SpeedParam;
            return 1;
        }

        private static void AddPose(AnimatorStateMachine sm, string name, PoseSlot pose, int row)
        {
            if (pose.clip == null) return;
            var state = sm.AddState(name, new Vector3(600, row * 60 - 120));
            state.motion = pose.clip;
            state.writeDefaultValues = false;
            if (pose.holdAt >= 0f)
            {
                state.speed = 0f;
                state.cycleOffset = Mathf.Clamp(pose.holdAt, 0f, 0.99f);
            }
        }

        private static void RemoveLayer(AnimatorController controller, string name)
        {
            for (int i = controller.layers.Length - 1; i >= 0; i--)
                if (controller.layers[i].name == name) controller.RemoveLayer(i);
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

        private static AvatarMask GetOrCreateShieldMask()
        {
            var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(ShieldMaskPath);
            if (mask == null)
            {
                mask = new AvatarMask();
                AssetDatabase.CreateAsset(mask, ShieldMaskPath);
            }
            for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
            {
                var part = (AvatarMaskBodyPart)i;
                mask.SetHumanoidBodyPartActive(part, part == AvatarMaskBodyPart.LeftArm || part == AvatarMaskBodyPart.LeftFingers);
            }
            EditorUtility.SetDirty(mask);
            return mask;
        }

        // ================================================================== shields

        private static Dictionary<string, ItemDefinition> CreateShieldItems()
        {
            var items = new Dictionary<string, ItemDefinition>
            {
                ["WoodenShield"] = Shield("WoodenShield", "Tahta Kalkan", 3.5f, 20, new Color(0.5f, 0.34f, 0.18f), new Vector3(0.5f, 0.6f, 0.05f),
                    "Sol kola takılır. Sağ tıkı basılı tut: önden gelen yakın dövüş darbelerinin çoğunu durdurur. Mermiye karşı zayıf.",
                    ShieldStats.Wooden),
                ["RiotShield"] = Shield("RiotShield", "Çevik Kuvvet Kalkanı", 8f, 120, new Color(0.22f, 0.26f, 0.32f), new Vector3(0.55f, 1.0f, 0.04f),
                    "Büyük polis kalkanı. Önden gelen mermileri neredeyse tamamen durdurur, zayıf darbeler sekip saldıranı sendeletir. " +
                    "Ağırdır: kalkıkken yavaş yürürsün, kaçınma kısa ve pahalı olur. Tabancayla nişan alırken de kalkıktır.",
                    ShieldStats.Riot)
            };
            Log.Add("Items: Tahta Kalkan, Çevik Kuvvet Kalkanı.");
            return items;
        }

        private static ItemDefinition Shield(string key, string displayName, float weight, int value, Color color, Vector3 size, string description, ShieldStats stats)
        {
            string path = $"{ItemsDir}/Item_{key}.asset";
            var item = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
            bool isNew = item == null;
            if (isNew) item = ScriptableObject.CreateInstance<ItemDefinition>();

            item.displayName = displayName;
            item.category = ItemCategory.Apparel;
            item.equipSlot = EquipSlot.Shield;
            item.armor = 0f;
            item.weight = weight;
            item.value = value;
            item.useAmount = 1;
            item.worldColor = color;
            item.worldScale = size;
            item.description = description;
            item.shield = stats;

            if (isNew) AssetDatabase.CreateAsset(item, path);
            else EditorUtility.SetDirty(item);
            return item;
        }

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

        private static void AddLoot(string table, params (ItemDefinition item, float weight)[] entries)
        {
            var t = AssetDatabase.LoadAssetAtPath<LootTable>($"{LootDir}/{table}.asset");
            if (t == null) { Log.Add("WARNING loot table missing: " + table); return; }
            foreach (var e in entries)
            {
                if (t.entries.Exists(x => x.item == e.item)) continue;
                t.entries.Add(new LootEntry { item = e.item, weight = e.weight, minAmount = 1, maxAmount = 1 });
            }
            EditorUtility.SetDirty(t);
            Log.Add("Loot: shields added to " + table);
        }

        private static void AddTestItems(IEnumerable<ItemDefinition> items)
        {
            var content = AssetDatabase.LoadAssetAtPath<CombatContent>(ContentPath);
            if (content == null) { Log.Add("WARNING CombatContent missing (run Setup Package 1)"); return; }
            var list = new List<ItemDefinition>(content.testItems ?? new ItemDefinition[0]);
            foreach (var item in items)
                if (!list.Contains(item)) list.Add(item);
            content.testItems = list.ToArray();
            EditorUtility.SetDirty(content);
            Log.Add("Test kit: shields added (one of each in the backpack at scene start).");
        }

        // ================================================================== weapons

        private static void SetWeaponCategories()
        {
            SetCategory("Assets/Prefabs/Weapons/Weapon_Knife.prefab", MeleeCategory.Short);
            SetCategory("Assets/Prefabs/Weapons/Weapon_WoodenClub.prefab", MeleeCategory.OneHand);
            SetCategory("Assets/Prefabs/Weapons/Weapon_BaseballBat.prefab", MeleeCategory.TwoHand);
        }

        private static void SetCategory(string path, MeleeCategory category)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { Log.Add("WARNING weapon prefab missing: " + path); return; }
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var melee = root.GetComponent<MeleeWeapon>();
                if (melee == null) return;
                melee.stats.category = category;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Log.Add($"{Path.GetFileNameWithoutExtension(path)}: {category}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ================================================================== enemies

        private static List<GameObject> CreateEnemyVariants(Dictionary<string, ItemDefinition> shields)
        {
            var result = new List<GameObject>();
            var baseEnemy = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPath);
            var club = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemsDir}/Item_WoodenClub.asset");
            var pistol = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemsDir}/Item_Pistol.asset");
            if (baseEnemy == null || club == null || pistol == null) { Log.Add("ERROR enemy prefab or weapon items missing"); return result; }

            result.Add(Variant(baseEnemy, "Enemy_Melee_Shield", club, shields["WoodenShield"], e =>
            {
                e.maxHealth = 110f;
                e.moveSpeed = 3.4f;
                e.attackCooldown = 1.1f;
                e.meleeRange = 1.5f;
                e.meleeChargeSpeedMultiplier = 1.5f;
            }));
            result.Add(Variant(baseEnemy, "Enemy_Riot", pistol, shields["RiotShield"], e =>
            {
                e.maxHealth = 110f;
                e.moveSpeed = 3f;
                e.attackRange = 11f;
                e.attackCooldown = 1.1f;
                e.aimSpreadClose = 1.5f;
                e.aimSpreadFar = 4f;
            }));
            Log.Add("Enemy variants: Enemy_Melee_Shield (club + wooden shield), Enemy_Riot (pistol + riot shield).");
            return result;
        }

        private static GameObject Variant(GameObject baseEnemy, string name, ItemDefinition weaponItem, ItemDefinition shieldItem, System.Action<EnemyController> configure)
        {
            string path = $"Assets/Prefabs/Enemies/{name}.prefab";

            void Configure(GameObject go)
            {
                var enemy = go.GetComponent<EnemyController>();
                enemy.heldWeaponItem = weaponItem;
                enemy.equipHeldWeaponOnSpawn = true;
                enemy.heldShieldItem = shieldItem;
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

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(baseEnemy);
            instance.name = name;
            Configure(instance);
            var prefab = PrefabUtility.SaveAsPrefabAsset(instance, path);
            Object.DestroyImmediate(instance);
            return prefab;
        }

        private static void AddToCamps(List<GameObject> variants)
        {
            if (variants.Count == 0) return;
            foreach (var path in CampPrefabs)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var camp = root.GetComponentInChildren<EnemyCampPOI>(true);
                    if (camp == null) continue;
                    var list = new List<GameObject>(camp.enemyPrefabs ?? new GameObject[0]);
                    bool changed = false;
                    foreach (var v in variants)
                    {
                        if (v == null || list.Contains(v)) continue;
                        list.Add(v);
                        changed = true;
                    }
                    if (!changed) continue;
                    camp.enemyPrefabs = list.ToArray();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            Log.Add("Camps: shield enemies added to the mix (one each).");
        }
    }
}

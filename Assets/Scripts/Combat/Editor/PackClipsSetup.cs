using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EndlessCombat.Combat.Editor
{
    /// <summary>
    /// Puts the Asset Store pack clips (Kevin Iglesias Human Animations FREE) into the melee sockets in place of the
    /// UAL placeholders, then rebuilds the animator. Nothing is authored: the clips are used as they are, only the
    /// hit window is measured from the clip (the frame where the right hand moves fastest).
    /// Menu: Tools/Combat/Melee/Use Pack Clips (Kevin Iglesias)
    /// Batch: -executeMethod EndlessCombat.Combat.Editor.PackClipsSetup.RunBatch
    /// </summary>
    public static class PackClipsSetup
    {
        private const string SetPath = "Assets/Resources/MeleeAnimationSet.asset";
        private const string Dir = "Assets/Kevin Iglesias/Human Animations/Animations/Male/Combat/";
        private const string ModelPath = "Assets/Kevin Iglesias/Human Animations/Models/HumanM_Model.fbx";

        private const string Attack1H = Dir + "1H/HumanM@Attack1H01_R.fbx";
        private const string Attack2H = Dir + "2H/HumanM@Attack2H01.fbx";
        private const string AttackPolearm = Dir + "Polearm/HumanM@AttackPolearm01.fbx";
        private const string Damage01 = Dir + "HumanM@Damage01.fbx";
        private const string Damage02 = Dir + "HumanM@Damage02.fbx";
        private const string CombatDamage = Dir + "HumanM@CombatDamage01.fbx";

        private const string Ual2 = "Assets/_AssetPacks/Universal Animation Library 2[Standard]/Universal Animation Library 2[Standard]/Unity/UAL2_Standard.fbx";

        /// <summary>Raised when the clip choice below changes: the sockets and the animator are rebuilt once by itself.</summary>
        public const int ClipSetVersion = 4;

        private static readonly List<string> Log = new List<string>();

        /// <summary>
        /// After a compile: when the sockets were filled by an older version of this tool, fill them again (waits
        /// until the editor is not in play mode). No menu click needed.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void AutoUpdate()
        {
            EditorApplication.delayCall += TryAutoUpdate;
        }

        private static void TryAutoUpdate()
        {
            var set = AssetDatabase.LoadAssetAtPath<MeleeAnimationSet>(SetPath);
            if (set == null || set.clipSetVersion >= ClipSetVersion) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                // Try again when play mode ends
                EditorApplication.playModeStateChanged -= OnPlayMode;
                EditorApplication.playModeStateChanged += OnPlayMode;
                return;
            }
            Run();
            foreach (var l in Log) Debug.Log("[PackClipsSetup] (auto) " + l);
        }

        private static void OnPlayMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.playModeStateChanged -= OnPlayMode;
            EditorApplication.delayCall += TryAutoUpdate;
        }

        private static AnimationClip UalClip(string name)
        {
            var c = AssetDatabase.LoadAllAssetsAtPath(Ual2).OfType<AnimationClip>().FirstOrDefault(x => x.name == "Armature|" + name);
            if (c == null) Log.Add("missing UAL2 clip: " + name);
            return c;
        }

        [MenuItem("Tools/Combat/Melee/Use Pack Clips (Kevin Iglesias)")]
        public static void RunMenu()
        {
            Run();
            EditorUtility.DisplayDialog("Pack Clips", string.Join("\n", Log), "OK");
        }

        public static void RunBatch()
        {
            Run();
            foreach (var l in Log) Debug.Log("[PackClipsSetup] " + l);
        }

        private static void Run()
        {
            Log.Clear();
            var set = AssetDatabase.LoadAssetAtPath<MeleeAnimationSet>(SetPath);
            if (set == null) { Log.Add("MeleeAnimationSet not found, run Tools > Combat > Melee > Setup first."); return; }

            AnimationClip a1h = Clip(Attack1H), a2h = Clip(Attack2H), pole = Clip(AttackPolearm);
            AnimationClip d1 = Clip(Damage01), d2 = Clip(Damage02), cd = Clip(CombatDamage);
            if (a1h == null || a2h == null) { Log.Add("Kevin Iglesias clips missing, nothing changed."); return; }

            Undo.RecordObject(set, "Use pack clips");

            // The combo uses different moves (2026-11-10, the user: "every weapon does the same single move"):
            // side swing -> overhead blow -> forward thrust, heavy = slow overhead blow. Only pack clips, nothing authored.
            if (pole == null) pole = a1h;
            // One handed weapons must stay in ONE hand for the whole combo (2026-11-12, the user: "the first swing is
            // one handed, the second one grabs the club with both hands"). The pack has a single one handed attack,
            // so the 2nd and 3rd moves and the heavy blow come from the Universal Animation Library's one handed
            // sword set (right hand only): backhand from above, diagonal cut, wide side cut.
            AnimationClip regA = UalClip("Sword_Regular_A"), regB = UalClip("Sword_Regular_B"), regC = UalClip("Sword_Regular_C");
            if (regA == null) regA = a1h;
            if (regB == null) regB = a1h;
            if (regC == null) regC = a1h;
            // One handed (club, plank, machete): side swing -> overhead backhand -> diagonal cut, heavy = wide slow cut
            Attack(set.oneHand.light1, a1h, 1f);
            Attack(set.oneHand.light2, regB, 1.15f);
            Attack(set.oneHand.light3, regC, 1.15f);
            Attack(set.oneHand.heavy, regA, 0.8f);
            // Short (knife), always one hand: quick swing -> diagonal cut -> quick swing, heavy = overhead backhand
            Attack(set.shortBlade.light1, a1h, 1.25f);
            Attack(set.shortBlade.light2, regC, 1.4f);
            Attack(set.shortBlade.light3, a1h, 1.35f);
            Attack(set.shortBlade.heavy, regB, 1f);
            // Two handed (baseball bat, or a club gripped with both hands), 2026-11-12 v3, souls-like chain:
            // wide cut from the right -> backhand from the left -> diagonal cut, heavy = slow straight overhead blow.
            // The swing clips are the one handed sword set; HandGripIK keeps the left hand on the handle through
            // the whole swing, so they read as two handed swings. The overhead clip is a real two handed one.
            Attack(set.twoHand.light1, regA, 0.95f);
            Attack(set.twoHand.light2, regB, 1f);
            Attack(set.twoHand.light3, regC, 1f);
            Attack(set.twoHand.heavy, a2h, 0.8f);
            // Rest stance with a two handed grip: the pack's two handed combat idle on the upper body
            AnimationClip idle2h = Clip(Dir + "2H/HumanM@CombatIdle2H01.fbx");
            if (idle2h != null)
            {
                set.twoHandCarry.clip = idle2h;
                set.twoHandCarry.holdAt = idle2h.isLooping ? -1f : 0.5f;
            }

            // Reactions (the light hit keeps the user's own Mixamo Hit Reaction)
            if (d2 != null) Reaction(set.hitHeavy, d2, "Hit Heavy");
            if (cd != null) Reaction(set.guardHit, cd, "Guard Hit");
            if (d1 != null) Reaction(set.rebound, d1, "Rebound");

            set.clipSetVersion = ClipSetVersion;
            EditorUtility.SetDirty(set);
            AssetDatabase.SaveAssets();
            Log.Add("Sockets saved. Rebuilding the animator...");
            MeleeCombatSetup.RebuildBatch();
            Log.Add("Done.");
        }

        private static AnimationClip Clip(string path)
        {
            var c = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(x => !x.name.StartsWith("__preview__"));
            if (c == null) Log.Add("missing: " + path);
            return c;
        }

        private static void Reaction(ReactionSlot slot, AnimationClip clip, string label)
        {
            slot.clip = clip;
            slot.speed = 1f;
            Log.Add($"{label}: {clip.name} ({clip.length:0.00}s)");
        }

        private static void Attack(AttackSlot slot, AnimationClip clip, float speed)
        {
            slot.clip = clip;
            slot.speed = speed;
            if (MeasureSwing(clip, out float peak, out float start, out float end))
            {
                slot.hitStart = start;
                slot.hitEnd = end;
                // Free again a bit after the hit (the rest of the clip is the return to guard)
                slot.recoverAt = Mathf.Clamp(end + 0.3f, 0.6f, 0.95f);
                Log.Add($"{clip.name} x{speed:0.0}: right hand fastest at {peak:0.00}, hit window {start:0.00}-{end:0.00}, free at {slot.recoverAt:0.00}");
            }
            else
            {
                Log.Add($"{clip.name}: swing not measured, keeps hit window {slot.hitStart:0.00}-{slot.hitEnd:0.00}");
            }
        }

        /// <summary>
        /// Samples the clip on the pack's own character and finds where the right hand moves fastest relative to
        /// the hips. The hit window is the span around it where the hand is above 55% of that speed.
        /// </summary>
        private static bool MeasureSwing(AnimationClip clip, out float peak, out float start, out float end)
        {
            peak = start = end = 0f;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (prefab == null) return false;
            var go = Object.Instantiate(prefab);
            go.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                var animator = go.GetComponent<Animator>();
                if (animator == null || !animator.isHuman) return false;
                Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (hand == null || hips == null) return false;

                const int steps = 60;
                var speeds = new float[steps];
                Vector3 prev = Vector3.zero;
                AnimationMode.StartAnimationMode();
                try
                {
                    for (int i = 0; i <= steps; i++)
                    {
                        float t = clip.length * i / steps;
                        AnimationMode.BeginSampling();
                        AnimationMode.SampleAnimationClip(go, clip, t);
                        AnimationMode.EndSampling();
                        Vector3 p = hips.InverseTransformPoint(hand.position);
                        if (i > 0) speeds[i - 1] = (p - prev).magnitude;
                        prev = p;
                    }
                }
                finally
                {
                    AnimationMode.StopAnimationMode();
                }

                int best = 0;
                for (int i = 1; i < steps; i++) if (speeds[i] > speeds[best]) best = i;
                if (speeds[best] < 1e-5f) return false;
                float limit = speeds[best] * 0.55f;
                int a = best, b = best;
                while (a > 0 && speeds[a - 1] >= limit) a--;
                while (b < steps - 1 && speeds[b + 1] >= limit) b++;
                peak = (best + 0.5f) / steps;
                start = a / (float)steps;
                end = (b + 1) / (float)steps;
                // At least 12% of the clip, centred on the fastest frame (a few frames only would miss at low fps)
                float mid = peak;
                if (end - start < 0.12f) { start = mid - 0.06f; end = mid + 0.06f; }
                start = Mathf.Clamp(start, 0.1f, 0.85f);
                end = Mathf.Clamp(end, start + 0.05f, 0.92f);
                return true;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}

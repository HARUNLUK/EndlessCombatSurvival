using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace EndlessCombat.Character.Editor
{
    /// <summary>
    /// Batch check of the player body in Game.unity (2026-11-18): avatar, renderers, and the idle clip sampled on the
    /// player's Animator at a few times; logs hips / head / hands / upper arm direction so a lying, flying or
    /// arms-up body shows in numbers. Does not save the scene.
    /// Batch: -executeMethod EndlessCombat.Character.Editor.SurvivorPoseCheck.Run
    /// </summary>
    public static class SurvivorPoseCheck
    {
        [MenuItem("Endless Survival/Player Model: Check Pose")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Game.unity", OpenSceneMode.Single);
            var clip = AssetDatabase.LoadAllAssetsAtPath(SurvivorIdleSetup.FbxPath).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Animator a in root.GetComponentsInChildren<Animator>(true))
                {
                    if (a.GetComponentInParent<PlayerMotor>(true) == null) continue;
                    Transform t = a.transform;
                    var rends = a.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    Debug.Log($"[PoseCheck] player '{a.name}' avatar={(a.avatar ? a.avatar.name : "none")} human={(a.avatar && a.avatar.isHuman)} " +
                              $"controller={(a.runtimeAnimatorController ? a.runtimeAnimatorController.name : "none")} skinned={rends.Length} " +
                              $"active={a.gameObject.activeInHierarchy} enabledRenderers={rends.Count(r => r.enabled)} " +
                              $"twist={a.GetComponent<TwistBones>() != null} muscle={a.GetComponent<MuscleBones>() != null}");
                    foreach (var r in rends)
                        Debug.Log($"[PoseCheck]   renderer {r.name}: mesh={(r.sharedMesh ? r.sharedMesh.name : "NULL")} " +
                                  $"bones={r.bones.Length} nullBones={r.bones.Count(x => x == null)} rootBone={(r.rootBone ? r.rootBone.name : "NULL")} " +
                                  $"mats={string.Join(",", r.sharedMaterials.Select(m => m ? m.name : "NULL"))} layer={LayerMask.LayerToName(r.gameObject.layer)}");
                    Report(a, "bind pose");
                    if (clip == null) { Debug.Log("[PoseCheck] idle clip missing"); continue; }
                    foreach (float time in new[] { 0f, 2f, 5f })
                    {
                        clip.SampleAnimation(a.gameObject, time);
                        Report(a, "idle t=" + time);
                    }
                }
        }

        private static void Report(Animator a, string label)
        {
            Transform t = a.transform;
            Vector3 L(HumanBodyBones b) { Transform x = a.GetBoneTransform(b); return x ? t.InverseTransformPoint(x.position) : Vector3.zero; }
            Vector3 hips = L(HumanBodyBones.Hips), head = L(HumanBodyBones.Head);
            Vector3 hl = L(HumanBodyBones.LeftHand), hr = L(HumanBodyBones.RightHand);
            Vector3 ual = L(HumanBodyBones.LeftUpperArm), lal = L(HumanBodyBones.LeftLowerArm);
            float armFromDown = Vector3.Angle(lal - ual, Vector3.down);
            Debug.Log($"[PoseCheck] {label}: hips {hips:F2} head {head:F2} handL {hl:F2} handR {hr:F2} " +
                      $"upperArmL from down {armFromDown:F0} deg (0 = hanging, 90 = level)");
        }
    }
}

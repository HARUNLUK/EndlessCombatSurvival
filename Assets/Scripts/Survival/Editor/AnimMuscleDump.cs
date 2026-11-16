using System.Text;
using UnityEditor;
using UnityEngine;

namespace EndlessSurvival.Survival.Editor
{
    /// <summary>Debug: prints a few humanoid muscle values of each survival clip at 50% (PoseShots/anim_muscles.txt).</summary>
    public static class AnimMuscleDump
    {
        public static void RunBatch()
        {
            var sb = new StringBuilder();
            string[] want = { "Left Upper Leg Front-Back", "Left Lower Leg Stretch", "Right Upper Leg Front-Back", "Right Lower Leg Stretch", "RootT.y" };
            foreach (var folder in new[] { "Assets/Animations/Survival", "Assets/Animations" })
            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { folder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (folder == "Assets/Animations" && path.Contains("/Survival/")) continue;
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (!(o is AnimationClip c) || c.name.StartsWith("__preview") || !c.isHumanMotion) continue;
                    if (c.name != "Surv_KneelFire" && c.name != "Surv_Toilet") continue;
                    foreach (float fr in new[] { 0.1f, 0.3f, 0.5f, 0.7f })
                    {
                        sb.Append((c.name + " @" + fr).PadRight(26));
                        foreach (var b in AnimationUtility.GetCurveBindings(c))
                        {
                            if (System.Array.IndexOf(want, b.propertyName) < 0) continue;
                            var curve = AnimationUtility.GetEditorCurve(c, b);
                            sb.Append($" {b.propertyName}={curve.Evaluate(c.length * fr):0.00}");
                        }
                        sb.AppendLine();
                    }
                }
            }
            System.IO.File.WriteAllText("PoseShots/anim_muscles.txt", sb.ToString());
            EditorApplication.Exit(0);
        }
    }
}

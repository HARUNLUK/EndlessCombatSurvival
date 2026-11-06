#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using EndlessSurvival.World.Road;

namespace EndlessSurvival.World.Editor
{
    /// <summary>
    /// Builds the first N chunks of a seed in edit mode and writes what came out (types, road, seams, terrain checksum,
    /// generated object counts). Two reports of the same seed must be identical; used to verify chunk system refactors.
    /// Batch: Unity -batchmode -executeMethod EndlessSurvival.World.Editor.ChunkPlanReport.RunBatch -reportPath X -reportSeed S -reportCount N
    /// </summary>
    public static class ChunkPlanReport
    {
        private const string ScenePath = "Assets/Scenes/Game.unity";

        [MenuItem("Endless Survival/Chunk Planı Raporu (konsola)", false, 40)]
        public static void RunFromMenu()
        {
            ChunkManager mgr = Object.FindAnyObjectByType<ChunkManager>();
            if (mgr == null) { Debug.LogError("[ChunkPlanReport] Sahnede ChunkManager yok."); return; }
            Debug.Log(Build(mgr, string.IsNullOrEmpty(mgr.masterSeed) ? "report" : mgr.masterSeed, 12));
        }

        public static void RunBatch()
        {
            string path = Arg("-reportPath", "ChunkPlanReport.txt");
            string seed = Arg("-reportSeed", "report");
            int count = int.Parse(Arg("-reportCount", "12"));

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            ChunkManager mgr = Object.FindAnyObjectByType<ChunkManager>();
            if (mgr == null) { Debug.LogError("[ChunkPlanReport] Sahnede ChunkManager yok."); EditorApplication.Exit(1); return; }

            var sb = new StringBuilder();
            foreach (string s in seed.Split(','))
            {
                sb.Append(Build(mgr, s, count));
                sb.AppendLine();
            }
            File.WriteAllText(path, sb.ToString());
            Debug.Log($"[ChunkPlanReport] Yazıldı: {path}");
        }

        private static string Arg(string name, string fallback)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return fallback;
        }

        /// <summary>Also write POI positions, the first vegetation objects and terrain height rows.</summary>
        public static bool Detail;

        public static string Build(ChunkManager mgr, string seed, int count)
        {
            Detail = Arg("-reportDetail", "0") == "1";
            string oldSeed = mgr.masterSeed;
            mgr.masterSeed = seed;
            var sb = new StringBuilder();
            sb.AppendLine($"=== seed '{seed}' ({count} chunks)");
            try
            {
                mgr.SpawnEditorPreviewChunks(count);
                for (int i = 0; i < mgr.ActiveChunks.Count; i++)
                {
                    Chunk c = mgr.ActiveChunks[i];
                    if (c == null) { sb.AppendLine($"[{i}] null"); continue; }
                    sb.AppendLine($"[{i}] {c.name} type={c.TypeName} biome={c.biomeType} flat={c.flatSeamEdges} mode={c.generationMode}");
                    sb.AppendLine($"    sea={c.seaSide} coastStart={c.coastContinuesAtStart} coastEnd={c.coastContinuesAtEnd} variant={c.mountainVariant} " +
                                  $"layout={c.mountainLayout} uphillR={c.uphillOnRight} lowland={c.foothillLowland}");
                    sb.AppendLine($"    road {c.roadType}/{c.crossSectionType}/{c.currentElevationType} p={c.currentElevationParam:0.###} " +
                                  $"entry=({c.roadEntryX:0.##},{c.roadEntryY:0.##}) exit=({c.roadExitX:0.##},{c.roadExitY:0.##}) " +
                                  $"pos={c.transform.position.x:0.##},{c.transform.position.y:0.##},{c.transform.position.z:0.##}");
                    sb.AppendLine($"    seamOffset={mgr.SeamRoadOffset(i):0.###} seamHeight={mgr.SeamRoadHeight(i, c.baseElevation):0.###}");

                    Terrain t = c.GetComponentInChildren<Terrain>();
                    if (t != null && t.terrainData != null)
                    {
                        TerrainData td = t.terrainData;
                        int res = td.heightmapResolution;
                        float[,] h = td.GetHeights(0, 0, res, res);
                        double sum = 0;
                        for (int z = 0; z < res; z += 4)
                            for (int x = 0; x < res; x += 4)
                                sum += h[z, x] * (1 + ((x * 31 + z * 17) % 7));
                        var layers = td.terrainLayers;
                        var names = new StringBuilder();
                        foreach (var l in layers) names.Append(l != null ? l.name : "null").Append(',');
                        sb.AppendLine($"    terrain hsum={sum:0.####} trees={td.treeInstanceCount} layers={names}");
                    }

                    var spline = c.GetComponentInChildren<RoadSpline>();
                    if (spline != null)
                    {
                        var p = new StringBuilder();
                        for (int k = 0; k <= 4; k++)
                        {
                            Vector3 v = spline.GetPointAtZ(c.chunkLength * k / 4f);
                            p.Append($"({v.x:0.##},{v.y:0.##}) ");
                        }
                        sb.AppendLine($"    spline {p}");
                    }

                    foreach (Transform child in c.transform)
                    {
                        int n = child.GetComponentsInChildren<Transform>(true).Length;
                        if (n > 1) sb.AppendLine($"    child {child.name}: {n}");
                    }

                    if (Detail)
                    {
                        foreach (var poi in c.GetComponentsInChildren<EndlessSurvival.World.POI.PointOfInterest>(true))
                        {
                            Vector3 lp = c.transform.InverseTransformPoint(poi.transform.position);
                            sb.AppendLine($"    poi {poi.name} active={poi.gameObject.activeInHierarchy} at ({lp.x:0.##},{lp.y:0.##},{lp.z:0.##})");
                        }
                        Transform veg = c.transform.Find("Vegetation_Container");
                        if (veg != null)
                        {
                            for (int k = 0; k < Mathf.Min(4, veg.childCount); k++)
                            {
                                Transform v = veg.GetChild(k);
                                Vector3 lp = c.transform.InverseTransformPoint(v.position);
                                sb.AppendLine($"    veg[{k}] {v.name} ({lp.x:0.##},{lp.y:0.##},{lp.z:0.##}) n={v.childCount}");
                            }
                        }
                        if (t != null && t.terrainData != null)
                        {
                            // Height profile across the chunk at its middle and at both seams
                            foreach (float z in new[] { 1f, 250f, 499f })
                            {
                                var row = new StringBuilder();
                                for (float x = -240f; x <= 240f; x += 60f)
                                {
                                    Vector3 w = c.transform.TransformPoint(new Vector3(x, 0f, z));
                                    row.Append($"{t.SampleHeight(w) + t.transform.position.y:0.#} ");
                                }
                                sb.AppendLine($"    h z={z}: {row}");
                            }
                        }
                    }
                }
            }
            finally
            {
                mgr.ContextClearChildChunks();
                mgr.masterSeed = oldSeed;
            }
            return sb.ToString();
        }
    }
}
#endif

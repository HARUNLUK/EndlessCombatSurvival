#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;
using EndlessSurvival.World.Road;

namespace EndlessSurvival.World.Editor
{
    /// <summary>
    /// Lone-chunk editor preview seed &lt;-&gt; inspector options, one letter per option.
    /// Seed: "CODE-rest". Letter i of CODE is option i (<see cref="Slot"/>): 1st letter = type (a = first type,
    /// b = second...), 2nd = sea side, etc. Numbers (heights, hill size) are 36 steps between the type's min and max
    /// (a = min, 9 = max). "rest" drives the random part: road bend layout, camps, POIs, trees, rocks.
    /// Any text works: missing letters are derived from the text, unknown characters map onto the alphabet.
    /// Changing an option in the inspector rewrites only its letter.
    /// </summary>
    public static class ChunkPreviewSeed
    {
        public const string Alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
        private const int Steps = 36;

        public enum Slot
        {
            Type, Sea, CoastStart, CoastEnd, Variant, Lowland, Layout, Uphill,
            Road, Elevation, Param, Cross, EntryY, ExitY, EntryX, ExitX, Tier
        }

        public static readonly int CodeLength = Enum.GetValues(typeof(Slot)).Length;

        private static readonly CoastSide[] Seas = { CoastSide.Left, CoastSide.Right };
        private static readonly MountainVariant[] Variants = { MountainVariant.Snowy, MountainVariant.Alpine };
        private static readonly FoothillLowland[] Lowlands =
            { FoothillLowland.Forest, FoothillLowland.Field, FoothillLowland.LandThenCoast, FoothillLowland.Coast };
        private static readonly MountainLayout[] Layouts = { MountainLayout.Valley, MountainLayout.Hillside, MountainLayout.Ledge };
        private static readonly ChunkRoadType[] Roads =
            { ChunkRoadType.Straight, ChunkRoadType.CurvedLeft, ChunkRoadType.CurvedRight, ChunkRoadType.HazardZone };
        private static readonly RoadElevationType[] Elevations =
        {
            RoadElevationType.Flat, RoadElevationType.HillCrest, RoadElevationType.ValleyDip,
            RoadElevationType.RollingHills, RoadElevationType.MountainPass, RoadElevationType.ElevatedChicane
        };
        private static readonly RoadCrossSectionPreset[] Crosses =
        {
            RoadCrossSectionPreset.FullHighway, RoadCrossSectionPreset.SidewalkOnly, RoadCrossSectionPreset.GuardrailOnly,
            RoadCrossSectionPreset.OpenRoad, RoadCrossSectionPreset.HazardFortified
        };

        // ---------------------------------------------------------------- seed text

        public static int SymbolIndex(char ch)
        {
            int i = Alphabet.IndexOf(char.ToLowerInvariant(ch));
            return i >= 0 ? i : ch % Steps;
        }

        /// <summary>The option letters of a seed (missing ones derived from the text). Null for an empty seed.</summary>
        public static int[] Symbols(string seed)
        {
            string whole = Clean(seed);
            if (whole.Length == 0) return null;
            int dash = whole.IndexOf('-');
            string code = dash < 0 ? whole : whole.Substring(0, dash);
            int hash = SeededRandom.HashString(whole);

            var sym = new int[CodeLength];
            for (int i = 0; i < CodeLength; i++)
                sym[i] = i < code.Length ? SymbolIndex(code[i]) : (SeededRandom.Combine(hash, i) & 0x7fffffff) % Steps;
            return sym;
        }

        public static string Compose(int[] sym, string rest)
        {
            var sb = new StringBuilder(CodeLength + 1 + rest.Length);
            for (int i = 0; i < sym.Length; i++) sb.Append(Alphabet[sym[i] % Steps]);
            return sb.Append('-').Append(rest).ToString();
        }

        public static string RandomText(int length)
        {
            var rng = new System.Random(Guid.NewGuid().GetHashCode());
            var sb = new StringBuilder(length);
            for (int i = 0; i < length; i++) sb.Append(Alphabet[rng.Next(Steps)]);
            return sb.ToString();
        }

        /// <summary>Everything new: options and random part.</summary>
        public static string NewSeed() => RandomText(CodeLength) + "-" + RandomText(6);

        /// <summary>New options, same road bends / camps / trees.</summary>
        public static string ShuffleOptions(string seed)
        {
            string rest = Rest(seed);
            return RandomText(CodeLength) + "-" + (rest.Length > 0 ? rest : RandomText(6));
        }

        /// <summary>Same options, new road bends / camps / trees.</summary>
        public static string ShuffleShape(string seed)
        {
            int[] sym = Symbols(seed);
            return sym == null ? NewSeed() : Compose(sym, RandomText(6));
        }

        /// <summary>
        /// Writes the letters of the changed options. With no seed yet, every current option is written, so nothing
        /// visible changes except what was edited.
        /// </summary>
        public static string WriteChanges(Chunk c, string seed, Dictionary<string, string> before, Dictionary<string, string> after)
        {
            int[] sym = Symbols(seed);
            bool fresh = sym == null;
            if (fresh) sym = new int[CodeLength];
            string rest = fresh ? RandomText(6) : Rest(seed);

            foreach (Slot slot in Enum.GetValues(typeof(Slot)))
            {
                string key = slot.ToString();
                before.TryGetValue(key, out string old);
                after.TryGetValue(key, out string now);
                if (fresh || old != now) sym[(int)slot] = Encode(c, slot);
            }
            return Compose(sym, rest);
        }

        private static string Clean(string seed)
        {
            if (string.IsNullOrEmpty(seed)) return "";
            string s = seed.Trim();
            int bar = s.IndexOf('|');
            return bar >= 0 ? s.Substring(0, bar) : s;
        }

        private static string Rest(string seed) => Chunk.PreviewSeedBase(seed);

        // ---------------------------------------------------------------- inspector values

        /// <summary>Current option values (for change detection), keyed by <see cref="Slot"/> name.</summary>
        public static Dictionary<string, string> Capture(Chunk c)
        {
            return new Dictionary<string, string>
            {
                { nameof(Slot.Type), c.previewProfile != null ? c.previewProfile.name : "" },
                { nameof(Slot.Sea), c.seaSide.ToString() },
                { nameof(Slot.CoastStart), c.coastContinuesAtStart.ToString() },
                { nameof(Slot.CoastEnd), c.coastContinuesAtEnd.ToString() },
                { nameof(Slot.Variant), c.mountainVariant.ToString() },
                { nameof(Slot.Lowland), c.foothillLowland.ToString() },
                { nameof(Slot.Layout), c.mountainLayout.ToString() },
                { nameof(Slot.Uphill), c.uphillOnRight.ToString() },
                { nameof(Slot.Road), c.roadType.ToString() },
                { nameof(Slot.Elevation), c.currentElevationType.ToString() },
                { nameof(Slot.Param), F(c.currentElevationParam) },
                { nameof(Slot.Cross), c.crossSectionType.ToString() },
                { nameof(Slot.EntryY), F(c.roadEntryY) },
                { nameof(Slot.ExitY), F(c.roadExitY) },
                { nameof(Slot.EntryX), F(c.roadEntryX) },
                { nameof(Slot.ExitX), F(c.roadExitX) },
                { nameof(Slot.Tier), c.difficultyTier.ToString(CultureInfo.InvariantCulture) },
            };
        }

        /// <summary>Lone chunks in a scene (not spawned by ChunkManager, not a prefab asset) use the preview seed.</summary>
        public static bool Supports(Chunk c)
        {
            return c != null && !Application.isPlaying && c.Manager == null && !EditorUtility.IsPersistent(c);
        }

        // ---------------------------------------------------------------- seed -> options

        /// <summary>Sets every option from the seed's letters and rebuilds road, terrain, vegetation and preview.</summary>
        public static void Apply(Chunk c)
        {
            if (!Supports(c)) return;
            int[] sym = Symbols(c.editorPreviewSeed);
            if (sym == null)
            {
                c.GenerateEditorPreview(c.GetEditorPreviewSeed());
                return;
            }

            Undo.RecordObject(c, "Preview Seed");
            bool procedural = c.generationMode == ChunkGenerationMode.Procedural;

            if (procedural)
            {
                List<ChunkTypeProfile> types = Profiles();
                if (types.Count > 0)
                {
                    c.previewProfile = types[sym[(int)Slot.Type] % types.Count];
                    c.ApplyProfile(c.previewProfile);
                }
            }

            MountainTypeSettings ms = c.mountainSettings ?? new MountainTypeSettings();
            c.seaSide = Pick(Seas, sym[(int)Slot.Sea]);
            c.coastContinuesAtStart = sym[(int)Slot.CoastStart] % 2 == 0;
            c.coastContinuesAtEnd = sym[(int)Slot.CoastEnd] % 2 == 0;
            c.mountainVariant = Pick(Variants, sym[(int)Slot.Variant]);
            c.foothillLowland = Pick(Lowlands, sym[(int)Slot.Lowland]);
            c.mountainLayout = c.IsFoothill ? MountainLayout.Foothill : Pick(Layouts, sym[(int)Slot.Layout]);
            c.uphillOnRight = sym[(int)Slot.Uphill] % 2 == 0;
            c.difficultyTier = sym[(int)Slot.Tier] % 5 + 1;

            var gen = c.GetComponentInChildren<RoadGenerator>();
            var spline = c.GetComponentInChildren<RoadSpline>();
            if (procedural)
            {
                // Road ends: mountains are up at roadMin-MaxHeight, a ledge runs along the uphill side
                float ex = 0f, xx = 0f, ey = c.baseElevation, xy = c.baseElevation;
                if (c.IsMountain)
                {
                    ey = Value(sym[(int)Slot.EntryY], ms.roadMinHeight, ms.roadMaxHeight);
                    xy = Value(sym[(int)Slot.ExitY], ms.roadMinHeight, ms.roadMaxHeight);
                    if (c.mountainLayout == MountainLayout.Ledge)
                    {
                        float side = c.uphillOnRight ? 1f : -1f;
                        ex = side * Value(sym[(int)Slot.EntryX], ms.ledgeRoadMinOffset, ms.ledgeRoadMaxOffset);
                        xx = side * Value(sym[(int)Slot.ExitX], ms.ledgeRoadMinOffset, ms.ledgeRoadMaxOffset);
                    }
                }
                c.roadEntryX = ex; c.roadExitX = xx; c.roadEntryY = ey; c.roadExitY = xy;

                c.roadType = Pick(Roads, sym[(int)Slot.Road]);
                c.currentElevationType = Pick(Elevations, sym[(int)Slot.Elevation]);
                ParamRange(c, c.currentElevationType, out float pMin, out float pMax);
                c.currentElevationParam = Value(sym[(int)Slot.Param], pMin, pMax);
                c.crossSectionType = Pick(Crosses, sym[(int)Slot.Cross]);

                if (gen != null && spline != null)
                {
                    Undo.RecordObject(spline, "Preview Seed");
                    int shapeSeed = SeededRandom.Combine(SeededRandom.HashString(Rest(c.editorPreviewSeed)), "road");
                    BuildManualRoad(c, spline, new SeededRandom(shapeSeed));
                    gen.ApplyCrossSectionPreset(c.crossSectionType);
                    EditorUtility.SetDirty(spline);
                }
            }

            EditorUtility.SetDirty(c);
            RebuildPreview(c, spline, gen);
        }

        /// <summary>Letter of one option from the chunk's current value.</summary>
        public static int Encode(Chunk c, Slot slot)
        {
            MountainTypeSettings ms = c.mountainSettings ?? new MountainTypeSettings();
            switch (slot)
            {
                case Slot.Type: return Mathf.Max(0, Profiles().IndexOf(c.previewProfile));
                case Slot.Sea: return Index(Seas, c.seaSide);
                case Slot.CoastStart: return c.coastContinuesAtStart ? 0 : 1;
                case Slot.CoastEnd: return c.coastContinuesAtEnd ? 0 : 1;
                case Slot.Variant: return Index(Variants, c.mountainVariant);
                case Slot.Lowland: return Index(Lowlands, c.foothillLowland);
                case Slot.Layout: return Index(Layouts, c.mountainLayout);
                case Slot.Uphill: return c.uphillOnRight ? 0 : 1;
                case Slot.Road: return Index(Roads, c.roadType);
                case Slot.Elevation: return Index(Elevations, c.currentElevationType);
                case Slot.Param:
                    ParamRange(c, c.currentElevationType, out float pMin, out float pMax);
                    return Step(c.currentElevationParam, pMin, pMax);
                case Slot.Cross: return Index(Crosses, c.crossSectionType);
                case Slot.EntryY: return Step(c.roadEntryY, ms.roadMinHeight, ms.roadMaxHeight);
                case Slot.ExitY: return Step(c.roadExitY, ms.roadMinHeight, ms.roadMaxHeight);
                case Slot.EntryX: return Step(Mathf.Abs(c.roadEntryX), ms.ledgeRoadMinOffset, ms.ledgeRoadMaxOffset);
                case Slot.ExitX: return Step(Mathf.Abs(c.roadExitX), ms.ledgeRoadMinOffset, ms.ledgeRoadMaxOffset);
                case Slot.Tier: return Mathf.Clamp(c.difficultyTier - 1, 0, 4);
            }
            return 0;
        }

        /// <summary>Builds the spline from the chunk's road type / elevation fields.</summary>
        public static void BuildManualRoad(Chunk c, RoadSpline spline, SeededRandom rng)
        {
            RoadCurveSettings settings = c.curveSettings ?? new RoadCurveSettings();
            bool curvy = c.roadType == ChunkRoadType.CurvedLeft || c.roadType == ChunkRoadType.CurvedRight
                         || c.roadType == ChunkRoadType.HazardZone;
            bool sharp = c.roadType == ChunkRoadType.HazardZone;

            float param = c.currentElevationParam;
            if (c.currentElevationType != RoadElevationType.Flat && param <= 0f) param = 12f;

            float peak = spline.SetProceduralPreset(settings, curvy, sharp, c.currentElevationType,
                param, c.currentElevationType == RoadElevationType.RollingHills ? param * 0.7f : param, rng,
                c.roadEntryX, c.roadExitX, c.roadEntryY, c.roadExitY);

            // Honor requested side: CurvedRight = biggest bend to +X, CurvedLeft = -X
            // (mirroring is only possible for a centered road; an off-center one must keep its entry/exit X)
            bool centered = Mathf.Abs(c.roadEntryX) < 0.01f && Mathf.Abs(c.roadExitX) < 0.01f;
            bool wantRight = c.roadType == ChunkRoadType.CurvedRight;
            bool wantLeft = c.roadType == ChunkRoadType.CurvedLeft;
            if (centered && ((wantRight && peak < 0f) || (wantLeft && peak > 0f)))
            {
                for (int i = 0; i < spline.waypoints.Count; i++)
                {
                    Vector3 p = spline.waypoints[i];
                    p.x = -p.x;
                    spline.waypoints[i] = p;
                }
            }
        }

        /// <summary>Terrain follows the road, then trees and the full preview (camps, POIs, edges, backdrop).</summary>
        public static void RebuildPreview(Chunk c, RoadSpline spline, RoadGenerator gen)
        {
            Terrain terrain = c.GetComponentInChildren<Terrain>();
            if (terrain != null && spline != null && gen != null) RoadTerrainAdapter.ConformTerrainToRoad(terrain, spline, gen, true);

            var vegetation = c.GetComponentInChildren<ChunkVegetationSpawner>();
            if (vegetation != null) vegetation.GenerateVegetation();

            c.GenerateEditorPreview(c.GetEditorPreviewSeed());
        }

        // ---------------------------------------------------------------- legend

        public static string SlotLabel(Slot slot)
        {
            switch (slot)
            {
                case Slot.Type: return "Tip";
                case Slot.Sea: return "Deniz tarafı";
                case Slot.CoastStart: return "Kıyı başta devam";
                case Slot.CoastEnd: return "Kıyı sonda devam";
                case Slot.Variant: return "Dağ türü";
                case Slot.Lowland: return "Etek alçak arazi";
                case Slot.Layout: return "Dağ yerleşimi";
                case Slot.Uphill: return "Dağ tarafı";
                case Slot.Road: return "Yol tipi";
                case Slot.Elevation: return "Yükselti";
                case Slot.Param: return "Tepe/çukur boyu";
                case Slot.Cross: return "Yol kesiti";
                case Slot.EntryY: return "Giriş yüksekliği";
                case Slot.ExitY: return "Çıkış yüksekliği";
                case Slot.EntryX: return "Giriş kenar uzaklığı";
                case Slot.ExitX: return "Çıkış kenar uzaklığı";
                case Slot.Tier: return "Zorluk";
            }
            return slot.ToString();
        }

        /// <summary>"a=Forest b=Coast ..." for one option (numbers: min..max range).</summary>
        public static string SlotValues(Chunk c, Slot slot)
        {
            MountainTypeSettings ms = c.mountainSettings ?? new MountainTypeSettings();
            switch (slot)
            {
                case Slot.Type:
                {
                    List<ChunkTypeProfile> types = Profiles();
                    var names = new string[types.Count];
                    for (int i = 0; i < types.Count; i++) names[i] = types[i].DisplayName;
                    return Letters(names);
                }
                case Slot.Sea: return Letters("Sol", "Sağ");
                case Slot.CoastStart:
                case Slot.CoastEnd: return Letters("Evet", "Hayır");
                case Slot.Variant: return Letters(Names(Variants));
                case Slot.Lowland: return Letters(Names(Lowlands));
                case Slot.Layout: return c.IsFoothill ? "Dağ eteğinde hep Foothill" : Letters(Names(Layouts));
                case Slot.Uphill: return Letters("Sağ", "Sol");
                case Slot.Road: return Letters(Names(Roads));
                case Slot.Elevation: return Letters(Names(Elevations));
                case Slot.Param:
                    ParamRange(c, c.currentElevationType, out float pMin, out float pMax);
                    return Range(pMin, pMax, "m");
                case Slot.Cross: return Letters(Names(Crosses));
                case Slot.EntryY:
                case Slot.ExitY: return c.IsMountain ? Range(ms.roadMinHeight, ms.roadMaxHeight, "m") : "Dağ değil: hep " + F(c.baseElevation) + "m";
                case Slot.EntryX:
                case Slot.ExitX:
                    return c.IsMountain && c.mountainLayout == MountainLayout.Ledge
                        ? Range(ms.ledgeRoadMinOffset, ms.ledgeRoadMaxOffset, "m") : "Kenar yolu değil: yol ortada";
                case Slot.Tier: return Letters("1", "2", "3", "4", "5");
            }
            return "";
        }

        private static string Letters(params string[] names)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < names.Length; i++)
            {
                if (i > 0) sb.Append("  ");
                sb.Append(Alphabet[i]).Append('=').Append(names[i]);
            }
            return sb.ToString();
        }

        private static string[] Names<T>(T[] values)
        {
            var names = new string[values.Length];
            for (int i = 0; i < values.Length; i++) names[i] = values[i].ToString();
            return names;
        }

        private static string Range(float min, float max, string unit) =>
            "a=" + F(min) + unit + " … 9=" + F(max) + unit + " (36 adım)";

        // ---------------------------------------------------------------- helpers

        private static T Pick<T>(T[] values, int symbol) => values[symbol % values.Length];

        private static int Index<T>(T[] values, T value)
        {
            int i = Array.IndexOf(values, value);
            return i < 0 ? 0 : i;
        }

        private static float Value(int symbol, float min, float max) => Mathf.Lerp(min, max, (symbol % Steps) / (Steps - 1f));

        private static int Step(float value, float min, float max)
        {
            if (Mathf.Abs(max - min) < 0.001f) return 0;
            return Mathf.Clamp(Mathf.RoundToInt(Mathf.InverseLerp(min, max, value) * (Steps - 1)), 0, Steps - 1);
        }

        private static void ParamRange(Chunk c, RoadElevationType elevation, out float min, out float max)
        {
            ChunkElevationConfig config = c.elevationConfig ?? new ChunkElevationConfig();
            switch (elevation)
            {
                case RoadElevationType.HillCrest:
                case RoadElevationType.RollingHills:
                case RoadElevationType.MountainPass:
                    min = config.minHillHeight; max = config.maxHillHeight; return;
                case RoadElevationType.ValleyDip:
                    min = config.minDipDepth; max = config.maxDipDepth; return;
                case RoadElevationType.ElevatedChicane:
                    min = config.minHillHeight * 0.7f; max = config.maxHillHeight * 0.7f; return;
                default:
                    min = max = 0f; return;
            }
        }

        /// <summary>Type letters: the scene ChunkManager's type list order (a = first), else every profile by name.</summary>
        public static List<ChunkTypeProfile> Profiles()
        {
            var list = new List<ChunkTypeProfile>();
            ChunkManager mgr = UnityEngine.Object.FindAnyObjectByType<ChunkManager>();
            if (mgr != null && mgr.chunkTypes != null)
            {
                for (int i = 0; i < mgr.chunkTypes.Count; i++)
                {
                    var e = mgr.chunkTypes[i];
                    if (e != null && e.profile != null && !list.Contains(e.profile)) list.Add(e.profile);
                }
            }
            if (list.Count > 0) return list;

            string[] guids = AssetDatabase.FindAssets("t:ChunkTypeProfile");
            for (int i = 0; i < guids.Length; i++)
            {
                var p = AssetDatabase.LoadAssetAtPath<ChunkTypeProfile>(AssetDatabase.GUIDToAssetPath(guids[i]));
                if (p != null) list.Add(p);
            }
            list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return list;
        }

        private static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
#endif

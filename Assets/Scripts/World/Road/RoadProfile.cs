using UnityEngine;

namespace EndlessSurvival.World.Road
{
    /// <summary>
    /// Preset cross-section styles defining the presence and layout of curbs, sidewalks, and guardrails.
    /// </summary>
    public enum RoadCrossSectionPreset
    {
        ProfileDefault,   // Use base profile parameters
        FullHighway,      // Both curbs, wide sidewalks, and outer guardrails (Kaldırımlı ve Bariyerli)
        SidewalkOnly,     // Raised curbs and pedestrian sidewalks, no guardrails (Kaldırımlı Düz / Şehir)
        GuardrailOnly,    // Heavy metal guardrails on low shoulder, no raised sidewalk (Bariyerli Hızlı Yol)
        OpenRoad,         // Pure asphalt with flush ground transition, no curbs or barriers (Açık Kırsal)
        HazardFortified   // Narrowed road with heavy dual guardrails and high curbs (Tehlike Bölgesi)
    }

    /// <summary>
    /// ScriptableObject defining parametric road visuals and cross-section dimensions for a specific biome.
    /// </summary>
    [CreateAssetMenu(fileName = "RoadProfile_Default", menuName = "EndlessSurvival/Road Profile")]
    public class RoadProfile : ScriptableObject
    {
        [Header("Biome Association")]
        [Tooltip("Biome this profile is configured for")]
        public ChunkBiomeType targetBiome = ChunkBiomeType.Forest;

        [Header("Default Cross-Section Preset")]
        [Tooltip("Default cross-section layout preset for this biome")]
        public RoadCrossSectionPreset defaultCrossSection = RoadCrossSectionPreset.FullHighway;

        [Header("Road Dimensions")]
        [Tooltip("Width of the main drivable road surface in meters")]
        [Range(6f, 24f)]
        public float roadWidth = 11.0f;

        [Tooltip("Whether raised curb stones are enabled")]
        public bool hasCurbs = true;

        [Tooltip("Width of the curb stone in meters")]
        [Range(0.15f, 0.6f)]
        public float curbWidth = 0.30f;

        [Tooltip("Height of the curb step above road surface in meters")]
        [Range(0.05f, 0.4f)]
        public float curbHeight = 0.18f;

        [Tooltip("Whether pedestrian sidewalks / shoulders are enabled")]
        public bool hasSidewalks = true;

        [Tooltip("Width of the pedestrian sidewalk / shoulder in meters")]
        [Range(0.5f, 6f)]
        public float shoulderWidth = 2.2f;

        [Header("Materials (URP)")]
        [Tooltip("Primary material for the drivable road surface")]
        public Material roadMaterial;

        [Tooltip("Material for the raised curb stones (bordür taşı)")]
        public Material curbMaterial;

        [Tooltip("Material for the road shoulders / sidewalks")]
        public Material shoulderMaterial;

        [Header("UV Tiling")]
        [Tooltip("Texture tiling rate along the road length")]
        public float textureTileRate = 0.2f;

        [Header("Guardrails & Roadside Props")]
        [Tooltip("Whether this road profile features roadside guardrails")]
        public bool hasGuardrails = true;

        [Tooltip("Material for the metal guardrail")]
        public Material guardrailMaterial;

        [Tooltip("Spacing in meters between guardrail support posts along the road")]
        [Range(2f, 15f)]
        public float guardrailSpacing = 3.5f;

        [Tooltip("Lateral offset from road center to guardrail position")]
        public float guardrailOffset = 8.0f;
    }
}

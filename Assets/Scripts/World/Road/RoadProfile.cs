using UnityEngine;

namespace EndlessSurvival.World.Road
{
    /// <summary>
    /// ScriptableObject defining parametric road visuals and cross-section dimensions for a specific biome.
    /// </summary>
    [CreateAssetMenu(fileName = "RoadProfile_Default", menuName = "EndlessSurvival/Road Profile")]
    public class RoadProfile : ScriptableObject
    {
        [Header("Biome Association")]
        [Tooltip("Biome this profile is configured for")]
        public ChunkBiomeType targetBiome = ChunkBiomeType.Forest;

        [Header("Road Dimensions")]
        [Tooltip("Width of the main drivable road surface in meters")]
        [Range(6f, 24f)]
        public float roadWidth = 12.0f;

        [Tooltip("Width of the left and right gravel/dirt shoulders in meters")]
        [Range(0.5f, 6f)]
        public float shoulderWidth = 2.5f;

        [Tooltip("Height step between the road edge and the shoulder ditch")]
        [Range(0f, 0.5f)]
        public float curbDrop = 0.08f;

        [Header("Materials (URP)")]
        [Tooltip("Primary material for the drivable road surface")]
        public Material roadMaterial;

        [Tooltip("Material for the road shoulders / side banks")]
        public Material shoulderMaterial;

        [Header("UV Tiling")]
        [Tooltip("Texture tiling rate along the road length")]
        public float textureTileRate = 0.2f;

        [Header("Guardrails & Roadside Props")]
        [Tooltip("Whether this road profile features roadside guardrails")]
        public bool hasGuardrails = false;

        [Tooltip("Prefab for guardrail posts or barriers")]
        public GameObject guardrailPrefab;

        [Tooltip("Spacing in meters between guardrail instances along the road")]
        [Range(2f, 15f)]
        public float guardrailSpacing = 4.0f;

        [Tooltip("Lateral offset from road center to guardrail position")]
        public float guardrailOffset = 6.8f;
    }
}

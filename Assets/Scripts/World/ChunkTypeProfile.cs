using UnityEngine;
using EndlessSurvival.World.Road;

namespace EndlessSurvival.World
{
    /// <summary>
    /// One procedural chunk TYPE (Orman, Kıyı, Düzlük, Dağ, Dağ Eteği...): every setting that differs between types.
    /// All types share one chunk prefab (ChunkManager.chunkPrefab); at spawn the planned type's profile is applied to
    /// the new instance (<see cref="Chunk.ApplyProfile"/>). A new type = a new profile asset, no new prefab.
    /// Handcrafted chunks are separate prefabs (ChunkManager.specialChunks) and keep their own settings.
    /// </summary>
    [CreateAssetMenu(menuName = "Endless Survival/Chunk Tipi Profili", fileName = "ChunkType_New", order = 0)]
    public class ChunkTypeProfile : ScriptableObject
    {
        [Header("Kimlik")]
        [Tooltip("Görünen ad (log ve chunk isimlerinde). Boşsa asset adı kullanılır.")]
        public string displayName = "";

        [Tooltip("Arazi motoru: kenar/kıyı/dağ/düzlük üreticilerinin bu tipte hangi davranışı kullanacağı")]
        public ChunkBiomeType biome = ChunkBiomeType.Forest;

        [Tooltip("Kenarları düz (uçurumsuz) bir tip mi? Komşu chunk'ların uçurumları bu tipe değen seam'de sıfıra iner.")]
        public bool flatSeamEdges = false;

        [Tooltip("Özel (el yapımı) chunk'lar bu tipin bölümlerinin içine girebilir mi? " +
                 "Özel chunk'ın Allowed Types listesi boşsa bu ayar belirler; listede bu tip açıkça varsa yine de gelebilir.")]
        public bool allowSpecialChunks = true;

        [Header("Yol")]
        [Tooltip("Eğim (tepe/vadi) ihtimalleri ve yükseklik sınırları")]
        public ChunkElevationConfig elevationConfig = new ChunkElevationConfig();
        [Tooltip("Viraj üretimi (viraj sayısı, sapma, dönüş yarıçapı)")]
        public RoadCurveSettings curveSettings = new RoadCurveSettings();
        [Tooltip("Yol karakteri (viraj sıklığı, şikan, zorunlu kesit)")]
        public ChunkRoadStyle roadStyle = new ChunkRoadStyle();

        [Header("Dağ (sadece Biome = Mountain)")]
        [Tooltip("Alt tip (karlı/alpin), kenar yolu, yol yüksekliği, Dağ Eteği")]
        public MountainTypeSettings mountainSettings = new MountainTypeSettings();

        [Header("Nehir Vadisi (sadece Biome = Forest)")]
        [Tooltip("Açıksa tip Nehir Vadisi'dir: yolun yanında yolla birlikte akan geniş bir nehir, ara sıra köprüyle karşıya geçiş")]
        public RiverValleySettings riverValley = new RiverValleySettings();

        [Header("Kenar (uçurum / kayalar)")]
        public BiomeBoundarySettings boundary = new BiomeBoundarySettings();

        [Header("Arka Plan Dağları")]
        [Tooltip("Bu tipin arka plan dağları (dağ tipinde karlı alt tip)")]
        public BiomeBackdropSettings backdrop = new BiomeBackdropSettings();
        [Tooltip("Sadece dağ tiplerinde: alpin (karsız) alt tipin arka planı")]
        public BiomeBackdropSettings alpineBackdrop = BiomeBackdropSettings.AlpineMountain();
        [Tooltip("Sadece Dağ Eteği: dağın karşısındaki alçak tarafın ufku")]
        public BiomeBackdropSettings foothillLowlandBackdrop = BiomeBackdropSettings.Lowland();

        public string DisplayName => !string.IsNullOrEmpty(displayName) ? displayName : name;
        public bool IsMountain => biome == ChunkBiomeType.Mountain;
        public bool IsFoothill => IsMountain && mountainSettings != null && mountainSettings.foothills;
        public bool IsRiverValley => biome == ChunkBiomeType.Forest && riverValley != null && riverValley.enabled;

        /// <summary>Backdrop look for a mountain sub-type (alpine mountains have their own).</summary>
        public BiomeBackdropSettings BackdropFor(MountainVariant variant)
        {
            if (IsMountain && variant == MountainVariant.Alpine && alpineBackdrop != null) return alpineBackdrop;
            return backdrop;
        }

        private void OnValidate()
        {
            // Settings blocks carry their own biome tag (used by the generators); keep it in sync with the type
            if (boundary != null) boundary.biome = biome;
            if (backdrop != null) backdrop.biome = biome;
        }
    }

    /// <summary>
    /// The planned type of a chunk index: a procedural type profile, or a special (handcrafted) prefab placed inside
    /// a run of <see cref="profile"/>. Two chunks are "the same type" (a type's own seam rules apply between them:
    /// coast continues, mountain road climbs, backdrop does not blend) only if both are the same profile and neither
    /// is special.
    /// </summary>
    public readonly struct ChunkTypeRef : System.IEquatable<ChunkTypeRef>
    {
        public readonly ChunkTypeProfile profile;
        /// <summary>Special chunk prefab, or null for a procedural chunk of <see cref="profile"/>.</summary>
        public readonly Chunk special;
        /// <summary>Special chunk: use the run type's backdrop and edge look instead of its own components' settings.</summary>
        public readonly bool inheritRunLook;

        public ChunkTypeRef(ChunkTypeProfile profile, Chunk special = null, bool inheritRunLook = false)
        {
            this.profile = profile;
            this.special = special;
            this.inheritRunLook = special != null && inheritRunLook;
        }

        public bool IsValid => profile != null || special != null;
        public bool IsSpecial => special != null;

        public ChunkBiomeType Biome => special != null ? special.biomeType : profile != null ? profile.biome : ChunkBiomeType.Forest;
        public bool FlatSeamEdges => special != null ? special.flatSeamEdges : profile != null && profile.flatSeamEdges;
        public MountainTypeSettings Mountain => special != null ? special.mountainSettings : profile != null ? profile.mountainSettings : null;
        public bool IsMountain => Biome == ChunkBiomeType.Mountain;
        public bool IsFoothill => IsMountain && Mountain != null && Mountain.foothills;
        public bool IsProcedural => special == null || special.generationMode == ChunkGenerationMode.Procedural;

        public string Name => special != null ? (!string.IsNullOrEmpty(special.typeName) ? special.typeName : special.name)
            : profile != null ? profile.DisplayName : "?";

        /// <summary>
        /// Backdrop settings on one side (null = no backdrop). A foothill shows mountains only on its mountain side
        /// (<paramref name="uphillOnRight"/>) and its lowland horizon on the other.
        /// </summary>
        public BiomeBackdropSettings BackdropSide(MountainVariant variant, float sideSign, bool uphillOnRight, ChunkBackdropGenerator sharedBackdrop)
        {
            if (special != null && !inheritRunLook)
            {
                var own = special.GetComponent<ChunkBackdropGenerator>();
                if (own == null || !own.backdropEnabled) return null;
                bool foothill = special.IsFoothill;
                if (foothill && own.foothillLowlandSettings != null && Mathf.Approximately(sideSign, uphillOnRight ? -1f : 1f))
                    return own.foothillLowlandSettings;
                return own.ResolveSettings(special.biomeType, variant);
            }

            if (profile == null) return null;
            if (sharedBackdrop != null && !sharedBackdrop.backdropEnabled) return null;
            if (profile.IsFoothill && profile.foothillLowlandBackdrop != null && Mathf.Approximately(sideSign, uphillOnRight ? -1f : 1f))
                return profile.foothillLowlandBackdrop;
            return profile.BackdropFor(variant);
        }

        public bool Equals(ChunkTypeRef other) => profile == other.profile && special == other.special;
        public override bool Equals(object obj) => obj is ChunkTypeRef other && Equals(other);
        public override int GetHashCode() => ((profile != null ? profile.GetHashCode() : 0) * 397) ^ (special != null ? special.GetHashCode() : 0);
        public static bool operator ==(ChunkTypeRef a, ChunkTypeRef b) => a.Equals(b);
        public static bool operator !=(ChunkTypeRef a, ChunkTypeRef b) => !a.Equals(b);
        public override string ToString() => Name;
    }
}

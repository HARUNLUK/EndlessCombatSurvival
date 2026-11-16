using UnityEngine;

namespace EndlessSurvival.World
{
    /// <summary>
    /// An inset type (town, rest stop) is not a run of its own: it replaces 1-2 chunks in the middle of a host run
    /// (never the run's first chunk, which is the transition from the previous type, nor its last one). The chunks keep
    /// the host's terrain, colours and backdrop; only the settlement comes from the inset profile.
    /// </summary>
    [System.Serializable]
    public class InsetSettings
    {
        [UnityEngine.Tooltip("Açıksa bu tip sadece başka bir bölümün içine yerleşim olarak girer")]
        public bool enabled;
        [UnityEngine.Tooltip("Hangi tiplerin bölümlerine girebilir (Çorak Arazi, Düzlük...)")]
        public System.Collections.Generic.List<ChunkTypeProfile> hosts = new System.Collections.Generic.List<ChunkTypeProfile>();
        [UnityEngine.Tooltip("Uygun bir bölümde görünme ihtimali (bölüm başına en fazla bir yerleşim)")]
        [UnityEngine.Range(0f, 1f)] public float chancePerRun = 0.35f;
        [UnityEngine.Range(1, 4)] public int minLength = 1;
        [UnityEngine.Range(1, 4)] public int maxLength = 2;
    }

    /// <summary>What a chunk type builds beside its road (none, a ruined town, a roadside rest stop).</summary>
    public enum SettlementKind
    {
        None,
        /// <summary>Harabe Kasaba: rows of ruined and intact buildings along both sides of the road.</summary>
        Town,
        /// <summary>Mola Yeri: a roadside stop (fuel station, diner, motel, parking...) on one side of the road.</summary>
        RestStop
    }

    /// <summary>
    /// Settlement of a chunk type. The layout is NOT a fixed prefab: every chunk rolls its own from its seed (which
    /// buildings, how many, their size, order, colour, damage, side of the road), so no two towns / stops look alike.
    /// Built by EndlessSurvival.Survival.Settlements after the chunk is built.
    /// </summary>
    [System.Serializable]
    public class SettlementSettings
    {
        public SettlementKind kind = SettlementKind.None;

        [Header("Kasaba")]
        [Tooltip("Bir parselin binayla dolu olma ihtimali (gerisi boş arsa / çitli arsa / yıkıntı)")]
        [Range(0f, 1f)] public float townDensity = 0.72f;
        [Tooltip("Yol eksenine en yakın bina cephesi (m)")]
        public float townSetbackMin = 12f;
        public float townSetbackMax = 17f;
        [Tooltip("Ön sıranın arkasında ikinci sıra binanın olma ihtimali (parsel başına)")]
        [Range(0f, 1f)] public float townBackRowChance = 0.3f;
        [Tooltip("Binaların yıkık olma oranı (0 = hepsi sağlam, 1 = hepsi harabe)")]
        [Range(0f, 1f)] public float townRuinShare = 0.55f;

        [Header("Mola Yeri")]
        [Tooltip("Mola yerinde benzinlik olma ihtimali")]
        [Range(0f, 1f)] public float stopFuelChance = 0.8f;
        [Tooltip("Mola yerinde tüccar olma ihtimali")]
        [Range(0f, 1f)] public float stopTraderChance = 0.3f;
        [Tooltip("Yolun karşı yanında da küçük bir kısım (piknik, tır, tabela) olma ihtimali")]
        [Range(0f, 1f)] public float stopOppositeSideChance = 0.4f;

        public bool Enabled => kind != SettlementKind.None;
    }
}

using UnityEngine;

namespace EndlessSurvival.Story
{
    /// <summary>
    /// Configuration for roadside narrative story events (Lighthouse, Cave, Ambush, etc.).
    /// </summary>
    [CreateAssetMenu(fileName = "New Story Event", menuName = "Endless Survival/Story/Story Event Definition")]
    public class StoryEventDefinition : ScriptableObject
    {
        [Tooltip("Benzersiz event kimliği (örn: Event_Lighthouse)")]
        public string eventId = "Event_Lighthouse";

        [Tooltip("Event görünen adı")]
        public string eventName = "Yalnız Deniz Feneri";

        [Tooltip("Yol kenarına yerleştirilecek event prefab'ı")]
        public GameObject eventPrefab;

        [Tooltip("Chunk başına bu eventin seçilme ağırlığı")]
        [Range(0f, 1f)]
        public float spawnChance = 0.35f;

        [Tooltip("Aynı eventin tekrar çıkması için aradan geçmesi gereken minimum chunk sayısı")]
        public int minChunkInterval = 3;

        [Tooltip("Bu eventin en erken hangi chunk indeksinden itibaren çıkabileceği")]
        public int minChunkIndex = 2;

        [Tooltip("Yol orta çizgisinden sağa veya sola ne kadar uzaklığa yerleştirileceği")]
        public float lateralDistance = 42f;

        [Header("Road Placement Settings")]
        [Tooltip("Bu etkinliğin yol ile olan konumsal kuralı. OnRoad ise yalnızca yol üstündeki özel noktalara yerleşebilir.")]
        public EndlessSurvival.World.POI.POIPlacementZone placementZone = EndlessSurvival.World.POI.POIPlacementZone.Roadside;

        [Tooltip("Yol üstü için izin verilmiş özel event mi? True ise yalnızca yol üstü noktalarında çıkar.")]
        public bool isAllowedOnRoad = false;

        [Tooltip("Yola göre yönelimi: True ise yola doğru değil, yoldan uzağa doğru bakar (Mağara gibi tüneli arkaya uzanan yapılar için)")]
        public bool orientAwayFromRoad = false;

        [Tooltip("Yol orta çizgisinden olması gereken minimum güvenli mesafe (metre)")]
        public float minRoadDistance = 35f;
    }
}

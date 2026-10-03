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
    }
}

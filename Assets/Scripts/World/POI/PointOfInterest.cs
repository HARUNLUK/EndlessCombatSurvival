using UnityEngine;
using EndlessSurvival.World;

namespace EndlessSurvival.World.POI
{
    public abstract class PointOfInterest : MonoBehaviour
    {
        [Tooltip("Sifir ile bir arasinda bu POI'nin var olma ihtimali (1 = kesin cikar).")]
        [Range(0f, 1f)]
        public float spawnChance = 1f;

        /// <summary>
        /// Called by the parent Chunk after initialization to give the POI its
        /// deterministic random. This replaces the old Start()-based spawn roll
        /// so the result is independent of Unity's non-deterministic Start order.
        /// </summary>
        public void InitializeFromChunk(SeededRandom poiRng, Transform container = null)
        {
            if (!poiRng.Chance(spawnChance))
            {
                gameObject.SetActive(false);
                return;
            }

            OnSpawn(poiRng, container ?? transform);
        }

        protected virtual void Start()
        {
            // If the POI was not initialized from the chunk (e.g. placed manually in editor),
            // fall back to the old non-deterministic path.
            // Normally, chunks call InitializeFromChunk() before Start() runs.
        }

        protected abstract void OnSpawn(SeededRandom rng, Transform container);
    }
}

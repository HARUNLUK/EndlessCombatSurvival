using UnityEngine;

namespace EndlessSurvival.World.POI
{
    public abstract class PointOfInterest : MonoBehaviour
    {
        [Tooltip("Sıfır ile bir arasında bu POI'nin var olma ihtimali (1 = kesin çıkar).")]
        [Range(0f, 1f)]
        public float spawnChance = 1f;

        protected virtual void Start()
        {
            if (Random.value > spawnChance)
            {
                gameObject.SetActive(false);
                return;
            }

            OnSpawn();
        }

        protected abstract void OnSpawn();
    }
}

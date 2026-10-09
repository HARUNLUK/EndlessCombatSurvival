using UnityEngine;
using EndlessSurvival.Story;
using EndlessSurvival.World;

namespace EndlessSurvival.World.POI
{
    /// <summary>
    /// Spawns a deterministic note in a POI or Chunk based on the Chunk's NotesRandom seed.
    /// </summary>
    public class NoteSpawner : MonoBehaviour
    {
        [Tooltip("Prefab containing the InteractableNote component.")]
        public InteractableNote notePrefab;

        [Tooltip("List of possible notes that can spawn here.")]
        public NoteDefinition[] possibleNotes;

        [Tooltip("Chance to spawn a note (0 = never, 1 = always).")]
        [Range(0f, 1f)]
        public float spawnChance = 0.5f;

        private void Start()
        {
            Chunk chunk = GetComponentInParent<Chunk>();
            SeededRandom rng = chunk != null ? chunk.NotesRandom : new SeededRandom(0);

            // Wait for the terrain to be shaped when the chunk is built over several frames
            if (chunk != null) chunk.WhenBuilt(() => GenerateNote(rng, transform));
            else GenerateNote(rng, transform);
        }

        public void GenerateNote(SeededRandom rng, Transform container)
        {
            if (notePrefab == null || possibleNotes == null || possibleNotes.Length == 0)
            {
                Debug.LogWarning($"NoteSpawner on {gameObject.name}: Prefab or Notes array is missing!");
                return;
            }

            SeededRandom spawnerRng = new SeededRandom(SeededRandom.Combine(rng.Seed, Mathf.RoundToInt(transform.localPosition.sqrMagnitude)));

            if (spawnerRng.Chance(spawnChance))
            {
                NoteDefinition selectedNote = spawnerRng.Pick(possibleNotes);
                
                Vector3 spawnPos = transform.position;
                Chunk chunk = GetComponentInParent<Chunk>();
                Terrain tComponent = chunk?.ChunkTerrain;
                if (tComponent != null)
                {
                    spawnPos.y = tComponent.SampleHeight(spawnPos) + tComponent.transform.position.y + 0.1f;
                }
                else if (Physics.Raycast(spawnPos + Vector3.up * 50f, Vector3.down, out RaycastHit hit, 100f))
                {
                    spawnPos.y = hit.point.y + 0.1f; // Yüzeyden çok az yukarıda
                }

#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    GameObject n = UnityEditor.PrefabUtility.InstantiatePrefab(notePrefab.gameObject, container) as GameObject;
                    if (n != null)
                    {
                        n.transform.position = spawnPos;
                        n.transform.rotation = transform.rotation;
                        InteractableNote note = n.GetComponent<InteractableNote>();
                        if (note != null) note.noteDefinition = selectedNote;
                    }
                }
                else
#endif
                {
                    InteractableNote spawnedNote = Instantiate(notePrefab, spawnPos, transform.rotation, container);
                    spawnedNote.noteDefinition = selectedNote;
                }
            }
        }
    }
}

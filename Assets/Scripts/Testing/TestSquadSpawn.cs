using UnityEngine;
using EndlessCombat.AI;

namespace EndlessSurvival.Testing
{
    /// <summary>
    /// Test scenes: one squad of the new enemy AI, placed in the scene hierarchy (TestArena > NPCs > this). Its
    /// TestNpcSpawn children become the members at Play. Move, add or delete markers in the editor freely.
    /// </summary>
    public class TestSquadSpawn : MonoBehaviour
    {
        [Tooltip("Squad kind (Patrol walks, Camp stays home, Ambush holds fire, Raid comes at the player)")]
        public SquadKind squadKind = SquadKind.Patrol;
        [Tooltip("Starting state of the members that do not set their own")]
        public EnemyController.EnemyState state = EnemyController.EnemyState.Patrolling;
        [Tooltip("Spawn this squad when Play starts (and with the panel button)")]
        public bool spawnOnStart = true;

        private void OnDrawGizmos()
        {
            var markers = GetComponentsInChildren<TestNpcSpawn>();
            if (markers.Length == 0) return;
            Vector3 c = Vector3.zero;
            foreach (var m in markers) c += m.transform.position;
            c /= markers.Length;
            Gizmos.color = spawnOnStart ? new Color(1f, 0.6f, 0.1f, 0.8f) : new Color(0.5f, 0.5f, 0.5f, 0.5f);
            foreach (var m in markers) Gizmos.DrawLine(c + Vector3.up * 0.1f, m.transform.position + Vector3.up * 0.1f);
            Gizmos.DrawWireSphere(c, 0.4f);
        }
    }
}

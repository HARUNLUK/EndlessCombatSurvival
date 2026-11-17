using UnityEngine;
using EndlessCombat.AI;

namespace EndlessSurvival.Testing
{
    /// <summary>
    /// Test scenes: where one enemy stands at Play, and with what. Child of a TestSquadSpawn. Drawn in the Scene view
    /// as a coloured body with a facing arrow (red rifle, orange pistol, yellow knife / bat, blue shield).
    /// </summary>
    public class TestNpcSpawn : MonoBehaviour
    {
        public TestArena.Kind kind = TestArena.Kind.Rifle;
        [Tooltip("Use the squad's starting state")]
        public bool useSquadState = true;
        public EnemyController.EnemyState state = EnemyController.EnemyState.Idle;

        private void OnDrawGizmos()
        {
            Color c;
            switch (kind)
            {
                case TestArena.Kind.Rifle: c = new Color(0.9f, 0.2f, 0.2f); break;
                case TestArena.Kind.Pistol: c = new Color(1f, 0.55f, 0.15f); break;
                case TestArena.Kind.ShieldMelee:
                case TestArena.Kind.ShieldPistol: c = new Color(0.3f, 0.5f, 1f); break;
                default: c = new Color(0.95f, 0.85f, 0.2f); break;
            }
            Vector3 p = transform.position;
            Gizmos.color = c;
            Gizmos.DrawWireCube(p + Vector3.up * 0.9f, new Vector3(0.6f, 1.8f, 0.6f));
            Gizmos.DrawSphere(p + Vector3.up * 1.65f, 0.15f);
            Gizmos.DrawLine(p + Vector3.up * 1.2f, p + Vector3.up * 1.2f + transform.forward * 1.2f);
        }
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace EndlessSurvival.World
{
    /// <summary>
    /// Destroys runtime-generated meshes together with the GameObject that uses them
    /// (meshes created with new Mesh() are not freed automatically).
    /// </summary>
    public class OwnedMeshes : MonoBehaviour
    {
        public readonly List<Mesh> meshes = new List<Mesh>();

        private void OnDestroy()
        {
            for (int i = 0; i < meshes.Count; i++)
            {
                if (meshes[i] == null) continue;
                if (Application.isPlaying) Destroy(meshes[i]);
                else DestroyImmediate(meshes[i]);
            }
            meshes.Clear();
        }
    }
}

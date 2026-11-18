using System.Collections.Generic;
using UnityEngine;

namespace EndlessCombat.Pooling
{
    /// <summary>
    /// Reuses short lived objects (bullet impact effects, later: muzzle flashes, sparks, blood, shells) instead of
    /// Instantiate + Destroy every time. Matters once real effects with particle systems replace the placeholders.
    /// Spawn(prefab, ..., lifetime) hands out an instance and takes it back after lifetime seconds; Release takes it
    /// back at once. Pooled objects live under a hidden root in the current scene, so a scene change clears them
    /// (stale entries are skipped).
    /// </summary>
    public static class PrefabPool
    {
        /// <summary>Most idle copies kept per prefab; more are destroyed when released.</summary>
        public static int MaxIdlePerPrefab = 48;

        private static readonly Dictionary<GameObject, Stack<GameObject>> Idle = new Dictionary<GameObject, Stack<GameObject>>();
        private static Transform _root;

        /// <summary>Copies made so far / copies handed out again (tuning panel performance page).</summary>
        public static int Created { get; private set; }
        public static int Reused { get; private set; }
        public static int Active { get; private set; }

        public static GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime = -1f)
        {
            if (prefab == null) return null;
            GameObject go = null;
            if (Idle.TryGetValue(prefab, out var stack))
            {
                while (stack.Count > 0 && go == null) go = stack.Pop(); // destroyed with an old scene: skipped
            }

            PooledInstance tag;
            if (go == null)
            {
                go = Object.Instantiate(prefab, position, rotation);
                tag = go.AddComponent<PooledInstance>();
                tag.prefab = prefab;
                Created++;
            }
            else
            {
                go.transform.SetParent(null, false);
                go.transform.SetPositionAndRotation(position, rotation);
                tag = go.GetComponent<PooledInstance>();
                go.SetActive(true);
                Restart(go);
                Reused++;
            }
            tag.inPool = false;
            tag.releaseAt = lifetime > 0f ? Time.time + lifetime : -1f;
            Active++;
            return go;
        }

        /// <summary>Puts an object from Spawn back (anything else is destroyed).</summary>
        public static void Release(GameObject go)
        {
            if (go == null) return;
            var tag = go.GetComponent<PooledInstance>();
            if (tag == null || tag.prefab == null)
            {
                Object.Destroy(go);
                return;
            }
            if (tag.inPool) return;
            tag.inPool = true;
            Active = Mathf.Max(0, Active - 1);

            if (!Idle.TryGetValue(tag.prefab, out var stack))
            {
                stack = new Stack<GameObject>();
                Idle[tag.prefab] = stack;
            }
            if (stack.Count >= MaxIdlePerPrefab)
            {
                Object.Destroy(go);
                return;
            }
            go.SetActive(false);
            go.transform.SetParent(Root, false);
            stack.Push(go);
        }

        private static Transform Root
        {
            get
            {
                if (_root == null)
                {
                    var host = new GameObject("PrefabPool");
                    host.hideFlags = HideFlags.HideInHierarchy;
                    _root = host.transform;
                }
                return _root;
            }
        }

        /// <summary>Particle systems and trails start over on a reused copy.</summary>
        private static void Restart(GameObject go)
        {
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Clear(true);
                if (ps.main.playOnAwake) ps.Play(true);
            }
            foreach (var tr in go.GetComponentsInChildren<TrailRenderer>(true)) tr.Clear();
        }
    }

    /// <summary>Marks an object made by PrefabPool and returns it when its time is up.</summary>
    [DisallowMultipleComponent]
    public class PooledInstance : MonoBehaviour
    {
        [HideInInspector] public GameObject prefab;
        [HideInInspector] public float releaseAt = -1f;
        [HideInInspector] public bool inPool;

        private void Update()
        {
            if (releaseAt > 0f && Time.time >= releaseAt)
            {
                releaseAt = -1f;
                PrefabPool.Release(gameObject);
            }
        }
    }
}

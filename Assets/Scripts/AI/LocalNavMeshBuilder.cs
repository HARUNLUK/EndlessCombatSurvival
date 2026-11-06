using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace EndlessCombat.AI
{
    /// <summary>
    /// Keeps a NavMesh baked in a box around the player at runtime. Chunks are procedural, so nothing
    /// can be baked in the editor; enemies only matter near the player, so one moving box is enough.
    /// The bake runs asynchronously and is refreshed when the player moves away from the last bake center.
    /// Created automatically when the scene starts if none exists; add it to the scene to tune the values.
    /// </summary>
    public class LocalNavMeshBuilder : MonoBehaviour
    {
        public static LocalNavMeshBuilder Instance { get; private set; }

        [Header("Area")]
        [Tooltip("Size of the baked box around the player (x/z horizontal, y vertical)")]
        public Vector3 bakeSize = new Vector3(200f, 160f, 200f);
        [Tooltip("Rebake when the player is this far (horizontal) from the last bake center")]
        public float rebakeDistance = 30f;
        [Tooltip("Minimum seconds between two bakes")]
        public float minRebakeInterval = 1f;

        [Header("Quality")]
        [Tooltip("Voxel size of the bake. Larger is faster but less accurate around small obstacles")]
        public float voxelSize = 0.25f;
        [Tooltip("Tile size in voxels")]
        public int tileSize = 128;
        [Tooltip("Layers whose colliders are part of the NavMesh (ground and obstacles)")]
        public LayerMask includedLayers = ~0;

        [Header("Debug")]
        public bool logBakes = false;

        public bool HasNavMesh { get; private set; }

        private NavMeshData _data;
        private NavMeshDataInstance _instance;
        private AsyncOperation _operation;
        private Vector3 _lastCenter;
        private float _lastBakeTime = -999f;
        private bool _hasBaked;
        private GameObject _player;
        private readonly List<NavMeshBuildSource> _sources = new List<NavMeshBuildSource>();
        private readonly List<NavMeshBuildMarkup> _markups = new List<NavMeshBuildMarkup>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindAnyObjectByType<LocalNavMeshBuilder>() != null) return;
            new GameObject("LocalNavMeshBuilder").AddComponent<LocalNavMeshBuilder>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnEnable()
        {
            _data = new NavMeshData(0);
            _instance = NavMesh.AddNavMeshData(_data);
        }

        private void OnDisable()
        {
            _instance.Remove();
            HasNavMesh = false;
            _hasBaked = false;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (_operation != null && !_operation.isDone) return;
            if (_operation != null)
            {
                _operation = null;
                HasNavMesh = true;
                if (logBakes) Debug.Log($"[LocalNavMeshBuilder] Bake finished around {_lastCenter}");
            }

            if (!TryGetCenter(out Vector3 center)) return;
            if (Time.time - _lastBakeTime < minRebakeInterval) return;

            Vector3 offset = center - _lastCenter;
            offset.y = 0f;
            if (_hasBaked && offset.magnitude < rebakeDistance) return;

            StartBake(center);
        }

        private bool TryGetCenter(out Vector3 center)
        {
            // The player object is deactivated while driving, so it is cached once and the camera is used instead
            if (_player == null) _player = GameObject.FindGameObjectWithTag("Player");
            if (_player != null && _player.activeInHierarchy)
            {
                center = _player.transform.position;
                return true;
            }

            var cam = Camera.main;
            if (cam != null)
            {
                center = cam.transform.position;
                return true;
            }

            center = Vector3.zero;
            return false;
        }

        private void StartBake(Vector3 center)
        {
            _lastCenter = center;
            _lastBakeTime = Time.time;
            _hasBaked = true;

            var bounds = new Bounds(center, bakeSize);
            NavMeshBuilder.CollectSources(bounds, includedLayers, NavMeshCollectGeometry.PhysicsColliders, 0, _markups, _sources);
            RemoveDynamicSources();

            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
            settings.overrideVoxelSize = true;
            settings.voxelSize = voxelSize;
            settings.overrideTileSize = true;
            settings.tileSize = tileSize;

            _operation = NavMeshBuilder.UpdateNavMeshDataAsync(_data, settings, _sources, bounds);
        }

        /// <summary>
        /// Characters, the vehicle, pickups and triggers must not become part of the ground or cut holes in it.
        /// </summary>
        private void RemoveDynamicSources()
        {
            _sources.RemoveAll(source =>
            {
                var col = source.component as Collider;
                if (col == null) return false;
                if (col.isTrigger) return true;
                if (col is CharacterController) return true;
                if (col.attachedRigidbody != null) return true;
                // Player and enemy hitboxes, weapons and limbs sit under a CharacterController
                if (col.GetComponentInParent<CharacterController>() != null) return true;
                if (col.GetComponentInParent<EnemyController>() != null) return true;
                return false;
            });
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.World.Road;
using EndlessSurvival.World.POI;

namespace EndlessSurvival.World
{
    public enum WreckPose
    {
        Shoulder,       // Yol kenarına park edilip bırakılmış
        StalledInLane,  // Şeritte kalmış, hafif yamuk
        Crossways,      // Şeride çapraz (kaza)
        OnSide,         // Yan yatmış
        Flipped,        // Ters dönmüş (tavan üstü)
        Ditch,          // Yoldan çıkıp hendeğe burnunu gömmüş
        OffRoad         // Yoldan uzağa, araziye savrulmuş
    }

    public enum WreckVehicleType
    {
        Sedan,
        Hatchback,
        Pickup,
        Van,
        BoxTruck
    }

    /// <summary>
    /// Terk edilmiş araç enkazları: yolun üstünde, kenarında ve hendeklerde farklı duruşlarla (park, çapraz,
    /// yan yatmış, ters, hendekte) küplerden oluşan basit araçlar. Tüm araçlar chunk başına tek birleşik mesh'tir
    /// (malzeme başına bir alt-mesh), her aracın ana gövdesi ayrı bir BoxCollider alır.
    /// Chunk'ın "wrecks" seed alt akışını kullanır; aynı seed aynı enkazları üretir.
    /// Yolun en az bir şeridi her zaman açık kalır (yol üstü enkazlar tek tarafta toplanır).
    /// </summary>
    public class AbandonedVehicleSpawner : MonoBehaviour
    {
        public const string CONTAINER_NAME = "Abandoned_Vehicles";

        [Header("Sayı")]
        public bool wrecksEnabled = true;
        [Tooltip("Chunk başına tekil araç sayısı (kaza yığınları hariç)")]
        public int minVehicles = 2;
        public int maxVehicles = 6;
        [Tooltip("Chunk'ta 3-5 araçlık bir kaza yığını olma ihtimali")]
        [Range(0f, 1f)] public float pileupChance = 0.3f;
        public int minPileupVehicles = 3;
        public int maxPileupVehicles = 5;

        [Header("Biyom Çarpanları")]
        public float forestMultiplier = 1f;
        public float fieldMultiplier = 1.2f;
        public float coastMultiplier = 0.8f;
        public float mountainMultiplier = 0.6f;

        [Header("Duruş Ağırlıkları")]
        public int shoulderWeight = 30;
        public int stalledWeight = 14;
        public int crosswaysWeight = 8;
        public int onSideWeight = 10;
        public int flippedWeight = 10;
        public int ditchWeight = 20;
        public int offRoadWeight = 8;

        [Header("Araç Tipi Ağırlıkları")]
        public int sedanWeight = 35;
        public int hatchbackWeight = 20;
        public int pickupWeight = 20;
        public int vanWeight = 15;
        public int boxTruckWeight = 10;

        [Header("Hasar")]
        [Range(0f, 1f)] public float burnedChance = 0.2f;
        [Range(0f, 1f)] public float rustedChance = 0.15f;
        [Range(0f, 1f)] public float rustPatchChance = 0.6f;
        [Range(0f, 1f)] public float missingWheelChance = 0.12f;
        [Range(0f, 1f)] public float openDoorChance = 0.25f;
        [Range(0f, 1f)] public float openHoodChance = 0.15f;
        [Range(0f, 1f)] public float brokenGlassChance = 0.3f;

        [Header("Yerleşim")]
        [Tooltip("Tekil enkazlar arası en az mesafe")]
        public float minSpacing = 14f;
        [Tooltip("Araziye savrulmuş enkazların yol merkezine uzaklığı")]
        public float offRoadMinDistance = 16f;
        public float offRoadMaxDistance = 40f;
        [Tooltip("Kamp/mağara/fener çevresinde enkaz konmaz")]
        public float poiClearance = 30f;
        [Tooltip("İlk chunk'ın başında (oyuncunun başladığı yer) enkaz konmayan uzunluk")]
        public float startClearLength = 90f;
        [Tooltip("Arazi bu açıdan dikse enkaz konmaz")]
        public float maxGroundSlope = 32f;

        [Header("Renkler")]
        public Color[] paintColors =
        {
            new Color(0.50f, 0.17f, 0.14f), // soluk kırmızı
            new Color(0.22f, 0.31f, 0.43f), // soluk mavi
            new Color(0.34f, 0.37f, 0.24f), // haki
            new Color(0.74f, 0.70f, 0.59f), // krem
            new Color(0.58f, 0.59f, 0.58f), // gri
            new Color(0.68f, 0.54f, 0.21f), // hardal
            new Color(0.13f, 0.14f, 0.15f)  // siyah
        };
        public Color rustColor = new Color(0.42f, 0.23f, 0.11f);
        public Color burntColor = new Color(0.09f, 0.08f, 0.075f);
        public Color glassColor = new Color(0.10f, 0.13f, 0.16f);
        public Color tireColor = new Color(0.06f, 0.06f, 0.06f);
        public Color trimColor = new Color(0.30f, 0.30f, 0.31f);
        public Color headlightColor = new Color(0.85f, 0.82f, 0.65f);
        public Color taillightColor = new Color(0.50f, 0.07f, 0.06f);

        // Bitki örtüsü bu alanlara ağaç dikmez (ChunkVegetationSpawner)
        private readonly List<ChunkVegetationSpawner.PoiExclusionZone> _clearings = new List<ChunkVegetationSpawner.PoiExclusionZone>();
        public IReadOnlyList<ChunkVegetationSpawner.PoiExclusionZone> VegetationClearings => _clearings;

        private struct Part
        {
            public Vector3 center, size;
            public Quaternion rot;
            public int submesh;
            public bool solid; // gövdeye ait (collider sınırına dahil)
        }

        private struct Placement
        {
            public Vector3 position;
            public float localZ;
            public float side;   // -1 sol, +1 sağ
            public bool onRoad;
        }

        // Generation state
        private Chunk _chunk;
        private RoadSpline _spline;
        private Terrain _terrain;
        private ChunkBoundaryGenerator _boundary;
        private FieldEdgeGenerator _fieldEdges;
        private float _asphaltHalf, _edgeHalf, _roadElevation, _splineLength;
        private readonly List<Placement> _placed = new List<Placement>();
        private readonly List<Vector3> _poiPositions = new List<Vector3>();
        private readonly List<Part> _parts = new List<Part>();

        private int SubRust => paintColors.Length;
        private int SubBurnt => paintColors.Length + 1;
        private int SubGlass => paintColors.Length + 2;
        private int SubTire => paintColors.Length + 3;
        private int SubTrim => paintColors.Length + 4;
        private int SubHeadlight => paintColors.Length + 5;
        private int SubTaillight => paintColors.Length + 6;
        private int SubmeshCount => paintColors.Length + 7;

        /// <summary>Enkazları <paramref name="parent"/> altına üretir. Arazi şekillendikten sonra çağrılmalı.</summary>
        public void Generate(Transform parent, Terrain terrain, SeededRandom rng)
        {
            Clear(parent);
            _clearings.Clear();
            _placed.Clear();
            if (!wrecksEnabled || parent == null || paintColors == null || paintColors.Length == 0) return;

            _chunk = GetComponent<Chunk>();
            if (_chunk == null) _chunk = GetComponentInParent<Chunk>();
            if (_chunk == null) return;

            _spline = _chunk.GetComponentInChildren<RoadSpline>();
            var roadGen = _chunk.GetComponentInChildren<RoadGenerator>();
            if (_spline == null || roadGen == null) return;

            if (rng == null) rng = new SeededRandom(SeededRandom.Combine(_chunk.ChunkSeed != 0 ? _chunk.ChunkSeed : 1, "wrecks"));

            _terrain = terrain;
            _boundary = _chunk.GetComponent<ChunkBoundaryGenerator>();
            _fieldEdges = _chunk.IsField ? _chunk.GetComponent<FieldEdgeGenerator>() : null;
            _asphaltHalf = roadGen.GetAsphaltHalfWidth();
            _edgeHalf = roadGen.GetTotalHalfWidth();
            _roadElevation = roadGen.roadElevation;
            _splineLength = Mathf.Max(1f, _spline.ApproximateLength());

            _poiPositions.Clear();
            var pois = _chunk.GetComponentsInChildren<PointOfInterest>(true);
            for (int i = 0; i < pois.Length; i++)
            {
                if (pois[i] != null) _poiPositions.Add(pois[i].transform.position);
            }

            // Container sits on the chunk origin so the combined mesh is built in chunk-local space
            Transform container = new GameObject(CONTAINER_NAME).transform;
            container.SetPositionAndRotation(_chunk.transform.position, _chunk.transform.rotation);
            container.SetParent(parent, true);

            var mb = new BoxMeshBuilder(SubmeshCount);
            bool hazard = _chunk.roadType == ChunkRoadType.HazardZone;

            // 1) Kaza yığını: tek noktada, yolun bir tarafında toplanmış birkaç araç
            if (rng.Chance(hazard ? Mathf.Min(1f, pileupChance * 2f) : pileupChance))
            {
                SpawnPileup(rng, mb, container);
            }

            // 2) Tekil enkazlar
            float multiplier = BiomeMultiplier();
            int count = Mathf.RoundToInt(rng.Range(minVehicles, maxVehicles + 1) * multiplier) + (hazard ? 2 : 0);
            for (int i = 0; i < count; i++)
            {
                WreckVehicleType type = PickType(rng);
                WreckPose pose = PickPose(rng);
                float side = rng.Value < 0.5f ? -1f : 1f;

                // Birkaç kez dene: yer bulunamazsa o araç atlanır
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    float t = rng.Range(0.03f, 0.97f);
                    if (TrySpawnVehicle(rng, mb, container, type, pose, t, side, minSpacing)) break;
                }
            }

            if (mb.VertexCount == 0) return;

            Mesh mesh = mb.ToMesh($"AbandonedVehicles_Chunk{_chunk.ChunkIndex}");
            container.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            container.gameObject.AddComponent<OwnedMeshes>().meshes.Add(mesh);

            var materials = new Material[SubmeshCount];
            for (int i = 0; i < paintColors.Length; i++) materials[i] = ChunkBoundaryGenerator.GetSharedMaterial(paintColors[i]);
            materials[SubRust] = ChunkBoundaryGenerator.GetSharedMaterial(rustColor);
            materials[SubBurnt] = ChunkBoundaryGenerator.GetSharedMaterial(burntColor);
            materials[SubGlass] = ChunkBoundaryGenerator.GetSharedMaterial(glassColor);
            materials[SubTire] = ChunkBoundaryGenerator.GetSharedMaterial(tireColor);
            materials[SubTrim] = ChunkBoundaryGenerator.GetSharedMaterial(trimColor);
            materials[SubHeadlight] = ChunkBoundaryGenerator.GetSharedMaterial(headlightColor);
            materials[SubTaillight] = ChunkBoundaryGenerator.GetSharedMaterial(taillightColor);
            container.gameObject.AddComponent<MeshRenderer>().sharedMaterials = materials;
        }

        // ---------------------------------------------------------------
        // Placement
        // ---------------------------------------------------------------

        private void SpawnPileup(SeededRandom rng, BoxMeshBuilder mb, Transform container)
        {
            float side = rng.Value < 0.5f ? -1f : 1f;
            int count = rng.Range(minPileupVehicles, maxPileupVehicles + 1);

            for (int attempt = 0; attempt < 6; attempt++)
            {
                float t0 = rng.Range(0.15f, 0.85f);
                if (!IsValidRoadSpot(t0, 0f)) continue;

                // İlk araç kazanın merkezi: şeride çapraz. Diğerleri arkasına/önüne yığılır.
                int spawned = 0;
                float forward = 0f, backward = 0f;
                for (int i = 0; i < count; i++)
                {
                    WreckVehicleType type = i == 0 ? PickCarType(rng) : PickType(rng);
                    WreckPose pose;
                    if (i == 0) pose = WreckPose.Crossways;
                    else
                    {
                        int roll = rng.Range(0, 100);
                        pose = roll < 25 ? WreckPose.StalledInLane
                             : roll < 45 ? WreckPose.OnSide
                             : roll < 62 ? WreckPose.Flipped
                             : roll < 82 ? WreckPose.Ditch
                             : WreckPose.Shoulder;
                    }

                    float offset = 0f;
                    if (i > 0)
                    {
                        bool ahead = rng.Value < 0.5f;
                        float step = rng.Range(6.5f, 11f);
                        if (ahead) { forward += step; offset = forward; }
                        else { backward += step; offset = -backward; }
                    }

                    float t = t0 + offset / _splineLength;
                    if (TrySpawnVehicle(rng, mb, container, type, pose, t, side, 5.5f, debris: true)) spawned++;
                }

                if (spawned > 0) return;
            }
        }

        private bool TrySpawnVehicle(SeededRandom rng, BoxMeshBuilder mb, Transform container,
            WreckVehicleType type, WreckPose pose, float t, float side, float spacing, bool debris = false)
        {
            if (t < 0.01f || t > 0.99f) return false;

            // Uzun araçlar yola çapraz duramaz (iki şeridi birden kapatır)
            if (pose == WreckPose.Crossways && (type == WreckVehicleType.Van || type == WreckVehicleType.BoxTruck))
                pose = WreckPose.StalledInLane;

            // Kenar yolunda (uçurum) yol dışı enkazlar sadece dağ tarafında
            bool offRoad = pose == WreckPose.Ditch || pose == WreckPose.OffRoad;
            bool roadSideVariant = (pose == WreckPose.OnSide || pose == WreckPose.Flipped) && rng.Value < 0.45f;
            if (roadSideVariant) offRoad = true;
            if (offRoad && _chunk.IsMountain && _chunk.mountainLayout == MountainLayout.Ledge) side = _chunk.GetLandSide(side);

            bool burned = rng.Chance(burnedChance);
            BuildVehicle(type, rng, burned, out float length, out float width, out Bounds solidBounds);

            // Duruşa göre yanal mesafe
            float lateral;
            switch (pose)
            {
                case WreckPose.Shoulder:
                    lateral = _asphaltHalf - width * 0.5f - rng.Range(0.1f, 0.5f);
                    break;
                case WreckPose.StalledInLane:
                    lateral = rng.Range(_asphaltHalf * 0.3f, Mathf.Max(_asphaltHalf * 0.3f, _asphaltHalf - width * 0.5f - 0.1f));
                    break;
                case WreckPose.Crossways:
                    lateral = Mathf.Clamp(_asphaltHalf - length * 0.5f, length * 0.5f + 0.2f, _asphaltHalf);
                    break;
                case WreckPose.OnSide:
                case WreckPose.Flipped:
                    lateral = offRoad
                        ? _edgeHalf + width * 0.5f + rng.Range(1.5f, 7f)
                        : rng.Range(_asphaltHalf * 0.35f, Mathf.Max(_asphaltHalf * 0.35f, _asphaltHalf - 1.2f));
                    break;
                case WreckPose.Ditch:
                    lateral = _edgeHalf + width * 0.5f + rng.Range(1.5f, 8f);
                    break;
                default: // OffRoad
                    lateral = rng.Range(offRoadMinDistance, offRoadMaxDistance);
                    break;
            }

            // Yol üstü: önce bu tarafta, olmazsa karşı tarafta dene. Yol dışı: su/uçurum/dağ içi kontrolü.
            if (!TryGetGround(t, side, lateral, offRoad, out Vector3 groundPos, out Quaternion groundRot, out float localZ))
            {
                side = -side;
                if (offRoad && _chunk.IsMountain && _chunk.mountainLayout == MountainLayout.Ledge) return false;
                if (!TryGetGround(t, side, lateral, offRoad, out groundPos, out groundRot, out localZ)) return false;
            }

            if (!IsValidRoadSpot(t, localZ, groundPos)) return false;
            if (!CheckSpacing(groundPos, localZ, side, !offRoad, spacing)) return false;

            // Trafik yönü: sağ şeritteki araç ileri, sol şeritteki geri bakar (biraz rastgele)
            float facing = (side > 0f) == (rng.Value < 0.85f) ? 0f : 180f;
            Quaternion poseRot = PoseRotation(pose, rng, side, facing);

            // Eksik tekerlek: araç o köşeye doğru hafif çöker
            if (_missingWheel && (pose == WreckPose.Shoulder || pose == WreckPose.StalledInLane || pose == WreckPose.Ditch))
                poseRot = poseRot * Quaternion.Euler(rng.Range(-3f, 3f), 0f, rng.Range(-4f, 4f));

            // Aracı zemine oturt: dönmüş parçaların en alt noktası zemin seviyesine gelir
            float minY = float.MaxValue;
            for (int i = 0; i < _parts.Count; i++)
            {
                Part p = _parts[i];
                minY = Mathf.Min(minY, LowestCorner(poseRot * p.center, poseRot * p.rot, p.size));
            }
            float sink = offRoad ? 0.12f : 0.02f;
            Vector3 lift = Vector3.up * (-minY - sink);

            Quaternion containerInv = Quaternion.Inverse(container.rotation);
            for (int i = 0; i < _parts.Count; i++)
            {
                Part p = _parts[i];
                Vector3 world = groundPos + groundRot * (poseRot * p.center + lift);
                Quaternion rot = groundRot * poseRot * p.rot;
                mb.Box(container.InverseTransformPoint(world), p.size, containerInv * rot, p.submesh);
            }

            // Gövde collider'ı (araç, oyuncu ve araç geçemez)
            var col = new GameObject("WreckCollider");
            col.transform.SetParent(container, false);
            col.transform.localPosition = container.InverseTransformPoint(groundPos + groundRot * (poseRot * solidBounds.center + lift));
            col.transform.localRotation = containerInv * groundRot * poseRot;
            col.AddComponent<BoxCollider>().size = solidBounds.size;

            if (debris || (!offRoad && (pose == WreckPose.Crossways || pose == WreckPose.OnSide || pose == WreckPose.Flipped)))
                SpawnDebris(rng, mb, container, groundPos, groundRot, length);

            _placed.Add(new Placement { position = groundPos, localZ = localZ, side = side, onRoad = !offRoad });
            _clearings.Add(new ChunkVegetationSpawner.PoiExclusionZone(groundPos, length * 0.5f + 2.5f));
            return true;
        }

        private Quaternion PoseRotation(WreckPose pose, SeededRandom rng, float side, float facing)
        {
            switch (pose)
            {
                case WreckPose.Shoulder:
                    return Quaternion.Euler(0f, facing + rng.Range(-6f, 6f), 0f);
                case WreckPose.StalledInLane:
                    return Quaternion.Euler(0f, facing + rng.Range(-25f, 25f), 0f);
                case WreckPose.Crossways:
                    return Quaternion.Euler(0f, (rng.Value < 0.5f ? 90f : -90f) + rng.Range(-20f, 20f), 0f);
                case WreckPose.OnSide:
                    return Quaternion.Euler(0f, facing + rng.Range(-40f, 40f), 0f)
                         * Quaternion.Euler(0f, 0f, (rng.Value < 0.5f ? 90f : -90f) + rng.Range(-4f, 4f));
                case WreckPose.Flipped:
                    return Quaternion.Euler(0f, facing + rng.Range(-50f, 50f), 0f)
                         * Quaternion.Euler(rng.Range(-5f, 5f), 0f, 180f + rng.Range(-6f, 6f));
                case WreckPose.Ditch:
                    // Burun yoldan dışarı dönük ve aşağı gömülü
                    float yaw = side * rng.Range(10f, 45f);
                    if (facing > 90f) yaw = 180f - yaw;
                    return Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(rng.Range(6f, 20f), 0f, rng.Range(-8f, 8f));
                default: // OffRoad
                    Quaternion yawRot = Quaternion.Euler(0f, rng.Range(0f, 360f), 0f);
                    float roll = rng.Value;
                    if (roll < 0.3f) return yawRot * Quaternion.Euler(0f, 0f, rng.Value < 0.5f ? 90f : -90f);
                    if (roll < 0.5f) return yawRot * Quaternion.Euler(0f, 0f, 180f + rng.Range(-8f, 8f));
                    return yawRot * Quaternion.Euler(rng.Range(-4f, 4f), 0f, rng.Range(-4f, 4f));
            }
        }

        private bool TryGetGround(float t, float side, float lateral, bool offRoad,
            out Vector3 groundPos, out Quaternion groundRot, out float localZ)
        {
            Transform st = _spline.transform;
            Vector3 centerLocal = _spline.GetPoint(t);
            Vector3 rightLocal = _spline.GetRight(t);
            Vector3 tangentWorld = st.TransformDirection(_spline.GetTangent(t));
            Vector3 center = st.TransformPoint(centerLocal);
            Vector3 right = st.TransformDirection(rightLocal);

            groundPos = center + right * side * lateral;
            Vector3 chunkLocal = _chunk.transform.InverseTransformPoint(groundPos);
            localZ = chunkLocal.z;
            groundRot = Quaternion.identity;

            if (localZ < 12f || localZ > _chunk.chunkLength - 12f || Mathf.Abs(chunkLocal.x) > 240f) return false;

            if (!offRoad)
            {
                // Yol yüzeyi: spline kotu + asfalt yüksekliği, yolun eğimini takip eder
                groundPos += Vector3.up * _roadElevation;
                groundRot = Quaternion.LookRotation(tangentWorld, Vector3.up);
                return true;
            }

            if (_terrain == null || _terrain.terrainData == null) return false;

            groundPos.y = _terrain.SampleHeight(groundPos) + _terrain.transform.position.y;

            // Deniz, nehir yatağı, kanyon ve dağ mesh'inin içi
            if (_chunk.IsCoast && groundPos.y < _chunk.transform.position.y + WorldConstants.SeaLevel + 1f) return false;
            if (_chunk.IsField && groundPos.y < _chunk.transform.position.y + _chunk.baseElevation - 1.5f) return false;
            if (_chunk.IsMountain && groundPos.y < center.y - 3f) return false;
            if (_boundary != null)
            {
                if (_boundary.IsBlockedForVegetation(chunkLocal)) return false;
                if (_chunk.IsMountain && _boundary.IsInsideMountain(chunkLocal)) return false;
            }

            // Zeminin eğimine yatır (çok dik yamaçlarda konmaz)
            Vector3 tp = _terrain.transform.position;
            Vector3 size = _terrain.terrainData.size;
            Vector3 normal = _terrain.terrainData.GetInterpolatedNormal((groundPos.x - tp.x) / size.x, (groundPos.z - tp.z) / size.z);
            if (Vector3.Angle(normal, Vector3.up) > maxGroundSlope) return false;

            Vector3 flatForward = Vector3.ProjectOnPlane(tangentWorld, Vector3.up);
            if (flatForward.sqrMagnitude < 0.001f) flatForward = _chunk.transform.forward;
            groundRot = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.LookRotation(flatForward.normalized, Vector3.up);
            return true;
        }

        /// <summary>Road point at <paramref name="t"/> (and the wreck itself at <paramref name="wreckPos"/>) is free of river and POIs.</summary>
        private bool IsValidRoadSpot(float t, float localZ, Vector3? wreckPos = null)
        {
            Vector3 roadWorld = _spline.transform.TransformPoint(_spline.GetPoint(t));
            if (localZ <= 0f) localZ = _chunk.transform.InverseTransformPoint(roadWorld).z;

            // Oyuncunun başladığı ilk chunk'ın başı boş kalır
            if (_chunk.Manager != null && _chunk.ChunkIndex == 0 && localZ < startClearLength) return false;

            if (_fieldEdges != null && _fieldEdges.IsNearRiver(_chunk.transform.InverseTransformPoint(roadWorld), 10f)) return false;

            Vector3 probe = wreckPos ?? roadWorld;
            float poiSqr = poiClearance * poiClearance;
            for (int i = 0; i < _poiPositions.Count; i++)
            {
                Vector3 d = _poiPositions[i] - probe;
                d.y = 0f;
                if (d.sqrMagnitude < poiSqr) return false;
            }
            return true;
        }

        private bool CheckSpacing(Vector3 pos, float localZ, float side, bool onRoad, float spacing)
        {
            float sqr = spacing * spacing;
            for (int i = 0; i < _placed.Count; i++)
            {
                Placement p = _placed[i];
                Vector3 d = p.position - pos;
                d.y = 0f;
                if (d.sqrMagnitude < sqr) return false;

                // Yolun iki şeridini aynı hizada kapatma: yol üstü enkazlar karşı karşıya gelemez
                if (onRoad && p.onRoad && p.side != side && Mathf.Abs(p.localZ - localZ) < 18f) return false;
            }
            return true;
        }

        private void SpawnDebris(SeededRandom rng, BoxMeshBuilder mb, Transform container, Vector3 groundPos, Quaternion groundRot, float length)
        {
            Quaternion containerInv = Quaternion.Inverse(container.rotation);
            int pieces = rng.Range(3, 8);
            for (int i = 0; i < pieces; i++)
            {
                Vector3 offset = new Vector3(rng.Range(-length * 0.6f, length * 0.6f), 0f, rng.Range(-length * 0.8f, length * 0.8f));
                // Etrafa saçılmış parçalar (collider yok, araç üstünden geçer)
                Vector3 local = groundRot * offset;
                Vector3 size = new Vector3(rng.Range(0.12f, 0.6f), rng.Range(0.03f, 0.12f), rng.Range(0.12f, 0.7f));
                int sub;
                float roll = rng.Value;
                if (roll < 0.15f)
                {
                    // Yuvarlanıp düşmüş lastik
                    size = new Vector3(0.66f, 0.26f, 0.66f);
                    sub = SubTire;
                }
                else sub = roll < 0.4f ? SubGlass : roll < 0.75f ? SubTrim : rng.Range(0, paintColors.Length);

                Vector3 world = groundPos + local + groundRot * Vector3.up * (size.y * 0.5f);
                Quaternion rot = groundRot * Quaternion.Euler(0f, rng.Range(0f, 360f), 0f);
                mb.Box(container.InverseTransformPoint(world), size, containerInv * rot, sub);
            }
        }

        // ---------------------------------------------------------------
        // Vehicle shapes (car-local: y = 0 ground, +Z forward)
        // ---------------------------------------------------------------

        private bool _missingWheel;

        private void BuildVehicle(WreckVehicleType type, SeededRandom rng, bool burned,
            out float length, out float width, out Bounds solidBounds)
        {
            _parts.Clear();
            _missingWheel = false;

            int paint = rng.Range(0, paintColors.Length);
            if (rng.Chance(rustedChance)) paint = SubRust;
            if (burned) paint = SubBurnt;
            bool glass = !burned && !rng.Chance(brokenGlassChance);

            switch (type)
            {
                case WreckVehicleType.Hatchback:
                    BuildCar(rng, paint, glass, burned, 3.9f, 1.72f, 0.28f, 0.65f, 2.3f, 1.5f, 0.6f, -0.45f, false);
                    length = 3.9f; width = 1.72f;
                    break;
                case WreckVehicleType.Pickup:
                    BuildCar(rng, paint, glass, burned, 5.3f, 1.95f, 0.45f, 0.72f, 1.75f, 1.78f, 0.66f, 0.6f, true);
                    length = 5.3f; width = 1.95f;
                    break;
                case WreckVehicleType.Van:
                    BuildVan(rng, paint, glass, burned);
                    length = 4.9f; width = 2.0f;
                    break;
                case WreckVehicleType.BoxTruck:
                    BuildTruck(rng, paint, glass, burned);
                    length = 7.2f; width = 2.4f;
                    break;
                default:
                    BuildCar(rng, paint, glass, burned, 4.5f, 1.8f, 0.3f, 0.65f, 2.3f, 1.55f, 0.55f, -0.25f, false);
                    length = 4.5f; width = 1.8f;
                    break;
            }

            // Collider: gövde parçalarının sınırı (açık kapı/kaput ve tekerlekler hariç)
            solidBounds = new Bounds();
            bool first = true;
            for (int i = 0; i < _parts.Count; i++)
            {
                if (!_parts[i].solid) continue;
                var b = new Bounds(_parts[i].center, _parts[i].size);
                if (first) { solidBounds = b; first = false; }
                else solidBounds.Encapsulate(b);
            }
        }

        private void BuildCar(SeededRandom rng, int paint, bool glass, bool burned,
            float L, float W, float clearance, float bodyH, float cabinL, float cabinW, float cabinH, float cabinZ, bool pickupBed)
        {
            float bodyTop = clearance + bodyH;
            Add(new Vector3(0f, clearance + bodyH * 0.5f, 0f), new Vector3(W, bodyH, L), paint, true);

            Vector3 cabinC = new Vector3(0f, bodyTop + cabinH * 0.5f, cabinZ);
            Add(cabinC, new Vector3(cabinW, cabinH, cabinL), paint, true);
            if (glass) AddGlass(cabinC, cabinW, cabinH, cabinL);

            if (pickupBed)
            {
                // Kasa yanları ve arka kapak
                float bedStart = cabinZ - cabinL * 0.5f - 0.1f;
                float bedEnd = -L * 0.5f + 0.05f;
                float bedLen = bedStart - bedEnd;
                float bedMid = (bedStart + bedEnd) * 0.5f;
                for (int s = -1; s <= 1; s += 2)
                    Add(new Vector3(s * (W * 0.5f - 0.05f), bodyTop + 0.22f, bedMid), new Vector3(0.1f, 0.44f, bedLen), paint, true);
                Add(new Vector3(0f, bodyTop + 0.22f, bedEnd + 0.05f), new Vector3(W, 0.44f, 0.1f), paint, true);
            }

            AddBumpersAndLights(L, W, clearance, burned);
            AddWheels(rng, burned, W, 0.66f, new[] { L * 0.5f - 0.85f, -L * 0.5f + 0.85f });
            AddDamage(rng, paint, burned, L, W, clearance, bodyH, bodyTop, cabinZ + cabinL * 0.5f, true);
        }

        private void BuildVan(SeededRandom rng, int paint, bool glass, bool burned)
        {
            const float L = 4.9f, W = 2.0f, clearance = 0.35f;
            float mainL = 3.9f, mainH = 1.75f;
            Vector3 mainC = new Vector3(0f, clearance + mainH * 0.5f, -0.5f);
            Add(mainC, new Vector3(W, mainH, mainL), paint, true);
            Add(new Vector3(0f, clearance + 0.375f, 1.95f), new Vector3(W, 0.75f, 1.0f), paint, true); // kaput

            if (glass)
            {
                // Ön cam ve ön yan camlar
                Add(new Vector3(0f, clearance + mainH * 0.72f, mainC.z + mainL * 0.5f + 0.01f), new Vector3(W * 0.86f, mainH * 0.32f, 0.04f), SubGlass, false);
                Add(new Vector3(0f, clearance + mainH * 0.72f, mainC.z + mainL * 0.5f - 0.6f), new Vector3(W + 0.03f, mainH * 0.3f, 0.9f), SubGlass, false);
            }

            AddBumpersAndLights(L, W, clearance, burned);
            AddWheels(rng, burned, W, 0.7f, new[] { L * 0.5f - 0.8f, -L * 0.5f + 0.9f });
            AddDamage(rng, paint, burned, L, W, clearance, 0.75f, clearance + 0.75f, 1.45f, false);
        }

        private void BuildTruck(SeededRandom rng, int paint, bool glass, bool burned)
        {
            const float L = 7.2f, W = 2.2f;
            // Şasi, kabin, kasa
            Add(new Vector3(0f, 0.75f, 0f), new Vector3(1.2f, 0.3f, L - 0.2f), SubTrim, true);
            Vector3 cabC = new Vector3(0f, 0.5f + 0.95f, 2.6f);
            Add(cabC, new Vector3(W, 1.9f, 2.0f), paint, true);
            if (glass)
                Add(new Vector3(0f, cabC.y + 0.4f, cabC.z + 1.01f), new Vector3(W * 0.84f, 0.7f, 0.04f), SubGlass, false);

            int cargo = burned ? SubBurnt : (rng.Value < 0.5f ? rng.Range(0, paintColors.Length) : SubTrim);
            Add(new Vector3(0f, 0.9f + 1.3f, -1.0f), new Vector3(2.4f, 2.6f, 5.0f), cargo, true);

            // Kasa kapağı açık kalmış
            if (rng.Chance(0.35f))
            {
                Vector3 hinge = new Vector3(1.2f, 2.2f, -3.5f);
                Quaternion r = Quaternion.Euler(0f, -rng.Range(60f, 110f), 0f);
                Add(hinge + r * new Vector3(-0.6f, 0f, 0f), new Vector3(1.2f, 2.5f, 0.06f), cargo, false, r);
            }

            AddBumpersAndLights(L, W, 0.5f, burned);
            AddWheels(rng, burned, W, 0.95f, new[] { 2.6f, -1.6f, -2.7f });
        }

        private void AddGlass(Vector3 cabinC, float cabinW, float cabinH, float cabinL)
        {
            // Kabinden 1.5cm taşan iki ince kutu: yan camlar ve ön/arka cam
            Vector3 c = cabinC + Vector3.up * cabinH * 0.06f;
            Add(c, new Vector3(cabinW + 0.03f, cabinH * 0.62f, cabinL * 0.86f), SubGlass, false);
            Add(c, new Vector3(cabinW * 0.86f, cabinH * 0.62f, cabinL + 0.03f), SubGlass, false);
        }

        private void AddBumpersAndLights(float L, float W, float clearance, bool burned)
        {
            float y = clearance + 0.12f;
            Add(new Vector3(0f, y, L * 0.5f + 0.04f), new Vector3(W + 0.04f, 0.22f, 0.16f), burned ? SubBurnt : SubTrim, true);
            Add(new Vector3(0f, y, -L * 0.5f - 0.04f), new Vector3(W + 0.04f, 0.22f, 0.16f), burned ? SubBurnt : SubTrim, true);
            if (burned) return;

            float ly = clearance + 0.38f;
            for (int s = -1; s <= 1; s += 2)
            {
                Add(new Vector3(s * (W * 0.5f - 0.3f), ly, L * 0.5f + 0.01f), new Vector3(0.32f, 0.14f, 0.04f), SubHeadlight, false);
                Add(new Vector3(s * (W * 0.5f - 0.25f), ly, -L * 0.5f - 0.01f), new Vector3(0.26f, 0.14f, 0.04f), SubTaillight, false);
            }
        }

        private void AddWheels(SeededRandom rng, bool burned, float W, float diameter, float[] axles)
        {
            for (int a = 0; a < axles.Length; a++)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    if (rng.Chance(missingWheelChance))
                    {
                        _missingWheel = true;
                        continue;
                    }
                    // Yanmış araçta lastik yok, sadece jant kalmış
                    float d = burned ? diameter * 0.62f : diameter;
                    Add(new Vector3(s * (W * 0.5f - 0.1f), d * 0.5f, axles[a]),
                        new Vector3(burned ? 0.2f : 0.28f, d, d), burned ? SubTrim : SubTire, false);
                }
            }
        }

        private void AddDamage(SeededRandom rng, int paint, bool burned, float L, float W, float clearance,
            float bodyH, float bodyTop, float hoodRearZ, bool hasDoors)
        {
            // Açık kalmış kapı (kabinin ön kenarından menteşeli)
            if (hasDoors && rng.Chance(openDoorChance))
            {
                float s = rng.Value < 0.5f ? -1f : 1f;
                Vector3 hinge = new Vector3(s * W * 0.5f, clearance + bodyH * 0.55f, hoodRearZ);
                Quaternion r = Quaternion.Euler(0f, -s * rng.Range(35f, 75f), 0f);
                Add(hinge + r * new Vector3(0f, 0f, -0.55f), new Vector3(0.07f, bodyH * 0.9f, 1.1f), paint, false, r);
            }

            // Açık kaput (ön camdan menteşeli, yukarı kalkmış)
            if (rng.Chance(openHoodChance))
            {
                float hoodLen = L * 0.5f - hoodRearZ;
                if (hoodLen > 0.6f)
                {
                    Vector3 hinge = new Vector3(0f, bodyTop + 0.03f, hoodRearZ);
                    Quaternion r = Quaternion.Euler(-rng.Range(35f, 70f), 0f, 0f);
                    Add(hinge + r * new Vector3(0f, 0f, hoodLen * 0.5f), new Vector3(W * 0.9f, 0.06f, hoodLen), paint, false, r);
                }
            }

            // Pas lekeleri (gövdenin iki yanında taşan ince şeritler)
            if (!burned && paint != SubRust && rng.Chance(rustPatchChance))
            {
                int patches = rng.Range(1, 4);
                for (int i = 0; i < patches; i++)
                {
                    float h = rng.Range(0.15f, bodyH * 0.6f);
                    float y = clearance + rng.Range(h * 0.5f, bodyH - h * 0.5f);
                    float len = rng.Range(0.3f, 1.1f);
                    float z = rng.Range(-L * 0.5f + len * 0.5f, L * 0.5f - len * 0.5f);
                    Add(new Vector3(0f, y, z), new Vector3(W + 0.025f, h, len), SubRust, false);
                }
            }
        }

        private void Add(Vector3 center, Vector3 size, int submesh, bool solid, Quaternion? rot = null)
        {
            _parts.Add(new Part { center = center, size = size, rot = rot ?? Quaternion.identity, submesh = submesh, solid = solid });
        }

        private static float LowestCorner(Vector3 center, Quaternion rot, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            float min = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) != 0 ? h.x : -h.x, (i & 2) != 0 ? h.y : -h.y, (i & 4) != 0 ? h.z : -h.z);
                min = Mathf.Min(min, (center + rot * c).y);
            }
            return min;
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private float BiomeMultiplier()
        {
            switch (_chunk.biomeType)
            {
                case ChunkBiomeType.Field: return fieldMultiplier;
                case ChunkBiomeType.Coast: return coastMultiplier;
                case ChunkBiomeType.Mountain: return mountainMultiplier;
                default: return forestMultiplier;
            }
        }

        private WreckPose PickPose(SeededRandom rng)
        {
            int[] w = { shoulderWeight, stalledWeight, crosswaysWeight, onSideWeight, flippedWeight, ditchWeight, offRoadWeight };
            return (WreckPose)PickWeighted(rng, w);
        }

        private WreckVehicleType PickType(SeededRandom rng)
        {
            int[] w = { sedanWeight, hatchbackWeight, pickupWeight, vanWeight, boxTruckWeight };
            return (WreckVehicleType)PickWeighted(rng, w);
        }

        /// <summary>Only vehicles short enough to lie across one lane.</summary>
        private WreckVehicleType PickCarType(SeededRandom rng)
        {
            int[] w = { sedanWeight, hatchbackWeight, pickupWeight };
            return (WreckVehicleType)PickWeighted(rng, w);
        }

        private static int PickWeighted(SeededRandom rng, int[] weights)
        {
            int total = 0;
            for (int i = 0; i < weights.Length; i++) total += Mathf.Max(0, weights[i]);
            if (total <= 0) return 0;
            int roll = rng.Range(0, total);
            for (int i = 0; i < weights.Length; i++)
            {
                roll -= Mathf.Max(0, weights[i]);
                if (roll < 0) return i;
            }
            return 0;
        }

        private static void Clear(Transform parent)
        {
            if (parent == null) return;
            Transform old = parent.Find(CONTAINER_NAME);
            if (old == null) return;

            if (!Application.isPlaying)
            {
                DestroyImmediate(old.gameObject);
            }
            else
            {
                old.name = CONTAINER_NAME + "_Destroying";
                old.gameObject.SetActive(false);
                Destroy(old.gameObject);
            }
        }
    }
}

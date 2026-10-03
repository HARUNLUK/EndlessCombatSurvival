using UnityEngine;
using EndlessSurvival.Inventory;

namespace EndlessSurvival.Camping
{
    public class PlayerCampPlacer : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Kamp kurma modunu açıp kapatan tuş")]
        public KeyCode placeKey = KeyCode.B;

        [Tooltip("Oyuncunun ne kadar önüne yerleştirileceği")]
        public float placementDistance = 4f;

        [Tooltip("Yerleştirilecek kamp ateşi prefab'i")]
        public GameObject campfirePrefab;

        [Header("State")]
        private bool _isPlacing = false;
        private GameObject _ghostPreview;
        private Material _ghostMat;
        private Camera _mainCam;
        private PlayerInventory _inventory;

        private void Start()
        {
            _mainCam = Camera.main;
            _inventory = GetComponent<PlayerInventory>();
            CreateGhostPreview();
        }

        private void CreateGhostPreview()
        {
            _ghostPreview = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _ghostPreview.name = "Campfire_GhostPreview";
            _ghostPreview.transform.localScale = new Vector3(1.8f, 0.2f, 1.8f);

            var col = _ghostPreview.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _ghostMat = new Material(shader);
            _ghostMat.color = new Color(0.2f, 1.0f, 0.3f, 0.45f);

            var renderer = _ghostPreview.GetComponent<Renderer>();
            renderer.sharedMaterial = _ghostMat;

            _ghostPreview.SetActive(false);
        }

        private void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            var mouse = UnityEngine.InputSystem.Mouse.current;

            bool placePressed = keyboard != null && keyboard.bKey.wasPressedThisFrame;
            if (placePressed)
            {
                // UI açıkken veya araçtayken kamp kurma moduna geçilmez
                if (CampfireUI.IsOpen || !gameObject.activeInHierarchy) return;

                TogglePlacementMode();
            }

            if (_isPlacing)
            {
                UpdatePlacementGhost();

                // Sol Tık veya Enter: Yerleştir
                bool confirmPlace = (mouse != null && mouse.leftButton.wasPressedThisFrame) ||
                                    (keyboard != null && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame));
                if (confirmPlace)
                {
                    PlaceCampfire();
                }

                // Sağ Tık veya ESC: İptal
                bool cancelPlace = (mouse != null && mouse.rightButton.wasPressedThisFrame) ||
                                   (keyboard != null && keyboard.escapeKey.wasPressedThisFrame);
                if (cancelPlace)
                {
                    CancelPlacement();
                }
            }
        }

        private void TogglePlacementMode()
        {
            _isPlacing = !_isPlacing;
            if (_ghostPreview != null) _ghostPreview.SetActive(_isPlacing);
        }

        private void CancelPlacement()
        {
            _isPlacing = false;
            if (_ghostPreview != null) _ghostPreview.SetActive(false);
        }

        private void UpdatePlacementGhost()
        {
            if (_ghostPreview == null) return;

            Vector3 origin = transform.position + Vector3.up * 1.5f;
            Vector3 forward = transform.forward;
            Vector3 targetPos = transform.position + forward * placementDistance;

            // Zemini raycast ile bul
            if (Physics.Raycast(targetPos + Vector3.up * 10f, Vector3.down, out RaycastHit hit, 20f))
            {
                _ghostPreview.transform.position = hit.point + Vector3.up * 0.1f;
                _ghostPreview.transform.up = hit.normal;
            }
            else
            {
                _ghostPreview.transform.position = targetPos;
            }
        }

        private void PlaceCampfire()
        {
            Vector3 spawnPos = _ghostPreview.transform.position;
            Quaternion spawnRot = Quaternion.identity;

            // Prefab kontrolü
            if (campfirePrefab == null)
            {
                campfirePrefab = Resources.Load<GameObject>("PlayerCampfire") ?? 
#if UNITY_EDITOR
                    UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Camping/PlayerCampfire.prefab");
#else
                    null;
#endif
            }

            if (campfirePrefab != null)
            {
                Instantiate(campfirePrefab, spawnPos, spawnRot);
            }
            else
            {
                // Fallback: Dinamik oluştur
                GameObject dynamicFire = CreateFallbackCampfire(spawnPos);
            }

            // Yakacak harca (varsa)
            if (_inventory != null)
            {
                ItemDefinition wood = FindItemNamed("Wood", "Scrap", "Material");
                if (wood != null && _inventory.Storage.GetCount(wood) > 0)
                {
                    _inventory.Storage.Remove(wood, 1);
                }
            }

            Debug.Log("<color=green>[PlayerCampPlacer] Kamp ateşi başarıyla kuruldu!</color>");
            CancelPlacement();
        }

        private ItemDefinition FindItemNamed(params string[] names)
        {
            if (_inventory == null || _inventory.Storage == null) return null;
            foreach (var pair in _inventory.Storage.Items)
            {
                if (pair.Key != null && pair.Value > 0)
                {
                    foreach (var n in names)
                    {
                        if (pair.Key.name.Contains(n) || pair.Key.displayName.Contains(n))
                            return pair.Key;
                    }
                }
            }
            return null;
        }

        private GameObject CreateFallbackCampfire(Vector3 pos)
        {
            GameObject fire = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            fire.name = "Player_Campfire";
            fire.transform.position = pos;
            fire.transform.localScale = new Vector3(1.2f, 0.2f, 1.2f);

            var lightObj = new GameObject("FireLight");
            lightObj.transform.SetParent(fire.transform);
            lightObj.transform.localPosition = new Vector3(0, 1f, 0);
            var l = lightObj.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.55f, 0.15f);
            l.range = 14f;
            l.intensity = 2.5f;

            var campComp = fire.AddComponent<PlayerCampfire>();
            campComp.fireLight = l;
            return fire;
        }
    }
}

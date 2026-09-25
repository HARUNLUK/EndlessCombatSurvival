using UnityEngine;
using UnityEditor;
using UnityEngine.AI;

namespace EndlessCombat.AI.Editor
{
    public class EnemySetupTool : EditorWindow
    {
        [MenuItem("Endless Combat/Setup Selected as Enemy")]
        public static void SetupEnemy()
        {
            GameObject selected = Selection.activeGameObject;
            
            if (selected == null)
            {
                EditorUtility.DisplayDialog("Hata", "Lutfen hiyerarsiden dusman yapmak istediginiz karakteri (ornek: Player kopyasi) secin.", "Tamam");
                return;
            }

            // 1. Tag Ayari
            selected.tag = "Enemy";

            // 2. Bilesenleri Ekle
            if (selected.GetComponent<NavMeshAgent>() == null)
            {
                var agent = selected.AddComponent<NavMeshAgent>();
                agent.speed = 3.5f;
                agent.stoppingDistance = 10f;
            }

            if (selected.GetComponent<EnemyController>() == null)
            {
                selected.AddComponent<EnemyController>();
            }

            // (Opsiyonel) PlayerShooter ve ThirdPersonController varsa sil
            // Cunku bu artik bir dusman, oyuncu degil.
            var playerShooter = selected.GetComponent("PlayerShooter");
            if (playerShooter != null) DestroyImmediate(playerShooter);

            var thirdPersonController = selected.GetComponent("ThirdPersonController");
            if (thirdPersonController != null) DestroyImmediate(thirdPersonController);

            var playerInput = selected.GetComponent("StarterAssetsInputs");
            if (playerInput != null) DestroyImmediate(playerInput);
            
            var playerInputComp = selected.GetComponent("PlayerInput");
            if (playerInputComp != null) DestroyImmediate(playerInputComp);

            var characterController = selected.GetComponent<CharacterController>();
            if (characterController != null) DestroyImmediate(characterController);

            // Kırmızı Materyal Uygulama
            ApplyRedMaterial(selected);

            EditorUtility.DisplayDialog("Basarili", 
                "Dusman setup tamamlandi!\n\nLutfen Unity menulerinden:\nGameObject -> 3D Object -> Ragdoll... secenegini acarak bu karakter icin Ragdoll olusturmayi (kemiklere rigidboy/collider eklemeyi) unutmayin.", "Tamam");
        }

        private static void ApplyRedMaterial(GameObject obj)
        {
            // Basit kirmizi bir materyal bul veya yarat
            string matPath = "Assets/Materials/EnemyRedMat.mat";
            Material redMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            
            if (redMat == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                {
                    AssetDatabase.CreateFolder("Assets", "Materials");
                }
                
                redMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                redMat.color = Color.red;
                AssetDatabase.CreateAsset(redMat, matPath);
                AssetDatabase.SaveAssets();
            }

            // Karakterin uzerindeki SkinnedMeshRenderer'lari bul ve boya
            SkinnedMeshRenderer[] renderers = obj.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var r in renderers)
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    mats[i] = redMat;
                }
                r.sharedMaterials = mats;
            }
        }
    }
}

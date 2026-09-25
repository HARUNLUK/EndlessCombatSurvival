#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EndlessSurvival.Vehicle.Editor
{
    /// <summary>
    /// Creates URP Lit materials for the split jeep model and remaps them on the FBX importer.
    /// Only base color and normal maps are assigned. The packed ORM textures are not converted.
    /// </summary>
    public static class JeepMaterialSetup
    {
        private const string ModelFolder = "Assets/Models/Jeep";
        private const string FbxPath = ModelFolder + "/Jeep_Split.fbx";
        private const string TextureFolder = ModelFolder + "/Textures";
        private const string MaterialFolder = ModelFolder + "/Materials";

        private struct MaterialSource
        {
            public string materialName;
            public string baseColor;
            public string normal;
            public float smoothness;
            public float metallic;
            public bool transparent;
            public bool solidColor;
            public Color color;
        }

        private static readonly Color RubberBlack = new Color(0.035f, 0.035f, 0.035f, 1f);

        private static readonly MaterialSource[] Sources =
        {
            new MaterialSource { materialName = "PAINT.001", baseColor = "PAINT_base_color.jpg", normal = "PAINT_normal.png", smoothness = 0.35f },
            new MaterialSource { materialName = "BODY.001", baseColor = "BODY_base_color.jpg", normal = "BODY_normal.png", smoothness = 0.35f },
            new MaterialSource { materialName = "IRON.001", baseColor = "IRON_base_color.jpg", normal = "IRON_normal.png", smoothness = 0.4f, metallic = 0.6f },
            new MaterialSource { materialName = "METAL.001", baseColor = "METAL_base_color.jpg", normal = "METAL_normal.png", smoothness = 0.5f, metallic = 0.7f },
            new MaterialSource { materialName = "EQUIPMENT.001", baseColor = "EQUIPMENT_base_color.jpg", normal = "EQUIPMENT_normal.png", smoothness = 0.3f },
            new MaterialSource { materialName = "HUD.001", baseColor = "HUD_base_color.jpg", normal = "HUD_normal.png", smoothness = 0.4f },
            new MaterialSource { materialName = "SEAT001.001", baseColor = "SEAT001_base_color.jpg", normal = "SEAT001_normal.png", smoothness = 0.2f },
            new MaterialSource { materialName = "LIGHT.001", baseColor = "LIGHT_base_color.png", normal = "LIGHT_normal.png", smoothness = 0.8f },
            new MaterialSource { materialName = "RIM.001", baseColor = "RIM_base_color.jpg", normal = "RIM_normal.png", smoothness = 0.4f, metallic = 0.5f },
            new MaterialSource { materialName = "TIRE.001", baseColor = null, normal = "TIRE_normal.png", smoothness = 0.1f, solidColor = true, color = RubberBlack },
            new MaterialSource { materialName = "RIM2", baseColor = "RIM-3A-3Abase_color-3A-3Aimage2.jpg", normal = "RIM-3A-3Anormal-3A-3Aimage2.png", smoothness = 0.4f, metallic = 0.5f },
            new MaterialSource { materialName = "TIRE2", baseColor = null, normal = "TIRE-3A-3Anormal-3A-3Aimage2.png", smoothness = 0.1f, solidColor = true, color = RubberBlack },
            new MaterialSource { materialName = "WINDOW.001", baseColor = null, normal = "WINDOW_normal.png", smoothness = 0.95f, transparent = true },
        };

        [MenuItem("Endless Survival/Setup Jeep Materials")]
        public static void SetupMaterials()
        {
            ModelImporter importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError("[JeepMaterialSetup] FBX not found at " + FbxPath);
                return;
            }

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                Debug.LogError("[JeepMaterialSetup] URP Lit shader not found. Is the project using URP?");
                return;
            }

            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder(ModelFolder, "Materials");

            MarkNormalMaps();

            var remaps = new Dictionary<string, Material>();
            foreach (MaterialSource source in Sources)
            {
                string matPath = MaterialFolder + "/" + source.materialName.Replace('.', '_') + ".mat";
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                {
                    mat = new Material(lit);
                    AssetDatabase.CreateAsset(mat, matPath);
                }

                ConfigureMaterial(mat, source);
                EditorUtility.SetDirty(mat);
                remaps[source.materialName] = mat;
            }

            foreach (KeyValuePair<string, Material> pair in remaps)
            {
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), pair.Key), pair.Value);
            }

            AssetDatabase.SaveAssets();
            importer.SaveAndReimport();
            Debug.Log("[JeepMaterialSetup] Created and remapped " + remaps.Count + " materials.");
        }

        private static void ConfigureMaterial(Material mat, MaterialSource source)
        {
            mat.SetFloat("_Smoothness", source.smoothness);
            mat.SetFloat("_Metallic", source.metallic);

            if (source.solidColor)
            {
                mat.SetTexture("_BaseMap", null);
                mat.SetColor("_BaseColor", source.color);
            }
            else if (!string.IsNullOrEmpty(source.baseColor))
            {
                Texture2D baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "/" + source.baseColor);
                mat.SetTexture("_BaseMap", baseTex);
                mat.SetColor("_BaseColor", Color.white);
            }

            if (!string.IsNullOrEmpty(source.normal))
            {
                Texture2D normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "/" + source.normal);
                if (normalTex != null)
                {
                    mat.SetTexture("_BumpMap", normalTex);
                    mat.EnableKeyword("_NORMALMAP");
                }
            }

            if (source.transparent)
            {
                mat.SetColor("_BaseColor", new Color(0.1f, 0.11f, 0.11f, 0.37f));
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            }
        }

        private static void MarkNormalMaps()
        {
            foreach (MaterialSource source in Sources)
            {
                if (string.IsNullOrEmpty(source.normal)) continue;

                string path = TextureFolder + "/" + source.normal;
                TextureImporter texImporter = AssetImporter.GetAtPath(path) as TextureImporter;
                if (texImporter == null || texImporter.textureType == TextureImporterType.NormalMap) continue;

                texImporter.textureType = TextureImporterType.NormalMap;
                texImporter.SaveAndReimport();
            }
        }
    }
}
#endif

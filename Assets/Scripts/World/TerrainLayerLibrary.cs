using System.Collections.Generic;
using UnityEngine;

namespace EndlessSurvival.World
{
    /// <summary>
    /// Plain-color terrain layers (snow, rock...) for runtime painting.
    /// Edit mode paints into the prefab's TerrainData asset, so there the layer (and its texture) must be
    /// assets too: they are created once under Assets/Prefabs/Chunks/TerrainLayers. In play mode / builds
    /// the asset is used if it exists, otherwise a runtime layer is generated.
    /// </summary>
    public static class TerrainLayerLibrary
    {
        private const string LayerFolder = "Assets/Prefabs/Chunks/TerrainLayers";
        private const string TextureFolder = "Assets/Prefabs/Chunks/Textures";

        private static readonly Dictionary<string, TerrainLayer> s_runtimeLayers = new Dictionary<string, TerrainLayer>();

        public static TerrainLayer Get(string layerName, Color color)
        {
#if UNITY_EDITOR
            string layerPath = $"{LayerFolder}/{layerName}.terrainlayer";
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
            if (asset != null) return asset;

            if (!Application.isPlaying)
            {
                if (!UnityEditor.AssetDatabase.IsValidFolder(TextureFolder) || !UnityEditor.AssetDatabase.IsValidFolder(LayerFolder))
                    return null;

                var tex = CreateSolidTexture("Tex_" + layerName, color);
                UnityEditor.AssetDatabase.CreateAsset(tex, $"{TextureFolder}/Tex_{layerName}.asset");
                var layer = new TerrainLayer { name = layerName, diffuseTexture = tex, tileSize = new Vector2(12f, 12f) };
                UnityEditor.AssetDatabase.CreateAsset(layer, layerPath);
                UnityEditor.AssetDatabase.SaveAssets();
                return layer;
            }
#endif
            if (!s_runtimeLayers.TryGetValue(layerName, out TerrainLayer runtime) || runtime == null)
            {
                runtime = new TerrainLayer
                {
                    name = layerName + "_Runtime",
                    diffuseTexture = CreateSolidTexture("Tex_" + layerName + "_Runtime", color),
                    tileSize = new Vector2(12f, 12f)
                };
                s_runtimeLayers[layerName] = runtime;
            }
            return runtime;
        }

        private static Texture2D CreateSolidTexture(string name, Color color)
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = name };
            var pixels = new Color[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }
}

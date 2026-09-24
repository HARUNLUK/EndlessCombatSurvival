using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Water_Volume Renderer Feature stub updated for Unity 6 (URP 17+) compatibility.
/// Legacy RenderTargetHandle and ScriptableRenderPass have been deprecated in Unity 6.
/// </summary>
public class Water_Volume : ScriptableRendererFeature
{
    [System.Serializable]
    public class _Settings
    {
        public Material material = null;
        public RenderPassEvent renderPass = RenderPassEvent.AfterRenderingSkybox;
    }

    public _Settings settings = new _Settings();

    public override void Create()
    {
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
    }
}




using System;

namespace UnityEngine.Rendering.PostProcessing
{
    [Serializable]
    [PostProcess(typeof(GroundTruthAORenderer), PostProcessEvent.BeforeStack, "TUFX/Ground Truth Ambient Occlusion (GTAO)", sortingPriority: 20)]
    public sealed class GroundTruthAO : PostProcessEffectSettings
    {
        [Range(0.25f, 1.0f), Tooltip("Resolution scale of the GTAO pass (1.0 = Full Resolution, 0.5 = Half Resolution, 0.25 = Quarter Resolution).")]
        public FloatParameter resolutionScale = new FloatParameter { value = 1.0f };

        [Range(0.05f, 5f), Tooltip("Radius of the occlusion sampling sphere in view space.")]
        public FloatParameter radius = new FloatParameter { value = 1.0f };

        [Range(0f, 4f), Tooltip("Occlusion darkness intensity.")]
        public FloatParameter intensity = new FloatParameter { value = 1.5f };

        [Range(0.1f, 5f), Tooltip("Object thickness modifier to prevent occlusion through thin surfaces.")]
        public FloatParameter thickness = new FloatParameter { value = 1.2f };

        [Range(0f, 1f), Tooltip("Multi-bounce approximation to simulate indirect bounce light in crevices.")]
        public FloatParameter multiBounce = new FloatParameter { value = 0.5f };

        [Range(0f, 1f), Tooltip("Specular Occlusion intensity for deferred shading PBR surfaces (attenuates fake specular leaks in cavities while preserving mirror highlights).")]
        public FloatParameter specularOcclusion = new FloatParameter { value = 0.5f };

        [Tooltip("Custom ambient occlusion shadow tint color.")]
        public ColorParameter color = new ColorParameter { value = Color.black };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value && intensity.value > 0f && SystemInfo.supportsImageEffects;
        }

        public override void Load(ConfigNode config)
        {
            loadFloatParameter(config, "ResolutionScale", resolutionScale);
            loadFloatParameter(config, "Radius", radius);
            loadFloatParameter(config, "Intensity", intensity);
            loadFloatParameter(config, "Thickness", thickness);
            loadFloatParameter(config, "MultiBounce", multiBounce);
            loadFloatParameter(config, "SpecularOcclusion", specularOcclusion);
            loadColorParameter(config, "Color", color);
        }

        public override void Save(ConfigNode config)
        {
            saveFloatParameter(config, "ResolutionScale", resolutionScale);
            saveFloatParameter(config, "Radius", radius);
            saveFloatParameter(config, "Intensity", intensity);
            saveFloatParameter(config, "Thickness", thickness);
            saveFloatParameter(config, "MultiBounce", multiBounce);
            saveFloatParameter(config, "SpecularOcclusion", specularOcclusion);
            saveColorParameter(config, "Color", color);
        }
    }

    [UnityEngine.Scripting.Preserve]
    internal sealed class GroundTruthAORenderer : PostProcessEffectRenderer<GroundTruthAO>
    {
        public override DepthTextureMode GetCameraFlags()
        {
            return DepthTextureMode.Depth | DepthTextureMode.DepthNormals;
        }

        public override void Render(PostProcessRenderContext context)
        {
            // XeGTAO is designed for near-field geometric crevices on local vessels and structures.
            // Bypass Main Menu, null cameras, and ScaledCamera completely to ensure zero planet interference.
            if (context.camera == null || HighLogic.LoadedScene == GameScenes.MAINMENU ||
                (ScaledCamera.Instance != null && context.camera == ScaledCamera.Instance.cam))
            {
                context.command.BlitFullscreenTriangle(context.source, context.destination);
                return;
            }

            var shader = (TUFX.TexturesUnlimitedFXLoader.INSTANCE != null) ? TUFX.TexturesUnlimitedFXLoader.INSTANCE.getShader("Hidden/TUFX/GroundTruthAO") : null;
            if (shader == null) shader = Shader.Find("Hidden/TUFX/GroundTruthAO");
            if (shader == null)
            {
                context.command.BlitFullscreenTriangle(context.source, context.destination);
                return;
            }

            var sheet = context.propertySheets.Get(shader);

            // Compute exact projection space reconstruction scale parameters for Intel XeGTAO
            Matrix4x4 proj = context.camera.projectionMatrix;
            float p00 = Mathf.Max(0.0001f, proj.m00);
            float p11 = Mathf.Max(0.0001f, proj.m11);
            float p02 = proj.m02;
            float p12 = proj.m12;

            Vector2 ndcToViewMul = new Vector2(2.0f / p00, 2.0f / p11);
            Vector2 ndcToViewAdd = new Vector2(-(1.0f + p02) / p00, -(1.0f + p12) / p11);

            sheet.properties.SetVector("_NDCToViewMul", ndcToViewMul);
            sheet.properties.SetVector("_NDCToViewAdd", ndcToViewAdd);
            sheet.properties.SetFloat("_EffectRadius", settings.radius.value);
            sheet.properties.SetFloat("_Intensity", settings.intensity.value);
            sheet.properties.SetFloat("_Thickness", settings.thickness.value);
            sheet.properties.SetFloat("_MultiBounce", settings.multiBounce.value);
            sheet.properties.SetFloat("_SpecularOcclusion", settings.specularOcclusion.value);
            sheet.properties.SetColor("_AOColor", settings.color.value);

            bool isDeferred = context.camera != null && context.camera.actualRenderingPath == RenderingPath.DeferredShading;
            sheet.properties.SetFloat("_IsDeferred", isDeferred ? 1.0f : 0.0f);
            if (isDeferred)
            {
                sheet.properties.SetMatrix("_WorldToCameraMatrix", context.camera.worldToCameraMatrix);
            }

            float scale = Mathf.Clamp(settings.resolutionScale.value, 0.25f, 1.0f);
            int width = Mathf.Max(1, Mathf.RoundToInt(context.width * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(context.height * scale));
            int rtRawAO = Shader.PropertyToID("_GTAORaw");
            int rtBlurAO = Shader.PropertyToID("_GTAOBlur");

            var cmd = context.command;
            cmd.GetTemporaryRT(rtRawAO, width, height, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);
            cmd.GetTemporaryRT(rtBlurAO, width, height, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);

            // Pass 0: Intel XeGTAO Compute Pass
            cmd.BlitFullscreenTriangle(context.source, rtRawAO, sheet, 0);

            // Pass 1: Intel XeGTAO Bilateral Denoise
            cmd.BlitFullscreenTriangle(rtRawAO, rtBlurAO, sheet, 1);

            // Pass 2: Composite onto scene color
            cmd.SetGlobalTexture("_AOTex", rtBlurAO);
            cmd.BlitFullscreenTriangle(context.source, context.destination, sheet, 2);

            cmd.ReleaseTemporaryRT(rtRawAO);
            cmd.ReleaseTemporaryRT(rtBlurAO);
        }
    }
}

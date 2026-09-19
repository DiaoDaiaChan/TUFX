using System;

namespace UnityEngine.Rendering.PostProcessing
{
    [Serializable]
    [PostProcess(typeof(GroundTruthAORenderer), PostProcessEvent.BeforeStack, "TUFX/Ground Truth Ambient Occlusion (GTAO)")]
    public sealed class GroundTruthAO : PostProcessEffectSettings
    {
        [Range(0.05f, 5f), Tooltip("Radius of the occlusion sampling sphere.")]
        public FloatParameter radius = new FloatParameter { value = 1.0f };

        [Range(0f, 4f), Tooltip("Occlusion darkness intensity.")]
        public FloatParameter intensity = new FloatParameter { value = 1.5f };

        [Range(0.1f, 5f), Tooltip("Object thickness modifier to prevent occlusion through thin surfaces.")]
        public FloatParameter thickness = new FloatParameter { value = 1.2f };

        [Range(0f, 1f), Tooltip("Multi-bounce approximation to simulate indirect bounce light in crevices.")]
        public FloatParameter multiBounce = new FloatParameter { value = 0.5f };

        [Tooltip("Custom ambient occlusion shadow tint color.")]
        public ColorParameter color = new ColorParameter { value = Color.black };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value && intensity.value > 0f && SystemInfo.supportsImageEffects;
        }

        public override void Load(ConfigNode config)
        {
            loadFloatParameter(config, "Radius", radius);
            loadFloatParameter(config, "Intensity", intensity);
            loadFloatParameter(config, "Thickness", thickness);
            loadFloatParameter(config, "MultiBounce", multiBounce);
            loadColorParameter(config, "Color", color);
        }

        public override void Save(ConfigNode config)
        {
            saveFloatParameter(config, "Radius", radius);
            saveFloatParameter(config, "Intensity", intensity);
            saveFloatParameter(config, "Thickness", thickness);
            saveFloatParameter(config, "MultiBounce", multiBounce);
            saveColorParameter(config, "Color", color);
        }
    }

    [UnityEngine.Scripting.Preserve]
    internal sealed class GroundTruthAORenderer : PostProcessEffectRenderer<GroundTruthAO>
    {
        public override DepthTextureMode GetCameraFlags()
        {
            return DepthTextureMode.Depth;
        }

        public override void Render(PostProcessRenderContext context)
        {
            var shader = (TUFX.TexturesUnlimitedFXLoader.INSTANCE != null) ? TUFX.TexturesUnlimitedFXLoader.INSTANCE.getShader("Hidden/TUFX/GroundTruthAO") : null;
            if (shader == null) shader = Shader.Find("Hidden/TUFX/GroundTruthAO");
            if (shader == null) return;

            var sheet = context.propertySheets.Get(shader);
            sheet.properties.SetFloat("_Radius", settings.radius.value);
            sheet.properties.SetFloat("_Intensity", settings.intensity.value);
            sheet.properties.SetFloat("_Thickness", settings.thickness.value);
            sheet.properties.SetFloat("_MultiBounce", settings.multiBounce.value);
            sheet.properties.SetColor("_AOColor", settings.color.value);

            int width = context.width;
            int height = context.height;
            int rtRawAO = Shader.PropertyToID("_GTAORaw");
            int rtBlurAO = Shader.PropertyToID("_GTAOBlur");

            var cmd = context.command;
            cmd.GetTemporaryRT(rtRawAO, width, height, 0, FilterMode.Bilinear, RenderTextureFormat.R8);
            cmd.GetTemporaryRT(rtBlurAO, width, height, 0, FilterMode.Bilinear, RenderTextureFormat.R8);

            // Pass 0: Compute GTAO
            cmd.BlitFullscreenTriangle(context.source, rtRawAO, sheet, 0);

            // Pass 1: Bilateral Blur
            cmd.BlitFullscreenTriangle(rtRawAO, rtBlurAO, sheet, 1);

            // Pass 2: Composite
            cmd.SetGlobalTexture("_AOTex", rtBlurAO);
            cmd.BlitFullscreenTriangle(context.source, context.destination, sheet, 2);

            cmd.ReleaseTemporaryRT(rtRawAO);
            cmd.ReleaseTemporaryRT(rtBlurAO);
        }
    }
}

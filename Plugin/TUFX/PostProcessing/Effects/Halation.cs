using System;
using UnityEngine;

namespace UnityEngine.Rendering.PostProcessing
{
    [Serializable]
    [PostProcess(typeof(HalationRenderer), PostProcessEvent.BeforeStack, "TUFX/Halation", sortingPriority: 70)]
    public sealed class Halation : PostProcessEffectSettings
    {
        [Range(0f, 5f), Tooltip("Halation intensity.")]
        public FloatParameter intensity = new FloatParameter { value = 0.35f };

        [Range(0.1f, 10f), Tooltip("Luminance threshold above which halation triggers.")]
        public FloatParameter threshold = new FloatParameter { value = 2.5f };

        [Range(0.5f, 5f), Tooltip("Halation glow radius / blur spread.")]
        public FloatParameter radius = new FloatParameter { value = 1.8f };

        [Tooltip("Color tint of the halation diffusion (warm red/orange CineStill 800T style).")]
        public ColorParameter colorTint = new ColorParameter { value = new Color(1.0f, 0.36f, 0.12f, 1.0f) };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value && intensity.value > 0f;
        }

        public override void Load(ConfigNode config)
        {
            loadFloatParameter(config, "Intensity", intensity);
            loadFloatParameter(config, "Threshold", threshold);
            loadFloatParameter(config, "Radius", radius);
            loadColorParameter(config, "ColorTint", colorTint);
        }

        public override void Save(ConfigNode config)
        {
            saveFloatParameter(config, "Intensity", intensity);
            saveFloatParameter(config, "Threshold", threshold);
            saveFloatParameter(config, "Radius", radius);
            saveColorParameter(config, "ColorTint", colorTint);
        }
    }

    [UnityEngine.Scripting.Preserve]
    internal sealed class HalationRenderer : PostProcessEffectRenderer<Halation>
    {
        private const int k_MaxPyramidLevels = 4;
        private readonly int[] m_MipsDown = new int[k_MaxPyramidLevels];
        private readonly int[] m_MipsUp = new int[k_MaxPyramidLevels];

        public HalationRenderer()
        {
            for (int k = 0; k < k_MaxPyramidLevels; k++)
            {
                m_MipsDown[k] = Shader.PropertyToID("_HalationMipDown_" + k);
                m_MipsUp[k] = Shader.PropertyToID("_HalationMipUp_" + k);
            }
        }

        public override void Init()
        {
            for (int k = 0; k < k_MaxPyramidLevels; k++)
            {
                m_MipsDown[k] = Shader.PropertyToID("_HalationMipDown_" + k);
                m_MipsUp[k] = Shader.PropertyToID("_HalationMipUp_" + k);
            }
        }

        public override void Render(PostProcessRenderContext context)
        {
            var shader = (TUFX.TexturesUnlimitedFXLoader.INSTANCE != null) ? TUFX.TexturesUnlimitedFXLoader.INSTANCE.getShader("Hidden/TUFX/Halation") : null;
            if (shader == null) shader = Shader.Find("Hidden/TUFX/Halation");
            if (shader == null) return;

            var sheet = context.propertySheets.Get(shader);
            sheet.properties.SetFloat("_Intensity", settings.intensity.value);
            sheet.properties.SetFloat("_Threshold", settings.threshold.value);
            sheet.properties.SetColor("_ColorTint", settings.colorTint.value);

            float r = Mathf.Clamp(settings.radius.value, 0.5f, 5.0f);
            sheet.properties.SetFloat("_SampleScale", r * 0.75f);

            int width = Mathf.Max(1, context.width / 2);
            int height = Mathf.Max(1, context.height / 2);

            int rtExtract = Shader.PropertyToID("_HalationExtract");
            var cmd = context.command;
            cmd.GetTemporaryRT(rtExtract, width, height, 0, FilterMode.Bilinear, context.sourceFormat);

            // Pass 0: Soft-Knee Threshold Extract
            cmd.BlitFullscreenTriangle(context.source, rtExtract, sheet, 0);

            // Allocate Mip Pyramid
            int tw = width;
            int th = height;
            for (int k = 0; k < k_MaxPyramidLevels; k++)
            {
                cmd.GetTemporaryRT(m_MipsDown[k], tw, th, 0, FilterMode.Bilinear, context.sourceFormat);
                cmd.GetTemporaryRT(m_MipsUp[k], tw, th, 0, FilterMode.Bilinear, context.sourceFormat);
                tw = Mathf.Max(1, tw / 2);
                th = Mathf.Max(1, th / 2);
            }

            // Downsample Pyramid (13-Tap Anti-Aliased Box Filter)
            cmd.BlitFullscreenTriangle(rtExtract, m_MipsDown[0], sheet, 1);
            for (int k = 1; k < k_MaxPyramidLevels; k++)
            {
                cmd.BlitFullscreenTriangle(m_MipsDown[k - 1], m_MipsDown[k], sheet, 1);
            }

            // Upsample Pyramid (9-Tap Tent Filter with Additive Accumulation)
            int lastUp = m_MipsDown[k_MaxPyramidLevels - 1];
            for (int k = k_MaxPyramidLevels - 2; k >= 0; k--)
            {
                cmd.SetGlobalTexture("_HalationBaseTex", m_MipsDown[k]);
                cmd.BlitFullscreenTriangle(lastUp, m_MipsUp[k], sheet, 2);
                lastUp = m_MipsUp[k];
            }

            // Pass 3: Composite (Tight Glow = m_MipsUp[0], Wide Halo = m_MipsUp[1])
            cmd.SetGlobalTexture("_HalationTightTex", m_MipsUp[0]);
            cmd.SetGlobalTexture("_HalationWideTex", m_MipsUp[1]);
            cmd.BlitFullscreenTriangle(context.source, context.destination, sheet, 3);

            // Cleanup
            cmd.ReleaseTemporaryRT(rtExtract);
            for (int k = 0; k < k_MaxPyramidLevels; k++)
            {
                cmd.ReleaseTemporaryRT(m_MipsDown[k]);
                cmd.ReleaseTemporaryRT(m_MipsUp[k]);
            }
        }
    }
}

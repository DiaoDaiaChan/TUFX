using System;
using UnityEngine;

namespace UnityEngine.Rendering.PostProcessing
{
    public enum MotionBlurSource
    {
        CameraOnly,
        CameraAndObjects
    }

    [Serializable]
    public sealed class MotionBlurSourceParameter : ParameterOverride<MotionBlurSource> {}

    [Serializable]
    [PostProcess(typeof(CameraMotionBlurRenderer), PostProcessEvent.BeforeStack, "TUFX/Camera Motion Blur", sortingPriority: 25)]
    public sealed class CameraMotionBlurEffect : PostProcessEffectSettings
    {
        [Tooltip("Motion blur calculation source: CameraOnly (analytical reprojection matrix), or CameraAndObjects (Unity Deferred per-object motion vectors buffer).")]
        public MotionBlurSourceParameter mode = new MotionBlurSourceParameter { value = MotionBlurSource.CameraAndObjects };

        [Range(0f, 360f), Tooltip("The rotary shutter opening angle in degrees. 180 = standard cinema, 270 = action sport, 90 = sharp crisp.")]
        public FloatParameter shutterAngle = new FloatParameter { value = 180f };

        [Range(4, 16), Tooltip("Number of velocity blur samples along the motion vector.")]
        public IntParameter sampleCount = new IntParameter { value = 8 };

        [Range(5f, 64f), Tooltip("Maximum motion blur radius in pixels.")]
        public FloatParameter maxBlurPixels = new FloatParameter { value = 32f };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value && shutterAngle.value > 0f && SystemInfo.supportsImageEffects;
        }

        public override void Load(ConfigNode config)
        {
            loadEnumParameter(config, "Mode", mode, typeof(MotionBlurSource));
            loadFloatParameter(config, "ShutterAngle", shutterAngle);
            loadIntParameter(config, "SampleCount", sampleCount);
            loadFloatParameter(config, "MaxBlurPixels", maxBlurPixels);
        }

        public override void Save(ConfigNode config)
        {
            saveEnumParameter(config, "Mode", mode);
            saveFloatParameter(config, "ShutterAngle", shutterAngle);
            saveIntParameter(config, "SampleCount", sampleCount);
            saveFloatParameter(config, "MaxBlurPixels", maxBlurPixels);
        }
    }

    [UnityEngine.Scripting.Preserve]
    internal sealed class CameraMotionBlurRenderer : PostProcessEffectRenderer<CameraMotionBlurEffect>
    {
        private Matrix4x4 m_PrevViewProj;
        private bool m_FirstFrame = true;

        public override DepthTextureMode GetCameraFlags()
        {
            if (settings.mode.value == MotionBlurSource.CameraAndObjects)
            {
                return DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
            }
            return DepthTextureMode.Depth;
        }

        public override void Render(PostProcessRenderContext context)
        {
            var shader = (TUFX.TexturesUnlimitedFXLoader.INSTANCE != null) ? TUFX.TexturesUnlimitedFXLoader.INSTANCE.getShader("Hidden/TUFX/CameraMotionBlur") : null;
            if (shader == null) shader = Shader.Find("Hidden/TUFX/CameraMotionBlur");
            if (shader == null) return;

            Matrix4x4 proj = GL.GetGPUProjectionMatrix(context.camera.projectionMatrix, false);
            Matrix4x4 view = context.camera.worldToCameraMatrix;
            Matrix4x4 currViewProj = proj * view;
            Matrix4x4 currInvViewProj = currViewProj.inverse;

            if (m_FirstFrame || m_ResetHistory)
            {
                m_PrevViewProj = currViewProj;
                m_FirstFrame = false;
                m_ResetHistory = false;
            }

            float shutterScale = (settings.shutterAngle.value / 360.0f);
            bool useMotionVectors = settings.mode.value == MotionBlurSource.CameraAndObjects && SystemInfo.supportsMotionVectors;

            var sheet = context.propertySheets.Get(shader);
            sheet.properties.SetMatrix("_CurrInvViewProj", currInvViewProj);
            sheet.properties.SetMatrix("_PrevViewProj", m_PrevViewProj);
            sheet.properties.SetFloat("_ShutterScale", shutterScale);
            sheet.properties.SetFloat("_MaxBlurRadius", settings.maxBlurPixels.value);
            sheet.properties.SetInt("_SampleCount", settings.sampleCount.value);
            sheet.properties.SetFloat("_UseMotionVectors", useMotionVectors ? 1.0f : 0.0f);

            context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, 0);

            m_PrevViewProj = currViewProj;
        }
    }
}

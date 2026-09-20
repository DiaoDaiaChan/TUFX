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

        [Tooltip("Preserve tracked spacecraft sharpness during high-speed flight, while blurring rushing background terrain and camera spin.")]
        public BoolParameter isolateVessel = new BoolParameter { value = true };

        [Range(0f, 360f), Tooltip("The rotary shutter opening angle in degrees. 180 = standard cinema, 270 = action sport, 90 = sharp crisp.")]
        public FloatParameter shutterAngle = new FloatParameter { value = 180f };

        [Range(0.2f, 5.0f), Tooltip("Motion blur intensity multiplier for cinematic speed streaks.")]
        public FloatParameter blurMultiplier = new FloatParameter { value = 1.0f };

        [Range(4, 24), Tooltip("Number of velocity blur samples along the motion vector.")]
        public IntParameter sampleCount = new IntParameter { value = 12 };

        [Range(8f, 128f), Tooltip("Maximum motion blur radius in pixels.")]
        public FloatParameter maxBlurPixels = new FloatParameter { value = 48f };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value && shutterAngle.value > 0f && SystemInfo.supportsImageEffects;
        }

        public override void Load(ConfigNode config)
        {
            loadEnumParameter(config, "Mode", mode, typeof(MotionBlurSource));
            loadBoolParameter(config, "IsolateVessel", isolateVessel);
            loadFloatParameter(config, "ShutterAngle", shutterAngle);
            loadFloatParameter(config, "BlurMultiplier", blurMultiplier);
            loadIntParameter(config, "SampleCount", sampleCount);
            loadFloatParameter(config, "MaxBlurPixels", maxBlurPixels);
        }

        public override void Save(ConfigNode config)
        {
            saveEnumParameter(config, "Mode", mode);
            saveBoolParameter(config, "IsolateVessel", isolateVessel);
            saveFloatParameter(config, "ShutterAngle", shutterAngle);
            saveFloatParameter(config, "BlurMultiplier", blurMultiplier);
            saveIntParameter(config, "SampleCount", sampleCount);
            saveFloatParameter(config, "MaxBlurPixels", maxBlurPixels);
        }
    }

    [UnityEngine.Scripting.Preserve]
    internal sealed class CameraMotionBlurRenderer : PostProcessEffectRenderer<CameraMotionBlurEffect>
    {
        private Vector3 m_PrevCamPos;
        private Quaternion m_PrevCamRot;
        private bool m_FirstFrame = true;

        public override DepthTextureMode GetCameraFlags()
        {
            return DepthTextureMode.Depth;
        }

        public override void Render(PostProcessRenderContext context)
        {
            // Bypass on Map View, Main Menu, or ScaledCamera
            if (context.camera == null || MapView.MapIsEnabled || HighLogic.LoadedScene == GameScenes.MAINMENU ||
                (ScaledCamera.Instance != null && context.camera == ScaledCamera.Instance.cam))
            {
                context.command.BlitFullscreenTriangle(context.source, context.destination);
                return;
            }

            var shader = (TUFX.TexturesUnlimitedFXLoader.INSTANCE != null) ? TUFX.TexturesUnlimitedFXLoader.INSTANCE.getShader("Hidden/TUFX/CameraMotionBlur") : null;
            if (shader == null) shader = Shader.Find("Hidden/TUFX/CameraMotionBlur");
            if (shader == null)
            {
                context.command.BlitFullscreenTriangle(context.source, context.destination);
                return;
            }

            Vector3 currCamPos = context.camera.transform.position;
            Quaternion currCamRot = context.camera.transform.rotation;
            float dt = Time.deltaTime;

            // Detect FloatingOrigin shifts, scene transitions, pauses, or camera cuts
            float camJump = Vector3.Distance(currCamPos, m_PrevCamPos);
            bool isTeleport = camJump > 150f || dt <= 0.0001f || Time.timeScale <= 0.0001f;

            if (m_FirstFrame || m_ResetHistory || isTeleport)
            {
                m_PrevCamPos = currCamPos;
                m_PrevCamRot = currCamRot;
                m_FirstFrame = false;
                m_ResetHistory = false;
                context.command.BlitFullscreenTriangle(context.source, context.destination);
                return;
            }

            // Camera translation in previous camera view space
            Vector3 camTranslationWorld = currCamPos - m_PrevCamPos;
            Vector3 camTranslationView = Quaternion.Inverse(m_PrevCamRot) * camTranslationWorld;

            // Rotation matrix from current camera space to previous camera space
            Quaternion rotDelta = Quaternion.Inverse(m_PrevCamRot) * currCamRot;
            Matrix4x4 rotMatrix = Matrix4x4.Rotate(rotDelta);

            // Reconstruct view perspective coefficients
            Matrix4x4 proj = context.camera.projectionMatrix;
            float p00 = Mathf.Max(0.0001f, proj.m00);
            float p11 = Mathf.Max(0.0001f, proj.m11);
            float p02 = proj.m02;
            float p12 = proj.m12;

            Vector2 ndcToViewMul = new Vector2(2.0f / p00, 2.0f / p11);
            Vector2 ndcToViewAdd = new Vector2(-(1.0f + p02) / p00, -(1.0f + p12) / p11);

            float shutterScale = (settings.shutterAngle.value / 360.0f);

            // Dynamically calculate active vessel boundary to isolate tracked craft from translational blur
            float vesselDist = 0f;
            float vesselRadius = 25f;
            if (FlightGlobals.ActiveVessel != null && context.camera != null)
            {
                vesselDist = Vector3.Distance(currCamPos, FlightGlobals.ActiveVessel.transform.position);
                vesselRadius = Mathf.Max(15f, FlightGlobals.ActiveVessel.vesselSize.magnitude * 0.65f);
            }
            float vesselMaxDepth = (settings.isolateVessel.value) ? (vesselDist + vesselRadius + 12f) : 0f;

            var sheet = context.propertySheets.Get(shader);
            sheet.properties.SetVector("_NDCToViewMul", ndcToViewMul);
            sheet.properties.SetVector("_NDCToViewAdd", ndcToViewAdd);
            sheet.properties.SetMatrix("_RotMatrix", rotMatrix);
            sheet.properties.SetVector("_CamTranslationView", camTranslationView);
            sheet.properties.SetFloat("_VesselMaxDepth", vesselMaxDepth);
            sheet.properties.SetFloat("_ShutterScale", shutterScale);
            sheet.properties.SetFloat("_BlurMultiplier", settings.blurMultiplier.value);
            sheet.properties.SetFloat("_MaxBlurRadius", settings.maxBlurPixels.value);
            sheet.properties.SetInt("_SampleCount", settings.sampleCount.value);

            context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, 0);

            m_PrevCamPos = currCamPos;
            m_PrevCamRot = currCamRot;
        }
    }
}

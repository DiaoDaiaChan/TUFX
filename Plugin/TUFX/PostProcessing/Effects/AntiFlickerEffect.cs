using System;
using UnityEngine;

namespace UnityEngine.Rendering.PostProcessing
{
    public enum AntiFlickerMotionSource
    {
        Auto = 0,
        Deferred = 1,
        CameraOnly = 2
    }

    public enum AntiFlickerHistoryFilter
    {
        BicubicCatmullRom5Tap = 0,
        Bilinear = 1
    }

    public enum AntiFlickerClippingMode
    {
        KDOP18 = 0,
        VarianceAABB = 1
    }

    [Serializable]
    public sealed class AntiFlickerMotionSourceParameter : ParameterOverride<AntiFlickerMotionSource> { }

    [Serializable]
    public sealed class AntiFlickerHistoryFilterParameter : ParameterOverride<AntiFlickerHistoryFilter> { }

    [Serializable]
    public sealed class AntiFlickerClippingModeParameter : ParameterOverride<AntiFlickerClippingMode> { }

    [Serializable]
    [PostProcess(typeof(AntiFlickerRenderer), PostProcessEvent.BeforeStack, "TUFX/Temporal Anti-Flicker", sortingPriority: 75)]
    public sealed class AntiFlickerEffect : PostProcessEffectSettings
    {
        [Tooltip("Motion vectors source: Auto (detects Deferred per-object vectors and falls back to camera matrix), Deferred (force per-pixel motion vectors), or CameraOnly (analytical camera matrix).")]
        public AntiFlickerMotionSourceParameter motionSource = new AntiFlickerMotionSourceParameter { value = AntiFlickerMotionSource.Auto };

        [Tooltip("History reconstruction filter: BicubicCatmullRom5Tap (5-tap bicubic Catmull-Rom for razor-sharp stability without blur accumulation) or Bilinear.")]
        public AntiFlickerHistoryFilterParameter historyFilter = new AntiFlickerHistoryFilterParameter { value = AntiFlickerHistoryFilter.BicubicCatmullRom5Tap };

        [Tooltip("Temporal clipping algorithm: KDOP18 (18-DOP convex polytope chamfering 12 diagonal half-spaces to eliminate fast-motion ghosting) or VarianceAABB (Brian Karis UE4/UE5 axis-aligned bounding box).")]
        public AntiFlickerClippingModeParameter clippingMode = new AntiFlickerClippingModeParameter { value = AntiFlickerClippingMode.KDOP18 };

        [Range(0.50f, 0.98f), Tooltip("Temporal stability blending weight. Higher values increase temporal smoothing on subpixel details (Default: 0.85).")]
        public FloatParameter stability = new FloatParameter { value = 0.85f };

        [Range(0.8f, 2.5f), Tooltip("Variance clipping box multiplier (k * sigma). Smaller values clip tighter to prevent ghosting; larger values allow more variance (Default: 1.25).")]
        public FloatParameter sharpness = new FloatParameter { value = 1.25f };

        [Tooltip("Anti-firefly inverse luminance weighting. Suppresses intense single-pixel specular glints on metallic antenna tips and solar array frames.")]
        public BoolParameter antiFirefly = new BoolParameter { value = true };

        [Tooltip("Preserve tracked spacecraft sharpness: locks camera tracking to the active vessel to maintain native sharpness on parts.")]
        public BoolParameter isolateVessel = new BoolParameter { value = true };

        [Tooltip("Temporal Super-Resolution: boosts Catmull-Rom subpixel high frequencies and adapts temporal integration to reconstruct 2x-equivalent geometric detail.")]
        public BoolParameter superResolution = new BoolParameter { value = false };

        [Range(0, 3), Tooltip("Diagnostic visualization: 0 = Normal Output, 1 = Show Stabilized Variance Heatmap, 2 = Show Clamped History, 3 = Show Motion Vectors Buffer.")]
        public IntParameter debugMode = new IntParameter { value = 0 };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value && SystemInfo.supportsImageEffects;
        }

        public override void Load(ConfigNode config)
        {
            loadEnumParameter(config, "MotionSource", motionSource, typeof(AntiFlickerMotionSource));
            loadEnumParameter(config, "HistoryFilter", historyFilter, typeof(AntiFlickerHistoryFilter));
            loadEnumParameter(config, "ClippingMode", clippingMode, typeof(AntiFlickerClippingMode));
            loadFloatParameter(config, "Stability", stability);
            loadFloatParameter(config, "Sharpness", sharpness);
            loadBoolParameter(config, "AntiFirefly", antiFirefly);
            loadBoolParameter(config, "IsolateVessel", isolateVessel);
            loadBoolParameter(config, "SuperResolution", superResolution);
            loadIntParameter(config, "DebugMode", debugMode);
        }

        public override void Save(ConfigNode config)
        {
            saveEnumParameter(config, "MotionSource", motionSource);
            saveEnumParameter(config, "HistoryFilter", historyFilter);
            saveEnumParameter(config, "ClippingMode", clippingMode);
            saveFloatParameter(config, "Stability", stability);
            saveFloatParameter(config, "Sharpness", sharpness);
            saveBoolParameter(config, "AntiFirefly", antiFirefly);
            saveBoolParameter(config, "IsolateVessel", isolateVessel);
            saveBoolParameter(config, "SuperResolution", superResolution);
            saveIntParameter(config, "DebugMode", debugMode);
        }
    }

    [UnityEngine.Scripting.Preserve]
    internal sealed class AntiFlickerRenderer : PostProcessEffectRenderer<AntiFlickerEffect>
    {
        private Vector3 m_PrevCamPos;
        private Quaternion m_PrevCamRot;
        private bool m_FirstFrame = true;

        private readonly RenderTexture[] m_HistoryTextures = new RenderTexture[2];
        private int m_HistoryPingPong = 0;
        private readonly RenderTargetIdentifier[] m_Mrt = new RenderTargetIdentifier[2];

        public override DepthTextureMode GetCameraFlags()
        {
            return DepthTextureMode.Depth | DepthTextureMode.MotionVectors;
        }

        public override void Release()
        {
            for (int i = 0; i < 2; i++)
            {
                if (m_HistoryTextures[i] != null)
                {
                    RenderTexture.ReleaseTemporary(m_HistoryTextures[i]);
                    m_HistoryTextures[i] = null;
                }
            }
            m_FirstFrame = true;
            m_ResetHistory = true;
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

            var shader = (TUFX.TexturesUnlimitedFXLoader.INSTANCE != null) ? TUFX.TexturesUnlimitedFXLoader.INSTANCE.getShader("Hidden/TUFX/AntiFlicker") : null;
            if (shader == null) shader = Shader.Find("Hidden/TUFX/AntiFlicker");
            if (shader == null)
            {
                context.command.BlitFullscreenTriangle(context.source, context.destination);
                return;
            }

            Vector3 currCamPos = context.camera.transform.position;
            Quaternion currCamRot = context.camera.transform.rotation;
            float dt = Time.deltaTime;

            // Detect FloatingOrigin shifts, scene transitions, pauses, or camera teleportation
            float camJump = Vector3.Distance(currCamPos, m_PrevCamPos);
            bool isTeleport = camJump > 150f || dt <= 0.0001f || Time.timeScale <= 0.0001f;

            if (m_FirstFrame || isTeleport)
            {
                m_ResetHistory = true;
            }

            int readIndex = m_HistoryPingPong;
            int writeIndex = (m_HistoryPingPong + 1) % 2;
            m_HistoryPingPong = writeIndex;

            RenderTextureFormat hdrFormat = context.sourceFormat;
            for (int i = 0; i < 2; i++)
            {
                var rt = m_HistoryTextures[i];
                if (rt == null || !rt.IsCreated() || rt.width != context.width || rt.height != context.height)
                {
                    if (rt != null)
                    {
                        RenderTexture.ReleaseTemporary(rt);
                    }
                    rt = context.GetScreenSpaceTemporaryRT(0, hdrFormat, RenderTextureReadWrite.Linear);
                    rt.filterMode = FilterMode.Bilinear;
                    rt.wrapMode = TextureWrapMode.Clamp;
                    m_HistoryTextures[i] = rt;
                    m_ResetHistory = true;
                }
            }

            if (m_ResetHistory)
            {
                context.command.BlitFullscreenTriangle(context.source, m_HistoryTextures[readIndex]);
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

            // Dynamically calculate active vessel boundary to isolate tracked craft from translational movement
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
            sheet.properties.SetFloat("_Stability", settings.stability.value);
            sheet.properties.SetFloat("_VarianceSharpness", settings.sharpness.value);
            sheet.properties.SetFloat("_AntiFirefly", settings.antiFirefly.value ? 1.0f : 0.0f);
            sheet.properties.SetFloat("_SuperResolution", settings.superResolution.value ? 1.0f : 0.0f);
            sheet.properties.SetFloat("_ResetHistory", m_ResetHistory ? 1.0f : 0.0f);
            sheet.properties.SetInt("_DebugMode", settings.debugMode.value);
            sheet.properties.SetInt("_MotionVectorSource", (int)settings.motionSource.value);
            sheet.properties.SetInt("_HistoryFilter", (int)settings.historyFilter.value);
            sheet.properties.SetInt("_ClippingMode", (int)settings.clippingMode.value);
            sheet.properties.SetTexture("_PrevColorTex", m_HistoryTextures[readIndex]);

            if (SystemInfo.supportedRenderTargetCount >= 2)
            {
                m_Mrt[0] = context.destination;
                m_Mrt[1] = m_HistoryTextures[writeIndex];
                context.command.BlitFullscreenTriangle(context.source, m_Mrt, context.source, sheet, 0);
            }
            else
            {
                context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, 1);
                context.command.BlitFullscreenTriangle(context.destination, m_HistoryTextures[writeIndex]);
            }

            m_PrevCamPos = currCamPos;
            m_PrevCamRot = currCamRot;
            m_FirstFrame = false;
            m_ResetHistory = false;
        }
    }
}

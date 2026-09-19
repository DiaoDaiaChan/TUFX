using System;
using UnityEngine;

namespace UnityEngine.Rendering.PostProcessing
{
    [Serializable]
    [PostProcess(typeof(SpectralBokehRenderer), PostProcessEvent.BeforeStack, "TUFX/Spectral Bokeh (Chromatic DoF)", sortingPriority: 10)]
    public sealed class SpectralBokeh : PostProcessEffectSettings
    {
        [Tooltip("Enable intelligent auto-focusing on active vessel or center screen target.")]
        public BoolParameter autoFocus = new BoolParameter { value = true };

        [Range(0.5f, 30f), Tooltip("Auto-focus racking speed.")]
        public FloatParameter autoFocusSpeed = new FloatParameter { value = 10f };

        [Range(0.1f, 500f), Tooltip("Distance to the focus plane in meters.")]
        public FloatParameter focusDistance = new FloatParameter { value = 10f };

        [Range(10f, 300f), Tooltip("Camera lens focal length in mm.")]
        public FloatParameter focalLength = new FloatParameter { value = 50f };

        [Range(0f, 2f), Tooltip("Chromatic dispersion strength on out-of-focus bokeh.")]
        public FloatParameter dispersionStrength = new FloatParameter { value = 0.5f };

        [Range(1f, 20f), Tooltip("Max bokeh blur radius.")]
        public FloatParameter maxBokehRadius = new FloatParameter { value = 8.0f };

        [Range(0.5f, 3.0f), Tooltip("Anamorphic squeeze ratio. 1.0 = round bokeh, 2.0 = classic 2x cinema anamorphic vertical oval bokeh.")]
        public FloatParameter anamorphicRatio = new FloatParameter { value = 1.0f };

        public static float LastFocusedDistance = 10f;

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value && maxBokehRadius.value > 0.5f && SystemInfo.supportsImageEffects;
        }

        public override void Load(ConfigNode config)
        {
            loadBoolParameter(config, "AutoFocus", autoFocus);
            loadFloatParameter(config, "AutoFocusSpeed", autoFocusSpeed);
            loadFloatParameter(config, "FocusDistance", focusDistance);
            loadFloatParameter(config, "FocalLength", focalLength);
            loadFloatParameter(config, "DispersionStrength", dispersionStrength);
            loadFloatParameter(config, "MaxBokehRadius", maxBokehRadius);
            loadFloatParameter(config, "AnamorphicRatio", anamorphicRatio);
        }

        public override void Save(ConfigNode config)
        {
            saveBoolParameter(config, "AutoFocus", autoFocus);
            saveFloatParameter(config, "AutoFocusSpeed", autoFocusSpeed);
            saveFloatParameter(config, "FocusDistance", focusDistance);
            saveFloatParameter(config, "FocalLength", focalLength);
            saveFloatParameter(config, "DispersionStrength", dispersionStrength);
            saveFloatParameter(config, "MaxBokehRadius", maxBokehRadius);
            saveFloatParameter(config, "AnamorphicRatio", anamorphicRatio);
        }

        public static float ComputeTargetDistance(Camera cam, float fallback = 10f)
        {
            if (cam == null)
            {
                if (FlightCamera.fetch != null && FlightCamera.fetch.mainCamera != null)
                    cam = FlightCamera.fetch.mainCamera;
                else
                    cam = Camera.main;
            }
            if (cam == null) return fallback;

            // 1. Raycast against vessel colliders and scenery at center screen
            Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int layerMask = ~((1 << 5) | (1 << 24)); // Exclude UI (5) and MapFX (24)
            if (Physics.Raycast(ray, out RaycastHit hit, 10000f, layerMask))
            {
                if (hit.distance > 0.1f)
                    return hit.distance;
            }

            // 2. Active vessel center of mass (in flight scene)
            if (HighLogic.LoadedSceneIsFlight && FlightGlobals.ActiveVessel != null)
            {
                float dist = Vector3.Distance(cam.transform.position, FlightGlobals.ActiveVessel.CoM);
                if (dist > 0.1f)
                    return dist;
            }

            // 3. Flight camera pivot distance
            if (HighLogic.LoadedSceneIsFlight && FlightCamera.fetch != null)
            {
                float dist = FlightCamera.fetch.Distance;
                if (dist > 0.1f)
                    return dist;
            }

            // 4. Editor root part (in VAB/SPH)
            if (HighLogic.LoadedSceneIsEditor && EditorLogic.fetch != null && EditorLogic.RootPart != null)
            {
                float dist = Vector3.Distance(cam.transform.position, EditorLogic.RootPart.transform.position);
                if (dist > 0.1f)
                    return dist;
            }

            return fallback;
        }
    }

    [UnityEngine.Scripting.Preserve]
    internal sealed class SpectralBokehRenderer : PostProcessEffectRenderer<SpectralBokeh>
    {
        private float m_CurrentFocusDistance = 10f;
        private bool m_FirstFrame = true;

        public override DepthTextureMode GetCameraFlags()
        {
            return DepthTextureMode.Depth;
        }

        public override void Render(PostProcessRenderContext context)
        {
            var shader = (TUFX.TexturesUnlimitedFXLoader.INSTANCE != null) ? TUFX.TexturesUnlimitedFXLoader.INSTANCE.getShader("Hidden/TUFX/SpectralBokeh") : null;
            if (shader == null) shader = Shader.Find("Hidden/TUFX/SpectralBokeh");
            if (shader == null) return;

            if (settings.autoFocus.value)
            {
                float targetDistance = SpectralBokeh.ComputeTargetDistance(context.camera, settings.focusDistance.value);
                float speed = Mathf.Max(0.5f, settings.autoFocusSpeed.value);
                float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                if (m_FirstFrame || m_CurrentFocusDistance <= 0.001f)
                {
                    m_CurrentFocusDistance = targetDistance;
                    m_FirstFrame = false;
                }
                else
                {
                    m_CurrentFocusDistance = Mathf.Lerp(m_CurrentFocusDistance, targetDistance, 1.0f - Mathf.Exp(-speed * dt));
                }
            }
            else
            {
                m_CurrentFocusDistance = settings.focusDistance.value;
            }

            SpectralBokeh.LastFocusedDistance = m_CurrentFocusDistance;

            var sheet = context.propertySheets.Get(shader);
            sheet.properties.SetFloat("_FocusDistance", m_CurrentFocusDistance);
            sheet.properties.SetFloat("_FocalLength", settings.focalLength.value);
            sheet.properties.SetFloat("_DispersionStrength", settings.dispersionStrength.value);
            sheet.properties.SetFloat("_MaxBokehRadius", settings.maxBokehRadius.value);
            sheet.properties.SetFloat("_AnamorphicRatio", settings.anamorphicRatio.value);

            context.command.BlitFullscreenTriangle(context.source, context.destination, sheet, 0);
        }
    }
}

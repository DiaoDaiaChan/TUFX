using System;

namespace UnityEngine.Rendering.PostProcessing
{
    [Serializable]
    [PostProcess(typeof(ContactShadowsRenderer), PostProcessEvent.BeforeStack, "TUFX/Screen Space Contact Shadows")]
    public sealed class ContactShadows : PostProcessEffectSettings
    {
        [Range(0.01f, 1f), Tooltip("Max length of contact shadow ray in view space.")]
        public FloatParameter rayLength = new FloatParameter { value = 0.15f };

        [Range(4, 16), Tooltip("Number of raymarching steps.")]
        public IntParameter raySteps = new IntParameter { value = 8 };

        [Range(0f, 1f), Tooltip("Contact shadow darkness.")]
        public FloatParameter intensity = new FloatParameter { value = 0.65f };

        [Range(0.005f, 0.2f), Tooltip("Surface thickness test value.")]
        public FloatParameter thickness = new FloatParameter { value = 0.03f };

        [Range(5f, 200f), Tooltip("Maximum distance in meters at which contact shadows are evaluated.")]
        public FloatParameter maxDistance = new FloatParameter { value = 50.0f };

        [Range(2f, 50f), Tooltip("Distance range over which contact shadows fade out smoothly.")]
        public FloatParameter fadeRange = new FloatParameter { value = 10.0f };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value && intensity.value > 0f && SystemInfo.supportsImageEffects;
        }

        public override void Load(ConfigNode config)
        {
            loadFloatParameter(config, "RayLength", rayLength);
            loadIntParameter(config, "RaySteps", raySteps);
            loadFloatParameter(config, "Intensity", intensity);
            loadFloatParameter(config, "Thickness", thickness);
            loadFloatParameter(config, "MaxDistance", maxDistance);
            loadFloatParameter(config, "FadeRange", fadeRange);
        }

        public override void Save(ConfigNode config)
        {
            saveFloatParameter(config, "RayLength", rayLength);
            saveIntParameter(config, "RaySteps", raySteps);
            saveFloatParameter(config, "Intensity", intensity);
            saveFloatParameter(config, "Thickness", thickness);
            saveFloatParameter(config, "MaxDistance", maxDistance);
            saveFloatParameter(config, "FadeRange", fadeRange);
        }
    }

    [UnityEngine.Scripting.Preserve]
    internal sealed class ContactShadowsRenderer : PostProcessEffectRenderer<ContactShadows>
    {
        public override DepthTextureMode GetCameraFlags()
        {
            return DepthTextureMode.Depth;
        }

        public override void Render(PostProcessRenderContext context)
        {
            // Bypass ScaledSpace camera entirely to prevent depth quantization artifacts on celestial bodies
            if (ScaledCamera.Instance != null && context.camera == ScaledCamera.Instance.cam)
            {
                context.command.BlitFullscreenTriangle(context.source, context.destination);
                return;
            }

            var shader = (TUFX.TexturesUnlimitedFXLoader.INSTANCE != null) ? TUFX.TexturesUnlimitedFXLoader.INSTANCE.getShader("Hidden/TUFX/ContactShadows") : null;
            if (shader == null) shader = Shader.Find("Hidden/TUFX/ContactShadows");
            if (shader == null) return;

            // Determine light direction in view space (Sun / Main Light)
            Vector3 sunDirWorld = Vector3.up;
            if (RenderSettings.sun != null)
            {
                sunDirWorld = -RenderSettings.sun.transform.forward;
            }
            else if (Sun.Instance != null && context.camera != null)
            {
                sunDirWorld = (Sun.Instance.transform.position - context.camera.transform.position).normalized;
            }
            else
            {
                var lights = GameObject.FindObjectsOfType<Light>();
                for (int i = 0; i < lights.Length; i++)
                {
                    if (lights[i].type == LightType.Directional && lights[i].isActiveAndEnabled)
                    {
                        sunDirWorld = -lights[i].transform.forward;
                        break;
                    }
                }
            }

            Vector3 lightDirView = (context.camera != null) ? context.camera.transform.InverseTransformDirection(sunDirWorld) : Vector3.forward;

            var sheet = context.propertySheets.Get(shader);
            sheet.properties.SetVector("_LightDirView", lightDirView);
            sheet.properties.SetFloat("_RayLength", settings.rayLength.value);
            sheet.properties.SetInt("_RaySteps", settings.raySteps.value);
            sheet.properties.SetFloat("_Intensity", settings.intensity.value);
            sheet.properties.SetFloat("_Thickness", settings.thickness.value);
            sheet.properties.SetFloat("_MaxDistance", settings.maxDistance.value);
            sheet.properties.SetFloat("_FadeRange", settings.fadeRange.value);

            int width = context.width;
            int height = context.height;
            int rtShadow = Shader.PropertyToID("_ContactShadowMap");

            var cmd = context.command;
            // Use ARGB32 to prevent single-channel texture swizzle and avoid channel zeroing bugs
            cmd.GetTemporaryRT(rtShadow, width, height, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);

            // Pass 0: Raymarch
            cmd.BlitFullscreenTriangle(context.source, rtShadow, sheet, 0);

            // Pass 1: Composite
            cmd.SetGlobalTexture("_ShadowTex", rtShadow);
            cmd.BlitFullscreenTriangle(context.source, context.destination, sheet, 1);

            cmd.ReleaseTemporaryRT(rtShadow);
        }
    }
}

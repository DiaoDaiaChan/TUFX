using System;

namespace UnityEngine.Rendering.PostProcessing
{
    [Serializable]
    [PostProcess(typeof(ContactShadowsRenderer), PostProcessEvent.BeforeStack, "TUFX/Screen Space Contact Shadows", sortingPriority: 30)]
    public sealed class ContactShadows : PostProcessEffectSettings
    {
        [Range(0.02f, 0.5f), Tooltip("Max length of contact shadow ray in view space.")]
        public FloatParameter rayLength = new FloatParameter { value = 0.12f };

        [Range(4, 16), Tooltip("Number of raymarching steps.")]
        public IntParameter raySteps = new IntParameter { value = 8 };

        [Range(0f, 1f), Tooltip("Contact shadow darkness.")]
        public FloatParameter intensity = new FloatParameter { value = 0.75f };

        [Range(0.01f, 0.2f), Tooltip("Surface thickness test value.")]
        public FloatParameter thickness = new FloatParameter { value = 0.04f };

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
        }

        public override void Save(ConfigNode config)
        {
            saveFloatParameter(config, "RayLength", rayLength);
            saveIntParameter(config, "RaySteps", raySteps);
            saveFloatParameter(config, "Intensity", intensity);
            saveFloatParameter(config, "Thickness", thickness);
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
            // Contact shadows are micro-geometry shadows for local vessels and EVA in 3D flight/editor scenes.
            // Bypass Main Menu and ScaledCamera completely to ensure zero planet interference.
            if (context.camera == null || HighLogic.LoadedScene == GameScenes.MAINMENU ||
                (ScaledCamera.Instance != null && context.camera == ScaledCamera.Instance.cam))
            {
                context.command.BlitFullscreenTriangle(context.source, context.destination);
                return;
            }

            var shader = (TUFX.TexturesUnlimitedFXLoader.INSTANCE != null) ? TUFX.TexturesUnlimitedFXLoader.INSTANCE.getShader("Hidden/TUFX/ContactShadows") : null;
            if (shader == null) shader = Shader.Find("Hidden/TUFX/ContactShadows");
            if (shader == null)
            {
                context.command.BlitFullscreenTriangle(context.source, context.destination);
                return;
            }

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

            Matrix4x4 proj = context.camera.projectionMatrix;
            float p00 = Mathf.Max(0.0001f, proj.m00);
            float p11 = Mathf.Max(0.0001f, proj.m11);
            float p02 = proj.m02;
            float p12 = proj.m12;

            Vector2 ndcToViewMul = new Vector2(2.0f / p00, 2.0f / p11);
            Vector2 ndcToViewAdd = new Vector2(-(1.0f + p02) / p00, -(1.0f + p12) / p11);

            sheet.properties.SetVector("_NDCToViewMul", ndcToViewMul);
            sheet.properties.SetVector("_NDCToViewAdd", ndcToViewAdd);
            sheet.properties.SetMatrix("_CameraProjectionMatrix", proj);
            sheet.properties.SetVector("_LightDirView", lightDirView);
            sheet.properties.SetFloat("_RayLength", settings.rayLength.value);
            sheet.properties.SetInt("_RaySteps", settings.raySteps.value);
            sheet.properties.SetFloat("_Intensity", settings.intensity.value);
            sheet.properties.SetFloat("_Thickness", settings.thickness.value);

            int width = context.width;
            int height = context.height;
            int rtShadow = Shader.PropertyToID("_ContactShadowMap");
            int rtBlur = Shader.PropertyToID("_ContactShadowBlur");

            var cmd = context.command;
            cmd.GetTemporaryRT(rtShadow, width, height, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);
            cmd.GetTemporaryRT(rtBlur, width, height, 0, FilterMode.Bilinear, RenderTextureFormat.ARGB32);

            // Pass 0: Raymarch
            cmd.BlitFullscreenTriangle(context.source, rtShadow, sheet, 0);

            // Pass 1: Edge-preserving Bilateral Denoise
            cmd.BlitFullscreenTriangle(rtShadow, rtBlur, sheet, 1);

            // Pass 2: Composite
            cmd.SetGlobalTexture("_ShadowTex", rtBlur);
            cmd.BlitFullscreenTriangle(context.source, context.destination, sheet, 2);

            cmd.ReleaseTemporaryRT(rtShadow);
            cmd.ReleaseTemporaryRT(rtBlur);
        }
    }
}

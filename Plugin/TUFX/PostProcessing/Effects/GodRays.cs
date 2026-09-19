using System;

namespace UnityEngine.Rendering.PostProcessing
{
    [Serializable]
    [PostProcess(typeof(GodRaysRenderer), PostProcessEvent.BeforeStack, "TUFX/Screen Space God Rays", sortingPriority: 40)]
    public sealed class GodRays : PostProcessEffectSettings
    {
        [Range(0f, 5f), Tooltip("God rays brightness intensity.")]
        public FloatParameter intensity = new FloatParameter { value = 1.0f };

        [Range(0.1f, 2f), Tooltip("Ray sampling density.")]
        public FloatParameter density = new FloatParameter { value = 0.85f };

        [Range(0.8f, 0.99f), Tooltip("Radial falloff decay per step.")]
        public FloatParameter decay = new FloatParameter { value = 0.95f };

        [Range(0.05f, 1f), Tooltip("Sampling weight.")]
        public FloatParameter weight = new FloatParameter { value = 0.4f };

        [Tooltip("Sunlight ray color tint.")]
        public ColorParameter rayColor = new ColorParameter { value = new Color(1.0f, 0.96f, 0.88f, 1.0f) };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value && intensity.value > 0f && SystemInfo.supportsImageEffects;
        }

        public override void Load(ConfigNode config)
        {
            loadFloatParameter(config, "Intensity", intensity);
            loadFloatParameter(config, "Density", density);
            loadFloatParameter(config, "Decay", decay);
            loadFloatParameter(config, "Weight", weight);
            loadColorParameter(config, "RayColor", rayColor);
        }

        public override void Save(ConfigNode config)
        {
            saveFloatParameter(config, "Intensity", intensity);
            saveFloatParameter(config, "Density", density);
            saveFloatParameter(config, "Decay", decay);
            saveFloatParameter(config, "Weight", weight);
            saveColorParameter(config, "RayColor", rayColor);
        }
    }

    [UnityEngine.Scripting.Preserve]
    internal sealed class GodRaysRenderer : PostProcessEffectRenderer<GodRays>
    {
        public override DepthTextureMode GetCameraFlags()
        {
            return DepthTextureMode.Depth;
        }

        public override void Render(PostProcessRenderContext context)
        {
            var shader = (TUFX.TexturesUnlimitedFXLoader.INSTANCE != null) ? TUFX.TexturesUnlimitedFXLoader.INSTANCE.getShader("Hidden/TUFX/GodRays") : null;
            if (shader == null) shader = Shader.Find("Hidden/TUFX/GodRays");
            if (shader == null) return;

            // Bypass on MainMenu or ScaledCamera
            if (HighLogic.LoadedScene == GameScenes.MAINMENU || (ScaledCamera.Instance != null && context.camera == ScaledCamera.Instance.cam))
            {
                context.command.BlitFullscreenTriangle(context.source, context.destination);
                return;
            }

            // Accurate Sun world direction from primary directional light
            Vector3 sunDirWorld = Vector3.forward;
            Light[] lights = Light.GetLights(LightType.Directional, 0);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].isActiveAndEnabled)
                {
                    sunDirWorld = -lights[i].transform.forward;
                    break;
                }
            }

            Vector3 sunPointWorld = (context.camera != null) ? (context.camera.transform.position + sunDirWorld * 10000.0f) : (Vector3.forward * 10000.0f);
            Vector3 vp = (context.camera != null) ? context.camera.WorldToViewportPoint(sunPointWorld) : Vector3.zero;
            float sunVisible = (vp.z > 0f && vp.x >= -0.4f && vp.x <= 1.4f && vp.y >= -0.4f && vp.y <= 1.4f) ? 1.0f : 0.0f;

            // Apparent sun disc radius in viewport height units
            float sunDiscRadiusHeights = 0.06f;
            if (context.camera != null && Planetarium.fetch != null && Planetarium.fetch.Sun != null)
            {
                try
                {
                    CelestialBody star = Planetarium.fetch.Sun;
                    Vector3 toStar = (Sun.Instance != null)
                        ? (Sun.Instance.transform.position - context.camera.transform.position)
                        : (Vector3)(star.position - (Vector3d)context.camera.transform.position);
                    double dist = toStar.magnitude;
                    if (dist > 1000.0)
                    {
                        double angularRadius = star.Radius / dist;
                        float p00 = Mathf.Max(0.0001f, context.camera.projectionMatrix.m00);
                        float sunDiscRadiusPixels = (float)(angularRadius * p00 * (context.width * 0.5));
                        sunDiscRadiusHeights = Mathf.Clamp(sunDiscRadiusPixels / Mathf.Max(1f, context.height), 0.02f, 0.25f);
                    }
                }
                catch { }
            }

            var sheet = context.propertySheets.Get(shader);
            sheet.properties.SetVector("_SunScreenPos", new Vector2(vp.x, vp.y));
            sheet.properties.SetFloat("_SunVisible", sunVisible);
            sheet.properties.SetFloat("_SunDiscRadius", sunDiscRadiusHeights);
            sheet.properties.SetFloat("_Density", settings.density.value);
            sheet.properties.SetFloat("_Decay", settings.decay.value);
            sheet.properties.SetFloat("_Weight", settings.weight.value);
            sheet.properties.SetFloat("_Intensity", settings.intensity.value);
            sheet.properties.SetColor("_RayColor", settings.rayColor.value);

            int width = context.width / 2;
            int height = context.height / 2;
            int rtExtract = Shader.PropertyToID("_GodRaysExtract");
            int rtRadial = Shader.PropertyToID("_GodRaysRadial");

            var cmd = context.command;
            cmd.GetTemporaryRT(rtExtract, width, height, 0, FilterMode.Bilinear, context.sourceFormat);
            cmd.GetTemporaryRT(rtRadial, width, height, 0, FilterMode.Bilinear, context.sourceFormat);

            // Pass 0: Extract
            cmd.BlitFullscreenTriangle(context.source, rtExtract, sheet, 0);

            // Pass 1: Radial Blur
            cmd.BlitFullscreenTriangle(rtExtract, rtRadial, sheet, 1);

            // Pass 2: Composite
            cmd.SetGlobalTexture("_RaysTex", rtRadial);
            cmd.BlitFullscreenTriangle(context.source, context.destination, sheet, 2);

            cmd.ReleaseTemporaryRT(rtExtract);
            cmd.ReleaseTemporaryRT(rtRadial);
        }
    }
}

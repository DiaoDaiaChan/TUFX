using System;
using UnityEngine;

namespace UnityEngine.Rendering.PostProcessing
{
    [Serializable]
    [PostProcess(typeof(GodRaysRenderer), PostProcessEvent.BeforeStack, "TUFX/Screen Space God Rays", sortingPriority: 40)]
    public sealed class GodRays : PostProcessEffectSettings
    {
        [Range(0f, 5f), Tooltip("God rays brightness intensity.")]
        public FloatParameter intensity = new FloatParameter { value = 0.85f };

        [Range(0f, 1f), Tooltip("Residual optical lens corona intensity in vacuum/space (0 = off, 0.45 = crisp space corona).")]
        public FloatParameter spaceIntensity = new FloatParameter { value = 0.45f };

        [Range(0.1f, 2f), Tooltip("Luminance threshold to extract sunlight sources.")]
        public FloatParameter threshold = new FloatParameter { value = 0.45f };

        [Range(0.1f, 2f), Tooltip("Ray sampling density.")]
        public FloatParameter density = new FloatParameter { value = 0.85f };

        [Range(0.8f, 0.99f), Tooltip("Radial falloff decay per step.")]
        public FloatParameter decay = new FloatParameter { value = 0.95f };

        [Range(0.05f, 1f), Tooltip("Sampling weight.")]
        public FloatParameter weight = new FloatParameter { value = 0.25f };

        [Tooltip("Sunlight ray color tint.")]
        public ColorParameter rayColor = new ColorParameter { value = new Color(1.0f, 0.95f, 0.85f, 1.0f) };

        public override bool IsEnabledAndSupported(PostProcessRenderContext context)
        {
            return enabled.value && intensity.value > 0f && SystemInfo.supportsImageEffects;
        }

        public override void Load(ConfigNode config)
        {
            loadFloatParameter(config, "Intensity", intensity);
            loadFloatParameter(config, "SpaceIntensity", spaceIntensity);
            loadFloatParameter(config, "Threshold", threshold);
            loadFloatParameter(config, "Density", density);
            loadFloatParameter(config, "Decay", decay);
            loadFloatParameter(config, "Weight", weight);
            loadColorParameter(config, "RayColor", rayColor);
        }

        public override void Save(ConfigNode config)
        {
            saveFloatParameter(config, "Intensity", intensity);
            saveFloatParameter(config, "SpaceIntensity", spaceIntensity);
            saveFloatParameter(config, "Threshold", threshold);
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

            // Accurate Sun world direction: prefer true astronomical Sun body
            Vector3 sunDirWorld = Vector3.forward;
            float celestialVisibility = 1.0f;

            if (Planetarium.fetch != null && Planetarium.fetch.Sun != null && context.camera != null)
            {
                Vector3d camPosD = (Vector3d)context.camera.transform.position;
                Vector3d sunPosD = Planetarium.fetch.Sun.position;
                Vector3d camToSun = sunPosD - camPosD;
                double sunDistD = camToSun.magnitude;
                Vector3d sunDirD = camToSun / sunDistD;
                sunDirWorld = (Vector3)sunDirD;

                // Astronomical ray-sphere occultation check across all celestial bodies (Earth, Kerbin, Mun, etc.)
                if (FlightGlobals.Bodies != null)
                {
                    int bodyCount = FlightGlobals.Bodies.Count;
                    for (int i = 0; i < bodyCount; i++)
                    {
                        CelestialBody body = FlightGlobals.Bodies[i];
                        if (body == null || body == Planetarium.fetch.Sun) continue;

                        Vector3d camToBody = body.position - camPosD;
                        double proj = Vector3d.Dot(camToBody, sunDirD);

                        // Body must be between camera and Sun
                        if (proj > 0.0 && proj < sunDistD)
                        {
                            double perpDistSq = camToBody.sqrMagnitude - proj * proj;
                            if (perpDistSq < 0.0) perpDistSq = 0.0;
                            double perpDist = Math.Sqrt(perpDistSq);
                            double bodyRadius = body.Radius;

                            // Calculate apparent solar disc radius at the body's distance
                            double sunRadius = Planetarium.fetch.Sun.Radius;
                            double apparentSunRadius = proj * (sunRadius / sunDistD);

                            // Full solid planetary eclipse (e.g. night side of Earth/Kerbin or Mun solar eclipse)
                            if (perpDist <= bodyRadius)
                            {
                                celestialVisibility = 0.0f;
                                break;
                            }
                            // Penumbra / sunset partial eclipse fade over the solar disc
                            else if (perpDist < bodyRadius + apparentSunRadius)
                            {
                                float penumbra = (float)((perpDist - bodyRadius) / apparentSunRadius);
                                celestialVisibility = Mathf.Min(celestialVisibility, Mathf.Clamp01(penumbra));
                            }

                            // Atmospheric extinction: if planet has atmosphere, sunlight is absorbed
                            if (body.atmosphere && body.atmosphereDepth > 0.0)
                            {
                                double atmRadius = bodyRadius + body.atmosphereDepth * 0.75;
                                if (perpDist < atmRadius)
                                {
                                    float atmPenetration = (float)((perpDist - bodyRadius) / (body.atmosphereDepth * 0.75));
                                    float atmTransmission = Mathf.Clamp01(atmPenetration * atmPenetration);
                                    celestialVisibility = Mathf.Min(celestialVisibility, atmTransmission);
                                }
                            }
                        }
                    }
                }
            }
            else if (RenderSettings.sun != null)
            {
                sunDirWorld = -RenderSettings.sun.transform.forward;
            }
            else
            {
                Light[] lights = Light.GetLights(LightType.Directional, 0);
                for (int i = 0; i < lights.Length; i++)
                {
                    if (lights[i].isActiveAndEnabled)
                    {
                        sunDirWorld = -lights[i].transform.forward;
                        break;
                    }
                }
            }

            Vector3 sunPointWorld = (context.camera != null) ? (context.camera.transform.position + sunDirWorld * 10000.0f) : (Vector3.forward * 10000.0f);
            Vector3 vp = (context.camera != null) ? context.camera.WorldToViewportPoint(sunPointWorld) : Vector3.zero;

            // Allow sun to be up to 0.75 outside the viewport so rays fan into the screen
            float inView = (vp.z > 0f && vp.x >= -0.75f && vp.x <= 1.75f && vp.y >= -0.75f && vp.y <= 1.75f) ? 1.0f : 0.0f;
            float sunVisible = inView * celestialVisibility;

            // Early exit if Sun is occulted or off-screen to save 100% of God Rays rendering overhead
            if (sunVisible <= 0.001f)
            {
                context.command.BlitFullscreenTriangle(context.source, context.destination);
                return;
            }

            // Auto-adapt intensity based on atmospheric density: dense Tyndall in air, clean optical corona in space
            double atmDensity = (FlightGlobals.ActiveVessel != null) ? FlightGlobals.ActiveVessel.atmDensity : 0.0;
            float minSpaceIntensity = Mathf.Max(0.4f, settings.spaceIntensity.value);
            float spaceFactor = Mathf.Lerp(minSpaceIntensity, 1.0f, Mathf.Clamp01((float)atmDensity * 2.0f));

            var sheet = context.propertySheets.Get(shader);
            sheet.properties.SetVector("_SunScreenPos", new Vector2(vp.x, vp.y));
            sheet.properties.SetFloat("_SunVisible", sunVisible);
            sheet.properties.SetFloat("_Threshold", settings.threshold.value);
            sheet.properties.SetFloat("_Density", settings.density.value);
            sheet.properties.SetFloat("_Decay", settings.decay.value);
            sheet.properties.SetFloat("_Weight", settings.weight.value);
            sheet.properties.SetFloat("_Intensity", settings.intensity.value * spaceFactor);
            sheet.properties.SetColor("_RayColor", settings.rayColor.value);

            // Full resolution up to 1440p (2560x1440) for 1:1 pixel-perfect truss lattice, struts, and antennas;
            // 2x downsampled for 4K+ to maintain peak performance.
            int width = (context.width <= 2560) ? context.width : (context.width / 2);
            int height = (context.height <= 1440) ? context.height : (context.height / 2);
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

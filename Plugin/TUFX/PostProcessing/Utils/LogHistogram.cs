namespace UnityEngine.Rendering.PostProcessing
{
    internal sealed class LogHistogram
    {
        public const int rangeMin = -9; // ev
        public const int rangeMax =  9; // ev

        // Don't forget to update 'ExposureHistogram.hlsl' if you change these values !
        const int k_Bins = 128;

        public ComputeBuffer data { get; private set; }

        public void Generate(PostProcessRenderContext context)
        {
            if (data == null)
                data = new ComputeBuffer(k_Bins, sizeof(uint));

            uint threadX, threadY, threadZ;
            var scaleOffsetRes = GetHistogramScaleOffsetRes(context);
            var compute = context.resources.computeShaders.exposureHistogram;
            var cmd = context.command;
            cmd.BeginSample("LogHistogram");

            // Clear the buffer on every frame as we use it to accumulate luminance values on each frame
            int kernel = compute.FindKernel("KEyeHistogramClear");
            cmd.SetComputeBufferParam(compute, kernel, "_HistogramBuffer", data);
            compute.GetKernelThreadGroupSizes(kernel, out threadX, out threadY, out threadZ);
            cmd.DispatchCompute(compute, kernel, Mathf.CeilToInt(k_Bins / (float)threadX), 1, 1);

            // Metering check: if a non-matrix metering mode is requested, blit through ExposureMetering shader
            RenderTargetIdentifier histogramSource = context.source;
            int tempMeterRT = -1;

            if (context.autoExposure != null && context.autoExposure.meteringMode.value != MeteringMode.Matrix)
            {
                var meterShader = (TUFX.TexturesUnlimitedFXLoader.INSTANCE != null)
                    ? TUFX.TexturesUnlimitedFXLoader.INSTANCE.getShader("Hidden/TUFX/ExposureMetering")
                    : null;
                if (meterShader == null) meterShader = Shader.Find("Hidden/TUFX/ExposureMetering");

                if (meterShader != null)
                {
                    var sheet = context.propertySheets.Get(meterShader);
                    float aspect = (float)context.width / Mathf.Max(1, context.height);
                    sheet.properties.SetVector("_MeteringParams", new Vector4(
                        (float)context.autoExposure.meteringMode.value,
                        context.autoExposure.spaceExposureFloor.value,
                        aspect,
                        0f
                    ));

                    tempMeterRT = Shader.PropertyToID("_ExposureMeteringRT");
                    cmd.GetTemporaryRT(tempMeterRT, context.width, context.height, 0, FilterMode.Bilinear, context.sourceFormat);
                    cmd.BlitFullscreenTriangle(context.source, tempMeterRT, sheet, 0);
                    histogramSource = tempMeterRT;
                }
            }

            // Get a log histogram
            kernel = compute.FindKernel("KEyeHistogram");
            cmd.SetComputeBufferParam(compute, kernel, "_HistogramBuffer", data);
            cmd.SetComputeTextureParam(compute, kernel, "_Source", histogramSource);
            cmd.SetComputeVectorParam(compute, "_ScaleOffsetRes", scaleOffsetRes);

            compute.GetKernelThreadGroupSizes(kernel, out threadX, out threadY, out threadZ);
            cmd.DispatchCompute(compute, kernel,
                Mathf.CeilToInt(scaleOffsetRes.z / 2f / threadX),
                Mathf.CeilToInt(scaleOffsetRes.w / 2f / threadY),
                1
            );

            if (tempMeterRT != -1)
            {
                cmd.ReleaseTemporaryRT(tempMeterRT);
            }

            cmd.EndSample("LogHistogram");
        }

        public Vector4 GetHistogramScaleOffsetRes(PostProcessRenderContext context)
        {
            float diff = rangeMax - rangeMin;
            float scale = 1f / diff;
            float offset = -rangeMin * scale;
            return new Vector4(scale, offset, context.width, context.height);
        }

        public void Release()
        {
            if (data != null)
                data.Release();

            data = null;
        }
    }
}

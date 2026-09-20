using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace TUFX.DirectML.Worker
{
    class Program
    {
        private static InferenceSession? s_CurrentSession = null;
        private static string s_LoadedModelPath = string.Empty;

        static int Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
            {
                PrintUsage();
                return 0;
            }

            if (args.Contains("--daemon"))
            {
                RunDaemon();
                return 0;
            }

            string modelPath = GetArgValue(args, "--model");
            string inputPath = GetArgValue(args, "--input");
            string outputPath = GetArgValue(args, "--output");

            if (string.IsNullOrEmpty(modelPath) || string.IsNullOrEmpty(inputPath) || string.IsNullOrEmpty(outputPath))
            {
                Console.Error.WriteLine("[TUFX Worker Error] Missing required arguments: --model, --input, --output");
                PrintUsage();
                return 1;
            }

            try
            {
                Stopwatch sw = Stopwatch.StartNew();
                EnsureSession(modelPath);
                UpscaleFile(inputPath, outputPath);
                sw.Stop();
                Console.WriteLine($"[TUFX Worker OK] Upscaled '{inputPath}' -> '{outputPath}' in {sw.ElapsedMilliseconds} ms");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[TUFX Worker Error] {ex.Message}");
                return 2;
            }
        }

        private static void RunDaemon()
        {
            Console.WriteLine("[TUFX Worker Daemon Ready]");
            string? line;
            while ((line = Console.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.Trim().Equals("EXIT", StringComparison.OrdinalIgnoreCase)) break;

                // Format: UPSCALE|<modelPath>|<inputPath>|<outputPath>
                string[] parts = line.Split('|');
                if (parts.Length == 4 && parts[0] == "UPSCALE")
                {
                    string model = parts[1];
                    string input = parts[2];
                    string output = parts[3];

                    try
                    {
                        Stopwatch sw = Stopwatch.StartNew();
                        EnsureSession(model);
                        UpscaleFile(input, output);
                        sw.Stop();
                        Console.WriteLine($"OK|{output}|{sw.ElapsedMilliseconds}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"ERROR|{ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("ERROR|Invalid command syntax. Expected: UPSCALE|<model>|<input>|<output>");
                }
            }
        }

        private static void EnsureSession(string modelPath)
        {
            if (s_CurrentSession != null && s_LoadedModelPath == modelPath)
            {
                return;
            }

            s_CurrentSession?.Dispose();
            s_CurrentSession = null;

            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException($"Model file not found: {modelPath}");
            }

            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL
            };

            // Attempt DirectML GPU Execution Provider
            try
            {
                options.AppendExecutionProvider_DML(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[TUFX Worker Warning] DirectML initialization notice: {ex.Message}. Falling back to CPU.");
            }

            s_CurrentSession = new InferenceSession(modelPath, options);
            s_LoadedModelPath = modelPath;
        }

        private static void UpscaleFile(string inputPath, string outputPath)
        {
            if (s_CurrentSession == null) throw new InvalidOperationException("Inference session is null");

            using Image<Rgba32> image = Image.Load<Rgba32>(inputPath);
            int w = image.Width;
            int h = image.Height;
            int totalPixels = w * h;

            // 1. Prepare CHW Float Tensor for RGB
            float[] rgbData = new float[3 * totalPixels];
            byte[] alphaData = new byte[totalPixels];

            int gOffset = totalPixels;
            int bOffset = totalPixels * 2;

            int pixelIndex = 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    Rgba32 pixel = image[x, y];
                    rgbData[pixelIndex] = pixel.R / 255.0f;
                    rgbData[gOffset + pixelIndex] = pixel.G / 255.0f;
                    rgbData[bOffset + pixelIndex] = pixel.B / 255.0f;
                    alphaData[pixelIndex] = pixel.A;
                    pixelIndex++;
                }
            }

            // 2. Run DirectML Model
            string inputName = s_CurrentSession.InputMetadata.Keys.First();
            var inTensor = new DenseTensor<float>(rgbData, new int[] { 1, 3, h, w });
            var inputs = new NamedOnnxValue[] { NamedOnnxValue.CreateFromTensor(inputName, inTensor) };

            int outW, outH;
            float[] outRgbData;

            using (var results = s_CurrentSession.Run(inputs))
            {
                var outTensor = results.First().AsTensor<float>();
                outH = outTensor.Dimensions[2];
                outW = outTensor.Dimensions[3];
                outRgbData = outTensor.ToArray();
            }

            // 3. Upscale Alpha Channel using ImageSharp Triangle (Bilinear)
            using Image<L8> alphaImg = Image.LoadPixelData<L8>(alphaData, w, h);
            alphaImg.Mutate(ctx => ctx.Resize(outW, outH, KnownResamplers.Triangle));

            // 4. Combine RGB + Alpha into Output Image
            using Image<Rgba32> outImage = new Image<Rgba32>(outW, outH);
            int outTotal = outW * outH;
            int outGOffset = outTotal;
            int outBOffset = outTotal * 2;

            int outPixelIndex = 0;
            for (int y = 0; y < outH; y++)
            {
                for (int x = 0; x < outW; x++)
                {
                    float r = Math.Clamp(outRgbData[outPixelIndex] * 255.0f, 0f, 255.0f);
                    float g = Math.Clamp(outRgbData[outGOffset + outPixelIndex] * 255.0f, 0f, 255.0f);
                    float b = Math.Clamp(outRgbData[outBOffset + outPixelIndex] * 255.0f, 0f, 255.0f);
                    byte a = alphaImg[x, y].PackedValue;

                    outImage[x, y] = new Rgba32((byte)r, (byte)g, (byte)b, a);
                    outPixelIndex++;
                }
            }

            string? outDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            outImage.SaveAsPng(outputPath);
        }

        private static string GetArgValue(string[] args, string name)
        {
            int idx = Array.IndexOf(args, name);
            if (idx >= 0 && idx + 1 < args.Length)
            {
                return args[idx + 1];
            }
            return string.Empty;
        }

        private static void PrintUsage()
        {
            Console.WriteLine("TUFX DirectML Super-Resolution Standalone Worker");
            Console.WriteLine("Usage:");
            Console.WriteLine("  TUFX_DirectML_Worker.exe --model <model.onnx> --input <in.png> --output <out.png>");
            Console.WriteLine("  TUFX_DirectML_Worker.exe --daemon");
        }
    }
}

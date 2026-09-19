using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;

public class BuildAdvancedBundle
{
    [MenuItem("TUFX/Build Advanced Shaders Bundle (Win64)")]
    public static void BuildAll()
    {
        BuildTarget target = BuildTarget.StandaloneWindows64;
        string outputDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../GameData/TUFX/Shaders"));
        if (!Directory.Exists(outputDir))
        {
            Directory.CreateDirectory(outputDir);
        }

        string[] shaderGuids = AssetDatabase.FindAssets("t:Shader", new string[] { "Assets/Shaders/TUFX" });
        List<string> assetPaths = new List<string>();
        foreach (string guid in shaderGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            assetPaths.Add(path);
            Debug.Log("[TUFX] Found Shader for bundle: " + path);
        }

        AssetBundleBuild build = new AssetBundleBuild
        {
            assetBundleName = "tufx-advanced.ssf",
            assetNames = assetPaths.ToArray()
        };

        Debug.Log("[TUFX] Building AssetBundle to: " + outputDir);
        BuildPipeline.BuildAssetBundles(outputDir, new AssetBundleBuild[] { build }, BuildAssetBundleOptions.ForceRebuildAssetBundle, target);
        Debug.Log("[TUFX] AssetBundle build complete!");
    }
}

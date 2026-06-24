using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Headless build entry points, invoked from the wrapper scripts via:
//   Unity.exe -batchmode -quit -executeMethod BuildScript.BuildWindows
//   Unity.exe -batchmode -quit -executeMethod BuildScript.BuildWebGL
//
// Both build from the scenes currently enabled in File > Build Settings.
// Windows -> Builds/Yum Jump.exe (zipped for download by package-demo.sh).
// WebGL   -> Builds-WebGL/        (served unzipped for in-browser play by
//                                  build-webgl.sh).
// Exits non-zero on failure so the wrapper script can detect a broken build.
public static class BuildScript
{
    private const string OutputExe = "Builds/Yum Jump.exe";

    // WebGL output is a *folder* of static files (index.html, Build/,
    // TemplateData/), served unzipped by the web host so the browser can
    // load the player. scripts/build-webgl.sh copies it into the site's
    // public/game/ directory.
    private const string OutputWebGL = "Builds-WebGL";

    public static void BuildWindows()
    {
        Build(OutputExe, BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone);
    }

    public static void BuildWebGL()
    {
        Build(OutputWebGL, BuildTarget.WebGL, BuildTargetGroup.WebGL);
    }

    private static void Build(string locationPathName, BuildTarget target, BuildTargetGroup targetGroup)
    {
        string[] scenes = EditorBuildSettings.scenes
            .Where(s => s.enabled)
            .Select(s => s.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            Console.Error.WriteLine("[BuildScript] No enabled scenes in Build Settings. Aborting.");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log($"[BuildScript] Building {target} ({scenes.Length} scene(s)): {string.Join(", ", scenes)}");

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = locationPathName,
            target = target,
            targetGroup = targetGroup,
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[BuildScript] Build succeeded: {summary.totalSize} bytes in {summary.totalTime}.");
            EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError($"[BuildScript] Build {summary.result} with {summary.totalErrors} error(s).");
            EditorApplication.Exit(1);
        }
    }
}

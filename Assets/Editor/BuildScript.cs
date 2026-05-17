using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Headless build entry point, invoked from scripts/build-game.sh via:
//   Unity.exe -batchmode -quit -executeMethod BuildScript.BuildWindows
//
// Builds the StandaloneWindows64 player from the scenes currently enabled
// in File > Build Settings, into Builds/Yum Jump.exe. Exits non-zero on
// failure so the wrapper script can detect a broken build.
public static class BuildScript
{
    private const string OutputExe = "Builds/Yum Jump.exe";

    public static void BuildWindows()
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

        Debug.Log($"[BuildScript] Building {scenes.Length} scene(s): {string.Join(", ", scenes)}");

        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = OutputExe,
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
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

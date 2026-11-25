using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DeckArpg.Editor
{
    /// <summary>
    /// Release (non-development) Windows build of the run scene. The debug menu is compiled out because it is guarded by
    /// <c>UNITY_EDITOR || DEVELOPMENT_BUILD</c>. Batchmode:
    /// <c>Unity.exe -batchmode -nographics -quit -projectPath . -executeMethod DeckArpg.Editor.ReleaseBuilder.BuildWindows</c>
    /// exits non-zero if the build fails.
    /// </summary>
    public static class ReleaseBuilder
    {
        public const string OutputDir = "Builds/Windows";
        public const string ExeName = "DeckBuilderARPG.exe";
        private const string RunScene = "Assets/_Project/Scenes/M3_Run.unity";

        [MenuItem("DeckArpg/Build Windows")]
        public static void BuildWindows()
        {
            if (Directory.Exists(OutputDir)) Directory.Delete(OutputDir, true);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { RunScene },
                locationPathName = Path.Combine(OutputDir, ExeName),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"Windows build {summary.result}: {summary.outputPath}, {summary.totalSize / (1024 * 1024)} MB, " +
                      $"{summary.totalErrors} errors, {summary.totalTime}");
            if (summary.result != BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}

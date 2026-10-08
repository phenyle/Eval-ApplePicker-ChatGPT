using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Forge.Bench.Editor
{
    /// <summary>
    /// Batch-mode player builds (plan §2.1, §2.7). One executable per game; architecture is a runtime choice.
    ///   Unity -batchmode -quit -projectPath P -executeMethod Forge.Bench.Editor.BenchBuild.Build
    ///         -buildOut &lt;dir&gt; -buildName AP [-dev]
    /// Exits with code 1 on failure so the calling script can detect it.
    /// </summary>
    public static class BenchBuild
    {
        public static void Build()
        {
            string outDir = Arg("-buildOut"), name = Arg("-buildName");
            bool dev = Array.IndexOf(Environment.GetCommandLineArgs(), "-dev") >= 0;
            if (outDir == null || name == null) { Debug.LogError("need -buildOut and -buildName"); EditorApplication.Exit(1); return; }
            string exeName = name + (dev ? "_DEV" : "");
            Directory.CreateDirectory(Path.Combine(outDir, exeName));
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Bench.unity" },
                locationPathName = Path.Combine(outDir, exeName, exeName + ".exe"),
                target = BuildTarget.StandaloneWindows64,
                options = dev ? BuildOptions.Development : BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Debug.Log($"[bench-build] {s.result} {s.outputPath} size={s.totalSize} time={s.totalTime} errors={s.totalErrors}");
            EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }

        static string Arg(string key)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == key) return a[i + 1];
            return null;
        }
    }
}

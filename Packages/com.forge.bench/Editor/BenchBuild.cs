using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Forge.Bench.Editor
{
    /// <summary>
    /// Batch-mode player builds (plan §2.1, §2.7). One executable per game; architecture is a runtime choice.
    ///   Unity -batchmode -quit -projectPath P -executeMethod Forge.Bench.Editor.BenchBuild.Build
    ///         -buildOut &lt;builds root&gt; -buildName AP [-dev]
    /// Output: &lt;builds root&gt;/AP[_DEV]/AP[_DEV].exe on Windows, &lt;builds root&gt;/AP[_DEV]/AP[_DEV].app on macOS
    /// (the target follows the editor's platform: IL2CPP players for macOS can only be built on a Mac).
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
            bool mac = Application.platform == RuntimePlatform.OSXEditor;
            if (mac) ConfigureMac();
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Bench.unity" },
                locationPathName = Path.Combine(outDir, exeName, exeName + (mac ? ".app" : ".exe")),
                target = mac ? BuildTarget.StandaloneOSX : BuildTarget.StandaloneWindows64,
                options = dev ? BuildOptions.Development : BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            Debug.Log($"[bench-build] {s.result} {s.outputPath} size={s.totalSize} time={s.totalTime} errors={s.totalErrors}");
            EditorApplication.Exit(s.result == BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>
        /// macOS player settings (2026-10-09 move to Apple silicon): Metal only, Apple-silicon (arm64) binary, and no
        /// Retina backing store, so a 1280x720 window renders 1280x720 pixels as on Windows. The Standalone settings
        /// shared with Windows (IL2CPP Release, stripping, frame timing stats, vsync off) come from BenchProjectSetup.
        /// </summary>
        public static void ConfigureMac()
        {
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneOSX, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneOSX, new[] { GraphicsDeviceType.Metal });
            PlayerSettings.macRetinaSupport = false;
            // UnityEditor.OSXStandalone.UserBuildSettings lives in the macOS build-support module; set it by reflection so
            // this file also compiles in editors without that module (the Windows laptop).
            var t = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("UnityEditor.OSXStandalone.UserBuildSettings", false)).FirstOrDefault(x => x != null);
            var prop = t?.GetProperty("architecture");
            if (prop != null)
            {
                prop.SetValue(null, Enum.Parse(prop.PropertyType, "ARM64"));
                Debug.Log($"[bench-build] macOS architecture = {prop.GetValue(null)}");
            }
            else Debug.LogError("[bench-build] could not set macOS architecture (UserBuildSettings.architecture not found)");
            AssetDatabase.SaveAssets();
        }

        static string Arg(string key)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == key) return a[i + 1];
            return null;
        }
    }
}

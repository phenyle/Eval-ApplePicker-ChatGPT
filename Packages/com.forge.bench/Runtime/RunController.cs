using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Forge.Bench
{
    public enum RunPhase : byte { Starting = 0, Ramp = 1, Warmup = 2, Measure = 3, Done = 4 }

    /// <summary>
    /// Drives a benchmark run (plan §2.4): Starting → (game calls MarkReady) → Ramp → Warmup → Measure → Done,
    /// using wall-clock durations from the config; then flushes all logs, writes summary_&lt;role&gt;.json, and
    /// quits with exit code 0 (or 2 on Fail()). Also applies global player settings at boot.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public class RunController : MonoBehaviour
    {
        public static RunPhase CurrentPhase { get; private set; } = RunPhase.Starting;
        public static bool Recording => CurrentPhase >= RunPhase.Ramp && CurrentPhase < RunPhase.Done;
        public static RunController Instance { get; private set; }
        public static event Action<string> OnFlush;   // components write their own logs into out_dir
        public static readonly Dictionary<string, object> Extra = new();

        static readonly Stopwatch Clock = new();
        static string _failReason;
        BenchConfig _cfg;
        double _tReady;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            CurrentPhase = RunPhase.Starting; Instance = null; _failReason = null; OnFlush = null; Extra.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            var cfg = BenchConfig.Load();
            if (cfg.job_workers > 0)
                Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount =
                    Math.Min(cfg.job_workers, Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerMaximumCount);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            Application.runInBackground = true;
            ServicePointManager.DefaultConnectionLimit = 4096;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;
        }

        /// <summary>Writes 1 when the job body runs Burst-compiled; the [BurstDiscard] call (removed by Burst) writes 0
        /// when it runs as managed code. Guards against a stale or failed Burst AOT library in the player.</summary>
        [Unity.Burst.BurstCompile]
        struct BurstProbe : Unity.Jobs.IJob
        {
            public Unity.Collections.NativeArray<int> Out;
            public void Execute() { Out[0] = 1; Managed(ref Out); }
            [Unity.Burst.BurstDiscard] static void Managed(ref Unity.Collections.NativeArray<int> o) => o[0] = 0;
        }

        static bool ProbeBurst()
        {
            var a = new Unity.Collections.NativeArray<int>(1, Unity.Collections.Allocator.TempJob);
            Unity.Jobs.IJobExtensions.Schedule(new BurstProbe { Out = a }).Complete();
            bool on = a[0] == 1;
            a.Dispose();
            return on;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            Extra["burst_active"] = ProbeBurst() ? "true" : "false";
            var go = new GameObject("BenchRun");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<RunController>();
        }

        void Awake()
        {
            _cfg = BenchConfig.Load();
            PlayerLoopTimer.Install();
            FrameRecorder.Init();
            Clock.Start();
        }

        /// <summary>Called by the game once the scene is built (and, for networked roles, connected).</summary>
        public static void MarkReady()
        {
            if (CurrentPhase != RunPhase.Starting) return;
            Instance._tReady = Clock.Elapsed.TotalSeconds;
            CurrentPhase = RunPhase.Ramp;
        }

        public static void Fail(string reason)
        {
            _failReason = reason;
            Debug.LogError("[bench] FAIL: " + reason);
            if (Instance != null) Instance.Finish();
        }

        void Update()
        {
            double t = Clock.Elapsed.TotalSeconds;
            if (CurrentPhase == RunPhase.Starting)
            {
                if (t > 90) Fail("not ready after 90 s");
                return;
            }
            double s = t - _tReady;
            if (CurrentPhase == RunPhase.Ramp && s >= _cfg.ramp_s) CurrentPhase = RunPhase.Warmup;
            if (CurrentPhase == RunPhase.Warmup && s >= _cfg.ramp_s + _cfg.warmup_s) CurrentPhase = RunPhase.Measure;
            if (CurrentPhase == RunPhase.Measure && _cfg.screenshot_s > 0 && !_shot && s >= _cfg.ramp_s + _cfg.warmup_s + _cfg.screenshot_s)
            {
                _shot = true;
                ScreenCapture.CaptureScreenshot(Path.Combine(_cfg.out_dir, $"screenshot_{_cfg.role}.png"));
            }
            if (CurrentPhase == RunPhase.Measure && s >= _cfg.ramp_s + _cfg.warmup_s + _cfg.window_s) Finish();
        }

        bool _finished, _shot;

        void Finish()
        {
            if (_finished) return;
            _finished = true;
            CurrentPhase = RunPhase.Done;
            int code = _failReason == null ? 0 : 2;
            try
            {
                string role = _cfg.role;
                FrameRecorder.WriteCsv(Path.Combine(_cfg.out_dir, $"frames_{role}.csv"));
                OnFlush?.Invoke(_cfg.out_dir);
                WriteSummary(Path.Combine(_cfg.out_dir, $"summary_{role}.json"));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                code = 3;
            }
            FrameRecorder.Dispose();
            Application.Quit(code);
        }

        void WriteSummary(string path)
        {
            var (mean, mn, mx) = FrameRecorder.LiveStats();
            var j = new System.Text.StringBuilder();
            void Kv(string k, string v, bool last = false) => j.Append($"  \"{k}\": {v}{(last ? "" : ",")}\n");
            string S(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            j.Append("{\n");
            Kv("run_id", S(_cfg.run_id));
            Kv("role", S(_cfg.role));
            Kv("fail_reason", _failReason == null ? "null" : S(_failReason));
            Kv("frames_recorded", FrameRecorder.Count.ToString());
            Kv("ring_overflow", FrameRecorder.Overflow ? "true" : "false");
            Kv("ftm_supported", FrameRecorder.FtmSupported ? "true" : "false");
            Kv("gc_recorder_valid", FrameRecorder.GcRecorderValid ? "true" : "false");
            Kv("live_mean", mean.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            Kv("live_min", mn.ToString());
            Kv("live_max", mx.ToString());
            Kv("requests_sent_live", Fingerprint.SentLive.ToString());
            Kv("qpc_frequency", Stopwatch.Frequency.ToString());
            long qpc = Stopwatch.GetTimestamp();
            double unix = (DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;
            Kv("clock_anchor", $"{{\"qpc\": {qpc}, \"t_unix\": {unix.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}}}");
            Kv("job_worker_count", Unity.Jobs.LowLevel.Unsafe.JobsUtility.JobWorkerCount.ToString());
            foreach (var kv in Extra) Kv(kv.Key, kv.Value is string sv ? S(sv) : Convert.ToString(kv.Value, System.Globalization.CultureInfo.InvariantCulture));
            j.Append("  \"system_info\": {\n");
            j.Append($"    \"graphicsDeviceName\": {S(SystemInfo.graphicsDeviceName)},\n");
            j.Append($"    \"graphicsDeviceType\": {S(SystemInfo.graphicsDeviceType.ToString())},\n");
            j.Append($"    \"graphicsDeviceVersion\": {S(SystemInfo.graphicsDeviceVersion)},\n");
            j.Append($"    \"graphicsMultiThreaded\": {(SystemInfo.graphicsMultiThreaded ? "true" : "false")},\n");
            j.Append($"    \"renderingThreadingMode\": {S(SystemInfo.renderingThreadingMode.ToString())},\n");
            j.Append($"    \"processorType\": {S(SystemInfo.processorType)},\n");
            j.Append($"    \"processorCount\": {SystemInfo.processorCount},\n");
            j.Append($"    \"systemMemorySize\": {SystemInfo.systemMemorySize},\n");
            j.Append($"    \"operatingSystem\": {S(SystemInfo.operatingSystem)},\n");
            j.Append($"    \"unityVersion\": {S(Application.unityVersion)},\n");
            j.Append($"    \"screen\": {S(Screen.width + "x" + Screen.height)},\n");
            j.Append($"    \"vSyncCount\": {QualitySettings.vSyncCount},\n");
            j.Append($"    \"qualityLevel\": {S(QualitySettings.names[QualitySettings.GetQualityLevel()])},\n");
            j.Append($"    \"renderPipeline\": {S(QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline.name : (UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline != null ? UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline.name : "builtin"))}\n");
            j.Append("  }\n}\n");
            File.WriteAllText(path, j.ToString());
        }
    }
}

using System;
using System.IO;
using UnityEngine;

namespace Forge.Bench
{
    /// <summary>
    /// Per-run configuration written by analysis/harness/run_matrix.py (config_&lt;role&gt;.json) and passed with
    /// "-bench-config &lt;path&gt;". Field names match the JSON keys (see SCHEMA.md / run.json).
    /// </summary>
    [Serializable]
    public class BenchConfig
    {
        public int schema_version = 1;
        public string run_id = "editor";
        public string experiment = "editor";
        public string cell_id = "editor";
        public int rep;
        public long seed = 20271000;
        public string role = "player";
        public string game = "AP";
        public string arch = "OOP";
        public string net = "offline";
        public int load = 256;
        public string genai = "off";
        public string policy = "per_spawn";
        public float lambda;
        public int window_k = 256;
        public float ramp_s = 2f;
        public float warmup_s = 2f;
        public float window_s = 5f;
        public string out_dir = "";
        public int job_workers = -1;
        public string stub_url = "http://127.0.0.1:8787";
        public string live_url = "https://api-main.aiml.asu.edu";   // institutional gateway (PROGRESS 2026-10-08)
        public string model = "";
        public int max_output_tokens = 16;
        public float temperature = 0.8f;
        public string pregen_file = "";
        public int net_port = 7777;
        public int connect_port = 7778;
        public int sample_count = 64;
        public int[] affinity = Array.Empty<int>();
        public string recorder = "full";  // "minimal": frame time only, for the logger-overhead check (plan §2.9)
        public float screenshot_s;      // >0: capture screenshot_<role>.png this many seconds into the window (§2.8 visual parity)

        public static BenchConfig Current { get; private set; }
        public static bool FromCommandLine { get; private set; }

        public bool IsEcs => arch == "ECS" || arch == "ECS_ST";
        public bool EcsSingleThread => arch == "ECS_ST";
        public bool OopPooled => arch == "OOP_POOL";
        public bool Networked => net != "offline";
        public bool IsHost => role == "host";
        public bool IsClient => role == "client";
        public bool IsServerSide => role == "host" || role == "player";
        public bool GenAIOn => genai != "off";

        /// <summary>Load once; safe to call from any RuntimeInitializeOnLoadMethod and from ICustomBootstrap.</summary>
        public static BenchConfig Load()
        {
            if (Current != null) return Current;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-bench-config")
                {
                    Current = JsonUtility.FromJson<BenchConfig>(File.ReadAllText(args[i + 1]));
                    FromCommandLine = true;
                    break;
                }
            }
            if (Current == null)
            {
                Current = new BenchConfig();
                var env = Environment.GetEnvironmentVariable("FORGE_BENCH_ARCH");
                if (!string.IsNullOrEmpty(env)) Current.arch = env;
                Current.out_dir = Path.Combine(Application.persistentDataPath, "bench-editor");
            }
            Directory.CreateDirectory(Current.out_dir);
            return Current;
        }
    }
}

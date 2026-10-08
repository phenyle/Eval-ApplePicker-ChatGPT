using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Forge.Bench.Labels;
using UnityEngine;

namespace Forge.Bench.GenAI
{
    /// <summary>
    /// Shared GenAI pipeline (plan §2.2), identical in every variant:
    ///   schedule (fixed clock; per-spawn or rate-λ)  →  FIFO queue  →  window of K in flight
    ///   →  pregen lookup or HTTP (stub/live)  →  completed queue drained once per frame  →  label bound to target.
    /// Game code calls OnFixedStep / OnSpawn / OnDespawn; everything else happens in Update().
    /// Writes requests_&lt;role&gt;.csv at the end of the run.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public sealed class GenAIService : MonoBehaviour
    {
        public static GenAIService Instance { get; private set; }
        public const int FixedHz = 50;

        BenchConfig _cfg;
        GenAITransport _transport;
        PregenEntry[] _pregen;
        readonly Queue<GenAIRequest> _pending = new(1 << 14);
        readonly List<GenAIRequest> _all = new(1 << 16);
        readonly List<GenAIRequest> _appliedLastFrame = new();
        readonly SortedSet<int> _live = new();
        readonly List<GenAIRequest> _pregenDone = new();
        double _labelSeconds;
        int _inflight;
        bool _rate;

        /// <summary>Create the service if this run uses GenAI on the server/player side.</summary>
        public static void InitIfEnabled(double labelSeconds)
        {
            var cfg = BenchConfig.Load();
            if (!cfg.GenAIOn || !cfg.IsServerSide) return;
            var go = new GameObject("GenAIService");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<GenAIService>();
            Instance._labelSeconds = labelSeconds;
        }

        void Awake()
        {
            _cfg = BenchConfig.Load();
            _rate = _cfg.policy == "rate";
            if (_cfg.genai == "pregen")
            {
                var f = JsonUtility.FromJson<PregenFile>(File.ReadAllText(_cfg.pregen_file));
                _pregen = f.entries;
                RunController.Extra["pregen_sources"] = string.Join(";", f.sources);
                RunController.Extra["pregen_entries"] = _pregen.Length;
            }
            else
            {
                _transport = new GenAITransport(_cfg);
                RunController.Extra["request_body_bytes"] = _transport.BodyBytes;
            }
            RunController.OnFlush += Flush;
            PlayerLoopTimer.OnFrameStart += MarkVisible;
        }

        // ------------------------------------------------------------------ game hooks (main thread)

        /// <summary>Rate policy: λ arrivals per simulated second on the 50 Hz clock, targets chosen from live set.</summary>
        public void OnFixedStep(long k)
        {
            if (!_rate || !RunController.Recording) return;
            int n = BenchRng.EventsInStep(k, _cfg.lambda, FixedHz);
            for (int i = 0; i < n; i++)
            {
                if (_live.Count == 0) return;
                int id = _all.Count;
                int lo = _live.Min, hi = _live.Max;
                int probe = lo + (int)(BenchRng.U01d(_cfg.seed, BenchRng.StreamTarget, id) * (hi - lo + 1));
                var view = _live.GetViewBetween(probe, hi);
                int target = view.Count > 0 ? view.Min : lo;
                Schedule(id, target);
            }
        }

        public void OnSpawn(int spawnIdx)
        {
            if (_rate) { _live.Add(spawnIdx); return; }
            if (RunController.Recording) Schedule(_all.Count, spawnIdx);
        }

        public void OnDespawn(int spawnIdx)
        {
            if (_rate) _live.Remove(spawnIdx);
            LabelPool.Hide(spawnIdx);
        }

        void Schedule(int id, int target)
        {
            var r = new GenAIRequest { Id = id, Target = target, SchedQpc = Stopwatch.GetTimestamp() };
            _all.Add(r);
            _pending.Enqueue(r);
            Fingerprint.ReqScheduled++;
        }

        // ------------------------------------------------------------------ per-frame pump

        void Update()
        {
            long t0 = Stopwatch.GetTimestamp();
            var binder = LabelPool.Binder;
            double now = Time.timeAsDouble;

            while (_pending.Count > 0 && _inflight < _cfg.window_k)
            {
                var r = _pending.Dequeue();
                if (!binder.IsAlive(r.Target))
                {
                    r.Status = "dropped_before_send";
                    Fingerprint.ReqDropped++;
                    continue;
                }
                r.SentQpc = Stopwatch.GetTimestamp();
                _inflight++;
                Fingerprint.ReqSent++;
                if (_pregen != null)
                {
                    var e = _pregen[BenchRng.Pick(_cfg.seed, r.Id, _pregen.Length)];
                    r.DoneQpc = r.SentQpc; r.LatencyMs = 0f; r.HttpCode = e.http_code;
                    r.Status = e.http_code == 200 ? "ok" : "http_error";
                    r.Text = e.text ?? ""; r.TokensIn = e.tokens_in; r.TokensOut = e.tokens_out;
                    _pregenDone.Add(r);
                }
                else
                {
                    if (_cfg.genai == "live") Fingerprint.SentLive++;
                    r.BytesOut = _transport.BodyBytes;
                    _transport.Send(r);
                }
            }

            foreach (var r in _pregenDone) Complete(r, binder, now);
            _pregenDone.Clear();
            if (_transport != null)
                while (_transport.Completed.TryDequeue(out var r)) Complete(r, binder, now);

            LabelPool.Expire(now);
            Fingerprint.Inflight = _inflight;
            Fingerprint.QueueLen = _pending.Count;
            FrameRecorder.GenAITicks += Stopwatch.GetTimestamp() - t0;
        }

        void Complete(GenAIRequest r, ILabelBinder binder, double now)
        {
            _inflight--;
            Fingerprint.ReqDone++;
            if (r.Status != "ok") return;
            if (!binder.IsAlive(r.Target))
            {
                r.Status = "late";
                Fingerprint.ReqDropped++;
                return;
            }
            LabelPool.Show(r.Target, r.Text, r.Id, now + _labelSeconds);
            r.AppliedQpc = Stopwatch.GetTimestamp();
            r.VisibleFrame = Time.frameCount;
            _appliedLastFrame.Add(r);
            Fingerprint.ReqApplied++;
            Fingerprint.AppliedThisFrame++;
        }

        void MarkVisible()
        {
            if (_appliedLastFrame.Count == 0) return;
            long now = Stopwatch.GetTimestamp();
            foreach (var r in _appliedLastFrame) r.VisibleQpc = now;
            _appliedLastFrame.Clear();
        }

        // ------------------------------------------------------------------ output

        static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

        void Flush(string outDir)
        {
            var ci = CultureInfo.InvariantCulture;
            using var w = new StreamWriter(Path.Combine(outDir, $"requests_{_cfg.role}.csv"), false, new UTF8Encoding(false), 1 << 20);
            w.WriteLine("req_id,mode,target_spawn_idx,sched_qpc,sent_qpc,done_qpc,applied_qpc,visible_frame,visible_qpc," +
                        "status,http_code,retries,latency_ms,tokens_in,tokens_out,bytes_out,bytes_in,text");
            var sb = new StringBuilder(256);
            foreach (var r in _all)
            {
                string status = string.IsNullOrEmpty(r.Status) ? (r.SentQpc == 0 ? "pending" : "inflight") : r.Status;
                sb.Clear();
                sb.Append(r.Id).Append(',').Append(_cfg.genai).Append(',').Append(r.Target).Append(',')
                  .Append(r.SchedQpc).Append(',');
                if (r.SentQpc != 0) sb.Append(r.SentQpc); sb.Append(',');
                if (r.DoneQpc != 0) sb.Append(r.DoneQpc); sb.Append(',');
                if (r.AppliedQpc != 0) sb.Append(r.AppliedQpc); sb.Append(',');
                if (r.VisibleFrame >= 0) sb.Append(r.VisibleFrame); sb.Append(',');
                if (r.VisibleQpc != 0) sb.Append(r.VisibleQpc); sb.Append(',');
                sb.Append(status).Append(',');
                if (r.HttpCode >= 0) sb.Append(r.HttpCode); sb.Append(',');
                sb.Append(r.Retries).Append(',');
                if (!float.IsNaN(r.LatencyMs)) sb.Append(r.LatencyMs.ToString("0.###", ci)); sb.Append(',');
                if (r.TokensIn >= 0) sb.Append(r.TokensIn); sb.Append(',');
                if (r.TokensOut >= 0) sb.Append(r.TokensOut); sb.Append(',');
                if (r.BytesOut >= 0) sb.Append(r.BytesOut); sb.Append(',');
                if (r.BytesIn >= 0) sb.Append(r.BytesIn); sb.Append(',');
                sb.Append(Csv(r.Text));
                w.WriteLine(sb);
            }
        }
    }
}

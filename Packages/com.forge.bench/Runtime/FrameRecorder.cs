using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace Forge.Bench
{
    /// <summary>
    /// Per-frame recorder (plan §1.1; SCHEMA.md frames_&lt;role&gt;.csv). Rows live in a preallocated array and
    /// are written to disk only after the measurement window closes (no I/O during measurement).
    /// A row for frame f is finalised at the start of frame f+1, when f's phase timings are complete.
    /// </summary>
    public static class FrameRecorder
    {
        public struct Row
        {
            public int Frame; public byte Phase; public long TQpc; public double SimTime; public long FixedSteps;
            public float Dt, CpuFrame, CpuMain, CpuRender, Gpu, PresentWait;
            public float PhFixed, PhUpdate, PhPreLate, PhPostLate, PhGenAI;
            public int GcCount; public long GcAllocFrame, MonoUsed, TotalAlloc;
            public int Live; public long Spawned, Despawned, Hits;
            public long ReqSched, ReqSent, ReqDone, ReqApplied, ReqDropped;
            public int Inflight, QueueLen, LabelsVisible, AppliedThisFrame, NetTick, NetTickEst;
        }

        const int Capacity = 400_000;
        static Row[] _rows;
        static int _n;
        public static bool Overflow { get; private set; }
        public static int Count => _n;
        public static bool FtmSupported { get; private set; }
        public static bool GcRecorderValid { get; private set; }

        static readonly FrameTiming[] Timings = new FrameTiming[1];
        static ProfilerRecorder _gcAlloc;
        static long _frameStartQpc;
        static bool _haveFrame;
        public static long FixedStepsTotal;
        public static long GenAITicks;   // accumulated by GenAIService per frame

        static bool _minimal;

        public static void Init(bool minimal = false)
        {
            _minimal = minimal;
            _rows = new Row[Capacity];
            if (!minimal) _gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            PlayerLoopTimer.OnFrameStart += OnFrameStart;
        }

        static void OnFrameStart()
        {
            long now = Stopwatch.GetTimestamp();
            if (_haveFrame && RunController.Recording)
                Commit();
            _frameStartQpc = now;
            _haveFrame = true;
            PlayerLoopTimer.ResetAccum();
            GenAITicks = 0;
            Fingerprint.AppliedThisFrame = 0;
        }

        static void Commit()
        {
            if (_n >= Capacity) { Overflow = true; return; }
            if (_minimal)
            {
                // Frame time only (logger-overhead check): no FrameTimingManager, GC, profiler or phase timing.
                ref var q = ref _rows[_n++];
                q.Frame = Time.frameCount;
                q.Phase = (byte)RunController.CurrentPhase;
                q.TQpc = _frameStartQpc;
                q.SimTime = Time.timeAsDouble;
                q.FixedSteps = FixedStepsTotal;
                q.Dt = Time.unscaledDeltaTime * 1000f;
                q.CpuFrame = q.CpuMain = q.CpuRender = q.Gpu = q.PresentWait = float.NaN;
                q.GcAllocFrame = -1;
                q.Live = Fingerprint.Live; q.Spawned = Fingerprint.Spawned; q.Despawned = Fingerprint.Despawned;
                q.Hits = Fingerprint.Hits; q.NetTick = -1; q.NetTickEst = -1;
                return;
            }
            FrameTimingManager.CaptureFrameTimings();
            uint got = FrameTimingManager.GetLatestTimings(1, Timings);
            if (got > 0) FtmSupported = true;
            var acc = PlayerLoopTimer.Accum;
            ref var r = ref _rows[_n++];
            r.Frame = Time.frameCount;
            r.Phase = (byte)RunController.CurrentPhase;
            r.TQpc = _frameStartQpc;
            r.SimTime = Time.timeAsDouble;
            r.FixedSteps = FixedStepsTotal;
            r.Dt = Time.unscaledDeltaTime * 1000f;
            if (got > 0)
            {
                var t = Timings[0];
                r.CpuFrame = (float)t.cpuFrameTime; r.CpuMain = (float)t.cpuMainThreadFrameTime;
                r.CpuRender = (float)t.cpuRenderThreadFrameTime; r.Gpu = (float)t.gpuFrameTime;
                r.PresentWait = (float)t.cpuMainThreadPresentWaitTime;
            }
            else { r.CpuFrame = r.CpuMain = r.CpuRender = r.Gpu = r.PresentWait = float.NaN; }
            r.PhFixed = PlayerLoopTimer.Ms(acc[(int)PlayerLoopTimer.Phase.Fixed]);
            r.PhUpdate = PlayerLoopTimer.Ms(acc[(int)PlayerLoopTimer.Phase.Update]);
            r.PhPreLate = PlayerLoopTimer.Ms(acc[(int)PlayerLoopTimer.Phase.PreLate]);
            r.PhPostLate = PlayerLoopTimer.Ms(acc[(int)PlayerLoopTimer.Phase.PostLate]);
            r.PhGenAI = PlayerLoopTimer.Ms(GenAITicks);
            r.GcCount = GC.CollectionCount(0);
            GcRecorderValid = _gcAlloc.Valid;
            r.GcAllocFrame = _gcAlloc.Valid ? _gcAlloc.LastValue : -1;
            r.MonoUsed = Profiler.GetMonoUsedSizeLong();
            r.TotalAlloc = Profiler.GetTotalAllocatedMemoryLong();
            r.Live = Fingerprint.Live; r.Spawned = Fingerprint.Spawned; r.Despawned = Fingerprint.Despawned;
            r.Hits = Fingerprint.Hits;
            r.ReqSched = Fingerprint.ReqScheduled; r.ReqSent = Fingerprint.ReqSent; r.ReqDone = Fingerprint.ReqDone;
            r.ReqApplied = Fingerprint.ReqApplied; r.ReqDropped = Fingerprint.ReqDropped;
            r.Inflight = Fingerprint.Inflight; r.QueueLen = Fingerprint.QueueLen;
            r.LabelsVisible = Fingerprint.LabelsVisible; r.AppliedThisFrame = Fingerprint.AppliedThisFrame;
            r.NetTick = Fingerprint.NetTick; r.NetTickEst = Fingerprint.NetTickEst;
        }

        public static (double mean, int min, int max) LiveStats()
        {
            double sum = 0; int n = 0, mn = int.MaxValue, mx = int.MinValue;
            for (int i = 0; i < _n; i++)
            {
                if (_rows[i].Phase != (byte)RunPhase.Measure) continue;
                int v = _rows[i].Live; sum += v; n++;
                if (v < mn) mn = v; if (v > mx) mx = v;
            }
            return n == 0 ? (0, 0, 0) : (sum / n, mn, mx);
        }

        static readonly string[] PhaseNames = { "starting", "ramp", "warmup", "measure", "done" };

        public static void WriteCsv(string path)
        {
            var ci = CultureInfo.InvariantCulture;
            using var w = new StreamWriter(path, false, new UTF8Encoding(false), 1 << 20);
            w.WriteLine("frame,phase,t_qpc,sim_time_s,fixed_steps_total,dt_ms,cpu_frame_ms,cpu_main_ms,cpu_render_ms," +
                        "gpu_ms,present_wait_ms,ph_fixed_ms,ph_update_ms,ph_prelate_ms,ph_postlate_ms,ph_genai_ms," +
                        "gc_count,gc_alloc_frame_b,mono_used_b,total_alloc_b,live,spawned_total,despawned_total," +
                        "hits_total,req_sched_total,req_sent_total,req_done_total,req_applied_total,req_dropped_total," +
                        "inflight,queue_len,labels_visible,applied_this_frame,net_tick,net_tick_est");
            var sb = new StringBuilder(512);
            for (int i = 0; i < _n; i++)
            {
                ref var r = ref _rows[i];
                sb.Clear();
                sb.Append(r.Frame).Append(',').Append(PhaseNames[r.Phase]).Append(',').Append(r.TQpc).Append(',')
                  .Append(r.SimTime.ToString("R", ci)).Append(',').Append(r.FixedSteps).Append(',');
                F(sb, r.Dt, ci); F(sb, r.CpuFrame, ci); F(sb, r.CpuMain, ci); F(sb, r.CpuRender, ci); F(sb, r.Gpu, ci);
                F(sb, r.PresentWait, ci); F(sb, r.PhFixed, ci); F(sb, r.PhUpdate, ci); F(sb, r.PhPreLate, ci);
                F(sb, r.PhPostLate, ci); F(sb, r.PhGenAI, ci);
                sb.Append(r.GcCount).Append(',');
                if (r.GcAllocFrame >= 0) sb.Append(r.GcAllocFrame);
                sb.Append(',').Append(r.MonoUsed).Append(',').Append(r.TotalAlloc).Append(',')
                  .Append(r.Live).Append(',').Append(r.Spawned).Append(',').Append(r.Despawned).Append(',')
                  .Append(r.Hits).Append(',').Append(r.ReqSched).Append(',').Append(r.ReqSent).Append(',')
                  .Append(r.ReqDone).Append(',').Append(r.ReqApplied).Append(',').Append(r.ReqDropped).Append(',')
                  .Append(r.Inflight).Append(',').Append(r.QueueLen).Append(',').Append(r.LabelsVisible).Append(',')
                  .Append(r.AppliedThisFrame).Append(',');
                if (r.NetTick >= 0) sb.Append(r.NetTick);
                sb.Append(',');
                if (r.NetTickEst >= 0) sb.Append(r.NetTickEst);
                w.WriteLine(sb);
            }
        }

        static void F(StringBuilder sb, float v, IFormatProvider ci)
        {
            if (!float.IsNaN(v)) sb.Append(v.ToString("0.####", ci));
            sb.Append(',');
        }

        public static void Dispose()
        {
            if (_gcAlloc.Valid) _gcAlloc.Dispose();
        }
    }
}

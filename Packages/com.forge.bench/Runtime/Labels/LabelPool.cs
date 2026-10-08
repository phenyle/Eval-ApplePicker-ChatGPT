using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;

namespace Forge.Bench.Labels
{
    /// <summary>One pooled TextMeshPro (3D) label. Position is driven by the variant-specific binder (B2).</summary>
    public sealed class Label
    {
        public GameObject Go;
        public Transform T;
        public TextMeshPro Tmp;
        public int SpawnIdx = -1;
        public int Seq = -1;
        public double ExpireAt = double.PositiveInfinity;
    }

    /// <summary>
    /// Variant-specific glue between labels and game entities (OOP: GameObject transforms; ECS: entities).
    /// </summary>
    public interface ILabelBinder
    {
        bool IsAlive(int spawnIdx);
        void Attach(int spawnIdx, Label label);
        void Detach(int spawnIdx, Label label);
    }

    /// <summary>
    /// Shared label pool used by every variant (plan §2.2, variant-differences B2). Labels are TMP 3D text
    /// objects, created lazily and recycled; text is truncated to 32 characters. Logs shown/hidden events
    /// to labels_&lt;role&gt;.csv.
    /// </summary>
    public static class LabelPool
    {
        public const int MaxChars = 32;
        static readonly Stack<Label> Free = new();
        public static readonly Dictionary<int, Label> Active = new();
        static TMP_FontAsset _font;
        static float _fontSize;
        static ILabelBinder _binder;
        static readonly List<(long qpc, int frame, int spawn, int seq, bool shown)> Log = new(1 << 16);
        static bool _logHooked;

        public static ILabelBinder Binder => _binder;

        public static void Init(ILabelBinder binder, TMP_FontAsset font, float fontSize)
        {
            _binder = binder;
            _font = font;
            _fontSize = fontSize;
            if (!_logHooked) { RunController.OnFlush += Flush; _logHooked = true; }
        }

        public static Label Show(int spawnIdx, string text, int seq, double expireAt)
        {
            if (Active.TryGetValue(spawnIdx, out var existing)) Hide(spawnIdx);
            var l = Free.Count > 0 ? Free.Pop() : Create();
            l.SpawnIdx = spawnIdx; l.Seq = seq; l.ExpireAt = expireAt;
            if (text.Length > MaxChars) text = text.Substring(0, MaxChars);
            l.Tmp.text = text;
            l.Go.SetActive(true);
            Active[spawnIdx] = l;
            _binder.Attach(spawnIdx, l);
            Fingerprint.LabelsVisible = Active.Count;
            if (RunController.Recording) Log.Add((Stopwatch.GetTimestamp(), Time.frameCount, spawnIdx, seq, true));
            return l;
        }

        public static void Hide(int spawnIdx)
        {
            if (!Active.TryGetValue(spawnIdx, out var l)) return;
            Active.Remove(spawnIdx);
            _binder.Detach(spawnIdx, l);
            l.Go.SetActive(false);
            if (RunController.Recording) Log.Add((Stopwatch.GetTimestamp(), Time.frameCount, spawnIdx, l.Seq, false));
            l.SpawnIdx = -1; l.Seq = -1;
            Free.Push(l);
            Fingerprint.LabelsVisible = Active.Count;
        }

        static readonly List<int> Expired = new();

        /// <summary>Hide labels whose display time has elapsed (sim time).</summary>
        public static void Expire(double now)
        {
            Expired.Clear();
            foreach (var kv in Active) if (kv.Value.ExpireAt <= now) Expired.Add(kv.Key);
            foreach (var s in Expired) Hide(s);
        }

        static Label Create()
        {
            // Labels are scene roots (no shared parent) so IJobParallelForTransform can spread them across workers.
            var go = new GameObject("label");
            var tmp = go.AddComponent<TextMeshPro>();
            if (_font != null) tmp.font = _font;
            tmp.fontSize = _fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.color = Color.black;
            go.SetActive(false);
            return new Label { Go = go, T = go.transform, Tmp = tmp };
        }

        static void Flush(string outDir)
        {
            var cfg = BenchConfig.Current;
            using var w = new StreamWriter(Path.Combine(outDir, $"labels_{cfg.role}.csv"), false, new UTF8Encoding(false));
            w.WriteLine("qpc,frame,spawn_idx,label_seq,event");
            foreach (var e in Log)
                w.WriteLine(string.Join(",", e.qpc.ToString(CultureInfo.InvariantCulture), e.frame, e.spawn, e.seq,
                                        e.shown ? "shown" : "hidden"));
        }
    }
}

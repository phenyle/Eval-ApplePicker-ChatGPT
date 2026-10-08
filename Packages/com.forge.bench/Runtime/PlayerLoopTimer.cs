using System;
using System.Diagnostics;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace Forge.Bench
{
    /// <summary>
    /// Release-safe main-thread phase timing (plan metric R8, "PLM"). Inserts begin/end marker systems around
    /// the top-level player-loop phases and accumulates per-frame durations (FixedUpdate may run several
    /// times per frame; durations are summed). Must be installed after Entities/Netcode have appended their
    /// worlds to the player loop so that their groups fall inside the measured phases.
    /// Note: Entities runs FixedStepSimulationSystemGroup inside SimulationSystemGroup (Update phase), while
    /// MonoBehaviour.FixedUpdate runs in the FixedUpdate phase; compare fixed+update sums across variants.
    /// </summary>
    public static class PlayerLoopTimer
    {
        public enum Phase { Fixed = 0, Update = 1, PreLate = 2, PostLate = 3, Count = 4 }

        static readonly long[] Begin = new long[(int)Phase.Count];
        public static readonly long[] Accum = new long[(int)Phase.Count];
        public static Action OnFrameStart;
        static bool _installed;

        struct FrameStartMarker { }
        struct FixedBegin { } struct FixedEnd { }
        struct UpdateBegin { } struct UpdateEnd { }
        struct PreLateBegin { } struct PreLateEnd { }
        struct PostLateBegin { } struct PostLateEnd { }

        public static void Install()
        {
            if (_installed) return;
            var loop = PlayerLoop.GetCurrentPlayerLoop();
            var subs = loop.subSystemList;
            for (int i = 0; i < subs.Length; i++)
            {
                var t = subs[i].type;
                if (i == 0)
                    subs[i] = Wrap(subs[i], typeof(FrameStartMarker), FrameStart, null, null);
                if (t == typeof(FixedUpdate))
                    subs[i] = Wrap(subs[i], typeof(FixedBegin), () => Mark(Phase.Fixed, true), typeof(FixedEnd), () => Mark(Phase.Fixed, false));
                else if (t == typeof(Update))
                    subs[i] = Wrap(subs[i], typeof(UpdateBegin), () => Mark(Phase.Update, true), typeof(UpdateEnd), () => Mark(Phase.Update, false));
                else if (t == typeof(PreLateUpdate))
                    subs[i] = Wrap(subs[i], typeof(PreLateBegin), () => Mark(Phase.PreLate, true), typeof(PreLateEnd), () => Mark(Phase.PreLate, false));
                else if (t == typeof(PostLateUpdate))
                    subs[i] = Wrap(subs[i], typeof(PostLateBegin), () => Mark(Phase.PostLate, true), typeof(PostLateEnd), () => Mark(Phase.PostLate, false));
            }
            loop.subSystemList = subs;
            PlayerLoop.SetPlayerLoop(loop);
            _installed = true;
        }

        static PlayerLoopSystem Wrap(PlayerLoopSystem phase, Type beginType, PlayerLoopSystem.UpdateFunction begin,
                                     Type endType, PlayerLoopSystem.UpdateFunction end)
        {
            var list = phase.subSystemList ?? Array.Empty<PlayerLoopSystem>();
            int extra = (begin != null ? 1 : 0) + (end != null ? 1 : 0);
            var n = new PlayerLoopSystem[list.Length + extra];
            int j = 0;
            if (begin != null) n[j++] = new PlayerLoopSystem { type = beginType, updateDelegate = begin };
            Array.Copy(list, 0, n, j, list.Length);
            j += list.Length;
            if (end != null) n[j] = new PlayerLoopSystem { type = endType, updateDelegate = end };
            phase.subSystemList = n;
            return phase;
        }

        static void FrameStart() => OnFrameStart?.Invoke();

        static void Mark(Phase p, bool begin)
        {
            long now = Stopwatch.GetTimestamp();
            if (begin) Begin[(int)p] = now;
            else Accum[(int)p] += now - Begin[(int)p];
        }

        public static void ResetAccum()
        {
            for (int i = 0; i < Accum.Length; i++) Accum[i] = 0;
        }

        public static float Ms(long ticks) => (float)(ticks * 1000.0 / Stopwatch.Frequency);
    }
}

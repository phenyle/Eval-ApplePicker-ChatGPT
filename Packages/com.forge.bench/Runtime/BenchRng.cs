namespace Forge.Bench
{
    /// <summary>
    /// Counter-based deterministic randomness (plan §0.1). Every random decision is a pure function of
    /// (seed, stream, index), so OOP and ECS variants, worker threads, and frame rates all see the same
    /// values. Pure static math on ulong: callable from Burst jobs.
    /// Pick() must match genai_stub.pick() in analysis/harness/genai_stub.py.
    /// </summary>
    public static class BenchRng
    {
        // Streams keep independent decisions independent.
        public const ulong StreamTree = 1, StreamSpawnX = 2, StreamSpawnZ = 3, StreamTarget = 4, StreamBackoff = 5,
                           StreamSpawnTry = 6;

        public static ulong SplitMix64(ulong x)
        {
            unchecked
            {
                x += 0x9E3779B97F4A7C15UL;
                ulong z = x;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>Corpus/pregen entry selection; identical to the Python stub.</summary>
        public static int Pick(long seed, long reqId, int n)
        {
            unchecked
            {
                ulong x = (ulong)seed * 0x100000001B3UL + (ulong)reqId;
                return (int)(SplitMix64(x) % (ulong)n);
            }
        }

        /// <summary>Uniform float in [0,1) for (seed, stream, index).</summary>
        public static float U01(long seed, ulong stream, long index)
        {
            unchecked
            {
                ulong x = SplitMix64((ulong)seed ^ (stream << 56)) ^ (ulong)index * 0xD1B54A32D192ED03UL;
                return (SplitMix64(x) >> 40) * (1.0f / 16777216.0f);
            }
        }

        /// <summary>Uniform double in [0,1) for (seed, stream, index) — used where float resolution is not enough.</summary>
        public static double U01d(long seed, ulong stream, long index)
        {
            unchecked
            {
                ulong x = SplitMix64((ulong)seed ^ (stream << 56)) ^ (ulong)index * 0xD1B54A32D192ED03UL;
                return (SplitMix64(x) >> 11) * (1.0 / 9007199254740992.0);
            }
        }

        /// <summary>Number of events in fixed step k for a rate of `perSecond` on a `stepsPerSecond` clock
        /// (exactly perSecond events per simulated second, evenly spread).</summary>
        public static int EventsInStep(long k, double perSecond, int stepsPerSecond)
        {
            long a = (long)System.Math.Floor(k * perSecond / stepsPerSecond);
            long b = (long)System.Math.Floor((k + 1) * perSecond / stepsPerSecond);
            return (int)(b - a);
        }
    }
}

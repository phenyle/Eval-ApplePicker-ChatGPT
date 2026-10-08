using System.Collections.Generic;
using Forge.Bench;
using UnityEngine;

namespace Game.AP
{
    /// <summary>
    /// Apple Picker benchmark specification (plan §2.2). Every quantity both architectures need is defined
    /// here once. All gameplay decisions are on the 50 Hz fixed clock and are pure functions of (seed, step),
    /// so OOP and ECS runs with the same seed produce identical spawns, despawns, catches and GenAI targets.
    /// </summary>
    public static class APSpec
    {
        public const int FixedHz = 50;
        public const float Dt = 1f / FixedHz;
        public const float TreeY = 12f;
        public const float TreeSpeed = 10f;          // original AppleTree.speed
        public const float Edge = 24f;               // original leftAndRightEdge
        public const float FlipChance = 0.2f;        // original changeDirectionChance (per fixed step)
        public const float FallSpeed = 9.81f;        // GAS '26 constant-velocity fall
        public const int LifetimeSteps = 163;        // (12 - (-20)) / 9.81 / 0.02 = 163.1
        public const float BasketY = -13f;
        public const float BasketHalfBand = 0.5f;
        public const float BasketHalfWidth = 2f;
        public const float BasketAmp = 20f;
        public const float BasketPeriod = 7f;
        public const float OrthoSize = 16f;
        public const float LabelOffsetY = 1.1f;
        public const float LabelFontSize = 5f;

        /// <summary>Apple height `ageSeconds` after spawning (analytic; same in both architectures).</summary>
        public static float AppleY(double ageSeconds) => TreeY - FallSpeed * (float)ageSeconds;

        /// <summary>Basket x at time t. Called only from managed main-thread code in both architectures, once per
        /// fixed step, so the value is bit-identical across variants (Burst never evaluates the sine).</summary>
        public static float BasketX(double t) => (float)(BasketAmp * System.Math.Sin(2.0 * System.Math.PI * t / BasketPeriod));

        /// <summary>Apple ages (in fixed steps) whose analytic height lies inside the basket band; computed once in
        /// double precision so the catch test is an integer comparison in both architectures.</summary>
        public static readonly int CatchAgeMin, CatchAgeMax;

        static APSpec()
        {
            CatchAgeMin = int.MaxValue; CatchAgeMax = int.MinValue;
            for (int age = 0; age <= LifetimeSteps; age++)
            {
                double y = TreeY - (double)FallSpeed * age * Dt;
                if (y <= BasketY + BasketHalfBand && y >= BasketY - BasketHalfBand)
                {
                    if (age < CatchAgeMin) CatchAgeMin = age;
                    if (age > CatchAgeMax) CatchAgeMax = age;
                }
            }
        }

    }

    /// <summary>Pure math used inside Burst jobs (no static constructor, no managed state).</summary>
    public static class APMath
    {
        /// <summary>Fixed-step catch test: integer age band plus |x - basketX| &lt; half width (exact IEEE ops).</summary>
        public static bool Caught(long age, float x, float basketX, int ageMin, int ageMax)
        {
            if (age < ageMin || age > ageMax) return false;
            float d = x - basketX;
            return (d < 0 ? -d : d) < APSpec.BasketHalfWidth;
        }
    }

    /// <summary>Deterministic tree trajectory on the fixed clock: x(k) for step k (computed once, shared).</summary>
    public static class TreePath
    {
        static readonly List<float> Xs = new() { 0f };
        static float _dir = 1f;
        static long _seed;

        public static void Init(long seed) { _seed = seed; Xs.Clear(); Xs.Add(0f); _dir = 1f; }

        public static float X(long k)
        {
            while (Xs.Count <= k)
            {
                long i = Xs.Count - 1;
                float x = Xs[(int)i];
                if (BenchRng.U01(_seed, BenchRng.StreamTree, i) < APSpec.FlipChance) _dir = -_dir;
                x += _dir * APSpec.TreeSpeed * APSpec.Dt;
                if (x < -APSpec.Edge) _dir = 1f;
                else if (x > APSpec.Edge) _dir = -1f;
                Xs.Add(x);
            }
            return Xs[(int)k];
        }
    }
}

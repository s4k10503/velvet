using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Velvet
{
    // Scratch instrumentation: exclusive wall time and call counts per named stage. One global counter of
    // already-attributed ticks makes nesting and recursion work without a stack: a lap or scope end charges
    // its stage with the elapsed time minus whatever was attributed to other stages since it started.
    public static class PerfStage
    {
        public static bool Enabled;
        public static bool TrackAllocations;

        private static UnityEngine.Profiling.Recorder? s_recorder;
        private static long[] s_allocs = new long[256];
        private static long s_attributedAllocs;

        private static long Allocs()
        {
            if (!TrackAllocations) return 0;
            s_recorder ??= UnityEngine.Profiling.Recorder.Get("GC.Alloc");
            return s_recorder.sampleBlockCount;
        }

        private sealed class RefComparer : IEqualityComparer<string>
        {
            public bool Equals(string a, string b) => ReferenceEquals(a, b);
            public int GetHashCode(string s) => RuntimeHelpers.GetHashCode(s);
        }

        private static readonly Dictionary<string, int> s_byRef = new(new RefComparer());
        private static readonly Dictionary<string, int> s_byValue = new();
        private static readonly List<string> s_names = new();
        private static long[] s_excl = new long[256];
        private static long[] s_count = new long[256];
        private static long s_attributed;

        public struct Mark
        {
            internal long T;
            internal long A;
            internal long N;
            internal long AN;
        }

        public readonly struct Scope : IDisposable
        {
            private readonly string _name;
            private readonly long _t;
            private readonly long _a;
            private readonly long _n;
            private readonly long _an;

            internal Scope(string name, long t, long a, long n, long an)
            {
                _name = name;
                _t = t;
                _a = a;
                _n = n;
                _an = an;
            }

            public void Dispose()
            {
                if (_name == null) return;
                Charge(_name, _t, _a, _n, _an);
            }
        }

        public static Mark Start()
        {
            if (!Enabled) return default;
            return new Mark { T = Stopwatch.GetTimestamp(), A = s_attributed, N = Allocs(), AN = s_attributedAllocs };
        }

        public static Scope Begin(string name)
        {
            if (!Enabled) return default;
            return new Scope(name, Stopwatch.GetTimestamp(), s_attributed, Allocs(), s_attributedAllocs);
        }

        public static void Lap(ref Mark mark, string name)
        {
            if (!Enabled) return;
            var now = Stopwatch.GetTimestamp();
            ChargeAt(name, mark.T, mark.A, now, mark.N, mark.AN);
            mark.N = Allocs();
            mark.AN = s_attributedAllocs;
            mark.T = Stopwatch.GetTimestamp();
            mark.A = s_attributed;
        }

        private static void Charge(string name, long t, long a, long n, long an)
            => ChargeAt(name, t, a, Stopwatch.GetTimestamp(), n, an);

        private static void ChargeAt(string name, long t, long a, long now, long n, long an)
        {
            var own = now - t - (s_attributed - a);
            if (own < 0) own = 0;
            var ownAllocs = Allocs() - n - (s_attributedAllocs - an);
            if (ownAllocs < 0) ownAllocs = 0;
            var id = IdOf(name);
            s_excl[id] += own;
            s_count[id]++;
            s_attributed += own;
            s_allocs[id] += ownAllocs;
            s_attributedAllocs += ownAllocs;
        }

        private static int IdOf(string name)
        {
            if (s_byRef.TryGetValue(name, out var id)) return id;
            if (!s_byValue.TryGetValue(name, out id))
            {
                id = s_names.Count;
                s_names.Add(name);
                s_byValue[name] = id;
                if (id >= s_excl.Length)
                {
                    Array.Resize(ref s_excl, s_excl.Length * 2);
                    Array.Resize(ref s_count, s_count.Length * 2);
                    Array.Resize(ref s_allocs, s_allocs.Length * 2);
                }
            }
            s_byRef[name] = id;
            return id;
        }

        public static void Count(string name)
        {
            if (!Enabled) return;
            s_count[IdOf(name)]++;
        }

        public static void Reset()
        {
            Array.Clear(s_excl, 0, s_excl.Length);
            Array.Clear(s_count, 0, s_count.Length);
            Array.Clear(s_allocs, 0, s_allocs.Length);
            s_attributed = 0;
            s_attributedAllocs = 0;
        }

        public static long AllocsOf(string name) => s_byValue.TryGetValue(name, out var id) ? s_allocs[id] : 0;

        public static List<(string Name, double Ms, long Count)> Snapshot()
        {
            var freq = (double)Stopwatch.Frequency;
            var list = new List<(string, double, long)>();
            for (var i = 0; i < s_names.Count; i++)
            {
                if (s_count[i] == 0) continue;
                list.Add((s_names[i], s_excl[i] * 1000.0 / freq, s_count[i]));
            }
            return list;
        }
    }
}

using System.Collections.Generic;

namespace Velvet.CodeGen
{
    // Decides which of the runs found in a body become scopes of their own.
    internal static class ReactiveScopePlan
    {
        // Every scope costs an array of dependencies and a slot on each render, so a body is cut into few enough
        // that the bookkeeping does not outweigh what the cache saves.
        internal const int MaxScopes = 32;

        // A scope earns its slot where it can be reused while the run around it is rebuilt: it depends on something
        // that run does not. One that depends on exactly the values the run around it does is rebuilt exactly when
        // that run is, and caches nothing the run around it does not. A run that is the whole result of the method is
        // already covered by the gate on the method when there is one.
        public static List<ScopeRegion> Select(List<ScopeRegion> found, bool bodyGate, List<string> notes)
        {
            found.Sort(CompareOuterFirst);
            AssignParents(found);
            var kept = new List<ScopeRegion>();
            foreach (var region in found)
            {
                if (Earns(region, bodyGate)) kept.Add(region);
            }
            if (kept.Count > MaxScopes)
            {
                notes.Add($"{kept.Count - MaxScopes} further scopes were left uncached to keep the cost of the cache bounded");
                kept.RemoveRange(MaxScopes, kept.Count - MaxScopes);
            }
            return kept;
        }

        public static void Number(List<ScopeRegion> kept, int firstSlot)
        {
            for (var i = 0; i < kept.Count; i++) kept[i].Slot = firstSlot + i;
        }

        private static int CompareOuterFirst(ScopeRegion a, ScopeRegion b)
            => a.Start != b.Start ? a.Start.CompareTo(b.Start) : b.End.CompareTo(a.End);

        private static void AssignParents(List<ScopeRegion> sorted)
        {
            foreach (var region in sorted)
            {
                foreach (var candidate in sorted)
                {
                    if (!candidate.Contains(region)) continue;
                    if (region.Parent == null || region.Parent.Contains(candidate)) region.Parent = candidate;
                }
            }
        }

        private static bool Earns(ScopeRegion region, bool bodyGate)
        {
            if (region.Parent == null) return !(bodyGate && region.ReturnsFromMethod);
            return !SameDependencies(region, region.Parent);
        }

        private static bool SameDependencies(ScopeRegion inner, ScopeRegion outer)
        {
            if (inner.Dependencies.Count != outer.Dependencies.Count) return false;
            var outerKeys = new HashSet<string>();
            foreach (var each in outer.Dependencies) outerKeys.Add(each.Key);
            return inner.Dependencies.TrueForAll(each => outerKeys.Contains(each.Key));
        }
    }
}

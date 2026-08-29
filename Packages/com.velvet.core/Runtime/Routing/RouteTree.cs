using System;
using System.Collections.Generic;

namespace Velvet
{
    /// <summary>
    /// Ranks route branches by specificity before matching; declaration order breaks score ties only.
    /// Supports literal, dynamic (<c>:param</c>), optional (<c>:param?</c> / <c>segment?</c>), and splat
    /// (<c>*</c>) segments.
    /// </summary>
    public sealed class RouteTree
    {
        private readonly RouteDefinition[] _routes;
        private readonly List<RouteBranch> _rankedBranches;

        // One buffer for every branch probe rather than one array each. Match walks the ranked branches
        // until one succeeds, so a per-probe array is an allocation per branch not taken. Held on the
        // tree and not static: two trees are two navigations, and Match runs no caller code, so nothing
        // re-enters one tree's probe while it is using this.
        private int[] _taken = Array.Empty<int>();

        /// <param name="routes">Array of route definitions. null is not allowed.</param>
        public RouteTree(RouteDefinition[] routes)
        {
            _routes = routes ?? throw new ArgumentNullException(nameof(routes));
            _rankedBranches = new List<RouteBranch>();
            FlattenBranches(_routes, new List<RouteDefinition>(), _rankedBranches);

            // Stable sort by descending score so that, among branches of equal specificity, the earlier
            // declaration order is preserved (List.Sort is not stable, so encode the original index).
            _rankedBranches.Sort((a, b) =>
            {
                var byScore = b.Score.CompareTo(a.Score);
                return byScore != 0 ? byScore : a.Order.CompareTo(b.Order);
            });
        }

        /// <param name="path">Path to match. null and empty string are invalid and return null (use "/" for the root path).</param>
        /// <returns>The matched chain (parent first) or null when nothing matches.</returns>
        public IReadOnlyList<RouteMatch>? Match(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            var segments = NormalizePath(path);

            foreach (var branch in _rankedBranches)
            {
                if (TryMatchBranch(branch, segments, out var matches))
                {
                    return matches;
                }
            }

            return null;
        }

        #region Branch flattening

        private sealed class RouteBranch
        {
            public readonly RouteDefinition[] Chain;
            public readonly List<RouteSegment> Pattern;

            /// <summary>How many of <see cref="Pattern"/>'s segments each chain entry contributed, so a
            /// match reads its slice rather than parsing the route's path again.</summary>
            public readonly int[] SegmentCounts;

            public int Score;
            public int Order;

            public RouteBranch(RouteDefinition[] chain, List<RouteSegment> pattern, int[] segmentCounts)
            {
                Chain = chain;
                Pattern = pattern;
                SegmentCounts = segmentCounts;
            }
        }

        private readonly struct RouteSegment
        {
            public readonly string Value;
            public readonly bool IsParam;
            public readonly bool IsOptional;
            public readonly bool IsSplat;
            public readonly bool CaseSensitive;

            public RouteSegment(string value, bool isParam, bool isOptional, bool isSplat, bool caseSensitive)
            {
                Value = value;
                IsParam = isParam;
                IsOptional = isOptional;
                IsSplat = isSplat;
                CaseSensitive = caseSensitive;
            }
        }

        private int _branchCounter;

        private void FlattenBranches(
            RouteDefinition[]? routes, List<RouteDefinition> ancestors, List<RouteBranch> output)
        {
            if (routes == null) return;
            foreach (var route in routes)
            {
                ancestors.Add(route);

                var hasChildren = route.Children is { Length: > 0 };

                // The refusal inside ParseRouteSegments is over one route's own path, and a branch is
                // every ancestor's segments joined. A splat stops the walk where it matches, so a child
                // segment behind one is never compared: the branch outscores its splat-only parent and
                // wins for every path the parent would have taken, which is the swallowing the
                // one-path refusal is worded against.
                if (hasChildren && EndsWithSplat(route))
                {
                    throw new ArgumentException(
                        $"Splat segment '*' must be the last segment of a route path, but route "
                        + $"'{route.Path}' declares {route.Children!.Length} child route(s) behind it. "
                        + "A splat takes the whole tail, so nothing under it can be reached as "
                        + "declared. Move the children beside the splat route rather than under it.");
                }

                // Every route is a candidate so a parent can match with an empty Outlet. An index child is
                // scored above its bare parent and joins the chain when both consume the same path.
                output.Add(BuildBranch(ancestors));

                if (hasChildren)
                {
                    FlattenBranches(route.Children, ancestors, output);
                }

                ancestors.RemoveAt(ancestors.Count - 1);
            }
        }

        private static bool EndsWithSplat(RouteDefinition route)
        {
            var last = false;
            foreach (var seg in ParseRouteSegments(route))
            {
                last = seg.IsSplat;
            }

            return last;
        }

        private RouteBranch BuildBranch(List<RouteDefinition> chain)
        {
            var pattern = new List<RouteSegment>();
            var counts = new int[chain.Count];
            for (var index = 0; index < chain.Count; index++)
            {
                var before = pattern.Count;
                foreach (var seg in ParseRouteSegments(chain[index]))
                {
                    pattern.Add(seg);
                }
                counts[index] = pattern.Count - before;
            }

            var leaf = chain[chain.Count - 1];
            var isIndexLeaf = leaf.Path == "";

            return new RouteBranch(chain.ToArray(), pattern, counts)
            {
                Score = ComputeScore(pattern, isIndexLeaf),
                Order = _branchCounter++,
            };
        }

        private static IEnumerable<RouteSegment> ParseRouteSegments(RouteDefinition route)
        {
            var path = route.Path;
            if (string.IsNullOrEmpty(path) || path == "/")
            {
                yield break;
            }

            var trimmed = TrimSlashes(path);
            if (trimmed.Length == 0)
            {
                yield break;
            }

            var parts = trimmed.Split('/');
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part == "*")
                {
                    // Within one route path, a splat must be last because matching it stops segment checks.
                    if (i != parts.Length - 1)
                    {
                        throw new ArgumentException(
                            $"Splat segment '*' must be the last segment of a route path, but route '{path}' " +
                            "places it before another segment. Use a trailing '*' (e.g. 'files/*').");
                    }

                    yield return new RouteSegment("*", isParam: false, isOptional: false, isSplat: true, route.CaseSensitive);
                    continue;
                }

                var isOptional = part.EndsWith("?");
                var core = isOptional ? part.Substring(0, part.Length - 1) : part;

                if (core.StartsWith(":"))
                {
                    yield return new RouteSegment(core.Substring(1), isParam: true, isOptional, isSplat: false, route.CaseSensitive);
                }
                else
                {
                    yield return new RouteSegment(core, isParam: false, isOptional, isSplat: false, route.CaseSensitive);
                }
            }
        }

        #endregion

        #region Scoring

        // Per-segment scoring weights used to rank branches by specificity. The relative magnitudes and
        // ordering carry the meaning, not the absolute numbers: at equal depth static >> dynamic > splat, and
        // the splat weight is negative so a literal segment always outranks a "*" catch-all.
        private const int StaticSegmentScore = 10;
        private const int DynamicSegmentScore = 3;
        private const int IndexRouteScore = 2;
        private const int SplatPenalty = -2;
        private const int OptionalBonus = 1;
        private const int EmptySegmentScore = 1;

        private static int ComputeScore(List<RouteSegment> pattern, bool isIndexLeaf)
        {
            var score = isIndexLeaf ? IndexRouteScore : 0;

            foreach (var seg in pattern)
            {
                if (seg.IsSplat)
                {
                    score += SplatPenalty;
                }
                else if (seg.IsParam)
                {
                    score += DynamicSegmentScore;
                }
                else if (seg.Value.Length == 0)
                {
                    score += EmptySegmentScore;
                }
                else
                {
                    score += StaticSegmentScore;
                }

                if (seg.IsOptional)
                {
                    score += OptionalBonus;
                }
            }

            return score;
        }

        #endregion

        #region Branch matching

        private ref struct Walk
        {
            public readonly List<RouteSegment> Pattern;
            public readonly string[] Segments;

            /// <summary>Which path segment each pattern segment took, or -1 for one the match skipped.
            /// An absent optional literal is otherwise indistinguishable from one the URL held, and
            /// <see cref="BuildMatches"/> reads the pattern rather than the path.</summary>
            public readonly int[] Taken;

            public Dictionary<string, string>? Captured;

            public Walk(List<RouteSegment> pattern, string[] segments, int[] taken)
            {
                Pattern = pattern;
                Segments = segments;
                Taken = taken;
                for (var index = 0; index < pattern.Count; index++)
                {
                    Taken[index] = -1;
                }

                Captured = null;
            }
        }

        private bool TryMatchBranch(RouteBranch branch, string[] segments, out List<RouteMatch>? matches)
        {
            matches = null;

            if (_taken.Length < branch.Pattern.Count)
            {
                _taken = new int[branch.Pattern.Count];
            }

            var walk = new Walk(branch.Pattern, segments, _taken);

            if (!TryConsume(ref walk, 0, 0))
            {
                return false;
            }

            var captured = walk.Captured;
            var taken = walk.Taken;

            // A paramless successful match still exposes a (shared, empty) dictionary at every level,
            // preserving the pre-lazy contract that RouteMatch.Params is never null.
            matches = BuildMatches(branch, captured ?? new Dictionary<string, string>(), taken);
            return true;
        }

        /// <remarks>
        /// Writes into `taken` as it walks and never unwrites. Undoing a failed probe's entries was
        /// written here at first, on the strength of the capture snapshot below being the same
        /// discipline -- and it is not: `taken` is indexed by pattern position, which the next probe
        /// overwrites as it passes, where a dictionary keyed by name has no such property.
        /// </remarks>
        private static bool TryConsume(ref Walk walk, int pi, int si)
        {
            while (pi < walk.Pattern.Count)
            {
                var seg = walk.Pattern[pi];

                if (seg.IsSplat)
                {
                    // MUTANT_SURVIVES(equivalent): at si == Length the join is asked for zero
                    // elements and returns the empty string, which is what the other arm yields.
                    var rest = si >= walk.Segments.Length
                        ? string.Empty
                        : string.Join("/", walk.Segments, si, walk.Segments.Length - si);
                    walk.Captured ??= new Dictionary<string, string>();
                    walk.Captured["*"] = rest;
                    return true;
                }

                if (seg.IsOptional)
                {
                    // Greedily try the optional segment as present before falling through to the skip
                    // branch, which consumes no path segment.
                    if (si < walk.Segments.Length && TryConsumeOptionalPresent(ref walk, pi, si, seg))
                    {
                        walk.Taken[pi] = si;
                        return true;
                    }

                    if (TryConsume(ref walk, pi + 1, si))
                    {
                        return true;
                    }

                    return false;
                }

                if (si >= walk.Segments.Length || !TryMatchSingle(ref walk, seg, walk.Segments[si]))
                {
                    return false;
                }

                walk.Taken[pi] = si;
                pi++;
                si++;
            }

            if (si == walk.Segments.Length)
            {
                return true;
            }

            return false;
        }

        /// <remarks>
        /// A capture made here must not leak into the caller's skip branch, so the param key is snapshotted
        /// and restored when the downstream match fails.
        /// </remarks>
        private static bool TryConsumeOptionalPresent(ref Walk walk, int pi, int si, RouteSegment seg)
        {
            var keyExisted = false;
            string snap = "";
            if (seg.IsParam && walk.Captured != null)
            {
                keyExisted = walk.Captured.TryGetValue(seg.Value, out snap);
            }

            if (TryMatchSingle(ref walk, seg, walk.Segments[si]) && TryConsume(ref walk, pi + 1, si + 1))
            {
                return true;
            }

            // No null check, unlike the snapshot above: `TryMatchSingle` runs on the way in and creates
            // the dictionary for a param, so reaching here with `seg.IsParam` means it exists. The
            // `IsParam` test itself is load-bearing -- for a literal, `seg.Value` is its text, and
            // removing under it takes whatever param happens to share the spelling.
            if (seg.IsParam)
            {
                if (keyExisted)
                {
                    walk.Captured[seg.Value] = snap;
                }
                else
                {
                    walk.Captured.Remove(seg.Value);
                }
            }

            return false;
        }

        private static bool TryMatchSingle(ref Walk walk, RouteSegment seg, string pathSeg)
        {
            if (seg.IsParam)
            {
                walk.Captured ??= new Dictionary<string, string>();
                walk.Captured[seg.Value] = pathSeg;
                return true;
            }

            var comparison = seg.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            return string.Equals(seg.Value, pathSeg, comparison);
        }

        private static List<RouteMatch> BuildMatches(
            RouteBranch branch, Dictionary<string, string> captured, int[] taken)
        {
            var chain = branch.Chain;
            var matches = new List<RouteMatch>(chain.Length);
            var cumulativeId = string.Empty;
            // Each level stores a cumulative base because route-relative `..` pops a whole route level,
            // not one URL segment.
            var cumulativeResolved = string.Empty;
            // `taken` is indexed over the branch's flattened pattern, which is each route's segments in
            // chain order, so a route's slice starts where the routes above it ended.
            var patternOffset = 0;

            for (var level = 0; level < chain.Length; level++)
            {
                var route = chain[level];
                cumulativeId = AppendRouteId(cumulativeId, route);

                var resolvedSegment = ResolveRouteSegments(
                    branch.Pattern, branch.SegmentCounts[level], captured, taken, ref patternOffset);
                if (resolvedSegment.Length > 0)
                {
                    cumulativeResolved = cumulativeResolved.Length == 0
                        ? resolvedSegment
                        : cumulativeResolved + "/" + resolvedSegment;
                }

                // Every level exposes the full branch's captures, including captures from descendants.
                matches.Add(new RouteMatch
                {
                    Route = route,
                    Params = captured,
                    MatchedPath = ComputeMatchedPath(route),
                    PathnameBase = cumulativeResolved.Length == 0 ? "/" : "/" + cumulativeResolved,
                    RouteId = cumulativeId,
                });
            }

            return matches;
        }

        // Read off the branch's already-parsed pattern rather than the route's path. Two readings of one
        // path do not agree segment for segment -- one drops the empty part in `a//b` and the other keeps
        // it -- and `taken` is indexed over the pattern, so the pattern is the reading that can be
        // indexed. Re-parsing here also allocated an enumerator per route on every match.
        private static string ResolveRouteSegments(
            List<RouteSegment> pattern, int count, IReadOnlyDictionary<string, string> captured,
            int[] taken, ref int patternOffset)
        {
            var resolved = ScratchSegments;
            resolved.Clear();

            for (var step = 0; step < count; step++)
            {
                var seg = pattern[patternOffset];
                var index = patternOffset++;

                // `taken` is not read for a splat or a param: the first resolves from the captured
                // tail and the second from its capture, and only a literal has nothing else to say
                // whether the URL held it.
                if (seg.IsSplat)
                {
                    if (captured.TryGetValue("*", out var splat) && splat.Length > 0)
                    {
                        resolved.Add(splat);
                    }
                    continue;
                }

                if (seg.IsParam)
                {
                    // A captured segment can be empty -- the split that produces path segments keeps
                    // empty entries, so `//` reaches a param -- and a base carrying it would double the
                    // separator in every relative target resolved against it.
                    if (captured.TryGetValue(seg.Value, out var value) && value.Length > 0)
                    {
                        resolved.Add(value);
                    }
                    continue;
                }

                // A literal the match skipped is one the URL never held, so a base built from it would
                // resolve the next relative hop against a path that does not exist.
                // MUTANT_SURVIVES(unreachable): `taken` is as long as the branch pattern and `index`
                // walks that same pattern, so the bound is never the arm that decides.
                if (seg.IsOptional && (index >= taken.Length || taken[index] < 0))
                {
                    continue;
                }

                resolved.Add(seg.Value);
            }

            return string.Join("/", resolved);
        }

        // One list for every level of every match. BuildMatches reads the string this returns before it
        // asks for the next level, so nothing outlives a call. Static because RouteTree runs no caller
        // code between filling it and joining it.
        private static readonly List<string> ScratchSegments = new();

        private static string AppendRouteId(string parentId, RouteDefinition route)
        {
            // Index routes would otherwise reuse their parent's id, so disambiguate them explicitly.
            if (route.Path == "")
            {
                return parentId.Length == 0 ? "/?index" : parentId + "/?index";
            }

            if (route.Path == "/")
            {
                return "/";
            }

            var segment = TrimSlashes(route.Path ?? string.Empty);
            if (parentId.Length == 0 || parentId == "/")
            {
                return "/" + segment;
            }

            return parentId + "/" + segment;
        }

        private static string ComputeMatchedPath(RouteDefinition route)
        {
            if (route.Path == "/")
            {
                return "/";
            }

            if (route.Path == "")
            {
                return "";
            }

            return TrimSlashes(route.Path ?? string.Empty);
        }

        #endregion

        private static string[] NormalizePath(string path)
        {
            if (path == "/" || path == "")
            {
                return Array.Empty<string>();
            }

            var trimmed = TrimSlashes(path);
            return trimmed.Length == 0 ? Array.Empty<string>() : trimmed.Split('/');
        }

        private static string TrimSlashes(string path) => path.TrimStart('/').TrimEnd('/');
    }
}

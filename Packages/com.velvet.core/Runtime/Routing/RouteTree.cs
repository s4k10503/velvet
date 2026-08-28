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
            // Set together at construction, so neither is nullable and neither reading has a branch for
            // an absence that cannot happen. The two null checks that were here survived every mutant
            // that removed them, which is what an unreachable branch does.
            public readonly RouteDefinition[] Chain;
            public readonly List<RouteSegment> Pattern;
            public int Score;
            public int Order;

            public RouteBranch(RouteDefinition[] chain, List<RouteSegment> pattern)
            {
                Chain = chain;
                Pattern = pattern;
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

        private RouteBranch BuildBranch(List<RouteDefinition> chain)
        {
            var pattern = new List<RouteSegment>();
            foreach (var route in chain)
            {
                foreach (var seg in ParseRouteSegments(route))
                {
                    pattern.Add(seg);
                }
            }

            var leaf = chain[chain.Count - 1];
            var isIndexLeaf = leaf.Path == "";

            return new RouteBranch(chain.ToArray(), pattern)
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

        /// <summary>One branch probe's state: everything the walk carries that does not change as it
        /// descends, plus the two things it fills in.</summary>
        /// <remarks>
        /// A ref struct so the dictionary stays lazy. Match probes ranked branches until one succeeds
        /// and discards a failed probe's captures, so allocating eagerly pays per branch and keeps one.
        /// </remarks>
        private ref struct Walk
        {
            public readonly List<RouteSegment> Pattern;
            public readonly string[] Segments;

            /// <summary>Which path segment each pattern segment took, or -1 for one the match skipped.
            /// An absent optional literal is otherwise indistinguishable from one the URL held, and
            /// <see cref="BuildMatches"/> reads the pattern rather than the path.</summary>
            public readonly int[] Taken;

            public Dictionary<string, string>? Captured;

            public Walk(List<RouteSegment> pattern, string[] segments)
            {
                Pattern = pattern;
                Segments = segments;
                Taken = new int[pattern.Count];
                for (var index = 0; index < Taken.Length; index++)
                {
                    Taken[index] = -1;
                }

                Captured = null;
            }
        }

        private static bool TryMatchBranch(RouteBranch branch, string[] segments, out List<RouteMatch>? matches)
        {
            matches = null;

            var walk = new Walk(branch.Pattern, segments);

            if (!TryConsume(ref walk, 0, 0))
            {
                return false;
            }

            var captured = walk.Captured;
            var taken = walk.Taken;

            // A paramless successful match still exposes a (shared, empty) dictionary at every level,
            // preserving the pre-lazy contract that RouteMatch.Params is never null.
            matches = BuildMatches(branch.Chain, captured ?? new Dictionary<string, string>(), taken);
            return true;
        }

        /// <remarks>
        /// Writes into `taken` as it walks and never unwrites. A failed probe's entries are not the
        /// capture dictionary's problem one level up: `taken` is indexed by pattern position, and the
        /// walk that succeeds decides every position it passes, so nothing it did not decide is read.
        /// Undoing them was written here anyway, on the strength of the capture snapshot below being
        /// the same discipline -- and it is not, a dictionary keyed by name having no such property.
        /// Three mutants removing the undo all survived, and the suite is green without it.
        /// </remarks>
        private static bool TryConsume(ref Walk walk, int pi, int si)
        {
            while (pi < walk.Pattern.Count)
            {
                var seg = walk.Pattern[pi];

                if (seg.IsSplat)
                {
                    var rest = si >= walk.Segments.Length
                        ? string.Empty
                        : string.Join("/", walk.Segments, si, walk.Segments.Length - si);
                    walk.Captured ??= new Dictionary<string, string>();
                    walk.Captured["*"] = rest;
                    walk.Taken[pi] = si < walk.Segments.Length ? si : -1;
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

            // The failed attempt may have been what created the dictionary, so it can be non-null here
            // even when the snapshot above saw null.
            if (seg.IsParam && walk.Captured != null)
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
            RouteDefinition[] chain, Dictionary<string, string> captured, int[] taken)
        {
            var matches = new List<RouteMatch>(chain.Length);
            var cumulativeId = string.Empty;
            // Each level stores a cumulative base because route-relative `..` pops a whole route level,
            // not one URL segment.
            var cumulativeResolved = string.Empty;
            // `taken` is indexed over the branch's flattened pattern, which is each route's segments in
            // chain order, so a route's slice starts where the routes above it ended.
            var patternOffset = 0;

            foreach (var route in chain)
            {
                cumulativeId = AppendRouteId(cumulativeId, route);

                var resolvedSegment = ResolveRouteSegments(route, captured, taken, ref patternOffset);
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

        /// <summary>
        /// The path this route contributes to a match's base, with any segment the match skipped left
        /// out. Advances <paramref name="patternOffset"/> past this route's share of the branch pattern.
        /// </summary>
        /// <remarks>
        /// Walks <see cref="ParseRouteSegments"/> rather than splitting the path again: indexing into
        /// `taken` needs the two readings to agree segment for segment, and they did not — one dropped
        /// the empty part in `a//b` and the other kept it.
        /// </remarks>
        private static string ResolveRouteSegments(
            RouteDefinition route, IReadOnlyDictionary<string, string> captured, int[] taken,
            ref int patternOffset)
        {
            var resolved = new List<string>();

            foreach (var seg in ParseRouteSegments(route))
            {
                var index = patternOffset++;

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
                    if (captured.TryGetValue(seg.Value, out var value) && value.Length > 0)
                    {
                        resolved.Add(value);
                    }
                    continue;
                }

                // A literal the match skipped is one the URL never held, so a base built from it would
                // resolve the next relative hop against a path that does not exist.
                if (seg.IsOptional && (index >= taken.Length || taken[index] < 0))
                {
                    continue;
                }

                resolved.Add(seg.Value);
            }

            return string.Join("/", resolved);
        }

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

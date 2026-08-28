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
            public RouteDefinition[]? Chain;
            public List<RouteSegment>? Pattern;
            public int Score;
            public int Order;
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
            foreach (var route in chain)
            {
                foreach (var seg in ParseRouteSegments(route))
                {
                    pattern.Add(seg);
                }
            }

            var leaf = chain[chain.Count - 1];
            var isIndexLeaf = leaf.Path == "";

            return new RouteBranch
            {
                Chain = chain.ToArray(),
                Pattern = pattern,
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

        private static bool TryMatchBranch(RouteBranch branch, string[] segments, out List<RouteMatch>? matches)
        {
            matches = null;

            // Null until the first capture, and threaded by ref for that: Match probes ranked branches
            // until one succeeds, and a failed probe's dictionary is discarded, so allocating eagerly
            // pays per branch probed and keeps one.
            Dictionary<string, string>? captured = null;

            if (!TryConsume(branch.Pattern, 0, segments, 0, ref captured))
            {
                return false;
            }

            // A paramless successful match still exposes a (shared, empty) dictionary at every level,
            // preserving the pre-lazy contract that RouteMatch.Params is never null.
            matches = BuildMatches(branch.Chain, captured ?? new Dictionary<string, string>());
            return true;
        }

        private static bool TryConsume(
            List<RouteSegment>? pattern, int pi, string[] segments, int si, ref Dictionary<string, string>? captured)
        {
            if (pattern == null) return false;
            while (pi < pattern.Count)
            {
                var seg = pattern[pi];

                if (seg.IsSplat)
                {
                    var rest = si >= segments.Length
                        ? string.Empty
                        : string.Join("/", segments, si, segments.Length - si);
                    captured ??= new Dictionary<string, string>();
                    captured["*"] = rest;
                    return true;
                }

                if (seg.IsOptional)
                {
                    // Greedily try the optional segment as present before falling through to the skip
                    // branch, which consumes no path segment.
                    if (si < segments.Length &&
                        TryConsumeOptionalPresent(pattern, pi, segments, si, seg, ref captured))
                    {
                        return true;
                    }

                    return TryConsume(pattern, pi + 1, segments, si, ref captured);
                }

                if (si >= segments.Length || !TryMatchSingle(seg, segments[si], ref captured))
                {
                    return false;
                }

                pi++;
                si++;
            }

            return si == segments.Length;
        }

        /// <remarks>
        /// A capture made here must not leak into the caller's skip branch, so the param key is snapshotted
        /// and restored when the downstream match fails.
        /// </remarks>
        private static bool TryConsumeOptionalPresent(
            List<RouteSegment> pattern, int pi, string[] segments, int si, RouteSegment seg,
            ref Dictionary<string, string>? captured)
        {
            var keyExisted = false;
            string snap = "";
            if (seg.IsParam && captured != null)
            {
                keyExisted = captured.TryGetValue(seg.Value, out snap);
            }

            if (TryMatchSingle(seg, segments[si], ref captured) &&
                TryConsume(pattern, pi + 1, segments, si + 1, ref captured))
            {
                return true;
            }

            // The failed attempt may have been what created the dictionary, so it can be non-null here
            // even when the snapshot above saw null.
            if (seg.IsParam && captured != null)
            {
                if (keyExisted)
                {
                    captured[seg.Value] = snap;
                }
                else
                {
                    captured.Remove(seg.Value);
                }
            }

            return false;
        }

        private static bool TryMatchSingle(RouteSegment seg, string pathSeg, ref Dictionary<string, string>? captured)
        {
            if (seg.IsParam)
            {
                captured ??= new Dictionary<string, string>();
                captured[seg.Value] = pathSeg;
                return true;
            }

            var comparison = seg.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            return string.Equals(seg.Value, pathSeg, comparison);
        }

        private static List<RouteMatch> BuildMatches(RouteDefinition[]? chain, Dictionary<string, string> captured)
        {
            if (chain == null) return new List<RouteMatch>();
            var matches = new List<RouteMatch>(chain.Length);
            var cumulativeId = string.Empty;
            // Each level stores a cumulative base because route-relative `..` pops a whole route level,
            // not one URL segment.
            var cumulativeResolved = string.Empty;

            foreach (var route in chain)
            {
                cumulativeId = AppendRouteId(cumulativeId, route);

                var resolvedSegment = ResolveRouteSegments(route, captured);
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

        private static string ResolveRouteSegments(RouteDefinition route, IReadOnlyDictionary<string, string> captured)
        {
            if (string.IsNullOrEmpty(route.Path) || route.Path == "/" || route.Path == "")
            {
                return string.Empty;
            }

            var pattern = TrimSlashes(route.Path);
            var parts = pattern.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var resolved = new List<string>(parts.Length);

            foreach (var rawPart in parts)
            {
                var part = rawPart;
                var optional = part.Length > 0 && part[part.Length - 1] == '?';
                if (optional)
                {
                    part = part.Substring(0, part.Length - 1);
                }

                if (part == "*")
                {
                    if (captured.TryGetValue("*", out var splat) && splat.Length > 0)
                    {
                        resolved.Add(splat);
                    }
                    continue;
                }

                if (part.Length > 0 && part[0] == ':')
                {
                    var name = part.Substring(1);
                    if (captured.TryGetValue(name, out var value) && value.Length > 0)
                    {
                        resolved.Add(value);
                    }
                    continue;
                }

                resolved.Add(part);
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

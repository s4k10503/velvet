using System;
using System.Collections.Generic;

namespace Velvet
{
    /// <summary>
    /// Matching engine over the route definition tree.
    /// Flattens the tree into ranked branches: every leaf route is paired with its
    /// full ancestor chain, each branch is scored for specificity, and <see cref="Match"/> returns the
    /// highest-scoring branch whose pattern matches the path. Matching is therefore best-match, not
    /// declaration-order. Supports literal, dynamic (<c>:param</c>), optional (<c>:param?</c> / <c>segment?</c>),
    /// and splat (<c>*</c>) segments.
    /// </summary>
    public sealed class RouteTree
    {
        private readonly RouteDefinition[] _routes;
        private readonly List<RouteBranch> _rankedBranches;

        /// <summary>Builds the route definition tree and pre-computes ranked branches.</summary>
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

        /// <summary>
        /// Searches for the best-matching route branch for the given path.
        /// </summary>
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

                // Emit this route as its own terminal branch: a pathful parent route
                // can be the match on its own, rendering with an empty Outlet, when no child / index
                // matches. The root ("/") is emitted too so "/" resolves to it. Among equal-scoring
                // branches the index / deeper child outranks the bare parent via ComputeScore.
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
                // Root and index routes contribute no segments to the matchable pattern.
                yield break;
            }

            var trimmed = path.TrimStart('/').TrimEnd('/');
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
                    // A splat is a tail-only catch-all. A splat in any non-terminal
                    // position (e.g. "a/*/b") is rejected at definition time so it can never silently
                    // swallow the segments that follow it.
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
            // An index leaf outranks its bare pathful parent at the same depth.
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
            var captured = new Dictionary<string, string>();

            if (!TryConsume(branch.Pattern, 0, segments, 0, captured))
            {
                return false;
            }

            matches = BuildMatches(branch.Chain, captured);
            return true;
        }

        /// <summary>
        /// Recursively consumes the flattened pattern against the path segments, supporting optional and
        /// splat segments. Optional segments branch into "present" / "absent" attempts; a splat consumes
        /// the remaining path tail.
        /// </summary>
        private static bool TryConsume(
            List<RouteSegment>? pattern, int pi, string[] segments, int si, Dictionary<string, string> captured)
        {
            if (pattern == null) return false;
            while (pi < pattern.Count)
            {
                var seg = pattern[pi];

                if (seg.IsSplat)
                {
                    // Splat captures the (possibly empty) remaining tail and must be terminal.
                    var rest = si >= segments.Length
                        ? string.Empty
                        : string.Join("/", segments, si, segments.Length - si);
                    captured["*"] = rest;
                    return true;
                }

                if (seg.IsOptional)
                {
                    // Greedily try to match the optional segment. The capture must not leak into the
                    // skip branch (or sibling ranking attempts) when the downstream match fails, so
                    // snapshot the param key around the "present" attempt and restore it on failure.
                    if (si < segments.Length)
                    {
                        var keyExisted = false;
                        string snap = "";
                        if (seg.IsParam)
                        {
                            keyExisted = captured.TryGetValue(seg.Value, out snap);
                        }

                        if (TryMatchSingle(seg, segments[si], captured) &&
                            TryConsume(pattern, pi + 1, segments, si + 1, captured))
                        {
                            return true;
                        }

                        if (seg.IsParam)
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
                    }

                    // Skip the optional segment without consuming a path segment.
                    return TryConsume(pattern, pi + 1, segments, si, captured);
                }

                if (si >= segments.Length || !TryMatchSingle(seg, segments[si], captured))
                {
                    return false;
                }

                pi++;
                si++;
            }

            // Pattern exhausted: succeed only when the path is also fully consumed.
            return si == segments.Length;
        }

        private static bool TryMatchSingle(RouteSegment seg, string pathSeg, Dictionary<string, string> captured)
        {
            if (seg.IsParam)
            {
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
            // Resolved (params-substituted) URL pathname accumulated down the chain, without a leading
            // slash. Drives route-relative `..` resolution: each level records its cumulative resolved
            // pathname so a `..` can drop a whole route's contribution at once.
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

                // Each level receives the params captured by its own and ancestor segments (the cumulative
                // param set is exposed at every level), so share the captured dictionary.
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
        /// Resolves a route's own pattern (<see cref="ComputeMatchedPath"/>) into its concrete URL
        /// contribution by substituting captured params, expanding the splat tail, and dropping absent
        /// optional segments. Returns the empty string for the root (<c>/</c>) and index (<c>""</c>)
        /// routes, which contribute no URL segments. The returned string carries no leading/trailing slash.
        /// </summary>
        private static string ResolveRouteSegments(RouteDefinition route, IReadOnlyDictionary<string, string> captured)
        {
            if (string.IsNullOrEmpty(route.Path) || route.Path == "/" || route.Path == "")
            {
                return string.Empty;
            }

            var pattern = route.Path.TrimStart('/').TrimEnd('/');
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
                    // Splat captures the (possibly empty, possibly multi-segment) remaining tail.
                    if (captured.TryGetValue("*", out var splat) && splat.Length > 0)
                    {
                        resolved.Add(splat);
                    }
                    continue;
                }

                if (part.Length > 0 && part[0] == ':')
                {
                    var name = part.Substring(1);
                    // A present param (including a matched optional one) substitutes its captured value;
                    // an absent optional param simply contributes nothing.
                    if (captured.TryGetValue(name, out var value) && value.Length > 0)
                    {
                        resolved.Add(value);
                    }
                    continue;
                }

                // Literal segment (optional literals that were skipped do not appear in captured, but a
                // matched literal always contributes itself).
                resolved.Add(part);
            }

            return string.Join("/", resolved);
        }

        private static string AppendRouteId(string parentId, RouteDefinition route)
        {
            // Index routes (path="") and the root ("/") would otherwise collide with their parent's id,
            // so disambiguate them explicitly. Other levels append their own (trimmed) path segment.
            if (route.Path == "")
            {
                return parentId.Length == 0 ? "/?index" : parentId + "/?index";
            }

            if (route.Path == "/")
            {
                return "/";
            }

            var segment = route.Path?.TrimStart('/').TrimEnd('/') ?? string.Empty;
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

            return route.Path?.TrimStart('/').TrimEnd('/') ?? string.Empty;
        }

        #endregion

        private static string[] NormalizePath(string path)
        {
            if (path == "/" || path == "")
            {
                return Array.Empty<string>();
            }

            var trimmed = path.TrimStart('/').TrimEnd('/');
            return trimmed.Length == 0 ? Array.Empty<string>() : trimmed.Split('/');
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Velvet
{
    /// <summary>
    /// Ranks route branches as React Router's <c>flattenRoutes</c> and <c>rankRouteBranches</c> do: by
    /// <c>computeScore</c> over the branch's joined path, then siblings by declaration, sorted as V8 sorts, with
    /// each child's branch flattened ahead of its parent's. A path's optional segments (<c>:param?</c>,
    /// <c>segment?</c>) each make a branch with and without the segment; <c>*</c> is a splat.
    /// </summary>
    public sealed class RouteTree
    {
        private readonly RouteDefinition[] _routes;
        private readonly List<RouteBranch> _rankedBranches;

        /// <param name="routes">Array of route definitions. null is not allowed.</param>
        /// <exception cref="ArgumentException">Two routes share an id; an index route declares children, an
        /// empty array included; a splat route declares children; or an absolute child path does not start
        /// with the joined path of the routes above it.</exception>
        public RouteTree(RouteDefinition[] routes)
        {
            _routes = routes ?? throw new ArgumentNullException(nameof(routes));
            _rankedBranches = new List<RouteBranch>();
            FlattenBranches(BuildNodes(_routes, null), new List<RouteMeta>(), string.Empty, false, _rankedBranches);
            JsArraySort.Sort(_rankedBranches, RankComparison);
        }

        private static readonly Comparison<RouteBranch> RankComparison = RankRouteBranches;

        // React Router's rankRouteBranches comparator, with compareIndexes.
        private static int RankRouteBranches(RouteBranch a, RouteBranch b)
        {
            if (a.Score != b.Score)
            {
                return b.Score - a.Score;
            }
            // Siblings share every route above the last, and the tree refuses two routes one id.
            var left = a.Ids;
            var right = b.Ids;
            if (left.Length != right.Length)
            {
                return 0;
            }
            var shared = 0;
            while (shared < left.Length - 1 && left[shared] == right[shared])
            {
                shared++;
            }
            return shared == left.Length - 1 ? a.Position - b.Position : 0;
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

        /// <summary>
        /// The chain a navigation to a path no route matches commits: the one route a 404 renders at, whose
        /// boundary shows the error. It is the only top-level route when there is one, else the first that is
        /// an index route or has a null, empty or <c>/</c> path, else a route of no element.
        /// </summary>
        internal IReadOnlyList<RouteMatch> NotFoundMatches => _notFoundMatches ??= BuildNotFoundMatches(_routes);

        private IReadOnlyList<RouteMatch>? _notFoundMatches;

        #region Branch flattening

        private const string NotFoundShimId = "__shim-error-route__";

        private readonly struct RouteNode
        {
            public readonly RouteDefinition Route;
            public readonly string Id;
            public readonly int Position;
            public readonly RouteNode[] Children;

            public RouteNode(RouteDefinition route, string id, int position, RouteNode[] children)
            {
                Route = route;
                Id = id;
                Position = position;
                Children = children;
            }
        }

        private readonly struct RouteMeta
        {
            public readonly RouteNode Node;
            public readonly string RelativePath;

            public RouteMeta(RouteNode node, string relativePath)
            {
                Node = node;
                RelativePath = relativePath;
            }
        }

        private sealed class RouteBranch
        {
            public readonly RouteDefinition[] Chain;
            public readonly string[] Ids;
            public readonly List<RouteSegment> Pattern;

            /// <summary>How many of <see cref="Pattern"/>'s segments each chain entry contributed, so a
            /// match reads its slice rather than parsing the route's path again.</summary>
            public readonly int[] SegmentCounts;

            /// <summary>The last chain entry's position among its siblings, React Router's
            /// <c>childrenIndex</c> of the leaf.</summary>
            public readonly int Position;

            public int Score;

            public RouteBranch(
                RouteDefinition[] chain, string[] ids, List<RouteSegment> pattern, int[] segmentCounts,
                int position)
            {
                Chain = chain;
                Ids = ids;
                Pattern = pattern;
                SegmentCounts = segmentCounts;
                Position = position;
            }
        }

        private readonly struct RouteSegment
        {
            public readonly string Value;
            public readonly bool IsParam;
            public readonly bool IsSplat;
            public readonly bool CaseSensitive;

            public RouteSegment(string value, bool isParam, bool isSplat, bool caseSensitive)
            {
                Value = value;
                IsParam = isParam;
                IsSplat = isSplat;
                CaseSensitive = caseSensitive;
            }
        }

        private readonly HashSet<string> _ids = new();

        // React Router's convertRoutesToDataRoutes, which runs over the whole tree before flattenRoutes: an
        // id is the route's place in the declaration, however many branches its optional segments make.
        private RouteNode[] BuildNodes(RouteDefinition[] routes, string? parentPath)
        {
            var nodes = new RouteNode[routes.Length];
            for (var position = 0; position < routes.Length; position++)
            {
                var route = routes[position];
                var treePath = parentPath == null
                    ? position.ToString(CultureInfo.InvariantCulture)
                    : string.Concat(parentPath, "-", position.ToString(CultureInfo.InvariantCulture));
                var id = route.Id ?? treePath;
                if (route.Index && route.Children != null)
                {
                    throw new ArgumentException(
                        $"Cannot specify children on an index route: route \"{id}\" is an index route and declares "
                        + "a child list. Move the children beside it, under its parent.");
                }
                if (!_ids.Add(id))
                {
                    throw new ArgumentException(
                        $"Found a route id collision on id \"{id}\". Route ids must be unique; give the routes "
                        + "that share it distinct Id values.");
                }
                nodes[position] = new RouteNode(
                    route, id, position,
                    route.Children == null ? Array.Empty<RouteNode>() : BuildNodes(route.Children, treePath));
            }

            return nodes;
        }

        // React Router's flattenRoutes.
        private void FlattenBranches(
            RouteNode[] nodes, List<RouteMeta> parents, string parentPath, bool hasParentOptionalSegments,
            List<RouteBranch> output)
        {
            foreach (var node in nodes)
            {
                var path = node.Route.Path;
                if (string.IsNullOrEmpty(path) || !path.Contains('?'))
                {
                    FlattenRoute(node, null, parents, parentPath, hasParentOptionalSegments, output);
                    continue;
                }
                foreach (var exploded in ExplodeOptionalSegments(path))
                {
                    FlattenRoute(node, exploded, parents, parentPath, true, output);
                }
            }
        }

        private void FlattenRoute(
            RouteNode node, string? explodedPath, List<RouteMeta> parents, string parentPath,
            bool hasParentOptionalSegments, List<RouteBranch> output)
        {
            var route = node.Route;
            var relativePath = explodedPath ?? route.Path ?? string.Empty;
            if (relativePath.StartsWith("/", StringComparison.Ordinal))
            {
                if (!relativePath.StartsWith(parentPath, StringComparison.Ordinal))
                {
                    // A variant an optional segment made, on this route or above it, can leave a path this
                    // route was not written under, and that variant is dropped.
                    if (hasParentOptionalSegments)
                    {
                        return;
                    }
                    throw new ArgumentException(
                        $"Absolute route path \"{relativePath}\" nested under path \"{parentPath}\" is not valid. "
                        + "An absolute child route path must start with the combined path of all its parent routes.");
                }
                relativePath = relativePath.Substring(parentPath.Length);
            }

            var joined = JoinPaths(parentPath, relativePath);
            parents.Add(new RouteMeta(node, relativePath));

            if (node.Children.Length > 0)
            {
                // The refusal inside ParseRouteSegments is over one route's own path, and a branch is
                // every ancestor's segments joined. A splat stops the walk where it matches, so a child
                // segment behind one is never compared: the branch outscores its splat-only parent and
                // wins for every path the parent would have taken, which is the swallowing the
                // one-path refusal is worded against.
                if (EndsWithSplat(relativePath))
                {
                    throw new ArgumentException(
                        $"Splat segment '*' must be the last segment of a route path, but route "
                        + $"'{route.Path}' declares {node.Children.Length} child route(s) behind it. "
                        + "A splat takes the whole tail, so nothing under it can be reached as "
                        + "declared. Move the children beside the splat route rather than under it.");
                }

                FlattenBranches(node.Children, parents, joined, hasParentOptionalSegments, output);
            }

            // A route with no path is reached only through a child, unless it is an index route.
            if (route.Path != null || route.Index)
            {
                output.Add(BuildBranch(parents, joined, route.Index));
            }

            parents.RemoveAt(parents.Count - 1);
        }

        // React Router's explodeOptionalSegments.
        private static List<string> ExplodeOptionalSegments(string path)
        {
            var result = new List<string>();
            var slash = path.IndexOf('/');
            var first = slash < 0 ? path : path.Substring(0, slash);
            var isOptional = first.EndsWith("?", StringComparison.Ordinal);
            var required = isOptional ? first.Substring(0, first.Length - 1) : first;

            if (slash < 0)
            {
                result.Add(required);
                if (isOptional)
                {
                    result.Add(string.Empty);
                }
            }
            else
            {
                var restExploded = ExplodeOptionalSegments(path.Substring(slash + 1));
                foreach (var subpath in restExploded)
                {
                    result.Add(subpath.Length == 0 ? required : required + "/" + subpath);
                }
                if (isOptional)
                {
                    result.AddRange(restExploded);
                }
            }

            if (path.StartsWith("/", StringComparison.Ordinal))
            {
                for (var index = 0; index < result.Count; index++)
                {
                    if (result[index].Length == 0)
                    {
                        result[index] = "/";
                    }
                }
            }

            return result;
        }

        // React Router's joinPaths over two parts: joined by "/", every run of slashes made one. Where neither part
        // holds a run of its own, the run can only be where they meet, so the parts are joined around it.
        private static string JoinPaths(string parentPath, string relativePath)
        {
            if (parentPath.IndexOf("//", StringComparison.Ordinal) < 0
                && relativePath.IndexOf("//", StringComparison.Ordinal) < 0)
            {
                var head = parentPath.EndsWith("/", StringComparison.Ordinal) ? parentPath : parentPath + "/";
                return relativePath.StartsWith("/", StringComparison.Ordinal)
                    ? string.Concat(head, relativePath.Substring(1))
                    : string.Concat(head, relativePath);
            }
            var joined = string.Concat(parentPath, "/", relativePath);
            var collapsed = new StringBuilder(joined.Length);
            foreach (var character in joined)
            {
                if (character != '/' || collapsed.Length == 0 || collapsed[collapsed.Length - 1] != '/')
                {
                    collapsed.Append(character);
                }
            }

            return collapsed.ToString();
        }

        private static bool EndsWithSplat(string path)
        {
            var last = false;
            foreach (var seg in ParseRouteSegments(path, caseSensitive: false))
            {
                last = seg.IsSplat;
            }

            return last;
        }

        private static RouteBranch BuildBranch(List<RouteMeta> metas, string path, bool index)
        {
            var pattern = new List<RouteSegment>();
            var counts = new int[metas.Count];
            var chain = new RouteDefinition[metas.Count];
            var ids = new string[metas.Count];
            for (var level = 0; level < metas.Count; level++)
            {
                var meta = metas[level];
                var before = pattern.Count;
                pattern.AddRange(ParseRouteSegments(meta.RelativePath, meta.Node.Route.CaseSensitive));
                counts[level] = pattern.Count - before;
                chain[level] = meta.Node.Route;
                ids[level] = meta.Node.Id;
            }

            return new RouteBranch(chain, ids, pattern, counts, metas[metas.Count - 1].Node.Position)
            {
                Score = ComputeScore(path, index),
            };
        }

        private static IEnumerable<RouteSegment> ParseRouteSegments(string path, bool caseSensitive)
        {
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

                    yield return new RouteSegment("*", isParam: false, isSplat: true, caseSensitive);
                    continue;
                }

                yield return part.StartsWith(":", StringComparison.Ordinal)
                    ? new RouteSegment(part.Substring(1), isParam: true, isSplat: false, caseSensitive)
                    : new RouteSegment(part, isParam: false, isSplat: false, caseSensitive);
            }
        }

        #endregion

        #region Scoring

        private const int StaticSegmentScore = 10;
        private const int DynamicSegmentScore = 3;
        private const int IndexRouteScore = 2;
        private const int SplatPenalty = -2;
        private const int EmptySegmentScore = 1;

        // React Router's computeScore over the branch's joined path, read segment by segment in place of
        // path.split("/").
        private static int ComputeScore(string path, bool index)
        {
            var score = index ? IndexRouteScore : 0;
            var splat = false;
            var start = 0;
            while (start <= path.Length)
            {
                var end = path.IndexOf('/', start);
                end = end < 0 ? path.Length : end;
                var segment = path.AsSpan(start, end - start);
                score++;
                if (segment.Length == 1 && segment[0] == '*')
                {
                    splat = true;
                }
                else
                {
                    score += IsParamSegment(segment)
                        ? DynamicSegmentScore
                        : segment.Length == 0 ? EmptySegmentScore : StaticSegmentScore;
                }
                start = end + 1;
            }

            return splat ? score + SplatPenalty : score;
        }

        // React Router's /^:[\w-]+$/, whose \w is ASCII.
        private static bool IsParamSegment(ReadOnlySpan<char> segment)
        {
            var at = segment.Length > 1 && segment[0] == ':' ? 1 : segment.Length + 1;
            while (at < segment.Length && IsWordCharacter(segment[at]))
            {
                at++;
            }
            return at == segment.Length;
        }

        private static bool IsWordCharacter(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_' or '-';

        #endregion

        #region Branch matching

        private static bool TryMatchBranch(RouteBranch branch, string[] segments, out List<RouteMatch>? matches)
        {
            matches = null;
            Dictionary<string, string>? captured = null;
            var pattern = branch.Pattern;
            var si = 0;
            var splatTaken = false;
            for (var pi = 0; pi < pattern.Count && !splatTaken; pi++)
            {
                var seg = pattern[pi];
                if (seg.IsSplat)
                {
                    // MUTANT_SURVIVES(equivalent): at si == Length the join is asked for zero
                    // elements and returns the empty string, which is what the other arm yields.
                    var rest = si >= segments.Length
                        ? string.Empty
                        : string.Join("/", segments, si, segments.Length - si);
                    captured ??= new Dictionary<string, string>();
                    captured["*"] = rest;
                    si = segments.Length;
                    splatTaken = true;
                    continue;
                }

                if (si >= segments.Length || !TryMatchSingle(seg, segments[si], ref captured))
                {
                    return false;
                }

                si++;
            }

            if (si != segments.Length)
            {
                return false;
            }

            // A paramless successful match still exposes a (shared, empty) dictionary at every level,
            // preserving the pre-lazy contract that RouteMatch.Params is never null.
            matches = BuildMatches(branch, segments, captured ?? new Dictionary<string, string>());
            return true;
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

        private static List<RouteMatch> BuildMatches(
            RouteBranch branch, string[] segments, Dictionary<string, string> captured)
        {
            var chain = branch.Chain;
            var matches = new List<RouteMatch>(chain.Length);
            // Each level stores a cumulative base because route-relative `..` pops a whole route level,
            // not one URL segment.
            var cumulativeResolved = string.Empty;
            // A branch's pattern is each route's segments in chain order, and every segment before a splat
            // took the path segment at its own index, so a route's slice starts where the routes above it ended.
            var patternOffset = 0;

            for (var level = 0; level < chain.Length; level++)
            {
                var route = chain[level];

                var resolvedSegment = ResolveRouteSegments(
                    branch.Pattern, branch.SegmentCounts[level], segments, captured, ref patternOffset);
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
                    RouteId = branch.Ids[level],
                });
            }

            return matches;
        }

        // Read off the branch's already-parsed pattern rather than the route's path. Two readings of one
        // path do not agree segment for segment -- one drops the empty part in `a//b` and the other keeps
        // it -- and the pattern is the reading the URL's segments line up with.
        private static string ResolveRouteSegments(
            List<RouteSegment> pattern, int count, string[] segments, IReadOnlyDictionary<string, string> captured,
            ref int patternOffset)
        {
            var resolved = ScratchSegments;
            resolved.Clear();

            for (var step = 0; step < count; step++)
            {
                var index = patternOffset++;
                var seg = pattern[index];

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

                // The URL's own text rather than the pattern's, which a case-insensitive literal can differ
                // from, as React Router's pathname is the URL's.
                resolved.Add(segments[index]);
            }

            return string.Join("/", resolved);
        }

        // One list for every level of every match. BuildMatches reads the string this returns before it
        // asks for the next level, so nothing outlives a call. Static because RouteTree runs no caller
        // code between filling it and joining it.
        private static readonly List<string> ScratchSegments = new();

        private static IReadOnlyList<RouteMatch> BuildNotFoundMatches(RouteDefinition[] routes)
        {
            var position = -1;
            if (routes.Length == 1)
            {
                position = 0;
            }
            else
            {
                for (var index = 0; index < routes.Length && position < 0; index++)
                {
                    if (routes[index].Index || string.IsNullOrEmpty(routes[index].Path) || routes[index].Path == "/")
                    {
                        position = index;
                    }
                }
            }

            var route = position < 0 ? new RouteDefinition() : routes[position];
            return new[]
            {
                new RouteMatch
                {
                    Route = route,
                    Params = new Dictionary<string, string>(),
                    MatchedPath = ComputeMatchedPath(route),
                    RouteId = position < 0 ? NotFoundShimId : route.Id ?? position.ToString(CultureInfo.InvariantCulture),
                },
            };
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

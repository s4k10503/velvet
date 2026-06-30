using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Velvet.SourceGenerators.PurityAnalysis
{
    internal enum KnownPurity
    {
        Pure,
        Impure,
    }

    /// <summary>
    /// Dictionary that statically returns Pure / Impure for frequently used .NET BCL / Unity APIs.
    /// Not exhaustive — a sample set curated to avoid false classifications.
    /// </summary>
    internal static class KnownPurityDatabase
    {
        private static readonly SymbolDisplayFormat FullyQualifiedFormat = new(
            globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
            typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
            genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
            miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes);

        private static readonly HashSet<string> ImpureContainingTypes = new(StringComparer.Ordinal)
        {
            "System.Console",
            "System.IO.File",
            "System.IO.Directory",
            "System.Random",
            "System.Diagnostics.Stopwatch",
            "UnityEngine.Debug",
            "UnityEngine.PlayerPrefs",
            "UnityEngine.Random",
            "UnityEngine.Time",
            "UnityEngine.Application",
        };

        private static readonly HashSet<string> ImpureContainingTypePrefixes = new(StringComparer.Ordinal)
        {
            "System.Net.Http.",
        };

        private static readonly HashSet<string> ImpureMembers = new(StringComparer.Ordinal)
        {
            "System.DateTime.Now",
            "System.DateTime.UtcNow",
            "System.DateTime.Today",
            "System.DateTimeOffset.Now",
            "System.DateTimeOffset.UtcNow",
            "System.Guid.NewGuid",
            "System.Environment.TickCount",
            "System.Environment.TickCount64",
            "System.Threading.Tasks.Task.Run",
            "System.Threading.Tasks.Task.Delay",
            "System.Threading.Thread.Sleep",
            "System.Text.StringBuilder.Clear",
            "System.GC.Collect",
        };

        private static readonly HashSet<string> ImpureMemberPrefixes = new(StringComparer.Ordinal)
        {
            "System.Text.StringBuilder.Append",
            "System.Text.StringBuilder.Insert",
            "System.Text.StringBuilder.Replace",
            "System.Text.StringBuilder.Remove",
            "UnityEngine.Object.Destroy",
            "UnityEngine.Object.DestroyImmediate",
            "UnityEngine.Object.Instantiate",
        };

        private static readonly HashSet<string> PureContainingTypes = new(StringComparer.Ordinal)
        {
            "System.Math",
            "System.MathF",
            "System.Linq.Enumerable",
            "System.Linq.Queryable",
        };

        private static readonly HashSet<string> PureMembers = new(StringComparer.Ordinal)
        {
            "string.Substring",
            "string.ToUpper",
            "string.ToUpperInvariant",
            "string.ToLower",
            "string.ToLowerInvariant",
            "string.Trim",
            "string.TrimStart",
            "string.TrimEnd",
            "string.Replace",
            "string.Split",
            "string.Concat",
            "string.Join",
            "string.Format",
            "string.IsNullOrEmpty",
            "string.IsNullOrWhiteSpace",
            "string.Equals",
            "string.Contains",
            "string.StartsWith",
            "string.EndsWith",
            "string.IndexOf",
            "string.LastIndexOf",
            "string.PadLeft",
            "string.PadRight",
        };

        public static bool TryClassify(IMethodSymbol method, out KnownPurity kind)
        {
            if (method is null)
            {
                kind = default;
                return false;
            }

            var containingTypeName = method.ContainingType?.ToDisplayString(FullyQualifiedFormat) ?? string.Empty;
            var memberName = string.IsNullOrEmpty(containingTypeName)
                ? method.Name
                : containingTypeName + "." + method.Name;

            if (ImpureMembers.Contains(memberName) || HasPrefix(memberName, ImpureMemberPrefixes))
            {
                kind = KnownPurity.Impure;
                return true;
            }

            if (ImpureContainingTypes.Contains(containingTypeName) || HasPrefix(containingTypeName, ImpureContainingTypePrefixes))
            {
                kind = KnownPurity.Impure;
                return true;
            }

            if (PureMembers.Contains(memberName))
            {
                kind = KnownPurity.Pure;
                return true;
            }

            if (PureContainingTypes.Contains(containingTypeName))
            {
                kind = KnownPurity.Pure;
                return true;
            }

            kind = default;
            return false;
        }

        public static bool IsImpureProperty(IPropertySymbol property)
        {
            if (property is null)
            {
                return false;
            }
            var containing = property.ContainingType?.ToDisplayString(FullyQualifiedFormat) ?? string.Empty;
            var member = string.IsNullOrEmpty(containing)
                ? property.Name
                : containing + "." + property.Name;
            if (ImpureMembers.Contains(member) || HasPrefix(member, ImpureMemberPrefixes))
            {
                return true;
            }
            // Properties on a wholly-impure type read nondeterministic ambient state (e.g. UnityEngine.Time.deltaTime,
            // UnityEngine.Application.isPlaying). Mirror the method path's containing-type check so such reads are not
            // silently treated as pure.
            return ImpureContainingTypes.Contains(containing) || HasPrefix(containing, ImpureContainingTypePrefixes);
        }

        private static bool HasPrefix(string value, HashSet<string> prefixes)
        {
            foreach (var prefix in prefixes)
            {
                if (value.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }
}

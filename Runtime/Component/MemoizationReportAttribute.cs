using System;
using System.Collections.Generic;

namespace Velvet
{
    /// <summary>
    /// Written onto a <c>[Component]</c> method by the build-time compiler transform to record what it made of the
    /// body, so that a component cached in part can be inspected as readily as one cached whole. Do not apply by
    /// hand.
    /// </summary>
    /// <remarks>
    /// The memoization guide owns the reading of the fields and the ways to list them.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class MemoizationReportAttribute : Attribute
    {
        /// <summary>The key of the <c>AssemblyMetadata</c> an assembly carries to have what is left uncached reported as build warnings.</summary>
        public const string MetadataKey = "Velvet.MemoizationReport";

        /// <summary>The value of that <c>AssemblyMetadata</c>.</summary>
        public const string WarnValue = "warn";

        private static readonly string[] NoBailouts = Array.Empty<string>();

        /// <param name="bodyGate">Whether the body as a whole is keyed on its props and hook values.</param>
        /// <param name="scopes">How many parts of the body are cached on their own, apart from the body as a whole.</param>
        /// <param name="bailouts">One line per thing left uncached, with the reason, separated by line feeds.</param>
        public MemoizationReportAttribute(bool bodyGate, int scopes, string bailouts)
        {
            BodyGate = bodyGate;
            Scopes = scopes;
            Bailouts = string.IsNullOrEmpty(bailouts) ? NoBailouts : bailouts.Split('\n');
        }

        /// <summary>Whether the body as a whole returns its cached tree when its props and hook values compare equal.</summary>
        public bool BodyGate { get; }

        /// <summary>How many parts of the body are cached on their own, each keyed on only the values it reads.</summary>
        public int Scopes { get; }

        /// <summary>One entry per thing left uncached, each naming it and the reason; empty when nothing was left out.</summary>
        public IReadOnlyList<string> Bailouts { get; }
    }
}

using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Velvet.SourceGenerators.AutoDeps
{
    /// <summary>
    /// Shared syntax helpers for a deps-comparing hook's deps argument. Used from both the analyzer (to
    /// extract the identifier set) and the CodeFix (to mutate the initializer). Keeps the supported-form
    /// switch in one place so analyzer and CodeFix never disagree on what shapes count as analyzable.
    /// </summary>
    internal static class UseEffectDepsSyntax
    {
        /// <summary>
        /// Returns the <see cref="InitializerExpressionSyntax"/> of a deps argument when it is one
        /// of the supported array-creation forms (<c>new[] { ... }</c> / <c>new T[] { ... }</c>),
        /// or null for any other shape (variable reference, null literal, sized array without initializer, …).
        /// </summary>
        public static InitializerExpressionSyntax? TryGetInitializer(ExpressionSyntax depsExpr) =>
            depsExpr switch
            {
                ImplicitArrayCreationExpressionSyntax impl => impl.Initializer,
                ArrayCreationExpressionSyntax arr => arr.Initializer,
                _ => null,
            };

        /// <summary>
        /// Collects the dependency element expressions from a deps argument list. Handles both the array
        /// literal form at a single slot (<c>UseEffect(factory, new[]{a})</c>) and loose trailing arguments on
        /// a <c>params</c> hook (<c>UseCallback(cb, a, b)</c>). Returns null when no element shape is
        /// recognizable so the caller can fall back to silence (false-negative-tolerant). A single non-literal
        /// deps argument (e.g. a variable reference) is unanalyzable and yields null.
        /// </summary>
        public static IReadOnlyList<ExpressionSyntax>? TryGetDepsElements(
            IReadOnlyList<ArgumentSyntax> depsArgs,
            bool depsAreParams)
        {
            if (depsArgs.Count == 0)
            {
                return null;
            }

            // Single argument: only the explicit `new[] { ... }` form is analyzable. A lone non-literal argument
            // is ambiguous — for a fixed-arity hook it is the deps array passed by reference, and for a params
            // hook it is C#'s "pass the array directly" form (`UseCallback(cb, deps)` where deps is object[]),
            // not a single loose dependency. Both are unanalyzable, so bail to stay false-positive-free.
            if (depsArgs.Count == 1)
            {
                var expr = depsArgs[0].Expression;
                var initializer = TryGetInitializer(expr);
                if (initializer != null)
                {
                    return initializer.Expressions;
                }
                // An EMPTY deps array written without an initializer — `new object[0]` or `Array.Empty<object>()`
                // — is analyzable: it declares zero dependencies, so every captured value is missing. (A sized
                // array with non-zero/unknown length stays unanalyzable to remain false-positive-free.)
                if (IsEmptyDepsArray(expr))
                {
                    return System.Array.Empty<ExpressionSyntax>();
                }
                return null;
            }

            // Multiple arguments only occur on a params hook (the fixed-arity hooks accept exactly one deps
            // argument). Each trailing argument is one dependency element.
            var elements = new List<ExpressionSyntax>(depsArgs.Count);
            foreach (var arg in depsArgs)
            {
                elements.Add(arg.Expression);
            }
            return elements;
        }

        /// <summary>
        /// True for the two initializer-less EMPTY deps-array forms: <c>new T[0]</c> (a single rank with a
        /// literal size of 0) and <c>Array.Empty&lt;T&gt;()</c>. Both denote zero dependencies and are therefore
        /// analyzable as such. A non-zero or non-literal size (<c>new T[n]</c>) is not matched.
        /// </summary>
        private static bool IsEmptyDepsArray(ExpressionSyntax expr)
        {
            if (expr is ArrayCreationExpressionSyntax { Initializer: null } arr
                && arr.Type.RankSpecifiers.Count == 1
                && arr.Type.RankSpecifiers[0].Sizes.Count == 1
                && arr.Type.RankSpecifiers[0].Sizes[0] is LiteralExpressionSyntax { Token.ValueText: "0" })
            {
                return true;
            }

            // Array.Empty<T>() / System.Array.Empty<T>(): a zero-arg invocation of a generic member named "Empty"
            // whose receiver is `Array` (bare or qualified).
            if (expr is InvocationExpressionSyntax { ArgumentList.Arguments.Count: 0 } inv
                && inv.Expression is MemberAccessExpressionSyntax { Name: GenericNameSyntax { Identifier.ValueText: "Empty" } } member
                && ReceiverIsArray(member.Expression))
            {
                return true;
            }

            return false;
        }

        private static bool ReceiverIsArray(ExpressionSyntax receiver) =>
            receiver switch
            {
                IdentifierNameSyntax { Identifier.ValueText: "Array" } => true,
                MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Array" } => true,
                _ => false,
            };
    }
}

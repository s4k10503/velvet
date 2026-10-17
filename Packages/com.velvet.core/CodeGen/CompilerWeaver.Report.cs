using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Unity.CompilationPipeline.Common.Diagnostics;

namespace Velvet.CodeGen
{
    internal static partial class CompilerWeaver
    {
        // Why a [Component] is left unwoven: one value for each way TryAnalyze refuses a body.
        private enum Bailout
        {
            None,
            NoReturn,
            HookResultDiscarded,
            HookResultUnsound,
            HookResultUnsupported,
            NothingToKeyOn,
            PropsBailOwnsIt,
            ReturnAmongHooks,
            ConditionalHook,
            NoOpaqueBracket,
            HookInProtectedRegion,
        }

        private static string Describe(Bailout reason) => reason switch
        {
            Bailout.NoReturn => "the body has no return",
            Bailout.HookResultDiscarded => "a hook's result is discarded, so a reactive input would go uncompared",
            Bailout.HookResultUnsound =>
                "a hook's result is read in a way a comparison by reference cannot stand for (a whole tuple, a later element, a ref local)",
            Bailout.HookResultUnsupported => "a hook's result is consumed by a shape the weaver does not recognise",
            Bailout.NothingToKeyOn => "it has neither a parameter nor a value hook to key a cache on",
            Bailout.PropsBailOwnsIt => "[Component(Memoize = true)] already bails on its props, and it calls no hook",
            Bailout.ReturnAmongHooks => "a return stands ahead of the last hook call",
            Bailout.ConditionalHook => "a hook is skipped or repeated by a branch",
            Bailout.NoOpaqueBracket =>
                "a call ahead of the last hook may run a hook, and the Velvet assembly declares no bracket for it",
            Bailout.HookInProtectedRegion => "a try / catch / finally overlaps the hook section or a return",
            _ => "no reason recorded",
        };

        // One warning for each [Component] method that has a body, when the assembly asks for them: the reason a body is left unwoven, or what
        // was woven into it, so that a partly optimised component can be told from a fully optimised one.
        private static void Report(List<DiagnosticMessage> diagnostics, MethodDefinition method, string what)
        {
            diagnostics.Add(new DiagnosticMessage
            {
                DiagnosticType = DiagnosticType.Warning,
                MessageData = $"Velvet auto-memoization of '{method.FullName}': {what}",
            });
        }

        private static string DescribeWeave(HookAnalysis analysis, int scopes, bool scopesDropped, List<string> refusals)
        {
            var parts = new List<string> { scopes == 1 ? "woven with 1 scope" : $"woven with {scopes} scopes" };
            // MUTANT_SURVIVES(unreachable): the flag is set only after the stack check failed, which the planned scopes never cause.
            if (scopesDropped) parts.Add("scopes dropped because the weave failed the stack check");
            if (refusals.Count > 0)
            {
                var reasons = refusals.GroupBy(reason => reason).OrderByDescending(group => group.Count()).ThenBy(group => group.Key, System.StringComparer.Ordinal)
                    .Select(group => group.Count() == 1 ? group.Key : $"{group.Key} (x{group.Count()})");
                parts.Add($"{refusals.Count} child/derived expression(s) left uncached: {string.Join(", ", reasons)}");
            }
            if (analysis.Volatile) parts.Add("its gate never hits, because it reaches a hook no comparison stands for");
            if (analysis.OpaqueCalls.Count > 0) parts.Add($"{analysis.OpaqueCalls.Count} call(s) ahead of the gate bracketed");
            return string.Join("; ", parts);
        }
    }
}

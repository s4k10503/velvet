using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Velvet.CodeGen
{
    internal sealed partial class ReactiveScopeRegions
    {
        private readonly List<(int Start, int Call)> _hostCalls = new();
        private readonly HashSet<int> _hostFed = new();

        private void IndexHostCalls()
        {
            for (var i = 0; i < _flow.Instructions.Length; i++)
            {
                var instruction = _flow.Instructions[i];
                if (!_flow.Reached(i) || !ReactiveScopeVolatility.IsCall(instruction)) continue;
                if (!CompilerWeaver.MountsIntoHost((MethodReference)instruction.Operand)) continue;
                var first = OperandStart(i);
                _hostCalls.Add((first, i));
                // MUTANT_SURVIVES(equivalent, boundary): the instruction at `i` is the call, which is not an `ldloc`.
                for (var read = first; read < i; read++)
                {
                    if (_flow.Instructions[read].OpCode.Code == Code.Ldloc) _hostFed.Add(((VariableDefinition)_flow.Instructions[read].Operand).Index);
                }
            }
        }

        // A run that builds what a portal or world-space node mounts, directly or through a local the node reads.
        private string? CheckHost(int start, int end)
        {
            foreach (var (first, call) in _hostCalls)
            {
                if (first <= start && end < call) return "it is a child of a portal or world-space node";
            }
            var next = SkipNops(end + 1);
            // MUTANT_SURVIVES(unreachable, boundary): next reaches the instruction count only where nothing but nops follows the call.
            // ReactiveScopeSelectionTests.Given_EveryComponentBodyThisAssemblyHolds_When_TheFlowWalkReadsIt_Then_NoInstructionIsUnreachedAndNoneFallsOffTheEnd
            // pins that no component body of Velvet.Tests.Hooks.Editor ends on a non-terminating instruction.
            var stored = next < _flow.Instructions.Length && _flow.Instructions[next].OpCode.Code == Code.Stloc
                && _hostFed.Contains(((VariableDefinition)_flow.Instructions[next].Operand).Index);
            return stored ? "it is a child of a portal or world-space node" : null;
        }
    }
}

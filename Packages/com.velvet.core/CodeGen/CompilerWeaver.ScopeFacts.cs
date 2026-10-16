using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Velvet.CodeGen
{
    internal static partial class CompilerWeaver
    {
        // The variables a scope may read, and where the body touches each, gathered once per method. A variable is
        // a local, named by the local itself.
        private sealed class ScopeFacts
        {
            private readonly HookKeys _keys;
            private readonly Dictionary<object, List<int>> _writes = new();
            private readonly HashSet<object> _hookKeys;
            private readonly Dictionary<Instruction, int> _indexOf = new();

            public ScopeFacts(MethodBody body, Instruction[] instructions, int[] depths, HookAnalysis analysis, int boundary)
            {
                Instructions = instructions;
                Depths = depths;
                Boundary = boundary;
                _keys = analysis.Keys;
                _hookKeys = analysis.HookCaptures.Select(capture => capture.Local).OfType<object>().ToHashSet();
                for (var i = 0; i < instructions.Length; i++) _indexOf[instructions[i]] = i;
                Branches = instructions
                    .SelectMany((instr, index) => JumpTargets(instr).Select(target => (Source: index, Target: _indexOf[target])))
                    .ToList();
                CollectWrites();
            }

            public Instruction[] Instructions { get; }

            public int[] Depths { get; }

            public int Boundary { get; }

            public List<(int Source, int Target)> Branches { get; }

            // Every write there is to a variable, so that a scope can tell whether it stands as it was set.
            // Taking an address counts as one.
            private void CollectWrites()
            {
                for (var i = 0; i < Instructions.Length; i++)
                {
                    var key = WrittenKey(i);
                    if (key == null) continue;
                    if (!_writes.TryGetValue(key, out var list)) _writes[key] = list = new List<int>();
                    list.Add(i);
                }
            }

            private object? WrittenKey(int index)
            {
                var instr = Instructions[index];
                switch (instr.Operand)
                {
                    case VariableDefinition local when !IsClosureClass(local.VariableType):
                        return instr.OpCode.Code is Code.Stloc or Code.Ldloca ? local : null;
                    default:
                        return null;
                }
            }

            public DepSource ParameterDependency(ParameterDefinition parameter)
                => new(parameter, parameter.ParameterType) { Parameter = parameter };

            // A variable reads as itself, set once.
            public bool CanBeCompared(object key, TypeReference type)
            {
                if (_keys.Uncomparable.Contains(key) || !IsBoxable(type)) return false;
                return _hookKeys.Contains(key)
                    && !(_writes.TryGetValue(key, out var list) && list.Any(write => write > Boundary));
            }

        }
    }
}

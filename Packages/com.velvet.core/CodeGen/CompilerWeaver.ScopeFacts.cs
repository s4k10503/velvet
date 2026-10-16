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
            private enum Access
            {
                None,
                Read,
                Store,
            }

            private readonly MethodBody _body;
            private readonly HookKeys _keys;
            private readonly Dictionary<VariableDefinition, (int First, int Last)> _spans;
            private readonly Dictionary<object, List<int>> _writes = new();
            private readonly HashSet<object> _hookKeys;
            private readonly HashSet<object> _gateKeys = new();
            private readonly Dictionary<VariableDefinition, bool[]> _liveIn = new();
            private readonly Dictionary<Instruction, int> _indexOf = new();

            public ScopeFacts(MethodBody body, Instruction[] instructions, int[] depths, HookAnalysis analysis, int boundary)
            {
                _body = body;
                Instructions = instructions;
                Depths = depths;
                Boundary = boundary;
                _keys = analysis.Keys;
                _hookKeys = analysis.HookCaptures.Select(capture => capture.Local).OfType<object>().ToHashSet();
                for (var i = 0; i < instructions.Length; i++) _indexOf[instructions[i]] = i;
                _spans = instructions.Select((instr, index) => (Local: instr.Operand as VariableDefinition, Index: index))
                    .Where(touch => touch.Local != null).GroupBy(touch => touch.Local!)
                    .ToDictionary(group => group.Key, group => (group.Min(touch => touch.Index), group.Max(touch => touch.Index)));
                Branches = instructions
                    .SelectMany((instr, index) => JumpTargets(instr).Select(target => (Source: index, Target: _indexOf[target])))
                    .ToList();
                CollectWrites();
                CollectGateKeys(analysis);
            }

            public Instruction[] Instructions { get; }

            public ModuleDefinition Module => _body.Method.Module;

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
                        return instr.OpCode.Code == Code.Stloc
                            || (instr.OpCode.Code == Code.Ldloca && !ReadsInPlace(Instructions[index + 1], local.VariableType))
                            ? local
                            : null;
                    default:
                        return null;
                }
            }

            // What a scope reads that the gate compares as well: the component's parameters and each hook value the
            // gate copies. A scope reading all of them misses where the gate does, and is left to it.
            private void CollectGateKeys(HookAnalysis analysis)
            {
                foreach (var parameter in _body.Method.Parameters) _gateKeys.Add(parameter);
                foreach (var capture in analysis.HookCaptures)
                {
                    var key = capture.Local;
                    if (key == null) _gateKeys.Add(new object());
                    else if (!_keys.Uncomparable.Contains(key)) _gateKeys.Add(key);
                }
            }

            // Whether the value stored by the instruction at `consumer` is what the method returns: the `ret` itself, or
            // the temporary a build without optimisation returns through (`stloc; br; ldloc; ret`).
            public bool FlowsIntoReturn(int consumer)
            {
                var instr = Instructions[consumer];
                if (instr.OpCode.Code == Code.Ret) return true;
                // MUTANT_SURVIVES(unreachable, boundary): a body ends in ret, so a store is never its last instruction.
                if (instr.OpCode.Code != Code.Stloc || consumer + 1 >= Instructions.Length) return false;
                var next = Instructions[consumer + 1];
                var load = next.OpCode.Code == Code.Br ? (Instruction)next.Operand : next;
                return load.OpCode.Code == Code.Ldloc && ReferenceEquals(load.Operand, instr.Operand)
                    && load.Next?.OpCode.Code == Code.Ret;
            }

            public bool CoversGate(List<DepSource> deps) => _gateKeys.IsSubsetOf(deps.Select(dep => dep.Key));

            public DepSource ParameterDependency(ParameterDefinition parameter)
                => new(parameter, parameter.ParameterType) { Parameter = parameter };

            // A variable reads as itself, set once, or as a value type.
            public bool CanBeCompared(object key, TypeReference type, ElementRegion region)
            {
                if (_keys.Uncomparable.Contains(key) || !IsBoxable(type)) return false;
                if (IsValueLike(type)) return true;
                return _hookKeys.Contains(key)
                    && !(_writes.TryGetValue(key, out var list) && list.Any(write => write > Boundary));
            }

            // A local whose value the expression neither takes from outside nor leaves for what runs after it: one
            // no instruction outside touches, or one that is stored before it is read on every path through the
            // expression and read again only after another store (Roslyn gives the temporaries of two statements one
            // slot). The expression can run or be skipped without a later read seeing the difference.
            public bool IsPrivateTo(VariableDefinition local, ElementRegion region)
            {
                if (_spans.TryGetValue(local, out var span) && span.First >= region.Start && span.Last <= region.End) return true;
                var live = LiveIn(local);
                return !live[region.Start] && !live[region.End + 1];
            }

            // Whether a read of the local can be reached from each instruction before a store to it. A store
            // ends the walk; taking the address counts as a read, since the callee may read through it.
            private bool[] LiveIn(VariableDefinition local)
            {
                if (_liveIn.TryGetValue(local, out var known)) return known;
                var live = new bool[Instructions.Length + 1];
                for (var changed = true; changed;)
                {
                    changed = false;
                    // MUTANT_SURVIVES(equivalent, boundary): no region lies in a loop, so no read a region asks about is reached from a jump back to instruction zero.
                    for (var i = Instructions.Length - 1; i >= 0; i--)
                    {
                        var access = AccessOf(Instructions[i], local);
                        var value = access == Access.Read || (access == Access.None && Successors(i).Any(next => live[next]));
                        changed |= value != live[i];
                        live[i] = value;
                    }
                }
                // MUTANT_SURVIVES(equivalent): the cache holds what the walk above would compute again.
                _liveIn[local] = live;
                return live;
            }

            private IEnumerable<int> Successors(int index)
            {
                if (Instructions[index].OpCode.FlowControl is not (FlowControl.Branch or FlowControl.Return or FlowControl.Throw))
                {
                    yield return index + 1;
                }
                foreach (var target in JumpTargets(Instructions[index])) yield return _indexOf[target];
            }

            // `ldloca; initobj` is how `default(T?)` reaches a call: a store of the whole value, through the address.
            private static Access AccessOf(Instruction instr, VariableDefinition local)
            {
                if (!ReferenceEquals(instr.Operand, local)) return Access.None;
                return instr.OpCode.Code switch
                {
                    Code.Stloc => Access.Store,
                    Code.Ldloca when instr.Next?.OpCode.Code == Code.Initobj => Access.Store,
                    _ => Access.Read,
                };
            }
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Velvet.CodeGen
{
    internal static partial class CompilerWeaver
    {
        // The variables a scope may read, and where the body touches each, gathered once per method. A variable is
        // a local or a field of a display class, named by the local itself or by the field's definition.
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
            private readonly HashSet<object> _outputs = new();
            private readonly Dictionary<FieldDefinition, ParameterDefinition> _parameterFields = new();
            private readonly HashSet<string> _closureTypes;
            private readonly HashSet<FieldDefinition> _lambdaWritten;
            private readonly Dictionary<MethodDefinition, HashSet<FieldDefinition>?> _reads = new();
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
                _hookKeys = analysis.HookCaptures.Select(CaptureKey).OfType<object>().ToHashSet();
                for (var i = 0; i < instructions.Length; i++) _indexOf[instructions[i]] = i;
                _spans = instructions.Select((instr, index) => (Local: instr.Operand as VariableDefinition, Index: index))
                    .Where(touch => touch.Local != null).GroupBy(touch => touch.Local!)
                    .ToDictionary(group => group.Key, group => (group.Min(touch => touch.Index), group.Max(touch => touch.Index)));
                Branches = instructions
                    .SelectMany((instr, index) => JumpTargets(instr).Select(target => (Source: index, Target: _indexOf[target])))
                    .ToList();
                _closureTypes = body.Variables.Where(local => IsClosureClass(local.VariableType))
                    .Select(local => ElementOf(local.VariableType).FullName).ToHashSet();
                _lambdaWritten = FieldsWrittenByLambdas(body.Method);
                CollectWrites();
                CollectParameterFields();
                CollectGateKeys(analysis);
            }

            public Instruction[] Instructions { get; }

            public ModuleDefinition Module => _body.Method.Module;

            public int[] Depths { get; }

            public int Boundary { get; }

            public List<(int Source, int Target)> Branches { get; }

            private static object? CaptureKey(HookCapture capture)
                => capture.Local ?? (capture.Field == null ? null : FieldKey(capture.Field));

            // Every write there is to a variable, so that a scope can tell whether it stands as it was set.
            // Taking an address is one unless the callee is known to read.
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
                    // MUTANT_SURVIVES(equivalent, clause removed): no scope looks up a link field, so counting its stores changes no answer.
                    case FieldReference field when IsClosureField(field) && !IsParentLink(field):
                        return instr.OpCode.Code == Code.Stfld
                            || (instr.OpCode.Code == Code.Ldflda && !ReadsInPlace(Instructions[index + 1], field.FieldType))
                            ? FieldKey(field)
                            : null;
                    default:
                        return null;
                }
            }

            // `ldloc display; ldarg n; stfld field` is how a captured parameter is hoisted. The field then holds the
            // argument for as long as nothing writes either, and reading it is reading the parameter.
            private void CollectParameterFields()
            {
                var assigned = new HashSet<ParameterDefinition>(Instructions
                    .Where(instr => instr.OpCode.Code is Code.Starg or Code.Starg_S).Select(instr => (ParameterDefinition)instr.Operand));
                for (var i = 2; i < Instructions.Length; i++)
                {
                    if (Instructions[i].OpCode.Code != Code.Stfld || Instructions[i].Operand is not FieldReference field
                        || IsParentLink(field) || Instructions[i - 1].Operand is not ParameterDefinition parameter
                        || Instructions[i - 1].OpCode.Code != Code.Ldarg || assigned.Contains(parameter)
                        || Instructions[i - 2].Operand is not VariableDefinition receiver || !IsClosureClass(receiver.VariableType))
                    {
                        continue;
                    }
                    if (TryResolve(() => field.Resolve()) is { } definition && WriteCount(definition) == 1
                        && !_lambdaWritten.Contains(definition))
                    {
                        _parameterFields[definition] = parameter;
                    }
                }
            }

            // What a scope reads that the gate compares as well: the component's parameters and each hook value the
            // gate copies. A scope reading all of them misses where the gate does, and is left to it.
            private void CollectGateKeys(HookAnalysis analysis)
            {
                foreach (var parameter in _body.Method.Parameters) _gateKeys.Add(parameter);
                foreach (var capture in analysis.HookCaptures)
                {
                    var key = CaptureKey(capture);
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

            public void AcceptOutput(object key) => _outputs.Add(key);

            public int WriteCount(object key) => _writes.TryGetValue(key, out var list) ? list.Count : 0;

            // The variable an instruction stores its whole value into.
            public object? StoredKey(int index)
            {
                var instr = Instructions[index];
                switch (instr.Operand)
                {
                    case VariableDefinition local when instr.OpCode.Code == Code.Stloc && !IsClosureClass(local.VariableType):
                        return local;
                    case FieldReference field when instr.OpCode.Code == Code.Stfld && IsClosureField(field) && !IsParentLink(field):
                        return FieldKey(field);
                    default:
                        return null;
                }
            }

            public TypeReference TypeOfStoreAt(int index)
            {
                var instr = Instructions[index];
                if (instr.Operand is VariableDefinition local) return local.VariableType;
                var field = (FieldReference)instr.Operand;
                return InCallerTerms(field.FieldType, null, field.DeclaringType as GenericInstanceType)
                    ?? _body.Method.Module.TypeSystem.Object;
            }

            public DepSource ParameterDependency(ParameterDefinition parameter)
                => new(parameter, parameter.ParameterType) { Parameter = parameter };

            public bool IsComponentField(FieldDefinition field) => _closureTypes.Contains(field.DeclaringType.FullName);

            public HashSet<FieldDefinition>? CapturedReadsOf(MethodDefinition lambda)
                => CompilerWeaver.CapturedReadsOf(lambda, _reads);

            // A captured variable as a dependency. A captured parameter is the parameter. Read by the expression
            // itself it has to be a value the scopes compare (see CanBeCompared); read by a lambda it only has to
            // stand as it is once the scope has begun, whatever it holds, because the lambda reads it when it runs.
            public DepSource? FieldDependency(FieldDefinition field, bool direct, ElementRegion region)
            {
                if (_parameterFields.TryGetValue(field, out var parameter))
                {
                    return IsBoxable(parameter.ParameterType) ? ParameterDependency(parameter) : null;
                }
                var receiver = FindClosureLocal(_body, field.DeclaringType);
                var type = receiver == null ? null : InCallerTerms(field.FieldType, null, receiver.VariableType as GenericInstanceType);
                if (receiver == null || type == null || !IsBoxable(type)) return null;
                var comparable = !_lambdaWritten.Contains(field)
                    && (direct ? CanBeCompared(field, type, region) : StoresPrecede(field, region.Start));
                if (!comparable) return null;
                FieldReference reference = receiver.VariableType is GenericInstanceType instance
                    ? new FieldReference(field.Name, field.FieldType, instance)
                    : field;
                return new DepSource(field, type) { Field = reference, Receiver = receiver };
            }

            // A variable reads as itself, set once, or as the output of a scope.
            public bool CanBeCompared(object key, TypeReference type, ElementRegion region)
            {
                if (_keys.Uncomparable.Contains(key) || !IsBoxable(type)) return false;
                if (IsValueLike(type)) return true;
                if (_outputs.Contains(key)) return StoresPrecede(key, region.Start);
                return (_hookKeys.Contains(key) || _keys.Stable.Contains(key))
                    && !(_writes.TryGetValue(key, out var list) && list.Any(write => write > Boundary));
            }

            private bool StoresPrecede(object key, int start)
                // MUTANT_SURVIVES(unreachable, boundary): a write pops a value, so none is the first instruction of a region.
                => !_writes.TryGetValue(key, out var list) || list.All(write => write < start);

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

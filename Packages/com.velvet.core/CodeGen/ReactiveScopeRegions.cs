using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Velvet.CodeGen
{
    // A value a scope reads that was computed outside it.
    internal sealed class ScopeDependency
    {
        public ScopeDependency(string key, TypeReference type)
        {
            Key = key;
            Type = type;
        }

        public string Key { get; }

        public TypeReference Type { get; }

        public ParameterDefinition? Argument { get; init; }

        public VariableDefinition? Local { get; init; }

        // The closure local a field is read through, with the field.
        public VariableDefinition? Holder { get; init; }

        public FieldReference? Field { get; init; }

        // The field or automatic-property getter read off that value, when that is all the run reads of it.
        public MemberReference? Member { get; init; }
    }

    // A run of instructions that builds one VNode from a fixed set of outside values and changes nothing outside.
    internal sealed class ScopeRegion
    {
        public ScopeRegion(int start, int end, Instruction first, Instruction last, TypeReference result)
        {
            Start = start;
            End = end;
            First = first;
            Last = last;
            Result = result;
        }

        public int Start { get; }

        public int End { get; }

        public Instruction First { get; }

        public Instruction Last { get; }

        public TypeReference Result { get; }

        public string Callee { get; init; } = string.Empty;

        public List<ScopeDependency> Dependencies { get; } = new();

        public bool ReturnsFromMethod { get; init; }

        public ScopeRegion? Parent { get; set; }

        public int Slot { get; set; }

        public bool Contains(ScopeRegion other)
            => Start <= other.Start && other.End <= End && (Start != other.Start || End != other.End);
    }

    // How a local is touched by an instruction.
    internal enum LocalAccess
    {
        Read,
        Write,
        Address,
    }

    // Finds the runs of a body that can be cached on their own: the call producing a VNode together with the
    // instructions that build its arguments, where those instructions read only values from outside the run,
    // write only what they made themselves, and neither a hook nor a volatile value takes part.
    internal sealed partial class ReactiveScopeRegions
    {
        private readonly MethodBody _body;
        private readonly ReactiveScopeFlow _flow;
        private readonly ReactiveScopeVolatility _volatility;
        private readonly Dictionary<int, List<(int Index, LocalAccess Kind)>> _accesses = new();
        private readonly Dictionary<string, bool> _vnodeTypes = new();
        private readonly int _tailStart;
        private readonly string _vnodeFullName;
        private HashSet<int> _internalLocals = new();

        public ReactiveScopeRegions(MethodBody body, ReactiveScopeFlow flow, ReactiveScopeVolatility volatility,
            int tailStart, string vnodeFullName)
        {
            _body = body;
            _flow = flow;
            _volatility = volatility;
            _tailStart = tailStart;
            _vnodeFullName = vnodeFullName;
            IndexAccesses();
            IndexHostCalls();
        }

        // Why each candidate that was not cached was left alone.
        public List<string> Declined { get; } = new();

        public List<ScopeRegion> Find()
        {
            var found = new List<ScopeRegion>();
            for (var i = _tailStart; i < _flow.Instructions.Length; i++)
            {
                if (!_flow.Reached(i) || !IsVNodeCall(i)) continue;
                var region = TryBuild(i);
                if (region != null) found.Add(region);
            }
            return found;
        }

        private void IndexAccesses()
        {
            for (var i = 0; i < _flow.Instructions.Length; i++)
            {
                var instruction = _flow.Instructions[i];
                if (instruction.Operand is not VariableDefinition local) continue;
                var kind = LocalAccessOf(instruction.OpCode.Code);
                if (kind == null) continue;
                if (!_accesses.TryGetValue(local.Index, out var list)) _accesses[local.Index] = list = new();
                list.Add((i, kind.Value));
            }
        }

        private static LocalAccess? LocalAccessOf(Code code)
        {
            switch (code)
            {
                case Code.Ldloc:
                    return LocalAccess.Read;
                case Code.Stloc:
                    return LocalAccess.Write;
                case Code.Ldloca:
                    return LocalAccess.Address;
                default:
                    return null;
            }
        }

        private bool IsVNodeCall(int index)
        {
            var instruction = _flow.Instructions[index];
            if (instruction.OpCode.Code != Code.Call && instruction.OpCode.Code != Code.Callvirt) return false;
            var callee = (MethodReference)instruction.Operand;
            if (CompilerWeaver.IsHookKind(_volatility.KindAt(index))) return false;
            return DerivesFromVNode(callee.ReturnType);
        }

        private bool DerivesFromVNode(TypeReference type)
        {
            if (type.ContainsGenericParameter || type is ByReferenceType) return false;
            if (_vnodeTypes.TryGetValue(type.FullName, out var known)) return known;
            return _vnodeTypes[type.FullName] = ResolvesToVNode(type);
        }

        private bool ResolvesToVNode(TypeReference type)
        {
            var definition = ScopeResolution.Type(type);
            for (var depth = 0; definition != null && depth < 32; depth++)
            {
                if (definition.FullName == _vnodeFullName) return true;
                definition = definition.BaseType == null ? null : ScopeResolution.Type(definition.BaseType);
            }
            return false;
        }

        private ScopeRegion? TryBuild(int end)
        {
            var plain = ExtendOverEntries(OperandStart(end), end);
            var absorbed = AbsorbSetup(plain, end);
            string? reason = null;
            foreach (var start in absorbed == plain ? new[] { plain } : new[] { absorbed, plain })
            {
                if (start < _tailStart) continue;
                var region = TryBuildFrom(start, end, out reason);
                if (region != null) return region;
            }
            if (reason != null) Declined.Add($"the call to {CalleeName(end)} is not cached: {reason}");
            return null;
        }

        private string CalleeName(int end)
        {
            var callee = (MethodReference)_flow.Instructions[end].Operand;
            return $"{callee.DeclaringType.Name}.{callee.Name} (IL_{_flow.Instructions[end].Offset:X4})";
        }

        // The run is the instructions from `start` to the call at `end`; null, with the reason, where it cannot be cut out.
        private ScopeRegion? TryBuildFrom(int start, int end, out string? reason)
        {
            var callee = (MethodReference)_flow.Instructions[end].Operand;
            _internalLocals = new HashSet<int>();
            reason = Check(start, end);
            var region = new ScopeRegion(start, end, _flow.Instructions[start], _flow.Instructions[end], callee.ReturnType)
            {
                Callee = CalleeName(end),
                ReturnsFromMethod = FlowsToReturn(end),
            };
            reason ??= CollectDependencies(region);
            return reason == null ? region : null;
        }

        // An interpolated string is built in a local by statements that leave the evaluation stack as they found it,
        // ahead of the call that takes the string out of that local. The call's operands do not reach back to them,
        // so a run that starts at the operands would read a local it never set up. The statements just ahead of the
        // run that touch only the locals the run touches belong to it.
        private int AbsorbSetup(int start, int end)
        {
            for (var guard = 0; guard < _flow.Instructions.Length; guard++)
            {
                var previous = PreviousStatement(start);
                if (previous < 0 || !Belongs(previous, start - 1, start, end)) return start;
                start = previous;
            }
            return start;
        }

        // The first instruction of the statement that ends just ahead of `start`, or -1 where what precedes it is
        // not a whole statement at the depth the run begins at.
        private int PreviousStatement(int start)
        {
            if (start == 0 || !_flow.Reached(start)) return -1;
            var depth = _flow.Before[start]!.Length;
            for (var i = start - 1; i >= 0; i--)
            {
                var before = _flow.Before[i];
                if (before == null || before.Length < depth) return -1;
                if (before.Length == depth) return i;
            }
            return -1;
        }

        // A statement belongs to the run when everything it writes is something the run uses, since the run then
        // owns the value; what it only reads becomes a dependency of the run.
        private bool Belongs(int first, int last, int start, int end)
        {
            var written = LocalsIn(first, last, writes: true);
            if (written.Count == 0) return IsOnlyNops(first, last);
            return LocalsIn(start, end, writes: false).IsSupersetOf(written);
        }

        private bool IsOnlyNops(int first, int last)
        {
            for (var i = first; i <= last; i++)
            {
                if (_flow.Instructions[i].OpCode.Code != Code.Nop) return false;
            }
            return true;
        }

        private HashSet<int> LocalsIn(int first, int last, bool writes)
        {
            var locals = new HashSet<int>();
            for (var i = first; i <= last; i++)
            {
                var instruction = _flow.Instructions[i];
                if (instruction.Operand is not VariableDefinition local) continue;
                var kind = LocalAccessOf(instruction.OpCode.Code);
                if (kind != null && (!writes || kind != LocalAccess.Read)) locals.Add(local.Index);
            }
            return locals;
        }

        private string? Check(int start, int end)
            => CheckShape(start, end)
                ?? CheckBranches(start, end)
                ?? CheckHandlers(start, end)
                ?? CheckEffects(start, end)
                ?? CheckLocals(start, end)
                ?? CheckHost(start, end)
                ?? _volatility.RangeReason(start, end);

        private string? CheckShape(int start, int end)
        {
            if (!_flow.Reached(start) || !_flow.Reached(end)) return "it holds code nothing reaches";
            if (_flow.DepthAfter(end) != _flow.Before[start]!.Length + 1) return "it leaves more than its value on the stack";
            for (var i = start; i <= end; i++)
            {
                var problem = ShapeProblem(i, start);
                if (problem != null) return problem;
            }
            return null;
        }

        private string? ShapeProblem(int index, int start)
        {
            if (!_flow.Reached(index)) return "it holds code nothing reaches";
            foreach (var value in _flow.Popped[index])
            {
                if (value.Start < start) return "it takes a value built outside itself off the stack";
            }
            return ForbiddenOpcode(_flow.Instructions[index].OpCode.Code) ? "it contains an instruction that cannot be skipped" : null;
        }

        private static bool ForbiddenOpcode(Code code)
        {
            switch (code)
            {
                case Code.Ret:
                case Code.Throw:
                case Code.Rethrow:
                case Code.Leave:
                case Code.Endfinally:
                case Code.Endfilter:
                case Code.Jmp:
                case Code.Calli:
                case Code.Localloc:
                case Code.Arglist:
                case Code.Mkrefany:
                case Code.Refanyval:
                case Code.Refanytype:
                case Code.Tail:
                    return true;
                default:
                    return false;
            }
        }

        private string? CheckBranches(int start, int end)
        {
            foreach (var (head, tail) in _flow.Loops)
            {
                if (head <= end && tail >= start) return "it sits inside a loop";
            }
            for (var i = 0; i < _flow.Instructions.Length; i++)
            {
                var problem = BranchProblem(i, start, end);
                if (problem != null) return problem;
            }
            return null;
        }

        private string? BranchProblem(int index, int start, int end)
        {
            var inside = index >= start && index <= end;
            foreach (var target in TargetsOf(_flow.Instructions[index]))
            {
                var lands = target >= start && target <= end;
                if (inside && !lands) return "a branch leaves it";
                if (!inside && lands && target != start) return "a branch enters it from outside";
            }
            return null;
        }

        private IEnumerable<int> TargetsOf(Instruction instruction)
        {
            if (instruction.Operand is Instruction one)
            {
                yield return _flow.IndexOf(one);
            }
            else if (instruction.Operand is Instruction[] many)
            {
                foreach (var each in many) yield return _flow.IndexOf(each);
            }
        }

        // A conditional expression is cut across by the branch that chooses its arm: the value on the stack was
        // built from an arm, and the test that chose it sits ahead of the arm. The run starts where that test does.
        private int ExtendOverEntries(int start, int end)
        {
            for (var guard = 0; guard < _flow.Instructions.Length; guard++)
            {
                var entry = EntryFromBefore(start, end);
                if (entry < 0) return start;
                start = System.Math.Min(start, entry);
            }
            return start;
        }

        // Where the expression behind the first branch from ahead of `start` into (start, end] begins, or -1.
        private int EntryFromBefore(int start, int end)
        {
            for (var i = 0; i < start; i++)
            {
                foreach (var target in TargetsOf(_flow.Instructions[i]))
                {
                    if (target > start && target <= end) return OperandStart(i);
                }
            }
            return -1;
        }

        private int OperandStart(int index)
        {
            var start = index;
            foreach (var value in _flow.Popped[index]) start = System.Math.Min(start, value.Start);
            return start;
        }

        // A protected region cannot be entered or left except at its ends. A run that begins right after one ends
        // is fine, since its gate is kept out of the region it follows (see ReactiveScopeInjector), but one that
        // holds a boundary, or begins where a protected region begins, would have its gate on the wrong side of it.
        private string? CheckHandlers(int start, int end)
        {
            foreach (var handler in _body.ExceptionHandlers)
            {
                if (HoldsBeginning(handler, start, end) || HoldsEnding(handler, start, end)
                    || Covers(handler, start) != Covers(handler, end))
                {
                    return "a protected region begins or ends inside it";
                }
            }
            return null;
        }

        private bool HoldsBeginning(ExceptionHandler handler, int start, int end)
        {
            var beginnings = new List<int> { _flow.IndexOf(handler.TryStart), _flow.IndexOf(handler.HandlerStart) };
            if (handler.FilterStart != null) beginnings.Add(_flow.IndexOf(handler.FilterStart));
            return beginnings.Exists(b => b >= start && b <= end);
        }

        private bool HoldsEnding(ExceptionHandler handler, int start, int end)
        {
            var endings = new List<int>();
            if (handler.TryEnd != null) endings.Add(_flow.IndexOf(handler.TryEnd));
            if (handler.HandlerEnd != null) endings.Add(_flow.IndexOf(handler.HandlerEnd));
            return endings.Exists(b => b > start && b <= end);
        }

        private bool Covers(ExceptionHandler handler, int index)
        {
            var inTry = index >= _flow.IndexOf(handler.TryStart)
                && (handler.TryEnd == null || index < _flow.IndexOf(handler.TryEnd));
            var inHandler = index >= _flow.IndexOf(handler.HandlerStart)
                && (handler.HandlerEnd == null || index < _flow.IndexOf(handler.HandlerEnd));
            return inTry || inHandler;
        }

        private string? CheckEffects(int start, int end)
        {
            for (var i = start; i <= end; i++)
            {
                var problem = EffectProblem(i, start);
                if (problem != null) return problem;
            }
            return null;
        }

        private static readonly HashSet<Code> ObjectStores = new()
        {
            Code.Stfld, Code.Stelem_Any, Code.Stelem_I, Code.Stelem_I1, Code.Stelem_I2, Code.Stelem_I4, Code.Stelem_I8,
            Code.Stelem_R4, Code.Stelem_R8, Code.Stelem_Ref,
        };

        private static readonly HashSet<Code> PointerStores = new()
        {
            Code.Stind_I, Code.Stind_I1, Code.Stind_I2, Code.Stind_I4, Code.Stind_I8, Code.Stind_R4, Code.Stind_R8,
            Code.Stind_Ref, Code.Stobj, Code.Cpobj, Code.Initblk, Code.Cpblk,
        };

        private string? EffectProblem(int index, int start)
        {
            var instruction = _flow.Instructions[index];
            var code = instruction.OpCode.Code;
            if (ObjectStores.Contains(code))
            {
                return _flow.Popped[index][0].Fresh >= start ? null : "it writes into an object it did not create";
            }
            if (PointerStores.Contains(code)) return "it writes through a pointer";
            switch (code)
            {
                case Code.Stsfld:
                    return IsLambdaCache((FieldReference)instruction.Operand) ? null : "it writes a static";
                case Code.Starg:
                case Code.Ldarga:
                    return "it writes a parameter or takes its address";
                default:
                    return HookProblem(index);
            }
        }

        private string? HookProblem(int index)
        {
            var instruction = _flow.Instructions[index];
            if (!ReactiveScopeVolatility.IsCall(instruction)) return null;
            return CompilerWeaver.IsHookKind(_volatility.KindAt(index)) ? "it calls a hook" : null;
        }

        private static bool IsLambdaCache(FieldReference field)
        {
            var definition = ScopeResolution.Field(field);
            return definition != null
                && (definition.DeclaringType.Name == "<>c" || definition.Name.StartsWith("<>9__", System.StringComparison.Ordinal));
        }

        private bool FlowsToReturn(int end)
        {
            var next = SkipNops(end + 1);
            if (next >= _flow.Instructions.Length) return false;
            var instruction = _flow.Instructions[next];
            if (instruction.OpCode.Code == Code.Ret) return true;
            if (instruction.OpCode.Code != Code.Stloc) return false;
            return ReloadedThenReturned(next, (VariableDefinition)instruction.Operand);
        }

        // The debug shape: store the result, branch to the next instruction, load it back, return it.
        private bool ReloadedThenReturned(int store, VariableDefinition local)
        {
            var next = SkipNops(store + 1);
            if (next < _flow.Instructions.Length && _flow.Instructions[next].OpCode.Code == Code.Br)
            {
                next = SkipNops(_flow.IndexOf((Instruction)_flow.Instructions[next].Operand));
            }
            if (next + 1 >= _flow.Instructions.Length) return false;
            var load = _flow.Instructions[next];
            return load.OpCode.Code == Code.Ldloc && load.Operand == local
                && _flow.Instructions[SkipNops(next + 1)].OpCode.Code == Code.Ret;
        }

        private int SkipNops(int index)
        {
            while (index < _flow.Instructions.Length && _flow.Instructions[index].OpCode.Code == Code.Nop) index++;
            return index;
        }
    }
}

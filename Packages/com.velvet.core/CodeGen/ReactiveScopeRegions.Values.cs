using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Velvet.CodeGen
{
    internal sealed partial class ReactiveScopeRegions
    {
        // What reads a field or an element off the value and leaves the value as it was.
        private static readonly HashSet<Code> ReadsMember = new()
        {
            Code.Ldfld, Code.Ldelem_Any, Code.Ldelem_I, Code.Ldelem_I1, Code.Ldelem_I2, Code.Ldelem_I4, Code.Ldelem_I8,
            Code.Ldelem_R4, Code.Ldelem_R8, Code.Ldelem_Ref, Code.Ldelem_U1, Code.Ldelem_U2, Code.Ldelem_U4,
        };

        // Bounds the walk that follows a value through the locals and fields it is handed to.
        private const int MaxAliasDepth = 8;

        // The runs that compute what a local is then given: a derived array, a filtered list, a closure. Only a
        // reference the code builds is a candidate; a string, a number or a struct compares by content, so a rebuilt
        // one costs a scope nothing.
        public List<ScopeRegion> FindValues()
        {
            var found = new List<ScopeRegion>();
            for (var i = _tailStart; i < _flow.Instructions.Length; i++)
            {
                if (!_flow.Reached(i) || _flow.Instructions[i].OpCode.Code != Code.Stloc) continue;
                var local = (VariableDefinition)_flow.Instructions[i].Operand;
                if (!CachesAsValue(local.VariableType) || !ComputesValue(i)) continue;
                var region = TryBuild(i - 1, local);
                if (region != null) found.Add(region);
            }
            return found;
        }

        private bool CachesAsValue(TypeReference type)
        {
            if (type.IsValueType || type.IsGenericParameter || type.ContainsGenericParameter) return false;
            if (type.IsByReference || type.IsPointer || type.FullName == "System.String") return false;
            return type is ArrayType || !DerivesFromVNode(type);
        }

        // The value stored at `store` is the result of a call or an allocation, not a variable or a field read as it is.
        private bool ComputesValue(int store)
        {
            for (var i = _flow.Popped[store][0].Start; i < store; i++)
            {
                if (_flow.Instructions[i].OpCode.Code == Code.Newarr || ReactiveScopeVolatility.IsCall(_flow.Instructions[i])) return true;
            }
            return false;
        }

        // The run's value is cached, so the one object is handed to the code after it on every render that hits. That is
        // sound only while nothing writes into it.
        private string? ValueMutatedAfter(VariableDefinition local, int start, int end)
        {
            var seen = new HashSet<int> { local.Index };
            return ReadsOnly(local, start, end, seen, 0);
        }

        private string? ReadsOnly(VariableDefinition local, int start, int end, HashSet<int> seen, int depth)
        {
            if (!_accesses.TryGetValue(local.Index, out var accesses)) return null;
            foreach (var (index, kind) in accesses)
            {
                if ((index >= start && index <= end) || kind == LocalAccess.Write) continue;
                if (kind == LocalAccess.Address) return $"the address of local {local.Index} is taken";
                var problem = ConsumerProblem(_flow.Pushed[index]!, index, seen, depth, local.Index);
                if (problem != null) return problem;
            }
            return null;
        }

        // Why the instruction that takes `value` off the stack may write into it, or null where it only reads.
        private string? ConsumerProblem(StackValue value, int from, HashSet<int> seen, int depth, int origin)
        {
            var consumer = -1;
            for (var j = from + 1; j < _flow.Instructions.Length && consumer < 0; j++)
            {
                if (System.Array.IndexOf(_flow.Popped[j], value) >= 0) consumer = j;
            }
            if (consumer < 0) return $"local {origin} is left on the stack";
            var instruction = _flow.Instructions[consumer];
            if (ReadsMember.Contains(instruction.OpCode.Code))
            {
                return depth >= MaxAliasDepth ? $"local {origin} is passed on too deeply to follow" : ConsumerProblem(_flow.Pushed[consumer]!, consumer, seen, depth + 1, origin);
            }
            switch (instruction.OpCode.Code)
            {
                case Code.Ldlen:
                case Code.Brtrue:
                case Code.Brfalse:
                case Code.Ceq:
                case Code.Cgt_Un:
                    return null;
                case Code.Stloc:
                    return AliasProblem((VariableDefinition)instruction.Operand, seen, depth, origin);
                case Code.Call:
                case Code.Callvirt:
                case Code.Newobj:
                    return CallProblem(consumer, value, origin);
                default:
                    return $"local {origin} is used by {instruction.OpCode.Name}, which may write into it or hand it on";
            }
        }

        private string? AliasProblem(VariableDefinition alias, HashSet<int> seen, int depth, int origin)
        {
            if (!seen.Add(alias.Index)) return null;
            if (depth >= MaxAliasDepth) return $"local {origin} is passed on too deeply to follow";
            return ReadsOnly(alias, -1, -1, seen, depth + 1);
        }

        // A call the build classifies as pure leaves what it is handed as it found it, and so does a static one that
        // returns a value. A call that returns nothing, or is made on the value itself, may be made for what it changes.
        private string? CallProblem(int call, StackValue value, int origin)
        {
            var kind = _volatility.KindAt(call);
            if (kind == CompilerWeaver.CallKind.Pure) return null;
            var callee = (MethodReference)_flow.Instructions[call].Operand;
            var construction = _flow.Instructions[call].OpCode.Code == Code.Newobj;
            var onValue = !construction && callee.HasThis && _flow.Popped[call][0] == value;
            var returnsNothing = !construction && callee.ReturnType.FullName == "System.Void";
            if (kind == CompilerWeaver.CallKind.Plain && !onValue && !returnsNothing) return null;
            return $"local {origin} is handed to {callee.Name}, which may write into it";
        }
    }
}

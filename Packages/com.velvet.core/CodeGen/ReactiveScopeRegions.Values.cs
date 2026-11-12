using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Linq;

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
            // MUTANT_SURVIVES(equivalent, clause removed): a generic parameter reports that it contains one, so `ContainsGenericParameter` holds wherever this clause does.
            // ReactiveScopeValueCandidateTests.Given_AGenericParameter_When_AskedWhetherItContainsOne_Then_ItSaysItDoes
            // pins that for the Cecil the weaver is built against.
            if (type.IsValueType || type.IsGenericParameter || type.ContainsGenericParameter) return false;
            if (type.IsByReference || type.IsPointer || type.FullName == "System.String") return false;
            return type is ArrayType || !DerivesFromVNode(type);
        }

        // The value stored at `store` is the result of a call or an allocation, not a variable or a field read as it is.
        private bool ComputesValue(int store)
        {
            // MUTANT_SURVIVES(equivalent, boundary): the instruction at `store` is the `stloc` that `FindValues` found, which is neither a `newarr` nor a call.
            for (var i = _flow.Popped[store][0].Start; i < store; i++)
            {
                if (_flow.Instructions[i].OpCode.Code == Code.Newarr || ReactiveScopeVolatility.IsCall(_flow.Instructions[i])) return true;
            }
            return false;
        }

        // Only a node, an array of nodes and a list of nodes are walked by the sweep that keeps a cached value's nodes out
        // of the pool, so a value that can hold a node anywhere else is not cached.
        private string? NodeHolderProblem(TypeReference type)
        {
            if (IsNodeList(type)) return null;
            return ReachesNodes(type, new HashSet<string>(), 0) ? "it can hold nodes that nothing keeps out of the pool" : null;
        }

        private bool IsNodeList(TypeReference type)
        {
            if (type is ArrayType array) return array.Rank == 1 && IsNode(array.ElementType);
            return type is GenericInstanceType generic && generic.ElementType.FullName == "System.Collections.Generic.List`1"
                && generic.GenericArguments.Count == 1 && IsNode(generic.GenericArguments[0]);
        }

        private bool IsNode(TypeReference type) => type is not ArrayType && DerivesFromVNode(type);

        private bool ReachesNodes(TypeReference type, HashSet<string> seen, int depth)
        {
            if (type.IsGenericParameter || type.IsPrimitive || type.FullName == "System.String") return false;
            if (IsNode(type) || type.FullName == "System.Object") return true;
            if (depth >= MaxAliasDepth) return true;
            if (!seen.Add(type.FullName)) return false;
            if (type is ArrayType array) return ReachesNodes(array.ElementType, seen, depth + 1);
            if (type is GenericInstanceType generic && generic.GenericArguments.Any(each => ReachesNodes(each, seen, depth + 1)))
            {
                return true;
            }
            return !IsFrameworkType(type.FullName) && FieldsReachNodes(type, seen, depth);
        }

        private bool FieldsReachNodes(TypeReference type, HashSet<string> seen, int depth)
        {
            var definition = ScopeResolution.Type(type);
            if (definition == null) return true;
            if (definition.Fields.Any(field => !field.IsStatic && ReachesNodes(field.FieldType, seen, depth + 1))) return true;
            return definition.BaseType is { } baseType && baseType.FullName != "System.Object" && ReachesNodes(baseType, seen, depth + 1);
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
            // MUTANT_SURVIVES(equivalent, boundary): the search starts after the read at `from`, so a consumer it finds is never the first instruction.
            if (consumer < 0) return $"local {origin} is left on the stack";
            var instruction = _flow.Instructions[consumer];
            if (ReadsMember.Contains(instruction.OpCode.Code))
            {
                return depth >= MaxAliasDepth ? $"local {origin} is passed on too deeply to follow" : ConsumerProblem(_flow.Pushed[consumer]!, consumer, seen, depth + 1, origin);
            }
            switch (instruction.OpCode.Code)
            {
                case Code.Pop:
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
                    return CallProblem(consumer, value, origin, seen, depth);
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

        // A call the build classifies as pure leaves what it is handed as it found it, but what it returns may be the value
        // or part of it, so the result is followed like a field read. Any other call may write into what it is handed, as
        // React Compiler assumes of a function it cannot see into.
        private string? CallProblem(int call, StackValue value, int origin, HashSet<int> seen, int depth)
        {
            if (_volatility.KindAt(call) != CompilerWeaver.CallKind.Pure)
            {
                return $"local {origin} is handed to {((MethodReference)_flow.Instructions[call].Operand).Name}, which may write into it";
            }
            var construction = _flow.Instructions[call].OpCode.Code == Code.Newobj;
            var callee = (MethodReference)_flow.Instructions[call].Operand;
            var declared = construction ? callee.DeclaringType : callee.ReturnType;
            var result = CompilerWeaver.InCallerTerms(declared, callee as GenericInstanceMethod, callee.DeclaringType as GenericInstanceType);
            if (result != null && !CachesAsValue(result)) return null;
            if (depth >= MaxAliasDepth) return $"local {origin} is passed on too deeply to follow";
            return ConsumerProblem(_flow.Pushed[call]!, call, seen, depth + 1, origin);
        }
    }
}

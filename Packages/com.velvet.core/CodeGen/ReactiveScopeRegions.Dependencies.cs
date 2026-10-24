using Mono.Cecil.Cil;
using Mono.Cecil;
using System.Collections.Generic;

namespace Velvet.CodeGen
{
    internal sealed partial class ReactiveScopeRegions
    {
        // A local the run writes must be one the run alone makes use of: what it holds on the way in is
        // overwritten before it is read, and nothing after the run reads what it holds on the way out. Roslyn
        // gives temporaries of different statements one slot, so a slot also touched elsewhere is not a mark
        // against it.
        private string? CheckLocals(int start, int end)
        {
            foreach (var entry in _accesses)
            {
                var inside = entry.Value.FindAll(a => a.Index >= start && a.Index <= end);
                if (inside.Count == 0) continue;
                var problem = LocalProblem(entry.Key, entry.Value, inside, end);
                if (problem != null) return problem;
            }
            return null;
        }

        private string? LocalProblem(int local, List<(int Index, LocalAccess Kind)> all,
            List<(int Index, LocalAccess Kind)> inside, int end)
        {
            var first = Normalize(inside[0].Index, inside[0].Kind);
            var writes = inside.Exists(a => Normalize(a.Index, a.Kind) == LocalAccess.Write);
            if (first == LocalAccess.Read) return writes ? $"it writes local {local} after reading what it held" : null;
            if (!writes) return null;
            if (ReadAfter(all, end)) return $"local {local} holds a value the code after it reads";
            _internalLocals.Add(local);
            return null;
        }

        // An address taken of a local is a write where what receives it sets the local up.
        private LocalAccess Normalize(int index, LocalAccess kind)
        {
            if (kind != LocalAccess.Address) return kind;
            var pushed = _flow.Pushed[index];
            for (var j = index + 1; j < _flow.Instructions.Length; j++)
            {
                if (System.Array.IndexOf(_flow.Popped[j], pushed) < 0) continue;
                return InitializesLocal(_flow.Instructions[j]) ? LocalAccess.Write : LocalAccess.Read;
            }
            return LocalAccess.Read;
        }

        private static bool InitializesLocal(Instruction consumer)
        {
            if (consumer.OpCode.Code == Code.Initobj) return true;
            return consumer.OpCode.Code == Code.Call && consumer.Operand is MethodReference { Name: ".ctor" };
        }

        private bool ReadAfter(List<(int Index, LocalAccess Kind)> all, int end)
        {
            foreach (var (index, kind) in all)
            {
                if (index > end) return Normalize(index, kind) != LocalAccess.Write;
            }
            return false;
        }

        // The reads of values computed outside the run are gathered as dependencies with the member read off the value
        // where that is all the run does with it, and settled into the dependencies of the run once all are seen.
        private string? CollectDependencies(ScopeRegion region)
        {
            var uses = new List<ScopeDependency>();
            for (var i = region.Start; i <= region.End; i++)
            {
                var problem = ReadSource(i, region, uses);
                if (problem != null) return problem;
            }
            return AddDependencies(region, uses);
        }

        private string? ReadSource(int index, ScopeRegion region, List<ScopeDependency> uses)
        {
            var instruction = _flow.Instructions[index];
            switch (instruction.OpCode.Code)
            {
                case Code.Ldarg:
                    var parameter = (ParameterDefinition)instruction.Operand;
                    uses.Add(new ScopeDependency("A" + parameter.Index, parameter.ParameterType)
                    {
                        Argument = parameter,
                        Member = MemberReadAt(index + 1, parameter.ParameterType),
                    });
                    return null;
                case Code.Ldloc:
                case Code.Ldloca:
                    return ReadLocal(index, region, uses);
                default:
                    return null;
            }
        }

        private string? ReadLocal(int index, ScopeRegion region, List<ScopeDependency> uses)
        {
            var instruction = _flow.Instructions[index];
            var local = (VariableDefinition)instruction.Operand;
            if (_internalLocals.Contains(local.Index)) return null;
            if (IsClosure(local.VariableType)) return ReadClosure(index, region, uses, local);
            var member = instruction.OpCode.Code == Code.Ldloc ? MemberReadAt(index + 1, local.VariableType) : null;
            uses.Add(new ScopeDependency("L" + local.Index, local.VariableType) { Local = local, Member = member });
            return null;
        }

        // A closure is read through its fields. Handing the closure on to build a delegate stands for every field
        // of it, since the delegate reads them whenever it is called.
        private string? ReadClosure(int index, ScopeRegion region, List<ScopeDependency> uses, VariableDefinition holder)
        {
            var next = _flow.Instructions[index + 1];
            if (next.OpCode.Code == Code.Ldfld)
            {
                var field = (FieldReference)next.Operand;
                uses.Add(new ScopeDependency("L" + holder.Index + "." + field.Name, field.FieldType)
                {
                    Holder = holder,
                    Field = field,
                    Member = MemberReadAt(index + 2, field.FieldType),
                });
                return null;
            }
            if (next.OpCode.Code != Code.Ldftn && next.OpCode.Code != Code.Ldvirtftn)
            {
                return "it uses a closure for more than reading its fields";
            }
            return ClosureProblem(holder, region.End) ?? ReadEveryClosureField(uses, holder);
        }

        private string? ReadEveryClosureField(List<ScopeDependency> uses, VariableDefinition holder)
        {
            var definition = holder.VariableType.Resolve();
            foreach (var field in definition.Fields)
            {
                if (field.IsStatic) continue;
                var reference = _body.Method.Module.ImportReference(field);
                uses.Add(new ScopeDependency("L" + holder.Index + "." + field.Name, reference.FieldType)
                {
                    Holder = holder,
                    Field = reference,
                });
            }
            return null;
        }

        // The field, or the getter an automatic property declares, read off the value the instruction at `index`
        // finds on the stack, when the value is of a class. What the run reads is then that member and not the object.
        private MemberReference? MemberReadAt(int index, TypeReference sourceType)
        {
            if (index >= _flow.Instructions.Length || sourceType.IsValueType || sourceType.IsGenericParameter) return null;
            var next = _flow.Instructions[index];
            if (next.OpCode.Code == Code.Ldfld && next.Operand is FieldReference field)
            {
                return IsStorable(field.FieldType) ? field : null;
            }
            if (next.OpCode.Code != Code.Call && next.OpCode.Code != Code.Callvirt) return null;
            return next.Operand is MethodReference getter && IsAutomaticGetter(getter) ? getter : null;
        }

        private static bool IsStorable(TypeReference type)
            => !type.IsByReference && !type.IsPointer && !type.ContainsGenericParameter && !IsByRefLike(type);

        private static bool IsAutomaticGetter(MethodReference getter)
        {
            if (!getter.HasThis || getter.Parameters.Count != 0 || getter.HasGenericParameters) return false;
            if (!getter.Name.StartsWith("get_", System.StringComparison.Ordinal) || !IsStorable(getter.ReturnType)) return false;
            var definition = getter.Resolve();
            if (definition == null || definition.IsVirtual) return false;
            foreach (var attribute in definition.CustomAttributes)
            {
                if (attribute.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute") return true;
            }
            return false;
        }

        private static string? AddDependencies(ScopeRegion region, List<ScopeDependency> uses)
        {
            var seen = new HashSet<string>();
            foreach (var use in uses)
            {
                var problem = use.Member != null && AllReadMembers(uses, use.Key)
                    ? AddMember(region, seen, use)
                    : AddWhole(region, seen, use);
                if (problem != null) return problem;
            }
            return null;
        }

        private static bool AllReadMembers(List<ScopeDependency> uses, string key)
            => uses.TrueForAll(each => each.Key != key || each.Member != null);

        private static string? AddWhole(ScopeRegion region, HashSet<string> seen, ScopeDependency use)
        {
            if (use.Argument != null) return AddArgument(region, seen, use.Argument);
            if (use.Local != null) return AddLocal(region, seen, use.Local);
            return AddClosureField(region, seen, use.Holder!, use.Field!);
        }

        private static string? AddMember(ScopeRegion region, HashSet<string> seen, ScopeDependency use)
        {
            var key = use.Key + ":" + use.Member!.Name;
            if (!seen.Add(key)) return null;
            var type = use.Member is FieldReference field ? field.FieldType : ((MethodReference)use.Member).ReturnType;
            region.Dependencies.Add(new ScopeDependency(key, type)
            {
                Argument = use.Argument,
                Local = use.Local,
                Holder = use.Holder,
                Field = use.Field,
                Member = use.Member,
            });
            return null;
        }

        private static string? AddArgument(ScopeRegion region, HashSet<string> seen, ParameterDefinition parameter)
        {
            var key = "A" + parameter.Index;
            if (!seen.Add(key)) return null;
            if (parameter.ParameterType.IsByReference) return "it reads a by-reference parameter";
            region.Dependencies.Add(new ScopeDependency(key, parameter.ParameterType) { Argument = parameter });
            return null;
        }

        private static string? AddLocal(ScopeRegion region, HashSet<string> seen, VariableDefinition local)
        {
            var key = "L" + local.Index;
            if (!seen.Add(key)) return null;
            var type = local.VariableType;
            if (type.IsByReference || type.IsPointer) return "it reads a local that holds a reference";
            if (IsByRefLike(type)) return "it reads a local of a type that cannot be stored in an array";
            if (type is GenericInstanceType generic && generic.ElementType.FullName.StartsWith("System.ValueTuple`", System.StringComparison.Ordinal))
            {
                return "it reads a tuple local, which is compared by element";
            }
            region.Dependencies.Add(new ScopeDependency(key, type) { Local = local });
            return null;
        }

        // A ref struct is built and used in place, and cannot be boxed into the array of dependencies.
        private static bool IsByRefLike(TypeReference type)
        {
            var element = type is GenericInstanceType generic ? generic.ElementType : type;
            var definition = element.Resolve();
            if (definition == null || !definition.IsValueType) return false;
            foreach (var attribute in definition.CustomAttributes)
            {
                if (attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute") return true;
            }
            return false;
        }

        private static bool IsClosure(TypeReference type)
        {
            var element = type is GenericInstanceType generic ? generic.ElementType : type;
            return element.Name.StartsWith("<>c__DisplayClass", System.StringComparison.Ordinal);
        }

        private static string? AddClosureField(ScopeRegion region, HashSet<string> seen, VariableDefinition holder, FieldReference field)
        {
            var key = "L" + holder.Index + "." + field.Name;
            if (!seen.Add(key)) return null;
            region.Dependencies.Add(new ScopeDependency(key, field.FieldType) { Holder = holder, Field = field });
            return null;
        }

        // The delegate shares the closure with the code after it, so a closure that is changed once the delegate
        // exists, by that code or by a lambda, would show the delegate something the cached one never saw.
        private string? ClosureProblem(VariableDefinition holder, int end) => ComputeClosureProblem(holder, end);

        private string? ComputeClosureProblem(VariableDefinition holder, int end)
        {
            if (holder.VariableType is GenericInstanceType) return "it captures a closure of a generic method";
            TypeDefinition? definition = holder.VariableType.Resolve();
            if (definition == null) return "it captures a closure the build cannot read";
            if (definition.Fields.Count == 0) return null;
            if (HasClosureField(definition)) return "it captures a closure that holds another closure";
            if (LambdaWritesClosure(definition)) return "a lambda it captures assigns a variable of its closure";
            return AssignedAfter(holder, end) ? "the code after it assigns a variable its closure holds" : null;
        }

        private static bool HasClosureField(TypeDefinition closure)
        {
            foreach (var field in closure.Fields)
            {
                if (!field.IsStatic && IsClosure(field.FieldType)) return true;
            }
            return false;
        }

        private static bool LambdaWritesClosure(TypeDefinition closure)
        {
            foreach (var method in closure.Methods)
            {
                if (method.IsConstructor || !method.HasBody) continue;
                if (WritesOwnField(method, closure)) return true;
            }
            return false;
        }

        private static bool WritesOwnField(MethodDefinition method, TypeDefinition closure)
        {
            foreach (var instruction in method.Body.Instructions)
            {
                var code = instruction.OpCode.Code;
                if (code != Code.Stfld && code != Code.Ldflda) continue;
                if (instruction.Operand is not FieldReference field || field.DeclaringType.Resolve() != closure) continue;
                if (code == Code.Stfld || !ReadsThroughAddress(instruction.Next)) return true;
            }
            return false;
        }

        // The address of a field of a value type taken only to call one of its members that read it, as
        // `count.ToString()` does.
        private static bool ReadsThroughAddress(Instruction? consumer)
        {
            if (consumer == null || consumer.Operand is not MethodReference callee) return false;
            if (consumer.OpCode.Code != Code.Call && consumer.OpCode.Code != Code.Callvirt) return false;
            var name = callee.Name;
            var reads = name == "ToString" || name == "Equals" || name == "GetHashCode" || name == "CompareTo"
                || name.StartsWith("get_", System.StringComparison.Ordinal);
            return reads && callee.DeclaringType.IsValueType;
        }

        private bool AssignedAfter(VariableDefinition holder, int end)
        {
            var source = "L" + holder.Index;
            for (var i = end + 1; i < _flow.Instructions.Length; i++)
            {
                if (_flow.Instructions[i].OpCode.Code != Code.Stfld || !_flow.Reached(i)) continue;
                if (_flow.Popped[i][0].Source == source) return true;
            }
            return false;
        }
    }
}

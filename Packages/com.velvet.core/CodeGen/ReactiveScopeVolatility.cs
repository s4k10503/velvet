using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Velvet.CodeGen
{
    // Which variables a body writes from something a dependency comparison cannot stand for, found by running the
    // body's data flow to a fixed point. A scope reading one of them cannot be skipped on equal dependencies, since
    // the value it holds changes without its identity doing so. The reading is by variable and over the whole body,
    // not by position: a variable that is volatile anywhere is volatile everywhere.
    internal sealed class ReactiveScopeVolatility
    {
        private readonly ReactiveScopeFlow _flow;
        private readonly CompilerWeaver.CallClassifier _calls;
        private readonly HashSet<string> _containers = new();

        public ReactiveScopeVolatility(ReactiveScopeFlow flow, CompilerWeaver.CallClassifier calls)
        {
            _flow = flow;
            _calls = calls;
        }

        // Keys name a variable: "L3" a local, "A0" a parameter, "L3.count" a field of a closure held in local 3.
        public HashSet<string> Keys { get; } = new();

        public void Solve()
        {
            bool changed;
            do
            {
                changed = false;
                for (var i = 0; i < _flow.Instructions.Length; i++)
                {
                    if (_flow.Reached(i)) changed |= Propagate(i);
                }
            }
            while (changed);
        }

        // Why the instruction makes the value it feeds volatile, or null when it does not.
        public string? ReasonAt(int index)
        {
            var read = ReadKey(index);
            if (read != null && Keys.Contains(read)) return $"reads {read}, which an opaque value or a mutation reaches";
            var instruction = _flow.Instructions[index];
            switch (instruction.OpCode.Code)
            {
                case Code.Ldsfld:
                    return IsConstantStatic((FieldReference)instruction.Operand) ? null : $"reads the mutable static {((FieldReference)instruction.Operand).Name}";
                case Code.Ldflda:
                case Code.Ldsflda:
                case Code.Ldelema:
                    return "takes the address of a field or element";
                case Code.Calli:
                    return "calls through a function pointer";
                case Code.Call:
                case Code.Callvirt:
                case Code.Newobj:
                    return CallReason(instruction);
                default:
                    return null;
            }
        }

        public string? RangeReason(int first, int last)
        {
            for (var i = first; i <= last; i++)
            {
                var reason = ReasonAt(i);
                if (reason != null) return reason;
            }
            return null;
        }

        public bool RangeReads(int first, int last, HashSet<string> keys)
        {
            for (var i = first; i <= last; i++)
            {
                var read = ReadKey(i);
                if (read != null && keys.Contains(read)) return true;
            }
            return false;
        }

        public CompilerWeaver.CallKind KindAt(int index)
        {
            var instruction = _flow.Instructions[index];
            return _calls.Of((MethodReference)instruction.Operand, instruction.OpCode.Code == Code.Newobj);
        }

        public static bool IsCall(Instruction instruction)
            => instruction.OpCode.Code == Code.Call || instruction.OpCode.Code == Code.Callvirt
                || instruction.OpCode.Code == Code.Newobj;

        public string? ReadKey(int index)
        {
            var instruction = _flow.Instructions[index];
            switch (instruction.OpCode.Code)
            {
                case Code.Ldloc:
                case Code.Ldloca:
                    return "L" + ((VariableDefinition)instruction.Operand).Index;
                case Code.Ldarg:
                    return "A" + ((ParameterDefinition)instruction.Operand).Index;
                case Code.Ldfld:
                    return FieldKey(index, ((FieldReference)instruction.Operand).Name);
                default:
                    return null;
            }
        }

        // "L3.count" where the instruction at `index` reaches a field through a closure local, and null otherwise.
        private string? FieldKey(int index, string field)
        {
            var popped = _flow.Popped[index];
            var source = popped.Length > 0 ? popped[0].Source : null;
            return source != null && source[0] == 'L' ? source + "." + field : null;
        }

        private string? CallReason(Instruction instruction)
        {
            var kind = _calls.Of((MethodReference)instruction.Operand, instruction.OpCode.Code == Code.Newobj);
            var name = ((MethodReference)instruction.Operand).Name;
            switch (kind)
            {
                case CompilerWeaver.CallKind.Opaque:
                    return $"calls {name}, whose target the build cannot see";
                case CompilerWeaver.CallKind.Ambient:
                    return $"calls {name}, which reads state it was not handed";
                case CompilerWeaver.CallKind.VolatileHook:
                    return $"takes the result of {name}, which a dependency comparison does not represent";
                default:
                    return null;
            }
        }

        private static bool IsConstantStatic(FieldReference field)
        {
            var definition = field.Resolve();
            if (definition == null) return false;
            var holder = definition.DeclaringType.Name;
            return definition.IsInitOnly || holder == "<>c" || definition.Name.StartsWith("<>9__", System.StringComparison.Ordinal);
        }

        private bool Propagate(int index)
        {
            var instruction = _flow.Instructions[index];
            var changed = PropagateWrite(index, instruction);
            changed |= PropagateAddress(index, instruction);
            if (IsCall(instruction)) changed |= PropagateMutation(index);
            return changed;
        }

        private bool PropagateWrite(int index, Instruction instruction)
        {
            switch (instruction.OpCode.Code)
            {
                case Code.Stloc:
                    return CarryValue("L" + ((VariableDefinition)instruction.Operand).Index, _flow.Popped[index][0], index);
                case Code.Stfld:
                    var target = _flow.Popped[index][0].Source;
                    return target != null && target[0] == 'L'
                        && CarryValue(target + "." + ((FieldReference)instruction.Operand).Name, _flow.Popped[index][1], index);
                case Code.Starg:
                    return Keys.Add("A" + ((ParameterDefinition)instruction.Operand).Index);
                case Code.Ldarga:
                    return Keys.Add("A" + ((ParameterDefinition)instruction.Operand).Index);
                default:
                    return false;
            }
        }

        // The variable takes on whatever the value it is assigned is built from.
        private bool CarryValue(string key, StackValue value, int store)
        {
            var changed = false;
            if (RangeReason(value.Start, store - 1) != null) changed |= Keys.Add(key);
            if (RangeHoldsContainer(value.Start, store - 1)) changed |= _containers.Add(key);
            return changed;
        }

        private bool RangeHoldsContainer(int first, int last)
        {
            for (var i = first; i <= last; i++)
            {
                if (IsCall(_flow.Instructions[i]) && KindAt(i) == CompilerWeaver.CallKind.ContainerHook) return true;
                var read = ReadKey(i);
                if (read != null && _containers.Contains(read)) return true;
            }
            return false;
        }

        // A local whose address is handed to something that is not known to leave it alone.
        private bool PropagateAddress(int index, Instruction instruction)
        {
            var changed = false;
            foreach (var value in _flow.Popped[index])
            {
                if (value.AddressOfLocal < 0) continue;
                if (IsConstruction(instruction)) changed |= CarryConstruction(index, value.AddressOfLocal);
                else if (!ReadsOnly(instruction, index)) changed |= Keys.Add("L" + value.AddressOfLocal);
            }
            return changed;
        }

        private static bool IsConstruction(Instruction consumer)
            => consumer.OpCode.Code == Code.Call && consumer.Operand is MethodReference { Name: ".ctor" };

        // A value type built in place takes on whatever its constructor was handed.
        private bool CarryConstruction(int index, int local)
        {
            var arguments = _flow.Popped[index];
            for (var i = 1; i < arguments.Length; i++)
            {
                if (RangeReason(arguments[i].Start, index - 1) != null) return Keys.Add("L" + local);
            }
            return false;
        }

        private bool ReadsOnly(Instruction consumer, int index)
        {
            switch (consumer.OpCode.Code)
            {
                case Code.Ldfld:
                case Code.Ldobj:
                case Code.Initobj:
                    return true;
                case Code.Call:
                case Code.Callvirt:
                    return KindAt(index) == CompilerWeaver.CallKind.Pure || OfReadOnlyStruct((MethodReference)consumer.Operand);
                default:
                    return false;
            }
        }

        // A member of a readonly struct leaves the struct it is called on as it found it.
        private static bool OfReadOnlyStruct(MethodReference callee)
        {
            var type = callee.DeclaringType.Resolve();
            if (type == null || !type.IsValueType) return false;
            foreach (var attribute in type.CustomAttributes)
            {
                if (attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute") return true;
            }
            return false;
        }

        // A call that is not known to leave its arguments alone may change a stable container reaching it.
        private bool PropagateMutation(int index)
        {
            var kind = KindAt(index);
            if (kind == CompilerWeaver.CallKind.Pure || CompilerWeaver.IsHookKind(kind)) return false;
            var changed = false;
            foreach (var argument in _flow.Popped[index])
            {
                changed |= TaintContainersIn(argument.Start, index - 1);
            }
            return changed;
        }

        private bool TaintContainersIn(int first, int last)
        {
            var changed = false;
            for (var i = first; i <= last; i++)
            {
                var read = ReadKey(i);
                if (read != null && _containers.Contains(read)) changed |= Keys.Add(read);
            }
            return changed;
        }
    }
}

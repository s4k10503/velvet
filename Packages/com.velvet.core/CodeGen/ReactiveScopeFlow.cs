using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Velvet.CodeGen
{
    // One value on the abstract evaluation stack of a body read in instruction order.
    internal sealed class StackValue
    {
        public StackValue(int start)
        {
            Start = start;
        }

        // Index of the first instruction whose result the value is built from.
        public int Start { get; }

        // Index of the newobj / newarr that made the value, or -1 for a value read from somewhere.
        public int Fresh { get; init; } = -1;

        // "L3" or "A0" where the value is exactly a read of that variable, and null for anything computed.
        public string? Source { get; init; }

        // The local whose address the value is, or -1.
        public int AddressOfLocal { get; init; } = -1;
    }

    // Walks a body with its macros simplified once, in instruction order, carrying the evaluation stack as the
    // instructions that built each value. A forward pass is enough because a branch to an earlier instruction
    // is only ever a loop whose head holds an empty stack, which is checked rather than followed.
    internal sealed class ReactiveScopeFlow
    {
        private readonly Dictionary<Instruction, int> _indexOf = new();
        private readonly List<(int Head, int Tail)> _loops = new();

        private ReactiveScopeFlow(MethodBody body)
        {
            Instructions = new Instruction[body.Instructions.Count];
            body.Instructions.CopyTo(Instructions, 0);
            for (var i = 0; i < Instructions.Length; i++) _indexOf[Instructions[i]] = i;
            Before = new StackValue[Instructions.Length][];
            Popped = new StackValue[Instructions.Length][];
            Pushed = new StackValue?[Instructions.Length];
        }

        public Instruction[] Instructions { get; }

        // The stack ahead of each instruction, and null where nothing reaches it.
        public StackValue[]?[] Before { get; }

        public StackValue[][] Popped { get; }

        public StackValue?[] Pushed { get; }

        // True once the walk met a shape it does not model, which leaves every reading unusable.
        public bool Failed { get; private set; }

        public IReadOnlyList<(int Head, int Tail)> Loops => _loops;

        public int IndexOf(Instruction instruction) => _indexOf[instruction];

        public bool Reached(int index) => Before[index] != null;

        public int DepthAfter(int index)
            => Before[index]!.Length - Popped[index].Length + (Pushed[index] == null ? 0 : 1);

        public static ReactiveScopeFlow Run(MethodBody body)
        {
            var flow = new ReactiveScopeFlow(body);
            flow.Walk(body);
            return flow;
        }

        private void Walk(MethodBody body)
        {
            if (Instructions.Length == 0) return;
            Before[0] = System.Array.Empty<StackValue>();
            SeedHandlers(body);
            var heads = LoopHeads();
            for (var i = 0; i < Instructions.Length && !Failed; i++)
            {
                if (Before[i] == null && heads.Contains(i)) Before[i] = System.Array.Empty<StackValue>();
                var stack = Before[i];
                if (stack == null)
                {
                    // MUTANT_SURVIVES(unreachable, line removed): only an instruction nothing reaches gets here.
                    // ReactiveScopeSelectionTests.Given_EveryComponentBodyThisAssemblyHolds_When_TheFlowWalkReadsIt_Then_NoInstructionIsUnreachedAndNoneFallsOffTheEnd
                    // pins that the component bodies of Velvet.Tests.Hooks.Editor hold none.
                    Popped[i] = System.Array.Empty<StackValue>();
                    continue;
                }
                var next = Step(i, stack);
                if (next != null) Join(i + 1, next);
            }
        }

        // The instructions some later branch jumps back to. A loop is entered by jumping over its body to the
        // test, so the head is reached in instruction order only by that backward jump.
        private HashSet<int> LoopHeads()
        {
            var heads = new HashSet<int>();
            for (var i = 0; i < Instructions.Length; i++)
            {
                foreach (var target in TargetsOf(Instructions[i]))
                {
                    // MUTANT_SURVIVES(unreachable, boundary): a head added for a branch to itself matters only to an instruction nothing else reaches.
                    // ReactiveScopeSelectionTests.Given_EveryComponentBodyThisAssemblyHolds_When_TheFlowWalkReadsIt_Then_NoInstructionIsUnreachedAndNoneFallsOffTheEnd
                    // pins that the component bodies of Velvet.Tests.Hooks.Editor hold none.
                    if (target <= i) heads.Add(target);
                }
            }
            return heads;
        }

        private IEnumerable<int> TargetsOf(Instruction instruction)
        {
            if (instruction.Operand is Instruction one)
            {
                yield return _indexOf[one];
            }
            else if (instruction.Operand is Instruction[] many)
            {
                foreach (var each in many) yield return _indexOf[each];
            }
        }

        private void SeedHandlers(MethodBody body)
        {
            foreach (var handler in body.ExceptionHandlers)
            {
                var entersWithException = handler.HandlerType == ExceptionHandlerType.Catch
                    || handler.HandlerType == ExceptionHandlerType.Filter;
                var entry = entersWithException
                    ? new[] { new StackValue(_indexOf[handler.HandlerStart]) }
                    : System.Array.Empty<StackValue>();
                Before[_indexOf[handler.HandlerStart]] = entry;
                if (handler.FilterStart != null) Before[_indexOf[handler.FilterStart]] = entry;
            }
        }

        private StackValue[]? Step(int index, StackValue[] stack)
        {
            var instruction = Instructions[index];
            if (instruction.OpCode.Code == Code.Dup) return StepDup(index, stack);
            var pops = CompilerWeaver.Pops(instruction);
            if (pops > stack.Length) return Fail();
            var popped = new StackValue[pops];
            System.Array.Copy(stack, stack.Length - pops, popped, 0, pops);
            Popped[index] = popped;
            var remaining = new StackValue[stack.Length - pops];
            System.Array.Copy(stack, remaining, remaining.Length);
            if (instruction.OpCode.StackBehaviourPop == StackBehaviour.PopAll) remaining = System.Array.Empty<StackValue>();
            var result = CompilerWeaver.Pushes(instruction) == 0 ? remaining : Append(remaining, Produce(index, popped));
            return Continue(index, result);
        }

        private StackValue[]? StepDup(int index, StackValue[] stack)
        {
            if (stack.Length == 0) return Fail();
            var top = stack[stack.Length - 1];
            Popped[index] = System.Array.Empty<StackValue>();
            var copy = new StackValue(index) { Fresh = top.Fresh };
            // MUTANT_SURVIVES(equivalent, line removed): `Pushed` is read at a call, by `DepthAfter` for the end of a run, and at an `ldloca`, by `Normalize`; a `dup` is neither.
            Pushed[index] = copy;
            return Continue(index, Append(stack, copy));
        }

        private StackValue Produce(int index, StackValue[] popped)
        {
            var start = index;
            foreach (var value in popped) start = System.Math.Min(start, value.Start);
            var instruction = Instructions[index];
            var created = instruction.OpCode.Code == Code.Newobj || instruction.OpCode.Code == Code.Newarr;
            var produced = new StackValue(start)
            {
                Fresh = created ? index : -1,
                Source = SourceOf(instruction),
                AddressOfLocal = instruction.OpCode.Code == Code.Ldloca ? ((VariableDefinition)instruction.Operand).Index : -1,
            };
            Pushed[index] = produced;
            return produced;
        }

        private static string? SourceOf(Instruction instruction)
        {
            switch (instruction.OpCode.Code)
            {
                case Code.Ldloc:
                    return "L" + ((VariableDefinition)instruction.Operand).Index;
                case Code.Ldarg:
                    return "A" + ((ParameterDefinition)instruction.Operand).Index;
                default:
                    return null;
            }
        }

        private StackValue[]? Continue(int index, StackValue[] after)
        {
            var instruction = Instructions[index];
            switch (instruction.OpCode.FlowControl)
            {
                case FlowControl.Branch:
                    Send(index, instruction, after);
                    return null;
                case FlowControl.Cond_Branch:
                    Send(index, instruction, after);
                    return after;
                case FlowControl.Return:
                case FlowControl.Throw:
                    return null;
                default:
                    return after;
            }
        }

        private void Send(int from, Instruction instruction, StackValue[] after)
        {
            if (instruction.Operand is Instruction target)
            {
                Deliver(from, _indexOf[target], after);
                return;
            }
            if (instruction.Operand is not Instruction[] targets) return;
            foreach (var each in targets) Deliver(from, _indexOf[each], after);
        }

        private void Deliver(int from, int target, StackValue[] state)
        {
            if (target > from)
            {
                Join(target, state);
                return;
            }
            // A loop head is entered with nothing on the stack, and so is the jump back to it.
            if (state.Length != 0 || Before[target]!.Length != 0)
            {
                Failed = true;
                return;
            }
            _loops.Add((target, from));
        }

        private void Join(int target, StackValue[] state)
        {
            if (target >= Instructions.Length) return;
            var existing = Before[target];
            if (existing == null)
            {
                Before[target] = state;
                return;
            }
            if (existing.Length != state.Length)
            {
                Failed = true;
                return;
            }
            var merged = new StackValue[existing.Length];
            for (var i = 0; i < merged.Length; i++) merged[i] = Merge(existing[i], state[i]);
            Before[target] = merged;
        }

        private static StackValue Merge(StackValue a, StackValue b)
            => ReferenceEquals(a, b) ? a : new StackValue(System.Math.Min(a.Start, b.Start));

        private StackValue[]? Fail()
        {
            Failed = true;
            return null;
        }

        private static StackValue[] Append(StackValue[] stack, params StackValue[] values)
        {
            var grown = new StackValue[stack.Length + values.Length];
            stack.CopyTo(grown, 0);
            values.CopyTo(grown, stack.Length);
            return grown;
        }
    }
}

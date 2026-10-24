using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Velvet.CodeGen
{
    // Wraps each chosen run in its own gate: the dependencies are read into an array, the cache is asked for the
    // VNode those dependencies built last time, and the run itself executes only when it has none.
    //
    //     deps = [...]; if (TryGetScope(first, n, deps, out cached)) { value = cached; }
    //     else { value = <the run>; StoreScope(first, n, deps, value); }
    //
    // Everything below the run on the evaluation stack stays where it is across the gate, so a run that is the
    // third argument of a call is gated with two values already pushed.
    internal sealed class ReactiveScopeInjector
    {
        private readonly MethodDefinition _method;
        private readonly ILProcessor _il;
        private readonly WeaverContext _context;
        private readonly VariableDefinition _firstSlot;

        public ReactiveScopeInjector(MethodDefinition method, WeaverContext context, VariableDefinition firstSlot)
        {
            _method = method;
            _il = method.Body.GetILProcessor();
            _context = context;
            _firstSlot = firstSlot;
        }

        // Outermost first: a run that shares its first instruction with the run around it is gated after it, so
        // the branch the outer gate takes on a miss lands on the inner gate rather than skipping it.
        public void Gate(IEnumerable<ScopeRegion> regions)
        {
            foreach (var region in regions) GateOne(region);
        }

        private void GateOne(ScopeRegion region)
        {
            var module = _method.Module;
            var dependencies = new VariableDefinition(new ArrayType(module.TypeSystem.Object));
            var cached = new VariableDefinition(_context.VNode);
            var result = new VariableDefinition(module.ImportReference(region.Result));
            _method.Body.Variables.Add(dependencies);
            _method.Body.Variables.Add(cached);
            _method.Body.Variables.Add(result);

            var join = Instruction.Create(OpCodes.Nop);
            var gate = BuildGate(region, dependencies, cached, join);
            Retarget(region.First, gate[0]);
            foreach (var instruction in gate) _il.InsertBefore(region.First, instruction);
            EndProtectedRegionsBefore(region.First, gate[0]);
            InsertCommit(region, dependencies, result, join);
        }

        private List<Instruction> BuildGate(ScopeRegion region, VariableDefinition dependencies, VariableDefinition cached,
            Instruction join)
        {
            var gate = BuildDependencies(region, dependencies);
            gate.Add(Instruction.Create(OpCodes.Ldloc, _firstSlot));
            gate.Add(Instruction.Create(OpCodes.Ldc_I4, region.Slot));
            gate.Add(Instruction.Create(OpCodes.Ldloc, dependencies));
            gate.Add(Instruction.Create(OpCodes.Ldloca, cached));
            gate.Add(Instruction.Create(OpCodes.Call, _context.TryGetScope!));
            gate.Add(Instruction.Create(OpCodes.Brfalse, region.First));
            gate.Add(Instruction.Create(OpCodes.Ldloc, cached));
            if (region.Result.FullName != _context.VNode.FullName)
            {
                gate.Add(Instruction.Create(OpCodes.Castclass, _method.Module.ImportReference(region.Result)));
            }
            gate.Add(Instruction.Create(OpCodes.Br, join));
            return gate;
        }

        private List<Instruction> BuildDependencies(ScopeRegion region, VariableDefinition dependencies)
        {
            var load = new List<Instruction>
            {
                Instruction.Create(OpCodes.Ldc_I4, region.Dependencies.Count),
                Instruction.Create(OpCodes.Newarr, _method.Module.TypeSystem.Object),
            };
            for (var i = 0; i < region.Dependencies.Count; i++)
            {
                load.Add(Instruction.Create(OpCodes.Dup));
                load.Add(Instruction.Create(OpCodes.Ldc_I4, i));
                AppendRead(load, region.Dependencies[i]);
                load.Add(Instruction.Create(OpCodes.Stelem_Ref));
            }
            load.Add(Instruction.Create(OpCodes.Stloc, dependencies));
            return load;
        }

        private static void AppendRead(List<Instruction> load, ScopeDependency dependency)
        {
            if (dependency.Argument != null) load.Add(Instruction.Create(OpCodes.Ldarg, dependency.Argument));
            if (dependency.Local != null) load.Add(Instruction.Create(OpCodes.Ldloc, dependency.Local));
            if (dependency.Holder != null)
            {
                load.Add(Instruction.Create(OpCodes.Ldloc, dependency.Holder));
                load.Add(Instruction.Create(OpCodes.Ldfld, dependency.Field!));
            }
            var type = dependency.Type;
            var boxed = type.IsValueType || type.IsGenericParameter;
            if (dependency.Member == null)
            {
                if (boxed) load.Add(Instruction.Create(OpCodes.Box, type));
                return;
            }
            AppendMemberRead(load, dependency.Member, boxed ? type : null);
        }

        // The member is read off a value that may be null where the run itself would have tested for it first, so a
        // null stands for the member of nothing.
        private static void AppendMemberRead(List<Instruction> load, MemberReference member, TypeReference? boxed)
        {
            var read = member is FieldReference field
                ? Instruction.Create(OpCodes.Ldfld, field)
                : Instruction.Create(OpCodes.Callvirt, (MethodReference)member);
            var done = Instruction.Create(OpCodes.Nop);
            load.Add(Instruction.Create(OpCodes.Dup));
            load.Add(Instruction.Create(OpCodes.Brtrue, read));
            load.Add(Instruction.Create(OpCodes.Pop));
            load.Add(Instruction.Create(OpCodes.Ldnull));
            load.Add(Instruction.Create(OpCodes.Br, done));
            load.Add(read);
            if (boxed != null) load.Add(Instruction.Create(OpCodes.Box, boxed));
            load.Add(done);
        }

        // The run's own instructions follow the gate, so a branch that targeted the run's first instruction from
        // outside it now targets the gate.
        private void Retarget(Instruction from, Instruction to)
        {
            foreach (var instruction in _method.Body.Instructions)
            {
                if (ReferenceEquals(instruction.Operand, from))
                {
                    instruction.Operand = to;
                }
                else if (instruction.Operand is Instruction[] targets)
                {
                    instruction.Operand = System.Array.ConvertAll(targets, t => ReferenceEquals(t, from) ? to : t);
                }
            }
        }

        // A protected region that ended where the run begins ends at the gate now, not after it.
        private void EndProtectedRegionsBefore(Instruction first, Instruction gate)
        {
            foreach (var handler in _method.Body.ExceptionHandlers)
            {
                if (ReferenceEquals(handler.TryEnd, first)) handler.TryEnd = gate;
                if (ReferenceEquals(handler.HandlerEnd, first)) handler.HandlerEnd = gate;
            }
        }

        private void InsertCommit(ScopeRegion region, VariableDefinition dependencies, VariableDefinition result,
            Instruction join)
        {
            var commit = new[]
            {
                Instruction.Create(OpCodes.Stloc, result),
                Instruction.Create(OpCodes.Ldloc, _firstSlot),
                Instruction.Create(OpCodes.Ldc_I4, region.Slot),
                Instruction.Create(OpCodes.Ldloc, dependencies),
                Instruction.Create(OpCodes.Ldloc, result),
                Instruction.Create(OpCodes.Call, _context.StoreScope!),
                Instruction.Create(OpCodes.Ldloc, result),
                join,
            };
            var after = region.Last;
            foreach (var instruction in commit)
            {
                _il.InsertAfter(after, instruction);
                after = instruction;
            }
        }
    }
}

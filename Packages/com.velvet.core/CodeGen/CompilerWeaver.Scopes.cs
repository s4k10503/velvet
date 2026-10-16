using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Velvet.CodeGen
{
    // Reactive scopes: below a component's own gate, an expression is cached on the values it reads, as React
    // Compiler's reactive scopes are, so a render that changes one input builds again what reads it and hands the
    // reconciler the same instances for the rest.
    // A scope is one element of a `VNode[]` initializer (what `params VNode?[] children` and
    // `children: new[] { ... }` both compile to) that is a call or constructor returning a VNode, after the last hook
    // call (anywhere in a body that calls none).
    // It is woven only when everything the expression reads is a value compared where it starts:
    //   A parameter it reads is compared. A local it reads is compared when it is a hook value nothing stores to
    //   after the hook section. A local it writes, or takes the address of, is not compared.
    //   It calls no member of a Ref / MutableRef and no method taking one, and reads no hook value from UseService
    //   or from a custom hook unless value-like: a render reading through one has no value to compare.
    //   It reads no static, and calls only a V factory or a constructor.
    //   Control flow in it stays inside it and moves forward, nothing outside jumps into it, and nothing
    //   outside runs it twice (no loop around it).
    //   The method has no try / catch / finally.
    // What is inspected is the expression's own instructions: the fields it reads and each call it makes by the type
    // it is declared on and by the types of its parameters. A call is not followed into the code it reaches. The
    // target of a delegate the expression hands on (a method group, a lambda capturing nothing), the override a
    // virtual call dispatches to, `Func.Invoke` on a delegate it was given and a BCL generic instantiated over a Ref
    // (`List<Ref<int>>`, whose members name no Ref in their signatures) are all code or values this does not read.
    // An expression reading nothing is cached only when everything it calls is a V factory. One inside a scope
    // is left to it. Anything else is built on every miss of the gate, as before.
    internal static partial class CompilerWeaver
    {
        private static readonly string RefTypeFullName = typeof(Velvet.Ref<>).FullName!;
        private static readonly string MutableRefTypeFullName = typeof(Velvet.MutableRef<>).FullName!;
        private static readonly string FactoryTypeFullName = typeof(Velvet.V).FullName!;

        // What an expression cannot hold and still be re-run from its dependencies alone.
        private static readonly HashSet<Code> ScopeForbiddenCodes = new()
        {
            Code.Calli, Code.Localloc, Code.Jmp, Code.Ret, Code.Throw, Code.Rethrow, Code.Starg,
            Code.Leave, Code.Endfinally, Code.Endfilter, Code.Arglist, Code.Mkrefany, Code.Refanyval,
            Code.Refanytype, Code.Cpblk, Code.Initblk, Code.Tail, Code.Break,
        };

        private readonly record struct ElementRegion(int Start, int End);

        // Where a region came from.
        private sealed record ScopeCandidate(ElementRegion Span);

        // One value an expression reads, loaded again where the expression starts: a parameter or a local.
        private sealed class DepSource
        {
            public DepSource(object key, TypeReference type)
            {
                Key = key;
                Type = type;
            }

            public object Key { get; }

            public TypeReference Type { get; }

            public ParameterDefinition? Parameter { get; init; }

            public VariableDefinition? Local { get; init; }
        }

        private sealed record ScopePlan(ScopeCandidate Candidate, Instruction Start, Instruction Store,
            List<DepSource> Deps, int Index)
        {
            public ElementRegion Region => Candidate.Span;
        }

        // The slot the gate was granted, and the values passing through a scope's gate and store, which no other
        // scope's gate or store runs between.
        private sealed record ScopeLocals(VariableDefinition Slot, VariableDefinition Cached, VariableDefinition Result);

        private static List<ScopePlan> PlanScopes(MethodBody body, HookAnalysis analysis, WeaverContext context)
        {
            var plans = new List<ScopePlan>();
            var instructions = body.Instructions.ToArray();
            var depths = body.HasExceptionHandlers ? null : DepthsBefore(instructions);
            if (depths == null) return plans;
            var boundary = analysis.LastHookBoundary == null ? -1 : System.Array.IndexOf(instructions, analysis.LastHookBoundary);
            var facts = new ScopeFacts(body, instructions, depths, analysis, boundary);
            // MUTANT_SURVIVES(equivalent, boundary): a region beginning at the boundary begins with the hook call, which no scope may hold.
            foreach (var candidate in FindCandidates(facts).Where(each => each.Span.Start > boundary))
            {
                if (TryPlanCandidate(facts, candidate, plans) is not { } plan) continue;
                plans.Add(plan);
            }
            return plans;
        }

        // The stack depth before each instruction, or null where the body disagrees with itself about one: two
        // jumps landing on an instruction at different depths, an instruction taking more than is there, a `ret`
        // finding other than its value. Code no jump enters and nothing falls into starts empty, and a target
        // behind its jump was entered before it.
        private static int[]? DepthsBefore(Instruction[] body)
        {
            var before = new int[body.Length];
            var atTarget = new Dictionary<Instruction, int>();
            var depth = 0;
            // MUTANT_SURVIVES(equivalent, literal): the first instruction is entered at depth zero whether or not it is read as falling in.
            var flowsIn = true;
            for (var i = 0; i < body.Length; i++)
            {
                var instr = body[i];
                var entered = atTarget.TryGetValue(instr, out var reached);
                if (entered && flowsIn && reached != depth) return null;
                depth = entered ? reached : flowsIn ? depth : 0;
                before[i] = depth;
                var pops = Pops(instr);
                if (depth < pops || (instr.OpCode == OpCodes.Ret && depth != 1)) return null;
                depth = instr.OpCode.StackBehaviourPop == StackBehaviour.PopAll ? 0 : depth - pops + Pushes(instr);
                foreach (var target in JumpTargets(instr))
                {
                    if (atTarget.TryGetValue(target, out var seen) && seen != depth) return null;
                    atTarget[target] = depth;
                }
                flowsIn = instr.OpCode.FlowControl is not (FlowControl.Branch or FlowControl.Return or FlowControl.Throw);
            }
            return before;
        }

        private static IEnumerable<Instruction> JumpTargets(Instruction instr)
            => instr.Operand as Instruction[] ?? (instr.Operand is Instruction target ? new[] { target } : System.Array.Empty<Instruction>());

        // Outermost first, so that a scope is always planned before the scopes inside it, and a value a scope stores
        // is a scope's output before the scopes reading it.
        private static List<ScopeCandidate> FindCandidates(ScopeFacts facts)
            => FindElementRegions(facts.Instructions, facts.Depths).Select(span => new ScopeCandidate(span)).ToList();

        // Outermost first, so that a scope is always planned before the scopes inside it. `dup; ldc.i4 index` is what
        // puts an array element's index on the stack in an array initializer: the first element follows the
        // `newarr` naming the element type, and each later one the store of the element before it.
        private static List<ElementRegion> FindElementRegions(Instruction[] instructions, int[] depths)
        {
            var open = new List<(int Start, int Depth, TypeReference? Element)>();
            var elementOf = new Dictionary<int, TypeReference?>();
            var regions = new List<ElementRegion>();
            for (var i = 0; i < instructions.Length; i++)
            {
                if (instructions[i].OpCode == OpCodes.Stelem_Ref) CloseElement(open, regions, i, depths[i]);
                // MUTANT_SURVIVES(unreachable, boundary): a body ends in ret, so no dup; ldc.i4 pair is its last two instructions.
                if (i + 2 >= instructions.Length || instructions[i].OpCode != OpCodes.Dup || instructions[i + 1].OpCode != OpCodes.Ldc_I4) continue;
                // MUTANT_SURVIVES(unreachable, boundary): a dup at the first instruction would duplicate nothing, so none is there to read behind.
                var before = i > 0 ? instructions[i - 1] : instructions[i];
                elementOf[depths[i]] = before.OpCode == OpCodes.Newarr ? (TypeReference)before.Operand
                    : before.OpCode == OpCodes.Stelem_Ref ? elementOf.GetValueOrDefault(depths[i]) : null;
                open.Add((i + 2, depths[i + 2], elementOf[depths[i]]));
            }
            // MUTANT_SURVIVES(equivalent): ordering by descending end also plans a region ahead of those inside it; only the numbering of unrelated scopes moves.
            regions.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : b.End.CompareTo(a.End));
            return regions;
        }

        // An element opened and not closed by a store, such as a collection initializer's, is dropped when an
        // enclosing one closes.
        private static void CloseElement(List<(int Start, int Depth, TypeReference? Element)> open,
            List<ElementRegion> regions, int store, int depth)
        {
            for (var c = open.Count - 1; c >= 0; c--)
            {
                if (open[c].Depth + 1 != depth) continue;
                if (open[c].Element?.FullName == VNodeFullName) regions.Add(new ElementRegion(open[c].Start, store - 1));
                open.RemoveRange(c, open.Count - c);
                return;
            }
        }

        private static ScopePlan? TryPlanCandidate(ScopeFacts facts, ScopeCandidate candidate, List<ScopePlan> accepted)
        {
            var region = candidate.Span;
            var scan = new ScopeScan();
            if (facts.Branches.Any(branch => BranchBreaksRegion(branch.Source, branch.Target, region))
                || !Enumerable.Range(region.Start, region.End - region.Start + 1).All(i => ScanInstruction(facts, i, region, scan))
                || !scan.HasCall || (scan.Deps.Count == 0 && !scan.OnlyFactoryCalls))
            {
                return null;
            }
            return new ScopePlan(candidate, facts.Instructions[region.Start], facts.Instructions[region.End + 1], scan.Deps,
                accepted.Count);
        }

        // A jump from inside goes forward, to a place in the region or to the store that ends it. One from outside
        // lands in neither, and none from after the region goes back across it.
        private static bool BranchBreaksRegion(int source, int target, ElementRegion region)
        {
            var store = region.End + 1;
            // MUTANT_SURVIVES(unreachable, boundary): an element expression neither begins nor ends with a jump, and none jumps to itself.
            if (source >= region.Start && source <= region.End) return target <= source || target > store;
            // MUTANT_SURVIVES(unreachable, boundary): only the index load falls into the first instruction, and nothing outside jumps to the store.
            if (target >= region.Start && target <= store) return true;
            // MUTANT_SURVIVES(equivalent, boundary): a jump from the last instruction or to the first was decided by the two tests above.
            return source > region.End && target < region.Start;
        }

        private static bool ScanInstruction(ScopeFacts facts, int index, ElementRegion region, ScopeScan scan)
        {
            var instr = facts.Instructions[index];
            if (ScopeForbiddenCodes.Contains(instr.OpCode.Code)) return false;
            switch (instr.Operand)
            {
                case VariableDefinition local:
                    return ScanLocal(facts, index, local, region, scan);
                case ParameterDefinition parameter:
                    scan.Add(facts.ParameterDependency(parameter));
                    return IsBoxable(parameter.ParameterType) && instr.OpCode.Code != Code.Ldarga;
                case FieldReference field:
                    return ScanField(facts, index, field, region, scan);
                case MethodReference method:
                    return ScanMethod(facts, instr, method, region, scan);
                default:
                    return true;
            }
        }

        private static bool ScanLocal(ScopeFacts facts, int index, VariableDefinition local, ElementRegion region,
            ScopeScan scan)
        {
            var code = facts.Instructions[index].OpCode.Code;
            if (code is Code.Stloc or Code.Ldloca || !facts.CanBeCompared(local, local.VariableType, region)) return false;
            scan.Add(new DepSource(local, local.VariableType) { Local = local });
            return true;
        }

        private static bool ScanField(ScopeFacts facts, int index, FieldReference field, ElementRegion region, ScopeScan scan)
        {
            var instr = facts.Instructions[index];
            var code = instr.OpCode.Code;
            if (code is Code.Ldsfld or Code.Ldsflda or Code.Stsfld) return false;
            return !IsClosureField(field);
        }

        private static bool ScanMethod(ScopeFacts facts, Instruction instr, MethodReference method, ElementRegion region,
            ScopeScan scan)
        {
            if (instr.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj)
            {
                scan.HasCall = true;
                scan.OnlyFactoryCalls &= method.DeclaringType.FullName == FactoryTypeFullName;
                if (!IsRepeatableCall(instr, method)) return false;
            }
            return !TouchesRef(method);
        }

        // A call taken to be a function of its arguments. Any other may read state no dependency names, which a
        // miss of the gate alone would have refreshed.
        private static bool IsRepeatableCall(Instruction instr, MethodReference method)
            => instr.OpCode.Code == Code.Newobj || method.DeclaringType.FullName == FactoryTypeFullName;

        private static bool TouchesRef(MethodReference method)
            => IsRefType(method.DeclaringType) || method.Parameters.Any(parameter => IsRefType(parameter.ParameterType));

        // Ref<T> and MutableRef<T> by name, through the generic instance, array and by-reference wrappers.
        private static bool IsRefType(TypeReference type)
        {
            while (type is TypeSpecification specification) type = specification.ElementType;
            return type.FullName == RefTypeFullName || type.FullName == MutableRefTypeFullName;
        }

        // A type whose value is an immutable snapshot: comparing it under Object.is is comparing what an expression
        // reads of it.
        private static bool IsValueLike(TypeReference type) => type.IsValueType || type.MetadataType == MetadataType.String;

        // A value that can be boxed into the dependency array and compared there: all but a ref struct. One that cannot
        // be resolved is not.
        private static bool IsBoxable(TypeReference type)
            => !type.IsValueType || TryResolve(() => type.Resolve())?.CustomAttributes.Any(
                attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsByRefLikeAttribute") == false;

        // A reference that cannot be resolved is read as no definition at all, so what depends on it is not woven.
        private static T? TryResolve<T>(System.Func<T> resolve) where T : class
        {
            try
            {
                return resolve();
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        private static void InjectScopes(MethodBody body, List<ScopePlan> plans, WeaverContext context,
            VariableDefinition slotLocal)
        {
            if (plans.Count == 0) return;
            var cached = new VariableDefinition(context.VNode);
            var result = new VariableDefinition(context.VNode);
            body.Variables.Add(cached);
            body.Variables.Add(result);
            var locals = new ScopeLocals(slotLocal, cached, result);
            foreach (var plan in plans)
            {
                var deps = new VariableDefinition(new ArrayType(body.Method.Module.TypeSystem.Object));
                body.Variables.Add(deps);
                InjectScope(body, context, plan, deps, locals);
            }
        }

        // The store goes in and takes the branches that ended the expression first, so the hit path's own jump to
        // the store instruction is not among them.
        private static void InjectScope(MethodBody body, WeaverContext context, ScopePlan plan, VariableDefinition deps,
            ScopeLocals locals)
        {
            var il = body.GetILProcessor();
            var store = BuildScopeStore(context, plan, deps, locals);
            RetargetBranches(body, plan.Store, store[0]);
            foreach (var instruction in store) il.InsertBefore(plan.Store, instruction);
            foreach (var instruction in BuildScopeGate(body, context, plan, deps, locals)) il.InsertBefore(plan.Start, instruction);
        }

        // On a miss the expression's value is committed under the scope, then left on the stack for the store.
        private static List<Instruction> BuildScopeStore(WeaverContext context, ScopePlan plan, VariableDefinition deps,
            ScopeLocals locals)
            => new List<Instruction>
            {
                Instruction.Create(OpCodes.Stloc, locals.Result),
                Instruction.Create(OpCodes.Ldloc, locals.Slot),
                Instruction.Create(OpCodes.Ldc_I4, plan.Index),
                Instruction.Create(OpCodes.Ldloc, deps),
                Instruction.Create(OpCodes.Ldloc, locals.Result),
                Instruction.Create(OpCodes.Call, context.StoreMemoizedScope!),
                Instruction.Create(OpCodes.Ldloc, locals.Result),
            };

        // The dependencies are read where the expression starts, which is where it reads them.
        private static List<Instruction> BuildScopeGate(MethodBody body, WeaverContext context, ScopePlan plan,
            VariableDefinition deps, ScopeLocals locals)
        {
            var miss = Instruction.Create(OpCodes.Nop);
            var gate = new List<Instruction>
            {
                Instruction.Create(OpCodes.Ldc_I4, plan.Deps.Count),
                Instruction.Create(OpCodes.Newarr, body.Method.Module.TypeSystem.Object),
            };
            gate.AddRange(plan.Deps.SelectMany(DependencyStore));
            gate.AddRange(new[]
            {
                Instruction.Create(OpCodes.Stloc, deps),
                Instruction.Create(OpCodes.Ldloc, locals.Slot),
                Instruction.Create(OpCodes.Ldc_I4, plan.Index),
                Instruction.Create(OpCodes.Ldloc, deps),
                Instruction.Create(OpCodes.Ldloca, locals.Cached),
                Instruction.Create(OpCodes.Call, context.TryGetMemoizedScope!),
                Instruction.Create(OpCodes.Brfalse, miss),
                Instruction.Create(OpCodes.Ldloc, locals.Cached),
                Instruction.Create(OpCodes.Br, plan.Store),
                miss,
            });
            return gate;
        }

        // `dup; ldc.i4 index; <load>; box; stelem.ref` into the array the gate is building.
        private static IEnumerable<Instruction> DependencyStore(DepSource source, int index)
        {
            yield return Instruction.Create(OpCodes.Dup);
            yield return Instruction.Create(OpCodes.Ldc_I4, index);
            yield return source.Parameter != null
                ? Instruction.Create(OpCodes.Ldarg, source.Parameter)
                : Instruction.Create(OpCodes.Ldloc, source.Local!);
            if (source.Type.IsValueType || source.Type.IsGenericParameter) yield return Instruction.Create(OpCodes.Box, source.Type);
            yield return Instruction.Create(OpCodes.Stelem_Ref);
        }

        // The dependencies one expression reads and what it calls.
        private sealed class ScopeScan
        {
            private readonly HashSet<object> _seen = new();

            public List<DepSource> Deps { get; } = new();

            public bool HasCall { get; set; }

            public bool OnlyFactoryCalls { get; set; } = true;

            // A variable read twice is added once.
            public void Add(DepSource source)
            {
                if (_seen.Add(source.Key)) Deps.Add(source);
            }
        }
    }
}

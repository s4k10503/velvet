using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Velvet.CodeGen
{
    // Reactive scopes: below a component's own gate, a child VNode expression of the returned tree is cached on
    // the values it reads, so a render that changes one input rebuilds the children reading it and hands the
    // reconciler the same instances for the rest.
    // A scope is the expression of one element of a `VNode[]` initializer (what `params VNode?[] children` and
    // `children: new[] { ... }` both compile to), found by the `dup; ldc.i4; <expression>; stelem.ref` shape Roslyn
    // emits for it, after the last hook call. It is woven only when everything the expression reads is a value
    // compared where it starts:
    //   A parameter it reads is compared. A local it reads is compared when it is a hook value nothing stores to
    //   after the hook section, or a value type or string. A local it takes nothing from and leaves nothing in
    //   for what runs after it is its own and is not compared (see ScopeFacts.IsPrivateTo). A local it writes
    //   must be its own, and an address it takes of a parameter, or of a local that is not its own, must be read at
    //   once (see ReadsInPlace).
    //   It calls no member of a Ref / MutableRef and no method taking one, and reads no hook value from UseService
    //   or from a custom hook unless value-like: a render reading through one has no value to compare.
    //   It reads no mutable static, and calls only a V factory, a constructor, an instance property accessor, or a
    //   member of the BCL namespaces named in PureNamespaces that does not read the clock or the process.
    //   Control flow in it stays inside it and moves forward, nothing outside jumps into it, and nothing
    //   outside runs it twice (no loop around it).
    //   The method has no try / catch / finally.
    // An expression reading nothing is cached only when everything it calls is a V factory. One whose
    // dependencies equal those of the scope enclosing it is left to that scope. Anything else is built on every
    // miss of the gate, as before. The weave is checked afterwards against the stack discipline, and a method
    // failing the check is woven again without scopes (see TryWeaveMethod).
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

        private static readonly HashSet<string> PureNamespaces = new()
        {
            "System", "System.Collections", "System.Collections.Generic", "System.Linq", "System.Text",
            "System.Runtime.CompilerServices",
        };

        // Types whose every member reads or advances state of the process.
        private static readonly HashSet<string> AmbientStateTypes = new()
        {
            "System.Random", "System.Environment", "System.Console", "System.GC", "System.Activator",
            "System.AppDomain",
        };

        // Types whose static members read the clock or mint a value.
        private static readonly HashSet<string> AmbientStateStaticTypes = new()
        {
            "System.DateTime", "System.DateTimeOffset", "System.Guid", "System.TimeZoneInfo", "System.DateOnly",
            "System.TimeOnly",
        };

        private readonly record struct ElementRegion(int Start, int End);

        private sealed record ScopePlan(ElementRegion Region, Instruction Start, Instruction Store, List<object> Deps,
            int Index);

        // The slot the gate was granted, and the values passing through a scope's gate and store, which no other
        // scope's gate or store runs between.
        private sealed record ScopeLocals(VariableDefinition Slot, VariableDefinition Cached, VariableDefinition Result);

        private static bool IsUncomparableHookValue(MethodReference hook, bool isDirect, VariableDefinition local)
            => isDirect ? hook.Name == nameof(Velvet.Hooks.UseService) : !IsValueLike(local.VariableType);

        private static bool IsStackBalanced(MethodBody body) => DepthsBefore(body.Instructions.ToArray()) != null;

        private static List<ScopePlan> PlanScopes(MethodBody body, HookAnalysis analysis)
        {
            var plans = new List<ScopePlan>();
            var instructions = body.Instructions.ToArray();
            var depths = body.HasExceptionHandlers ? null : DepthsBefore(instructions);
            if (depths == null) return plans;
            var boundary = analysis.LastHookBoundary == null ? -1 : System.Array.IndexOf(instructions, analysis.LastHookBoundary);
            var facts = new ScopeFacts(instructions, analysis, boundary);
            // MUTANT_SURVIVES(equivalent, boundary): a region beginning at the boundary begins with the hook call, which no scope may hold.
            foreach (var region in FindElementRegions(instructions, depths).Where(each => each.Start > boundary))
            {
                if (TryPlanRegion(facts, region, plans) is { } plan) plans.Add(plan);
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

        private static ScopePlan? TryPlanRegion(ScopeFacts facts, ElementRegion region, List<ScopePlan> accepted)
        {
            var scan = new ScopeScan();
            if (facts.Branches.Any(branch => BranchBreaksRegion(branch.Source, branch.Target, region))
                || !Enumerable.Range(region.Start, region.End - region.Start + 1).All(i => ScanInstruction(facts, i, region, scan))
                || !scan.HasCall || (scan.Deps.Count == 0 && !scan.OnlyFactoryCalls)
                || RepeatsEnclosingScope(accepted, region, scan.Deps))
            {
                return null;
            }
            return new ScopePlan(region, facts.Instructions[region.Start], facts.Instructions[region.End + 1], scan.Deps,
                accepted.Count);
        }

        private static bool RepeatsEnclosingScope(List<ScopePlan> accepted, ElementRegion region, List<object> deps)
        {
            // MUTANT_SURVIVES(equivalent, boundary): a region enclosing another starts earlier and ends later, so equal bounds never decide.
            var outer = accepted.LastOrDefault(each => each.Region.Start <= region.Start && each.Region.End >= region.End);
            return outer != null && new HashSet<object>(outer.Deps).SetEquals(deps);
        }

        // A jump from inside goes forward, to a place in the region or to the store that ends it. One from
        // outside lands in neither, and none from after the region goes back across it.
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
            switch (instr.Operand)
            {
                case var _ when ScopeForbiddenCodes.Contains(instr.OpCode.Code):
                    return false;
                case VariableDefinition local:
                    return ScanLocal(facts, index, local, region, scan);
                case ParameterDefinition parameter:
                    scan.AddDependency(parameter);
                    return IsBoxable(parameter.ParameterType)
                        && (instr.OpCode.Code != Code.Ldarga || ReadsInPlace(facts.Instructions[index + 1], parameter.ParameterType));
                case FieldReference field:
                    return ScanField(instr, field);
                case MethodReference method:
                    return ScanMethod(instr, method, scan);
                default:
                    return true;
            }
        }

        private static bool ScanLocal(ScopeFacts facts, int index, VariableDefinition local, ElementRegion region,
            ScopeScan scan)
        {
            var code = facts.Instructions[index].OpCode.Code;
            if (facts.IsPrivateTo(local, region)) return true;
            if (code == Code.Stloc || !facts.CanBeCompared(local)) return false;
            if (code == Code.Ldloca && !ReadsInPlace(facts.Instructions[index + 1], local.VariableType)) return false;
            scan.AddDependency(local);
            return true;
        }

        // Taking the address of a local or a parameter is a read where nothing can write through it: the field load
        // that follows at once, or an instance call made with no argument on a primitive or an enum, which no instance
        // method changes, which is the call that follows (or the constrained prefix to it); and the same of a
        // primitive or enum field, whose address is taken first. An address passed as an argument, as `out var n`
        // does, is not.
        private static bool ReadsInPlace(Instruction next, TypeReference type)
        {
            if (next.OpCode.Code == Code.Ldfld) return true;
            if (next.OpCode.Code == Code.Ldflda) return IsPrimitiveOrEnum(((FieldReference)next.Operand).FieldType) && CallsInPlace(next.Next);
            return IsPrimitiveOrEnum(type) && CallsInPlace(next);
        }

        private static bool CallsInPlace(Instruction instr) => instr.OpCode.Code == Code.Constrained || CallsWithoutArguments(instr);

        private static bool CallsWithoutArguments(Instruction instr)
        {
            if (instr.OpCode.Code != Code.Call && instr.OpCode.Code != Code.Callvirt) return false;
            var method = (MethodReference)instr.Operand;
            return method.HasThis && !method.HasParameters;
        }

        private static bool ScanField(Instruction instr, FieldReference field)
        {
            var code = instr.OpCode.Code;
            return code is not (Code.Ldsfld or Code.Ldsflda or Code.Stsfld)
                || IsClosureCache(field) || (code == Code.Ldsfld && IsReadOnlyValue(field));
        }

        private static bool ScanMethod(Instruction instr, MethodReference method, ScopeScan scan)
        {
            if (instr.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj)
            {
                scan.HasCall = true;
                scan.OnlyFactoryCalls &= method.DeclaringType.FullName == FactoryTypeFullName;
                if (!IsRepeatableCall(instr, method)) return false;
            }
            return !TouchesRef(method);
        }

        // A call whose result is a function of its arguments and of what they hold. Anything else may read state no
        // dependency names, which the gate alone would have refreshed on an unrelated change.
        private static bool IsRepeatableCall(Instruction instr, MethodReference method)
        {
            var declaring = method.DeclaringType;
            while (declaring is TypeSpecification specification) declaring = specification.ElementType;
            var ambient = AmbientStateTypes.Contains(declaring.FullName)
                || (!method.HasThis && AmbientStateStaticTypes.Contains(declaring.FullName));
            return instr.OpCode.Code == Code.Newobj
                || declaring.FullName == FactoryTypeFullName
                || (method.HasThis && (method.Name.StartsWith("get_", System.StringComparison.Ordinal)
                    || method.Name.StartsWith("set_", System.StringComparison.Ordinal)))
                || (PureNamespaces.Contains(declaring.Namespace) && !ambient);
        }

        private static bool TouchesRef(MethodReference method)
            => IsRefType(method.DeclaringType) || method.Parameters.Any(parameter => IsRefType(parameter.ParameterType));

        // Ref<T> and MutableRef<T> by name, through the generic instance, array and by-reference wrappers.
        private static bool IsRefType(TypeReference type)
        {
            while (type is TypeSpecification specification) type = specification.ElementType;
            return type.FullName == RefTypeFullName || type.FullName == MutableRefTypeFullName;
        }

        // The compiler's cache for a lambda that captures nothing: a field written once, holding a delegate.
        private static bool IsClosureCache(FieldReference field)
            => field.DeclaringType.Name.StartsWith("<>c", System.StringComparison.Ordinal);

        private static bool IsReadOnlyValue(FieldReference field)
            => TryResolve(() => field.Resolve())?.IsInitOnly == true && IsValueLike(field.FieldType);

        // A type whose value is an immutable snapshot: comparing it under Object.is is comparing what an expression
        // reads of it.
        private static bool IsValueLike(TypeReference type) => type.IsValueType || type.MetadataType == MetadataType.String;

        private static bool IsPrimitiveOrEnum(TypeReference type)
            => (type.MetadataType >= MetadataType.Boolean && type.MetadataType <= MetadataType.Double)
                || TryResolve(() => type.Resolve())?.IsEnum == true;

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
            var locals = new ScopeLocals(slotLocal, new VariableDefinition(context.VNode), new VariableDefinition(context.VNode));
            body.Variables.Add(locals.Cached);
            body.Variables.Add(locals.Result);
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
            => new()
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
                Instruction.Create(OpCodes.Brfalse, plan.Start),
                Instruction.Create(OpCodes.Ldloc, locals.Cached),
                Instruction.Create(OpCodes.Br, plan.Store),
            });
            return gate;
        }

        // `dup; ldc.i4 index; <load>; box; stelem.ref` into the array the gate is building.
        private static IEnumerable<Instruction> DependencyStore(object source, int index)
        {
            var parameter = source as ParameterDefinition;
            var type = parameter?.ParameterType ?? ((VariableDefinition)source).VariableType;
            yield return Instruction.Create(OpCodes.Dup);
            yield return Instruction.Create(OpCodes.Ldc_I4, index);
            yield return parameter != null
                ? Instruction.Create(OpCodes.Ldarg, parameter)
                : Instruction.Create(OpCodes.Ldloc, (VariableDefinition)source);
            if (type.IsValueType || type.IsGenericParameter) yield return Instruction.Create(OpCodes.Box, type);
            yield return Instruction.Create(OpCodes.Stelem_Ref);
        }

        // The locals a scope may read, and where the body touches each, gathered once per method.
        private sealed class ScopeFacts
        {
            private enum Access
            {
                None,
                Read,
                Store,
            }

            private readonly Dictionary<VariableDefinition, (int First, int Last)> _spans;
            private readonly HashSet<VariableDefinition> _hookLocals;
            private readonly HashSet<VariableDefinition> _writtenAfterHooks;
            private readonly HashSet<VariableDefinition> _uncomparable;
            private readonly Dictionary<VariableDefinition, bool[]> _liveIn = new();
            private readonly Dictionary<Instruction, int> _indexOf = new();

            public ScopeFacts(Instruction[] instructions, HookAnalysis analysis, int boundary)
            {
                Instructions = instructions;
                _uncomparable = analysis.UncomparableLocals;
                _hookLocals = analysis.HookCaptures.Where(capture => capture.Local != null).Select(capture => capture.Local!).ToHashSet();
                for (var i = 0; i < instructions.Length; i++) _indexOf[instructions[i]] = i;
                var touches = instructions.Select((instr, index) => (Local: instr.Operand as VariableDefinition, Index: index))
                    .Where(touch => touch.Local != null).ToList();
                _spans = touches.GroupBy(touch => touch.Local!)
                    .ToDictionary(group => group.Key, group => (group.Min(touch => touch.Index), group.Max(touch => touch.Index)));
                _writtenAfterHooks = touches
                    .Where(touch => touch.Index > boundary && WritesLocal(instructions[touch.Index]))
                    .Select(touch => touch.Local!).ToHashSet();
                Branches = instructions
                    .SelectMany((instr, index) => JumpTargets(instr).Select(target => (Source: index, Target: _indexOf[target])))
                    .ToList();
            }

            public Instruction[] Instructions { get; }

            public List<(int Source, int Target)> Branches { get; }

            // A local whose value the expression neither takes from outside nor leaves for what runs after it: one
            // no instruction outside touches, or one that is stored before it is read on every path through the
            // expression and read again only after another store (Roslyn gives the temporaries of two statements one
            // slot). The expression can run or be skipped without a later read seeing the difference.
            public bool IsPrivateTo(VariableDefinition local, ElementRegion region)
            {
                if (_spans.TryGetValue(local, out var span) && span.First >= region.Start && span.Last <= region.End) return true;
                var live = LiveIn(local);
                return !live[region.Start] && !live[region.End + 2];
            }

            // A hook value stays the one the hook returned only while nothing stores to it after the hook section.
            public bool CanBeCompared(VariableDefinition local)
                => !_uncomparable.Contains(local)
                    && IsBoxable(local.VariableType)
                    && ((_hookLocals.Contains(local) && !_writtenAfterHooks.Contains(local)) || IsValueLike(local.VariableType));

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

            private static bool WritesLocal(Instruction instr) => instr.OpCode.Code == Code.Stloc || instr.OpCode.Code == Code.Ldloca;

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

        // The dependencies one expression reads and what it calls.
        private sealed class ScopeScan
        {
            private readonly HashSet<object> _seen = new();

            public List<object> Deps { get; } = new();

            public bool HasCall { get; set; }

            public bool OnlyFactoryCalls { get; set; } = true;

            public void AddDependency(object source)
            {
                if (_seen.Add(source)) Deps.Add(source);
            }
        }
    }
}

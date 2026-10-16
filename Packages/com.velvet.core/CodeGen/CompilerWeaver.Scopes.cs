using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Velvet.CodeGen
{
    // Reactive scopes: below a component's own gate, an expression is cached on the values it reads, as React
    // Compiler's reactive scopes are, so a render that changes one input builds again what reads it and hands the
    // reconciler the same instances for the rest.
    // A scope is one expression that leaves one value on the stack, after the last hook call (anywhere in a body
    // that calls none):
    //   a call or constructor returning a VNode, wherever it stands: an element of a `VNode[]` initializer (what
    //     `params VNode?[] children` and `children: new[] { ... }` both compile to), an argument (a single child, a
    //     member of a props record), the root;
    //   a delegate built from a lambda that captures variables (`ldloc display; ldftn; newobj`), cached under the
    //     captured variables the lambda reads when it runs, which the compiler hoists into fields of a display
    //     class;
    //   an expression stored once into a local or a captured variable (`var visible = items.Where(...)`), when
    //     nothing can change what it holds after the scope ended: a value type, a string, a delegate, or an interface
    //     exposing no way to change it (IEnumerable<T>, IReadOnlyList<T>, ...). A list or an array is not one,
    //     because the body may write to it afterwards.
    // It is woven only when everything the expression reads is a value compared where it starts:
    //   A parameter it reads is compared. A local it reads is compared when it is a hook value nothing stores to
    //   after the hook section, a value type or string, or the output of a scope of its own stored once before
    //   it. A captured variable is read through its display class and follows the same rules, and one a lambda
    //   of the scope reads is compared whichever it is, provided nothing stores to it once the scope began and
    //   no lambda writes it. A setter that a hook returns beside its value is one object for the life of the
    //   component, and is compared as a dependency that never changes. A local it takes nothing from and leaves
    //   nothing in for what runs after it is its own and is not compared (see ScopeFacts.IsPrivateTo). A local
    //   it writes must be its own, and an address it takes of a parameter, or of a local that is not its own,
    //   must be read at once (see ReadsInPlace).
    //   It calls no member of a Ref / MutableRef and no method taking one, and reads no hook value from UseService
    //   or from a custom hook unless value-like: a render reading through one has no value to compare.
    //   It reads no mutable static, and calls only a V factory, a constructor, an instance property accessor
    //   outside the namespaces named in StatefulAccessorNamespaces, or a member of the BCL namespaces named in
    //   PureNamespaces, other than a member of the types named in AmbientStateTypes and the static members of those
    //   in AmbientStateStaticTypes.
    //   Control flow in it stays inside it and moves forward, nothing outside jumps into it, and nothing
    //   outside runs it twice (no loop around it).
    //   The method has no try / catch / finally.
    // What is inspected is the expression's own instructions: the fields it reads, each call it makes by the type
    // it is declared on and by the types of its parameters, and the captured variables a lambda it creates reads.
    // A call is not followed into the code it reaches. The target of a delegate the expression hands on (a method
    // group, a lambda capturing nothing), the override a virtual call dispatches to, `Func.Invoke` on a delegate it
    // was given, the body of a lambda beyond the variables it captures, and a BCL generic instantiated over a Ref
    // (`List<Ref<int>>`, whose members name no Ref in their signatures) are all code or values this does not read.
    // As in React, render code must be pure: an expression that reads something no dependency stands for, through
    // one of those or through a property accessor of an object it holds, shows what it read when it last ran.
    // An expression reading nothing is cached only when everything it calls is a V factory. One whose
    // dependencies equal those of the scope enclosing it is left to that scope, and so is the tree the body
    // returns when it reads every input of the component's gate: it would miss wherever the gate misses. Anything
    // else is built on every miss of the gate, as before. The weave is checked afterwards against the stack
    // discipline, and a method failing the check is woven again without scopes (see TryWeaveMethod).
    internal static partial class CompilerWeaver
    {
        private static readonly string RefTypeFullName = typeof(Velvet.Ref<>).FullName!;
        private static readonly string MutableRefTypeFullName = typeof(Velvet.MutableRef<>).FullName!;
        private static readonly string FactoryTypeFullName = typeof(Velvet.V).FullName!;

        // The class the compiler declares the cache fields for method group conversions in; the IL case
        // Given_AChildHandingOnAMethodGroup_When_Woven_Then_EachChildGetsAScopeGate fails when it is not this name.
        private const string MethodGroupCacheClass = "<>O";

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

        // Instance property accessors are read like the value they belong to, except in these namespaces: a Stopwatch
        // advances without a render, so a child reading one is built on every render the gate misses.
        private static readonly HashSet<string> StatefulAccessorNamespaces = new()
        {
            "System.Diagnostics", "System.Threading",
        };

        // No call on these types is repeatable: an instance of Random advances its generator, and a member of
        // Environment reads the process. The static members of the types below are not repeatable either: they read
        // the clock (DateTime.Now, DateTimeOffset.UtcNow), the time zone (TimeZoneInfo.Local) or mint a value
        // (Guid.NewGuid). Their instance members are not excluded. A type not listed is covered by the rule that
        // render code is pure.
        private static readonly HashSet<string> AmbientStateTypes = new()
        {
            "System.Random", "System.Environment",
        };

        private static readonly HashSet<string> AmbientStateStaticTypes = new()
        {
            "System.DateTime", "System.DateTimeOffset", "System.Guid", "System.TimeZoneInfo",
        };

        // Interfaces declaring no member that changes what a value holds, so that one kept from an earlier render
        // is what the expression would build again.
        private static readonly HashSet<string> ReadOnlyInterfaces = new()
        {
            "System.Collections.Generic.IEnumerable`1", "System.Collections.Generic.IReadOnlyCollection`1",
            "System.Collections.Generic.IReadOnlyList`1", "System.Collections.Generic.IReadOnlyDictionary`2",
            "System.Collections.Generic.IReadOnlySet`1", "System.Linq.IOrderedEnumerable`1", "System.Linq.ILookup`2",
            "System.Linq.IGrouping`2",
        };

        private readonly record struct ElementRegion(int Start, int End);

        private enum ScopeKind
        {
            Node,
            Value,
        }

        // Where a region came from. Output: the local or captured variable the value is stored into, once.
        private sealed record ScopeCandidate(ElementRegion Span, ScopeKind Kind, TypeReference? ResultType, object? Output);

        // One value an expression reads, loaded again where the expression starts: a parameter, a local, or a
        // field of the display class held in Receiver. Direct: read by the expression itself, not only by a lambda it
        // creates.
        private sealed class DepSource
        {
            public DepSource(object key, TypeReference type, bool direct)
            {
                Key = key;
                Type = type;
                Direct = direct;
            }

            public object Key { get; }

            public TypeReference Type { get; }

            public bool Direct { get; set; }

            public ParameterDefinition? Parameter { get; init; }

            public VariableDefinition? Local { get; init; }

            public FieldReference? Field { get; init; }

            public VariableDefinition? Receiver { get; init; }
        }

        private sealed record ScopePlan(ScopeCandidate Candidate, Instruction Start, Instruction Store,
            List<DepSource> Deps, int Index)
        {
            public ElementRegion Region => Candidate.Span;
        }

        // The slot the gate was granted, and the values passing through a scope's gate and store, which no other
        // scope's gate or store runs between. A kind no scope of the method has gets no locals.
        private sealed record ScopeLocals(VariableDefinition Slot, VariableDefinition? Cached,
            VariableDefinition? Result, VariableDefinition? CachedValue, VariableDefinition? ResultValue);

        // Whether what runs after the hook section builds the same tree on every render: it calls only V factories
        // and delegate constructors over lambdas that capture nothing, reads no static other than the compiler's cache
        // for those, writes no field and has no loop or handler. A body that does anything else may read state that
        // changes without a render, or count its own renders, which a cache that never misses would stop.
        private static bool IsInputFree(MethodBody body, Instruction? boundary)
        {
            if (body.HasExceptionHandlers) return false;
            var instructions = body.Instructions;
            for (var i = boundary == null ? 0 : instructions.IndexOf(boundary) + 1; i < instructions.Count; i++)
            {
                if (!IsInputFreeInstruction(instructions[i])) return false;
            }
            return true;
        }

        private static bool IsInputFreeInstruction(Instruction instr)
        {
            var code = instr.OpCode.Code;
            if (code != Code.Ret && ScopeForbiddenCodes.Contains(code)) return false;
            if (JumpTargets(instr).Any(target => target.Offset <= instr.Offset)) return false;
            switch (instr.Operand)
            {
                case MethodReference method when code is Code.Call or Code.Callvirt or Code.Newobj:
                    return method.DeclaringType.FullName == FactoryTypeFullName || IsDelegateConstructor(method);
                case FieldReference field:
                    return code switch
                    {
                        Code.Ldsfld => IsClosureCache(field) || IsReadOnlyValue(field),
                        Code.Stsfld => IsClosureCache(field),
                        _ => false,
                    };
                default:
                    return true;
            }
        }

        private static bool IsDelegateConstructor(MethodReference constructor)
            => constructor.Name == ".ctor" && constructor.Parameters.Count == 2
                && constructor.Parameters[1].ParameterType.MetadataType == MetadataType.IntPtr;

        private static bool IsStackBalanced(MethodBody body) => DepthsBefore(body.Instructions.ToArray()) != null;

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
                if (candidate.Kind == ScopeKind.Value && (context.TryGetMemoizedValue == null || context.StoreMemoizedValue == null)) continue;
                if (TryPlanCandidate(facts, candidate, plans) is not { } plan) continue;
                plans.Add(plan);
                if (candidate.Output != null) facts.AcceptOutput(candidate.Output);
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
        {
            var found = new Dictionary<ElementRegion, ScopeCandidate>();
            foreach (var span in FindElementRegions(facts.Instructions, facts.Depths))
            {
                found[span] = new ScopeCandidate(span, ScopeKind.Node, null, null);
            }
            for (var i = 0; i < facts.Instructions.Length; i++)
            {
                if (CandidateEndingAt(facts, i) is { } built) found.TryAdd(built.Span, built);
                if (CandidateStoredAt(facts, i, found) is { } stored) found[stored.Span] = stored;
            }
            return found.Values.OrderBy(each => each.Span.Start).ThenByDescending(each => each.Span.End).ToList();
        }

        // A call or constructor producing a VNode, or a delegate over a lambda of a display class.
        private static ScopeCandidate? CandidateEndingAt(ScopeFacts facts, int index)
        {
            var instr = facts.Instructions[index];
            if (instr.OpCode.Code is not (Code.Call or Code.Callvirt or Code.Newobj) || instr.Operand is not MethodReference method)
            {
                return null;
            }
            var isDelegate = instr.OpCode.Code == Code.Newobj && index > 0 && IsClosureDelegate(facts.Instructions[index - 1], method);
            var produced = instr.OpCode.Code == Code.Newobj ? method.DeclaringType : method.ReturnType;
            var kind = isDelegate ? ScopeKind.Value : IsNodeType(produced) ? ScopeKind.Node : (ScopeKind?)null;
            return kind != null && SpanEndingAt(facts, index) is { } span
                ? new ScopeCandidate(span, kind.Value, kind == ScopeKind.Value ? produced : null, null)
                : null;
        }

        // `ldftn lambda; newobj Delegate::.ctor(object, native int)` over a lambda of a display class.
        private static bool IsClosureDelegate(Instruction before, MethodReference constructor)
            => before.OpCode.Code == Code.Ldftn && before.Operand is MethodReference lambda && IsClosureClass(lambda.DeclaringType)
                && IsDelegateConstructor(constructor);

        // An expression stored into a variable written nowhere else. A scope of its own whose output the scopes
        // reading the variable compare, so that the value they read is the same object while its inputs hold.
        private static ScopeCandidate? CandidateStoredAt(ScopeFacts facts, int index, Dictionary<ElementRegion, ScopeCandidate> found)
        {
            var key = facts.StoredKey(index);
            if (key == null || index <= facts.Boundary || facts.WriteCount(key) != 1) return null;
            if (SpanEndingAt(facts, index - 1) is not { } span) return null;
            var type = facts.TypeOfStoreAt(index);
            if (found.TryGetValue(span, out var existing)) return existing with { Output = key };
            var kind = IsNodeType(type) ? ScopeKind.Node : IsImmutableType(type) ? ScopeKind.Value : (ScopeKind?)null;
            return kind == null ? null : new ScopeCandidate(span, kind.Value, kind == ScopeKind.Value ? type : null, key);
        }

        // The expression whose last instruction is at `end`: the longest run before it that starts at the depth its
        // operands start at, never dips below it, and has no jump across its edge. A conditional expression is
        // entered at its test, and a run starting after the test has a jump into it.
        private static ElementRegion? SpanEndingAt(ScopeFacts facts, int end)
        {
            if (end < 0 || Pushes(facts.Instructions[end]) != 1) return null;
            var last = facts.Instructions[end];
            var baseDepth = facts.Depths[end] - Pops(last);
            for (var start = end; start >= 0 && facts.Depths[start] >= baseDepth; start--)
            {
                if (facts.Depths[start] != baseDepth) continue;
                var candidate = new ElementRegion(start, end);
                if (!facts.Branches.Any(branch => BranchBreaksRegion(branch.Source, branch.Target, candidate))) return candidate;
            }
            return null;
        }

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

        // An array of nodes, or a by-reference one, resolves to the node type it holds and is not a node.
        private static bool IsNodeType(TypeReference type)
        {
            if (type.IsPrimitive || type.MetadataType == MetadataType.Void || IsArrayOrIndirection(type)
                || type.FullName.StartsWith("System.", System.StringComparison.Ordinal))
            {
                return false;
            }
            var current = TryResolve(() => type.Resolve());
            while (current != null)
            {
                if (current.FullName == VNodeFullName) return true;
                var parent = current.BaseType;
                current = parent == null ? null : TryResolve(() => parent.Resolve());
            }
            return false;
        }

        private static bool IsArrayOrIndirection(TypeReference type)
            => type is ArrayType or ByReferenceType or PointerType or PinnedType;

        // A value type, a string, a delegate or an interface with no mutating member. An array of any of them is
        // an object the body can write to.
        private static bool IsImmutableType(TypeReference type)
        {
            if (IsArrayOrIndirection(type)) return false;
            if (IsValueLike(type) && IsBoxable(type)) return true;
            if (ReadOnlyInterfaces.Contains(ElementOf(type).FullName)) return true;
            var current = TryResolve(() => type.Resolve());
            while (current?.BaseType != null)
            {
                if (current.BaseType.FullName == "System.MulticastDelegate") return true;
                var parent = current.BaseType;
                current = TryResolve(() => parent.Resolve());
            }
            return false;
        }

        private static ScopePlan? TryPlanCandidate(ScopeFacts facts, ScopeCandidate candidate, List<ScopePlan> accepted)
        {
            var region = candidate.Span;
            var scan = new ScopeScan();
            if (accepted.Any(each => each.Region.End == region.End)
                || facts.Branches.Any(branch => BranchBreaksRegion(branch.Source, branch.Target, region))
                || !Enumerable.Range(region.Start, region.End - region.Start + 1).All(i => ScanInstruction(facts, i, region, scan))
                || !scan.HasCall || (scan.Deps.Count == 0 && !scan.OnlyFactoryCalls)
                || RepeatsEnclosingScope(accepted, region, scan.Deps)
                || (facts.FlowsIntoReturn(region.End + 1) && facts.CoversGate(scan.Deps)))
            {
                return null;
            }
            return new ScopePlan(candidate, facts.Instructions[region.Start], facts.Instructions[region.End + 1], scan.Deps,
                accepted.Count);
        }

        private static bool RepeatsEnclosingScope(List<ScopePlan> accepted, ElementRegion region, List<DepSource> deps)
        {
            // MUTANT_SURVIVES(equivalent, boundary): a region enclosing another starts earlier and ends later, so equal bounds never decide.
            var outer = accepted.LastOrDefault(each => each.Region.Start <= region.Start && each.Region.End >= region.End);
            return outer != null && outer.Deps.Select(dep => dep.Key).ToHashSet().SetEquals(deps.Select(dep => dep.Key));
        }

        // A jump from inside goes forward, to a place in the region or to the store that ends it. One from before
        // the region may land on its first instruction, as falling in would, and is sent to the gate there (see
        // InjectScopes); one that lands anywhere else in the region or on its store has skipped part of it, and
        // none from after the region goes back across it.
        private static bool BranchBreaksRegion(int source, int target, ElementRegion region)
        {
            var store = region.End + 1;
            // MUTANT_SURVIVES(unreachable, boundary): an element expression neither begins nor ends with a jump, and none jumps to itself.
            if (source >= region.Start && source <= region.End) return target <= source || target > store;
            if (target == region.Start) return source > region.End;
            // MUTANT_SURVIVES(unreachable, boundary): only the index load falls into the first instruction, and nothing outside jumps to the store.
            if (target >= region.Start && target <= store) return true;
            // MUTANT_SURVIVES(equivalent, boundary): a jump from the last instruction or to the first was decided by the tests above.
            return source > region.End && target < region.Start;
        }

        private static bool ScanInstruction(ScopeFacts facts, int index, ElementRegion region, ScopeScan scan)
        {
            var instr = facts.Instructions[index];
            if (ScopeForbiddenCodes.Contains(instr.OpCode.Code)) return false;
            switch (instr.Operand)
            {
                case VariableDefinition local:
                    return IsClosureClass(local.VariableType)
                        ? ReachesClosureMember(instr, facts.Instructions[index + 1])
                        : ScanLocal(facts, index, local, region, scan);
                case ParameterDefinition parameter:
                    scan.Add(facts.ParameterDependency(parameter, direct: true));
                    return IsBoxable(parameter.ParameterType)
                        && (instr.OpCode.Code != Code.Ldarga || ReadsInPlace(facts.Instructions[index + 1], parameter.ParameterType));
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
            if (facts.IsPrivateTo(local, region)) return true;
            if (code == Code.Stloc || !facts.CanBeCompared(local, local.VariableType, region)) return false;
            if (code == Code.Ldloca && !ReadsInPlace(facts.Instructions[index + 1], local.VariableType)) return false;
            scan.Add(new DepSource(local, local.VariableType, direct: true) { Local = local });
            return true;
        }

        // A display class is only ever read through one of its fields or handed to a lambda it creates: anything
        // else could write a captured variable or read one no dependency stands for.
        private static bool ReachesClosureMember(Instruction instr, Instruction next)
            => instr.OpCode.Code == Code.Ldloc && IsClosureMemberAccess(next);

        private static bool IsClosureMemberAccess(Instruction next)
            => next.OpCode.Code is Code.Ldfld or Code.Ldflda or Code.Ldftn or Code.Ldvirtftn;

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

        private static bool ScanField(ScopeFacts facts, int index, FieldReference field, ElementRegion region, ScopeScan scan)
        {
            var instr = facts.Instructions[index];
            var code = instr.OpCode.Code;
            if (code is Code.Ldsfld or Code.Ldsflda or Code.Stsfld)
            {
                return IsClosureCache(field) || (code == Code.Ldsfld && IsReadOnlyValue(field));
            }
            return !IsClosureField(field) || ScanClosureField(facts, index, field, region, scan);
        }

        private static bool ScanClosureField(ScopeFacts facts, int index, FieldReference field, ElementRegion region,
            ScopeScan scan)
        {
            var code = facts.Instructions[index].OpCode.Code;
            var next = facts.Instructions[index + 1];
            if (code == Code.Stfld) return false;
            if (IsParentLink(field)) return IsClosureMemberAccess(next);
            if (code == Code.Ldflda && !ReadsInPlace(next, field.FieldType)) return false;
            if (TryResolve(() => field.Resolve()) is not { } definition) return false;
            var dependency = facts.FieldDependency(definition, direct: true, region);
            if (dependency == null) return false;
            scan.Add(dependency);
            return true;
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
            if (instr.OpCode.Code is Code.Ldftn or Code.Ldvirtftn && IsClosureClass(method.DeclaringType)
                && !ScanClosureCreation(facts, method, region, scan))
            {
                return false;
            }
            return !TouchesRef(method);
        }

        // A lambda reads captured variables when it runs, so what a scope holding its creation depends on is what
        // the lambda reads of them, and the delegate kept from an earlier render reads them from the display class
        // it was created over.
        private static bool ScanClosureCreation(ScopeFacts facts, MethodReference lambda, ElementRegion region, ScopeScan scan)
        {
            if (TryResolve(() => lambda.Resolve()) is not { } definition || facts.CapturedReadsOf(definition) is not { } reads)
            {
                return false;
            }
            foreach (var field in reads.Where(facts.IsComponentField).OrderBy(each => each.FullName, System.StringComparer.Ordinal))
            {
                var dependency = facts.FieldDependency(field, direct: false, region);
                if (dependency == null) return false;
                scan.Add(dependency);
            }
            return true;
        }

        // A call taken to be a function of its arguments and of what they hold; the header says what that takes on
        // trust. Any other may read state no dependency names, which a miss of the gate alone would have refreshed.
        private static bool IsRepeatableCall(Instruction instr, MethodReference method)
        {
            var declaring = method.DeclaringType;
            while (declaring is TypeSpecification specification) declaring = specification.ElementType;
            var ambient = AmbientStateTypes.Contains(declaring.FullName)
                || (!method.HasThis && AmbientStateStaticTypes.Contains(declaring.FullName));
            return instr.OpCode.Code == Code.Newobj
                || declaring.FullName == FactoryTypeFullName
                || (method.HasThis && !StatefulAccessorNamespaces.Contains(declaring.Namespace)
                    && (method.Name.StartsWith("get_", System.StringComparison.Ordinal)
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

        // The compiler's cache for a lambda that captures nothing, or for a method group converted to a delegate: a
        // field written once, holding a delegate.
        private static bool IsClosureCache(FieldReference field)
            => field.DeclaringType.Name.StartsWith("<>c", System.StringComparison.Ordinal)
                || field.DeclaringType.Name == MethodGroupCacheClass;

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
            var locals = new ScopeLocals(slotLocal,
                NewLocalIf(body, plans, ScopeKind.Node, context.VNode), NewLocalIf(body, plans, ScopeKind.Node, context.VNode),
                NewLocalIf(body, plans, ScopeKind.Value, body.Method.Module.TypeSystem.Object),
                NewLocalIf(body, plans, ScopeKind.Value, body.Method.Module.TypeSystem.Object));
            var entered = new HashSet<Instruction>();
            foreach (var plan in plans)
            {
                var deps = new VariableDefinition(new ArrayType(body.Method.Module.TypeSystem.Object));
                body.Variables.Add(deps);
                var gate = InjectScope(body, context, plan, deps, locals);
                // Plans are numbered outermost first, so the first of those starting at one instruction owns the
                // first gate a jump into that instruction has to reach.
                if (entered.Add(plan.Start)) RetargetBranches(body, plan.Start, gate);
            }
        }

        private static VariableDefinition? NewLocalIf(MethodBody body, List<ScopePlan> plans, ScopeKind kind, TypeReference type)
        {
            if (plans.All(plan => plan.Candidate.Kind != kind)) return null;
            var local = new VariableDefinition(type);
            body.Variables.Add(local);
            return local;
        }

        // The store goes in and takes the branches that ended the expression first. The hit path jumps to a no-op
        // after the store rather than to the instruction that followed the expression, because a scope starting at
        // that instruction puts its gate ahead of it, and the hit would skip the gate. Returns the first instruction
        // of the gate, which is where a jump into the start of the expression has to land.
        private static Instruction InjectScope(MethodBody body, WeaverContext context, ScopePlan plan, VariableDefinition deps,
            ScopeLocals locals)
        {
            var il = body.GetILProcessor();
            var landing = Instruction.Create(OpCodes.Nop);
            var store = BuildScopeStore(context, plan, deps, locals);
            store.Add(landing);
            RetargetBranches(body, plan.Store, store[0]);
            foreach (var instruction in store) il.InsertBefore(plan.Store, instruction);
            var gate = BuildScopeGate(body, context, plan, deps, locals, landing);
            foreach (var instruction in gate) il.InsertBefore(plan.Start, instruction);
            return gate[0];
        }

        // On a miss the expression's value is committed under the scope, then left on the stack for the store.
        private static List<Instruction> BuildScopeStore(WeaverContext context, ScopePlan plan, VariableDefinition deps,
            ScopeLocals locals)
            => plan.Candidate.Kind == ScopeKind.Node
                ? new List<Instruction>
                {
                    Instruction.Create(OpCodes.Stloc, locals.Result!),
                    Instruction.Create(OpCodes.Ldloc, locals.Slot),
                    Instruction.Create(OpCodes.Ldc_I4, plan.Index),
                    Instruction.Create(OpCodes.Ldloc, deps),
                    Instruction.Create(OpCodes.Ldloc, locals.Result!),
                    Instruction.Create(OpCodes.Call, context.StoreMemoizedScope!),
                    Instruction.Create(OpCodes.Ldloc, locals.Result!),
                }
                : BuildValueStore(context, plan, deps, locals);

        // The value stays on the stack under a copy of it that is committed as an object.
        private static List<Instruction> BuildValueStore(WeaverContext context, ScopePlan plan, VariableDefinition deps,
            ScopeLocals locals)
        {
            var store = new List<Instruction> { Instruction.Create(OpCodes.Dup) };
            store.AddRange(Box(plan.Candidate.ResultType!));
            store.AddRange(new[]
            {
                Instruction.Create(OpCodes.Stloc, locals.ResultValue!),
                Instruction.Create(OpCodes.Ldloc, locals.Slot),
                Instruction.Create(OpCodes.Ldc_I4, plan.Index),
                Instruction.Create(OpCodes.Ldloc, deps),
                Instruction.Create(OpCodes.Ldloc, locals.ResultValue!),
                Instruction.Create(OpCodes.Call, context.StoreMemoizedValue!),
            });
            return store;
        }

        private static IEnumerable<Instruction> Box(TypeReference type)
        {
            if (type.IsValueType || type.IsGenericParameter) yield return Instruction.Create(OpCodes.Box, type);
        }

        // What a cached object becomes again: unboxed for a value, cast for a reference, left alone for an object.
        private static IEnumerable<Instruction> Unbox(TypeReference type)
        {
            if (type.IsValueType || type.IsGenericParameter) yield return Instruction.Create(OpCodes.Unbox_Any, type);
            else if (type.MetadataType != MetadataType.Object) yield return Instruction.Create(OpCodes.Castclass, type);
        }

        // The dependencies are read where the expression starts, which is where it reads them. A miss lands on a
        // no-op ahead of the expression, so that the gate of a scope starting at the same instruction, inserted
        // after this one, is the first thing the miss reaches.
        private static List<Instruction> BuildScopeGate(MethodBody body, WeaverContext context, ScopePlan plan,
            VariableDefinition deps, ScopeLocals locals, Instruction landing)
        {
            var isNode = plan.Candidate.Kind == ScopeKind.Node;
            var cached = isNode ? locals.Cached! : locals.CachedValue!;
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
                Instruction.Create(OpCodes.Ldloca, cached),
                Instruction.Create(OpCodes.Call, isNode ? context.TryGetMemoizedScope! : context.TryGetMemoizedValue!),
                Instruction.Create(OpCodes.Brfalse, miss),
                Instruction.Create(OpCodes.Ldloc, cached),
            });
            if (!isNode) gate.AddRange(Unbox(plan.Candidate.ResultType!));
            gate.Add(Instruction.Create(OpCodes.Br, landing));
            gate.Add(miss);
            return gate;
        }

        // `dup; ldc.i4 index; <load>; box; stelem.ref` into the array the gate is building.
        private static IEnumerable<Instruction> DependencyStore(DepSource source, int index)
        {
            yield return Instruction.Create(OpCodes.Dup);
            yield return Instruction.Create(OpCodes.Ldc_I4, index);
            if (source.Parameter != null)
            {
                yield return Instruction.Create(OpCodes.Ldarg, source.Parameter);
            }
            else if (source.Local != null)
            {
                yield return Instruction.Create(OpCodes.Ldloc, source.Local);
            }
            else
            {
                yield return Instruction.Create(OpCodes.Ldloc, source.Receiver!);
                yield return Instruction.Create(OpCodes.Ldfld, source.Field!);
            }
            if (source.Type.IsValueType || source.Type.IsGenericParameter) yield return Instruction.Create(OpCodes.Box, source.Type);
            yield return Instruction.Create(OpCodes.Stelem_Ref);
        }

        // The dependencies one expression reads and what it calls.
        private sealed class ScopeScan
        {
            private readonly Dictionary<object, DepSource> _seen = new();

            public List<DepSource> Deps { get; } = new();

            public bool HasCall { get; set; }

            public bool OnlyFactoryCalls { get; set; } = true;

            // A variable read directly and by a lambda is added once, having passed for both.
            public void Add(DepSource source)
            {
                if (_seen.TryGetValue(source.Key, out var known))
                {
                    known.Direct |= source.Direct;
                    return;
                }
                _seen[source.Key] = source;
                Deps.Add(source);
            }
        }
    }
}

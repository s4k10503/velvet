using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Unity.CompilationPipeline.Common.Diagnostics;

namespace Velvet.CodeGen
{
    // Build-time transform that weaves inner auto-memoization into every [Component] method,
    // default-on with no opt-in attribute. Hooks still run on every render; only the VNode construction
    // is cached, keyed on the values that flow out of hook calls: the component body is rebuilt only
    // when a hook-derived input changes.
    // The weaver only transforms a method when its body matches an analyzable shape it can prove correct:
    //   Static, returns Velvet.VNode, and carries [Component]. Parameters (props) are
    //   allowed: each is prepended to the deps array so a prop change is detected like any other input.
    //   Every hook the body reaches — directly or transitively through a custom hook — is on the SAFE
    //   allow-list (MemoSafeValueHookNames ∪ MemoSafeVoidHookNames). A hook that is
    //   not on the allow-list (a known memo-unsafe hook, or an unknown / future hook) bails the whole method.
    //   The allow-list is the safety contract: a hook is admitted only when its re-render trigger is soundly
    //   represented by an Object.is comparison of a value captured into the deps array, or when it
    //   returns nothing reactive at all.
    //   At least one hook call, with every return placed after the last hook call, every hook reached
    //   unconditionally, and no hook inside a loop (Rules of Hooks).
    //   Each value-returning hook result captured via var x = Hooks.UseXxx(...) (IL
    //   call → stloc) or a single-element deconstruction var (x, _, _) = Hooks.UseXxx(...)
    //   (IL call → ldfld → stloc). A void hook (UseEffect and friends) captures no dep but still
    //   advances the hook boundary so the cache gate is injected after it.
    //   No hook call or return inside a try/catch/finally region (the Leave protocol is not woven).
    // The deps array (component parameters followed by the values flowing out of value-returning hook calls) is
    // compared with Object.is, the same strictness the reconciler and Provider use to drive a re-render,
    // so a fresh record prop or a changed context value is treated as a miss rather than a stale hit.
    // Any shape the weaver cannot prove correct — a discarded hook result, a deconstruction that drops the
    // value element, a body that reaches a non-allow-listed hook, a call whose hook safety cannot be confirmed
    // (Resolve() fails, or the call is an open virtual / interface dispatch outside the known BCL / Unity /
    // UniTask carve-out — an override composing a hook can live in an assembly this scan never sees, regardless
    // of whether the statically declared base/interface's own assembly references Velvet), a return before the
    // hook section, a hook skipped or repeated by a branch, a hook return consumed by an unsupported pattern, or
    // a protected region overlapping the hook section — is left byte-for-byte unchanged
    // (graceful bailout). The allow-list defaults unknown hooks to bail, so correctness is never traded for
    // coverage; no diagnostic is emitted for a bailout. The goal is "memoize less but never wrong". The one
    // dispatch this static model still cannot see through is an override of a BCL / Unity virtual signature
    // (e.g. object.ToString) that itself calls a hook: the carve-out treats the base signature as hook-free
    // because the BCL/Unity type declaring it cannot itself compose a hook, but a user override of it can —
    // that stays outside this static model and is a rules-of-hooks violation the analyzer layer reports.
    // A body that already contains a Velvet.Hooks.TryGetMemoizedVNode call (a hand-written memoization,
    // e.g. in a test fixture) is skipped so the weaver does not memoize it a second time.
    internal static class CompilerWeaver
    {
        private const string ComponentAttrFullName = "Velvet.ComponentAttribute";
        private const string CompilerPropertyName = "Compiler";
        private const string HooksTypeFullName = "Velvet.Hooks";
        private const string VNodeFullName = "Velvet.VNode";
        private const string SystemVoidFullName = "System.Void";
        private const string TryGetMemoizedVNodeName = "TryGetMemoizedVNode";

        private static readonly HashSet<string> PositionalHookMethodNames =
            new(Velvet.PositionalHookNames.All);

        // SAFE value-returning hooks. Each returns a value that is captured into the deps array and compared with
        // Object.is, the same strictness the reconciler and Provider use to drive a re-render. Admitting one of
        // these is sound because every reactive change the hook can drive is observable as an Object.is difference
        // in the captured return value:
        //   UseState / UseReducer / UseStore / UseOptimistic — the current value flows out and changes on update.
        //   UseContext — the live context value flows out and changes when the Provider supplies a new value.
        //   UseDeferredValue — the deferred value flows out and changes as it catches up.
        //   UseTransition — the (startTransition, isPending) tuple flows out; isPending changes drive a re-render.
        //   UseId — a stable id; constant across renders, never hides a change.
        //   UseCallback — a memoized delegate whose identity is itself the dep; a fresh identity is a miss.
        //   UseMemo — the memoized value flows out and changes only when its deps change; any change is an
        //     Object.is difference in the captured value (the same soundness argument as UseCallback).
        //   UseRef / UseMutableRef / UseService — a stable reference that does NOT self-trigger a re-render.
        //     Reading through the ref is non-reactive by design, so capturing the stable
        //     reference as a constant dep never produces a stale VNode: a change that must repaint flows through
        //     some other reactive hook (state / store / context), which is itself captured.
        private static readonly HashSet<string> MemoSafeValueHookNames = new()
        {
            nameof(Velvet.Hooks.UseState),
            nameof(Velvet.Hooks.UseReducer),
            nameof(Velvet.Hooks.UseStore),
            nameof(Velvet.Hooks.UseContext),
            nameof(Velvet.Hooks.UseDeferredValue),
            nameof(Velvet.Hooks.UseId),
            nameof(Velvet.Hooks.UseOptimistic),
            nameof(Velvet.Hooks.UseTransition),
            nameof(Velvet.Hooks.UseCallback),
            nameof(Velvet.Hooks.UseMemo),
            nameof(Velvet.Hooks.UseService),
            nameof(Velvet.Hooks.UseRef),
            nameof(Velvet.Hooks.UseMutableRef),
        };

        // SAFE void hooks. These run for their side effect only and return nothing, so there is no value to
        // capture into the deps array. They are admitted because they cannot drive a re-render through a return
        // value at all: their effect (or imperative handle assignment) runs every render the body is rebuilt, and
        // a body is rebuilt whenever a captured value-hook dep changes. The hook boundary is advanced past a void
        // hook call so the cache gate is injected after the whole hook section, without capturing a dep.
        private static readonly HashSet<string> MemoSafeVoidHookNames = new()
        {
            nameof(Velvet.Hooks.UseEffect),
            nameof(Velvet.Hooks.UseLayoutEffect),
            nameof(Velvet.Hooks.UseInsertionEffect),
            nameof(Velvet.Hooks.UseImperativeHandle),
            // Runs for its side effect only (the per-frame tick reads the latest closure through a ref
            // slot); its callback never feeds a captured return value, so it cannot go stale in a memo.
            nameof(Velvet.Hooks.UseFrame),
        };

        // UNSAFE hooks — every PositionalHookName not in the SAFE sets above — bail the whole method:
        //   Use (Suspense) swings between throwing and returning across the suspend / resolve boundary, so
        //     caching its captured result would suppress that swap.
        //   UseMutation returns a MutationResult whose reference is STABLE across renders; Mutate() mutates the
        //     status / data fields in place and requests a re-render, but the captured reference stays Object.is
        //     equal, so the deps array would never see the change and the memo would return a stale (e.g. Idle)
        //     VNode after the mutation, freezing the UI.
        //   UseBlocker returns a stable navigation-blocker state whose effect (blocking a navigation attempt) is
        //     not represented by an Object.is difference in the captured value.
        //   UseFallback is Suspense / ErrorBoundary control flow, not a value-capture hook.
        // Any unknown or future hook is also UNSAFE by default because it is absent from both allow-lists.

        public static bool Weave(ModuleDefinition module, List<DiagnosticMessage> diagnostics)
        {
            var context = WeaverContext.TryResolve(module, out var resolutionFailure);
            if (context == null)
            {
                // A silent return here would leave every [Component] in the assembly permanently unwoven,
                // indistinguishable from "nothing needed weaving" — auto-memoization is default-on, so the
                // opt-out must be visible. Resolution can genuinely fail in ordinary workflows (e.g. a stale
                // or duplicate assembly copy that defeats the post-processor's resolver), so surface it.
                diagnostics.Add(new DiagnosticMessage
                {
                    DiagnosticType = DiagnosticType.Warning,
                    MessageData = WeaverDiagnostics.FormatResolutionFailureWarning(
                        module,
                        "auto-memoization",
                        resolutionFailure,
                        "Every [Component] method in this assembly is left unwoven."),
                });
                return false;
            }

            // Per-weave cache keyed by MethodReference.FullName for transitive hook detection.
            // IsHookCall is invoked for every Call/Callvirt instruction across every candidate
            // method body; the recursive scan inside CallsHookTransitively walks callee bodies and
            // would re-Resolve metadata for repeated targets (V.Label / V.Div / store accessors /
            // recursive custom-hook helpers) on every visit without this cache. FullName is used
            // because MethodReference does not implement value equality, and Resolve() returns a
            // fresh MethodDefinition per call.
            var transitiveHookCache = new Dictionary<string, bool>();

            // Separate cache: "reaches any hook" and "reaches a non-safe (unsafe / unknown / unverifiable) hook"
            // are distinct predicates and must not share memoized results.
            var nonSafeHookCache = new Dictionary<string, bool>();

            var changed = false;
            foreach (var type in module.GetTypes())
            {
                foreach (var method in type.Methods)
                {
                    if (!IsCandidate(method)) continue;
                    if (!method.HasBody) continue;
                    if (IsAlreadyMemoized(method)) continue;
                    if (TryWeaveMethod(method, context, transitiveHookCache, nonSafeHookCache))
                    {
                        changed = true;
                    }
                }
            }
            return changed;
        }

        // A method is a weave candidate when it is a static component method that returns Velvet.VNode.
        // Auto-memoization applies to every [Component] regardless of the Memoize flag (that flag
        // is the props-bail, an orthogonal axis), unless the component opts out with
        // [Component(Compiler = false)] — an explicit "skip memoization" directive — which
        // leaves the body unwoven. Props-receiving components are candidates: each parameter is prepended to the
        // deps array (see InjectMemoization), so a prop change is detected by the same
        // Object.is comparison as a hook-derived input. By-reference parameters
        // (ref / out / in) are not loadable into an object[] as a plain value, so a
        // method that declares one is left unwoven; canonical [Component] factories take a single
        // by-value record prop and never hit this guard.
        private static bool IsCandidate(MethodDefinition method)
        {
            if (!method.IsStatic) return false;
            if (method.ReturnType.FullName != VNodeFullName) return false;
            foreach (var parameter in method.Parameters)
            {
                if (parameter.ParameterType.IsByReference) return false;
            }
            foreach (var attr in method.CustomAttributes)
            {
                if (attr.AttributeType.FullName == ComponentAttrFullName)
                {
                    return CompilerEnabled(attr);
                }
            }
            return false;
        }

        // Returns true unless the [Component] attribute carries Compiler = false. The property
        // defaults to true (the compiler transform is on for every component), so an
        // absent named argument means weave. A component opts out of memoization by
        // setting Compiler = false, read here as a named-argument false that leaves the body unwoven.
        private static bool CompilerEnabled(CustomAttribute attr)
        {
            foreach (var named in attr.Properties)
            {
                if (named.Name == CompilerPropertyName && named.Argument.Value is bool enabled)
                {
                    return enabled;
                }
            }
            return true;
        }

        // Returns true when the body already calls Velvet.Hooks.TryGetMemoizedVNode — a hand-written
        // memoization (e.g. a test fixture that exercises the slot API directly). Skipping such a body keeps the
        // weaver from memoizing the same component a second time.
        private static bool IsAlreadyMemoized(MethodDefinition method)
        {
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode != OpCodes.Call && instruction.OpCode != OpCodes.Callvirt) continue;
                if (instruction.Operand is MethodReference target
                    && target.Name == TryGetMemoizedVNodeName
                    && target.DeclaringType.FullName == HooksTypeFullName)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryWeaveMethod(MethodDefinition method, WeaverContext context, Dictionary<string, bool> transitiveHookCache, Dictionary<string, bool> nonSafeHookCache)
        {
            if (!TryAnalyze(method, transitiveHookCache, nonSafeHookCache, out var analysis))
            {
                return false;
            }

            InjectMemoization(method, analysis, context);
            return true;
        }

        private static bool TryAnalyze(MethodDefinition method, Dictionary<string, bool> transitiveHookCache, Dictionary<string, bool> nonSafeHookCache, out HookAnalysis analysis)
        {
            analysis = default!;
            var body = method.Body;
            var instructions = body.Instructions;

            var returns = new List<Instruction>();
            foreach (var instr in instructions)
            {
                if (instr.OpCode == OpCodes.Ret)
                {
                    returns.Add(instr);
                }
            }
            if (returns.Count == 0)
            {
                return false;
            }

            // Allow-list gate. Bail the whole method when any call reaches a non-SAFE hook — a known memo-unsafe
            // hook, an unknown / future hook absent from both allow-lists, or a call whose hook safety cannot be
            // confirmed because Resolve() fails. Caching the body when an unguarded hook is in play would
            // short-circuit a re-render the hook drives through a path the deps array does not track.
            foreach (var instr in instructions)
            {
                if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt) continue;
                if (instr.Operand is MethodReference callee && ReachesNonSafeHook(callee, nonSafeHookCache))
                {
                    return false;
                }
            }

            var hookPipedLocals = new List<VariableDefinition>();
            var hookCalls = new List<Instruction>();
            Instruction? lastHookBoundary = null;
            foreach (var instr in instructions)
            {
                if (!IsHookCall(instr, transitiveHookCache, out var hookTarget, out var isDirect)) continue;

                // Record every hook call site so the post-loop pass can verify none is conditionally skipped.
                hookCalls.Add(instr);

                // A direct SAFE void hook (UseEffect and friends) runs for its side effect only and pushes no
                // value, so there is no dep to capture. Advance the boundary past it so the cache gate lands after
                // the whole hook section, and continue without recording a dep. Only a direct hook is treated this
                // way: a void-returning custom hook would hide whatever value hook it composes internally, which
                // the deps array could not capture, so it falls through to the bail below.
                if (isDirect && IsDirectVoidSafeHookCall(hookTarget!))
                {
                    lastHookBoundary = instr;
                    continue;
                }

                var next = instr.Next;
                if (next == null)
                {
                    return false;
                }
                if (next.OpCode == OpCodes.Pop)
                {
                    // A discarded hook result means a reactive input is not captured in the deps array, so
                    // the memo would bail re-renders the discarded value should have triggered. Bail.
                    return false;
                }
                if (TryGetStlocVariable(next, body, out var local))
                {
                    // A whole-tuple capture (`var s = Hooks.UseState(...)`) stores the returned ValueTuple and is
                    // compared via ValueTuple.Equals — per-element EqualityComparer<T>.Default (structural). For a
                    // reference / float Item1 that diverges from the Object.is the reconciler uses to drive a
                    // re-render, so a fresh-but-equal value would be a stale hit. Bail: single-element
                    // deconstruction (`var (value, _) = ...`) captures Item1 directly and is compared soundly.
                    if (IsValueTupleType(local.VariableType))
                    {
                        return false;
                    }
                    hookPipedLocals.Add(local);
                    lastHookBoundary = next;
                    continue;
                }
                // Deconstruction shape that keeps only the first tuple element (the value):
                // `var (value, _) = Hooks.UseXxx(...);` emits `call → ldfld <Item1> → stloc`. Only Item1 is
                // a sound dep — capturing a later element (e.g. the stable setter of UseState while the value
                // is discarded) would leave the changing value out of the deps array. Bail on anything but Item1.
                if (next.OpCode == OpCodes.Ldfld && next.Operand is FieldReference field
                    && next.Next is { } afterLdfld
                    && TryGetStlocVariable(afterLdfld, body, out var deconstructedLocal))
                {
                    if (field.Name != "Item1")
                    {
                        return false;
                    }
                    hookPipedLocals.Add(deconstructedLocal);
                    lastHookBoundary = afterLdfld;
                    continue;
                }
                // Two-element deconstruction that keeps BOTH tuple elements — the idiomatic
                // `var (value, setter) = Hooks.UseState(...)`. Roslyn emits
                //   call -> dup -> ldfld Item1 -> stloc <value> -> ldfld Item2 -> st* <setter>
                // Only Item1 (the value) is a sound dep; Item2 (the reference-stable StateUpdater<T> setter) does
                // not change between renders, so it is NOT a dep. Capture Item1 and advance the boundary PAST the
                // setter store so the cache gate lands after the whole deconstruction (the stack must be balanced:
                // Item2 has to be consumed before the gate runs). Without this branch the leading `dup` matched no
                // shape and bailed the whole component — disabling auto-memo for every `var (x, setX) = ...` site.
                if (next.OpCode == OpCodes.Dup)
                {
                    var item1Ldfld = next.Next;
                    var valueStore = item1Ldfld?.Next;
                    var item2Ldfld = valueStore?.Next;
                    var setterStore = item2Ldfld?.Next;
                    if (item1Ldfld != null && item1Ldfld.OpCode == OpCodes.Ldfld
                        && item1Ldfld.Operand is FieldReference item1Field && item1Field.Name == "Item1"
                        && valueStore != null && TryGetStlocVariable(valueStore, body, out var tupleValueLocal)
                        && item2Ldfld != null && item2Ldfld.OpCode == OpCodes.Ldfld
                        && item2Ldfld.Operand is FieldReference item2Field && item2Field.Name == "Item2"
                        && setterStore != null && IsValueConsumingStore(setterStore))
                    {
                        hookPipedLocals.Add(tupleValueLocal);
                        lastHookBoundary = setterStore;
                        continue;
                    }
                    // A dup that is not the canonical two-element tuple deconstruction: unknown shape, bail.
                    return false;
                }
                // Hook return consumed by an unsupported pattern. Bail: the deps array would be incomplete.
                return false;
            }

            if (lastHookBoundary == null)
            {
                // No hook calls: nothing to key a cache on, so there is no inner memo to weave.
                return false;
            }

            if (hookPipedLocals.Count == 0 && method.Parameters.Count == 0)
            {
                // Only void hooks and no parameters: the deps array would be empty, so TryGetMemoizedVNode would
                // be an unconditional hit and freeze the body after the first render. A component with no reactive
                // input (no props, no value hook) is constant, so leave it unwoven rather than always-hit.
                return false;
            }

            // Every return path must come after the hook section. A return placed
            // before/inside the hook section means hooks are skipped on that path,
            // which would corrupt the position-based slot allocation.
            foreach (var ret in returns)
            {
                if (ret.Offset <= lastHookBoundary.Offset)
                {
                    return false;
                }
            }

            // Rules of hooks: every hook call must execute unconditionally on every render. If a forward branch
            // can jump OVER a hook call — `if (cond) { UseXxx(); }` — that hook is conditional. Weaving such a
            // component is unsound (the position-based slot allocation breaks) AND would place the cache gate
            // (anchored at lastHookBoundary) inside a conditional block, producing malformed IL. Bail and let the
            // runtime rules-of-hooks backstop report the violation. (A benign branch — a ternary in a hook arg or
            // between hooks — converges BEFORE the next hook, so its target is not past a hook and is not flagged.)
            if (HasConditionallySkippedHook(body, hookCalls))
            {
                return false;
            }

            // Injecting a raw `Ret` for the cache-hit path or wrapping a `Ret` inside
            // a `try`/`finally`/`catch` would bypass the `Leave` protocol that CLR
            // requires for protected regions, producing invalid IL. Bail out; a future
            // Leave-aware version of this weaver could handle these shapes instead.
            if (body.HasExceptionHandlers
                && IsInsideAnyHandler(body, lastHookBoundary, returns))
            {
                return false;
            }

            analysis = new HookAnalysis(hookPipedLocals, lastHookBoundary, returns);
            return true;
        }

        private static bool IsInsideAnyHandler(MethodBody body, Instruction insertAfter, IReadOnlyList<Instruction> returns)
        {
            foreach (var eh in body.ExceptionHandlers)
            {
                if (Overlaps(eh.TryStart, eh.TryEnd, insertAfter)) return true;
                if (eh.HandlerStart != null && Overlaps(eh.HandlerStart, eh.HandlerEnd, insertAfter)) return true;
                if (eh.FilterStart != null && Overlaps(eh.FilterStart, eh.HandlerStart, insertAfter)) return true;
                foreach (var ret in returns)
                {
                    if (Overlaps(eh.TryStart, eh.TryEnd, ret)) return true;
                    if (eh.HandlerStart != null && Overlaps(eh.HandlerStart, eh.HandlerEnd, ret)) return true;
                    if (eh.FilterStart != null && Overlaps(eh.FilterStart, eh.HandlerStart, ret)) return true;
                }
            }
            return false;
        }

        private static bool Overlaps(Instruction? rangeStart, Instruction? rangeEnd, Instruction target)
        {
            if (rangeStart == null) return false;
            return rangeStart.Offset <= target.Offset
                && (rangeEnd == null || target.Offset < rangeEnd.Offset);
        }

        private static bool IsHookCall(Instruction instr, Dictionary<string, bool> transitiveHookCache, out MethodReference? target, out bool isDirect)
        {
            target = null;
            isDirect = false;
            if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt)
            {
                return false;
            }
            if (instr.Operand is not MethodReference method) return false;

            if (IsDirectHookCall(method))
            {
                target = method;
                isDirect = true;
                return true;
            }

            // Transitive: the callee itself calls one of Velvet.Hooks.UseXxx (possibly through further
            // hops). By design, custom hooks (functions that compose hooks via plain method calls)
            // participate in deps capture without any opt-in attribute — custom-hook chains are
            // tracked transparently.
            if (CallsHookTransitively(method, transitiveHookCache))
            {
                target = method;
                return true;
            }

            return false;
        }

        private static bool IsDirectHookCall(MethodReference method)
            => method.DeclaringType.FullName == HooksTypeFullName
                && PositionalHookMethodNames.Contains(method.Name);

        private static bool IsDirectVoidSafeHookCall(MethodReference method)
            => method.DeclaringType.FullName == HooksTypeFullName
                && MemoSafeVoidHookNames.Contains(method.Name)
                && ReturnsVoid(method);

        private static bool ReturnsVoid(MethodReference? method)
            => method != null && method.ReturnType.FullName == SystemVoidFullName;

        // True when the local stores a whole ValueTuple (e.g. the (value, setter) returned by UseState captured
        // without deconstruction). Boxing the tuple and comparing it with ValueTuple.Equals applies per-element
        // EqualityComparer<T>.Default, which diverges from the Object.is the reconciler uses to drive a re-render
        // for a reference / float Item1, so such a capture is left unwoven.
        private static bool IsValueTupleType(TypeReference type)
            => type is GenericInstanceType git
                && git.ElementType.FullName.StartsWith("System.ValueTuple`", System.StringComparison.Ordinal);

        // A direct Velvet.Hooks.UseXxx call is SAFE iff it is on either allow-list.
        private static bool IsDirectSafeHookCall(MethodReference method)
            => method.DeclaringType.FullName == HooksTypeFullName
                && (MemoSafeValueHookNames.Contains(method.Name) || MemoSafeVoidHookNames.Contains(method.Name));

        // Returns true when method's body (recursively, across plain method calls)
        // reaches a direct HooksTypeFullName hook call. Cache by FullName: per-weave
        // memoization is essential because the same V.* DSL factories and store accessors appear in
        // every component body, and Resolve() walks metadata each invocation.
        // Cycle safety: each method is provisionally marked false before its body is scanned,
        // so a recursive self-reference (or a cycle in the call graph) returns false on the
        // re-entrance and the final result is committed only after the scan completes. The cache is
        // fully populated by the recursive descent, so the second visit returns in O(1).
        private static bool CallsHookTransitively(MethodReference method, Dictionary<string, bool> cache)
        {
            // A call into a well-known framework namespace (BCL / Unity / UniTask) cannot reach a Velvet hook,
            // so it calls no hook and needs neither Resolve() nor a body walk. This mirrors the namespace
            // short-circuit ReachesNonSafeHook already relies on, scoping the Resolve()/descent below to calls
            // that could plausibly compose a Velvet hook (Velvet DSL / app-defined custom hooks).
            if (CannotReachVelvetHook(method)) return false;

            var key = method.FullName;
            if (cache.TryGetValue(key, out var cached)) return cached;

            // Resolve() requires the referenced assembly to be reachable from the IL post-processor.
            // Cross-assembly failures (or body-less non-virtual methods — pinvoke / runtime impls) are
            // treated as "does not call hooks" and never block weaving on their own; the safety gate
            // (ReachesNonSafeHook) is what bails a method on an unverifiable callee.
            MethodDefinition? def;
            try
            {
                def = method.Resolve();
            }
            catch (System.Exception)
            {
                cache[key] = false;
                return false;
            }
            if (def == null)
            {
                cache[key] = false;
                return false;
            }
            if (IsDispatchOpen(def))
            {
                // An open virtual / interface dispatch resolves only to the statically declared method — the
                // runtime override's body is not the one below, and that override can be declared in an
                // assembly that references Velvet even when the statically declared base/interface's own
                // assembly does not — checking the DECLARING assembly for a Velvet reference proves nothing
                // about where an override can live. The CannotReachVelvetHook check above already excluded the
                // only case this method can rule out (a BCL / Unity / UniTask namespace root); every other
                // open dispatch reached this far is treated as reaching a hook, consistent with the safety
                // gate below, which applies the identical rule.
                cache[key] = true;
                return true;
            }
            if (!def.HasBody)
            {
                cache[key] = false;
                return false;
            }

            // Provisionally mark false so a recursive re-entrance for the same method short-circuits
            // without re-walking its body. The final result is written back after the full body scan.
            cache[key] = false;
            foreach (var instr in def.Body.Instructions)
            {
                if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt) continue;
                if (instr.Operand is not MethodReference callee) continue;

                if (IsDirectHookCall(callee) || CallsHookTransitively(callee, cache))
                {
                    cache[key] = true;
                    return true;
                }
            }
            cache[key] = false;
            return false;
        }

        // True when a Velvet.Hooks.* member is a positional hook NOT on the SAFE allow-list — a known memo-unsafe
        // hook (Use / UseMutation / UseBlocker / UseFallback) or a future positional hook nobody has classified
        // yet. A Hooks.* member that is not a positional hook (framework plumbing such as TryGetMemoizedVNode)
        // is not a reactive hook and is treated as safe.
        private static bool IsDirectNonSafeHookCall(MethodReference method)
            => method.DeclaringType.FullName == HooksTypeFullName
                && PositionalHookMethodNames.Contains(method.Name)
                && !IsDirectSafeHookCall(method);

        // Namespace roots whose members cannot transitively reach a Velvet hook: the runtime / Unity / UniTask
        // surfaces a component body calls for non-hook work (ToString, string.Concat, V is excluded as it lives
        // under Velvet). A call into one of these is a SAFE leaf, so the weaver neither resolves nor descends it.
        private static readonly string[] NonVelvetNamespaceRoots =
        {
            "System.",
            "Unity.",
            "UnityEngine.",
            "UnityEditor.",
            "Cysharp.",
            "Mono.",
        };

        // Returns true when method's declaring type lives in a framework namespace that
        // cannot reach a Velvet hook. The check is conservative: only well-known runtime / Unity / UniTask
        // roots short-circuit. Anything outside them (Velvet types, app-defined custom hooks, unknown
        // third-party code) is resolved and descended so a transitively composed hook is never missed.
        private static bool CannotReachVelvetHook(MethodReference method)
        {
            var declaringFullName = method.DeclaringType.FullName;
            foreach (var root in NonVelvetNamespaceRoots)
            {
                if (declaringFullName.StartsWith(root, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        // True when a call to this definition may dispatch to an override the static resolution does not
        // reveal: the method is virtual (interface members are virtual in metadata) and still overridable —
        // neither sealed itself nor declared on a sealed class, where no further override can exist. The
        // sealed-type carve-out is what keeps delegate invocations out of the bail: a delegate's Invoke is
        // virtual in metadata but declared on a sealed type. A hook reached only through delegate
        // indirection stays outside this static model (as it always has), which is a rules-of-hooks
        // violation the analyzer layer reports.
        private static bool IsDispatchOpen(MethodDefinition def)
            => def.IsVirtual
                && !def.IsFinal
                && !(def.DeclaringType.IsSealed && !def.DeclaringType.IsInterface);

        // Returns true when method reaches a non-SAFE hook, directly or transitively across
        // plain method calls. A hook is non-SAFE when it is a positional hook absent from both allow-lists
        // (known memo-unsafe or unknown / future), or when hook safety cannot be confirmed because
        // Resolve() fails for a callee whose body the weaver must inspect. Treating an unverifiable callee
        // as non-SAFE keeps the allow-list a closed safety contract: the weaver only weaves a body whose every
        // reachable hook is proven SAFE. A custom hook that composes UseMutation / Use is caught
        // here. Cycle safety follows the same rules as CallsHookTransitively.
        private static bool ReachesNonSafeHook(MethodReference method, Dictionary<string, bool> cache)
        {
            // A direct Velvet.Hooks.* call is classified by name alone: never descend into a hook's own body,
            // whose framework internals would be misread as the component's reachable hook set.
            if (method.DeclaringType.FullName == HooksTypeFullName)
            {
                return IsDirectNonSafeHookCall(method);
            }

            // A call into a well-known framework namespace (BCL / Unity / UniTask) cannot reach a Velvet hook,
            // so it is SAFE without resolving. This scopes the conservative Resolve-failure bail below to calls
            // that could plausibly compose a Velvet hook (Velvet DSL / app-defined custom hooks), instead of
            // bailing every component on an unresolvable BCL call like object.ToString or string.Concat.
            if (CannotReachVelvetHook(method))
            {
                return false;
            }

            var key = method.FullName;
            if (cache.TryGetValue(key, out var cached)) return cached;

            // Resolve() requires the referenced assembly to be reachable from the IL post-processor. A failure
            // (or a body-less method) means the weaver cannot inspect the callee to confirm it reaches no
            // non-SAFE hook. Bail conservatively: an unverifiable callee is treated as reaching a non-SAFE hook,
            // so the whole method is left unwoven rather than woven on an unproven assumption.
            MethodDefinition? def;
            try
            {
                def = method.Resolve();
            }
            catch (System.Exception)
            {
                cache[key] = true;
                return true;
            }
            if (def == null)
            {
                cache[key] = true;
                return true;
            }
            if (IsDispatchOpen(def))
            {
                // Resolve() on a virtual / abstract / interface target returns only the statically declared
                // method; the runtime dispatch may land on an override whose body the weaver cannot
                // enumerate, and that override can be declared in an assembly that references Velvet even
                // when the statically declared base/interface's own assembly does not — checking the
                // DECLARING assembly for a Velvet reference proves nothing about where an override can live.
                // The only case this weaver can rule out is the CannotReachVelvetHook namespace carve-out
                // checked above (a BCL / Unity / UniTask virtual signature — e.g. ToString — cannot itself
                // declare a hook call, though a user override of one still could, which stays outside this
                // static model and is a rules-of-hooks violation the analyzer layer reports). Every other
                // open dispatch reached this far is unverifiable and folds into the non-SAFE bucket like a
                // Resolve() failure.
                //
                // This also bails the common `var svc = Hooks.UseService<IFoo>(); svc.Method()` pattern even
                // when IFoo is an internal interface implemented only inside this same module — a case that
                // could in principle be proven hook-free from Cecil metadata alone (enumerate every type in
                // the module implementing IFoo, require the interface to lack an InternalsVisibleTo friend,
                // and verify every implementer's body reaches no hook). That proof was judged too fragile to
                // add here: it would have to separately rule out an implementation inherited through a base
                // class this module also declares, a further override of a non-sealed public implementer
                // from outside the module, generic interfaces/methods, and explicit interface
                // implementations, while reconciling two different recursive predicates (CallsHookTransitively's
                // "reaches any hook" and this method's "reaches a non-SAFE hook") over the same implementer
                // set without the two walkers disagreeing. Getting any one of those wrong would silently
                // reopen the exact soundness hole this bail exists to close, so the pattern stays bailed;
                // wrap the interface call in UseMemo/UseCallback to capture it as an explicit dep instead.
                cache[key] = true;
                return true;
            }
            if (!def.HasBody)
            {
                // A body-less non-virtual method (pinvoke / extern / runtime-implemented) composes no hooks
                // the weaver could miss: it has no IL that could call a Velvet.Hooks member. Treat as SAFE.
                cache[key] = false;
                return false;
            }

            // Provisionally mark SAFE so a recursive re-entrance for the same method short-circuits without
            // re-walking its body. The final result is written back after the full body scan.
            cache[key] = false;
            foreach (var instr in def.Body.Instructions)
            {
                if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt) continue;
                if (instr.Operand is not MethodReference callee) continue;

                if (ReachesNonSafeHook(callee, cache))
                {
                    cache[key] = true;
                    return true;
                }
            }
            cache[key] = false;
            return false;
        }

        private static bool TryGetStlocVariable(Instruction instr, MethodBody body, out VariableDefinition variable)
        {
            variable = null!;
            if (instr.OpCode == OpCodes.Stloc_0) { variable = body.Variables[0]; return true; }
            if (instr.OpCode == OpCodes.Stloc_1) { variable = body.Variables[1]; return true; }
            if (instr.OpCode == OpCodes.Stloc_2) { variable = body.Variables[2]; return true; }
            if (instr.OpCode == OpCodes.Stloc_3) { variable = body.Variables[3]; return true; }
            if ((instr.OpCode == OpCodes.Stloc || instr.OpCode == OpCodes.Stloc_S)
                && instr.Operand is VariableDefinition v)
            {
                variable = v;
                return true;
            }
            return false;
        }

        // Recognizes the consumer of a tuple's Item2 (the UseState setter) in the two-element deconstruction
        // shape: a store to a local (`var (v, setV) = ...`) or, when the setter is assigned straight to a static
        // field, an stsfld. An instance-field target (stfld) cannot occur here — Roslyn spills the whole tuple to
        // a temp first, which is caught earlier by the whole-tuple (ValueTuple) bail — so it is deliberately NOT
        // accepted: any unexpected store shape bails (fail-safe = unwoven) rather than risk an unbalanced stack.
        private static bool IsValueConsumingStore(Instruction instr)
        {
            var op = instr.OpCode;
            return op == OpCodes.Stloc_0 || op == OpCodes.Stloc_1 || op == OpCodes.Stloc_2
                || op == OpCodes.Stloc_3 || op == OpCodes.Stloc || op == OpCodes.Stloc_S
                || op == OpCodes.Stsfld;
        }

        // True when some branch can skip or repeat a hook call, i.e. a hook does not execute exactly once per
        // render (`if (cond) { UseXxx(); }`, a hook inside a loop, a hook in one arm of a branch). Such a
        // component violates the rules of hooks and must not be woven. Two shapes are detected:
        //   A forward branch whose target lands strictly AFTER a hook while the branch itself is strictly
        //   BEFORE it can skip the hook (`if`, and the head-tested loops, which enter through a forward jump
        //   past the body).
        //   A backward branch that jumps from AFTER a hook to AT-OR-BEFORE it re-enters the hook — the hook
        //   sits inside a loop body. A `do { UseXxx(); } while (cond)` is lowered with only this back-edge
        //   (no forward jump precedes the body), so forward-skip detection alone cannot see it; weaving it
        //   would also anchor the cache gate inside the loop, where its early return on a hit would abandon
        //   the remaining iterations and everything after the loop.
        // A benign branch (ternary in/around a hook arg, a loop that does not contain a hook) converges before
        // the next hook / never crosses one, so neither test fires.
        private static bool HasConditionallySkippedHook(MethodBody body, List<Instruction> hookCalls)
        {
            if (hookCalls.Count == 0) return false;
            foreach (var instr in body.Instructions)
            {
                var flow = instr.OpCode.FlowControl;
                if (flow != FlowControl.Branch && flow != FlowControl.Cond_Branch) continue;
                switch (instr.Operand)
                {
                    case Instruction target when SkipsAnyHook(instr, target, hookCalls):
                        return true;
                    case Instruction[] targets:
                        foreach (var t in targets)
                        {
                            if (SkipsAnyHook(instr, t, hookCalls)) return true;
                        }
                        break;
                }
            }
            return false;

            static bool SkipsAnyHook(Instruction branch, Instruction target, List<Instruction> hooks)
            {
                foreach (var h in hooks)
                {
                    // Forward jump over the hook: the hook can be skipped entirely.
                    if (branch.Offset < h.Offset && h.Offset < target.Offset) return true;
                    // Backward jump across the hook: the hook sits inside a loop body and can repeat.
                    if (branch.Offset > h.Offset && target.Offset <= h.Offset) return true;
                }
                return false;
            }
        }

        private static void InjectMemoization(MethodDefinition method, HookAnalysis analysis, WeaverContext context)
        {
            var body = method.Body;
            var module = method.Module;
            var il = body.GetILProcessor();

            body.SimplifyMacros();

            var vnodeType = context.VNode;
            var objectType = module.TypeSystem.Object;
            var depsLocal = new VariableDefinition(new ArrayType(objectType));
            var slotLocal = new VariableDefinition(module.TypeSystem.Int32);
            var cachedLocal = new VariableDefinition(vnodeType);
            var resultLocal = new VariableDefinition(vnodeType);
            body.Variables.Add(depsLocal);
            body.Variables.Add(slotLocal);
            body.Variables.Add(cachedLocal);
            body.Variables.Add(resultLocal);

            var insertAfter = analysis.LastHookBoundary;
            // Deps layout: each component parameter (a reactive prop) first, then every value flowing out of a
            // hook call. A prop change is therefore a miss under the same Object.is comparison as a hook input.
            var parameters = method.Parameters;
            var paramCount = parameters.Count;
            var depsCount = paramCount + analysis.HookPipedLocals.Count;

            var injected = new List<Instruction>(20 + depsCount * 4);
            injected.Add(Instruction.Create(OpCodes.Ldc_I4, depsCount));
            injected.Add(Instruction.Create(OpCodes.Newarr, objectType));
            for (var i = 0; i < paramCount; i++)
            {
                // Static [Component] method, so the i-th parameter is loaded by ldarg.i.
                var paramType = parameters[i].ParameterType;
                injected.Add(Instruction.Create(OpCodes.Dup));
                injected.Add(Instruction.Create(OpCodes.Ldc_I4, i));
                injected.Add(Instruction.Create(OpCodes.Ldarg, parameters[i]));
                if (paramType.IsValueType || paramType.IsGenericParameter)
                {
                    injected.Add(Instruction.Create(OpCodes.Box, paramType));
                }
                injected.Add(Instruction.Create(OpCodes.Stelem_Ref));
            }
            for (var i = 0; i < analysis.HookPipedLocals.Count; i++)
            {
                var local = analysis.HookPipedLocals[i];
                injected.Add(Instruction.Create(OpCodes.Dup));
                injected.Add(Instruction.Create(OpCodes.Ldc_I4, paramCount + i));
                injected.Add(Instruction.Create(OpCodes.Ldloc, local));
                if (local.VariableType.IsValueType || local.VariableType.IsGenericParameter)
                {
                    injected.Add(Instruction.Create(OpCodes.Box, local.VariableType));
                }
                injected.Add(Instruction.Create(OpCodes.Stelem_Ref));
            }
            injected.Add(Instruction.Create(OpCodes.Stloc, depsLocal));

            injected.Add(Instruction.Create(OpCodes.Ldloc, depsLocal));
            injected.Add(Instruction.Create(OpCodes.Ldloca, slotLocal));
            injected.Add(Instruction.Create(OpCodes.Ldloca, cachedLocal));
            injected.Add(Instruction.Create(OpCodes.Call, context.TryGetMemoizedVNode));

            var afterHitBranch = Instruction.Create(OpCodes.Nop);
            injected.Add(Instruction.Create(OpCodes.Brfalse, afterHitBranch));
            injected.Add(Instruction.Create(OpCodes.Ldloc, cachedLocal));
            injected.Add(Instruction.Create(OpCodes.Ret));
            injected.Add(afterHitBranch);

            var current = insertAfter;
            foreach (var ins in injected)
            {
                il.InsertAfter(current, ins);
                current = ins;
            }

            // Inject Store + reload at every return path so all `Ret` instructions
            // share the same memoization commit, regardless of which branch produced
            // the VNode.
            foreach (var returnInstr in analysis.Returns)
            {
                var preReturn = new[]
                {
                    Instruction.Create(OpCodes.Stloc, resultLocal),
                    Instruction.Create(OpCodes.Ldloc, slotLocal),
                    Instruction.Create(OpCodes.Ldloc, depsLocal),
                    Instruction.Create(OpCodes.Ldloc, resultLocal),
                    Instruction.Create(OpCodes.Call, context.StoreMemoizedVNode),
                    Instruction.Create(OpCodes.Ldloc, resultLocal),
                };
                foreach (var ins in preReturn)
                {
                    il.InsertBefore(returnInstr, ins);
                }
            }

            body.OptimizeMacros();
        }

        private readonly struct HookAnalysis
        {
            public HookAnalysis(IReadOnlyList<VariableDefinition> hookPipedLocals,
                Instruction lastHookBoundary,
                IReadOnlyList<Instruction> returns)
            {
                HookPipedLocals = hookPipedLocals;
                LastHookBoundary = lastHookBoundary;
                Returns = returns;
            }
            public IReadOnlyList<VariableDefinition> HookPipedLocals { get; }
            public Instruction LastHookBoundary { get; }
            public IReadOnlyList<Instruction> Returns { get; }
        }
    }

    internal sealed class WeaverContext
    {
        public required TypeReference VNode { get; init; }
        public required MethodReference TryGetMemoizedVNode { get; init; }
        public required MethodReference StoreMemoizedVNode { get; init; }

        // Resolves the Velvet runtime members the weaver injects calls to. On failure, returns null and
        // reports what could not be resolved through failure so the caller can emit a diagnostic
        // instead of silently skipping the assembly.
        public static WeaverContext? TryResolve(ModuleDefinition module, out string failure)
        {
            failure = string.Empty;

            var hooksType = module.GetType("Velvet.Hooks")
                ?? WeaverDiagnostics.ResolveExternal(module, "Velvet.Hooks");
            if (hooksType == null)
            {
                failure = "the type 'Velvet.Hooks' could not be resolved from the assembly's references.";
                return null;
            }

            var vnodeType = module.GetType("Velvet.VNode")
                ?? WeaverDiagnostics.ResolveExternal(module, "Velvet.VNode");
            if (vnodeType == null)
            {
                failure = "the type 'Velvet.VNode' could not be resolved from the assembly's references.";
                return null;
            }

            var tryGet = ResolveHookMethod(hooksType, "TryGetMemoizedVNode");
            var store = ResolveHookMethod(hooksType, "StoreMemoizedVNode");
            if (tryGet == null || store == null)
            {
                failure = "the memoization methods 'Velvet.Hooks.TryGetMemoizedVNode' /"
                    + " 'Velvet.Hooks.StoreMemoizedVNode' could not be resolved on the referenced"
                    + " Velvet assembly.";
                return null;
            }

            return new WeaverContext
            {
                VNode = module.ImportReference(vnodeType),
                TryGetMemoizedVNode = module.ImportReference(tryGet),
                StoreMemoizedVNode = module.ImportReference(store),
            };
        }

        private static MethodDefinition? ResolveHookMethod(TypeDefinition hooks, string name)
        {
            // Both TryGetMemoizedVNode and StoreMemoizedVNode take 3 parameters.
            // Asserting the arity guards against silently picking up a future overload.
            foreach (var m in hooks.Methods)
            {
                if (m.Name == name && m.Parameters.Count == 3) return m;
            }
            return null;
        }
    }
}

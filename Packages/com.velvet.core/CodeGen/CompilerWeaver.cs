using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;
using Unity.CompilationPipeline.Common.Diagnostics;

namespace Velvet.CodeGen
{
    // Build-time transform that weaves inner auto-memoization into every [Component] method,
    // default-on with no opt-in attribute. Hooks still run on every render; only the VNode construction
    // is cached, keyed on the component's parameters and the values that flow out of its hook calls, so the
    // component body is rebuilt when one of those inputs changes.
    // The weaver only transforms a method when its body matches an analyzable shape it can prove correct:
    //   Static, returns Velvet.VNode, and carries [Component]. Parameters (props) are
    //   allowed: each is prepended to the deps array so a prop change is detected like any other input.
    //   Every hook the scan sees the body reach — directly or transitively through a custom hook — is on the SAFE
    //   allow-list (MemoSafeValueHookNames ∪ MemoSafeVoidHookNames). A hook that is
    //   not on the allow-list (a known memo-unsafe hook, or an unknown / future hook) bails the whole method.
    //   The allow-list is the safety contract: a hook is admitted only when its re-render trigger is soundly
    //   represented by an Object.is comparison of a value captured into the deps array, or when it
    //   returns nothing reactive at all.
    //   At least one parameter or value-returning hook call, so the deps array is not empty. A body that
    //   calls a hook has every return placed after the last hook call, every hook reached unconditionally,
    //   and no hook inside a loop (Rules of Hooks); a body that calls none is gated at method entry unless
    //   it sets Memoize = true.
    //   Each value-returning hook result captured via var x = Hooks.UseXxx(...) (IL
    //   call → stloc) or a single-element deconstruction var (x, _, _) = Hooks.UseXxx(...)
    //   (IL call → ldfld → stloc), or left on the stack for whatever reads it next (IL call, or call → ldfld
    //   Item1, where no shape above matches and the next instruction is neither a pop nor a dup) — a hook value
    //   read once, or passed straight as an argument. A void hook
    //   (UseEffect and friends) captures no dep but still advances the hook boundary so the cache gate is
    //   injected after it.
    //   No hook call or return inside a try/catch/finally region (the Leave protocol is not woven).
    // The deps array (component parameters followed by the values flowing out of value-returning hook calls) is
    // compared with Object.is, the same strictness the reconciler and Provider use to drive a re-render,
    // so a fresh record prop or a changed context value is treated as a miss rather than a stale hit.
    // Any shape the weaver cannot prove correct — a discarded hook result, a deconstruction that drops the
    // value element, a body that reaches a non-allow-listed hook, a callee whose body Resolve() cannot reach,
    // a return before the hook section, a hook skipped or repeated by a branch, a hook return consumed by an
    // unsupported pattern, or a protected region overlapping the hook section — is left byte-for-byte unchanged
    // (graceful bailout). The allow-list defaults unknown hooks to bail, so correctness is never traded for
    // coverage; no diagnostic is emitted for a bailout. A body whose processing throws is restored to what it was
    // and reported as a warning, so a defect here costs one component its memoization rather than the build.
    // The goal is "memoize less but never wrong".
    // An open virtual / interface dispatch outside the BCL / Unity carve-out is opaque rather than a bail: an
    // override composing a hook can live in an assembly this scan never sees, so whether it reaches one is
    // decided where it runs. Ahead of the gate it bails the method, since a hook there would feed the body a
    // value the deps array does not capture; past the gate, Hooks.TryGetMemoizedVNode stops serving hits to a
    // body once a hook has run between its gate and its commit. An override of a BCL / Unity virtual signature
    // (object.ToString, say) is not opaque where the call names the BCL / Unity declaration: the carve-out reads
    // it as hook-free without resolving it, so a hook such an override calls ahead of the gate is outside this
    // model.
    // A body that already contains a Velvet.Hooks.TryGetMemoizedVNode call (a hand-written memoization,
    // e.g. in a test fixture) is skipped so the weaver does not memoize it a second time.
    internal static class CompilerWeaver
    {
        // This assembly references Velvet, so every name it matches against is taken from the type system
        // rather than spelled out: a rename on the runtime side then fails to compile here instead of
        // turning the weaver into a silent no-op.
        private static readonly string ComponentAttrFullName = typeof(Velvet.ComponentAttribute).FullName;
        private const string CompilerPropertyName = nameof(Velvet.ComponentAttribute.Compiler);
        private const string MemoizePropertyName = nameof(Velvet.ComponentAttribute.Memoize);
        private static readonly string HooksTypeFullName = typeof(Velvet.Hooks).FullName;
        private static readonly string VNodeFullName = typeof(Velvet.VNode).FullName;
        private static readonly string SystemVoidFullName = typeof(void).FullName;
        private const string TryGetMemoizedVNodeName = nameof(Velvet.Hooks.TryGetMemoizedVNode);

        private static readonly HashSet<string> PositionalHookMethodNames =
            new(Velvet.PositionalHookNames.All);

        // SAFE value-returning hooks. Each returns a value that is captured into the deps array and compared with
        // Object.is, the same strictness the reconciler and Provider use to drive a re-render. Admitting one of
        // these is sound because every reactive change the hook can drive is observable as an Object.is difference
        // in the captured return value:
        //   UseState / UseReducer / UseStore / UseSyncExternalStore / UseOptimistic — the current value flows out and
        //     changes on update.
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
            nameof(Velvet.Hooks.UseSyncExternalStore),
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

            // One fold of each kind per weave, shared by every candidate body: "what a call reaches" and "reaches a
            // non-safe (unsafe / unknown / unverifiable) hook" are distinct predicates and must not share results.
            var reach = new HookReachFold();
            var nonSafe = new NonSafeHookFold();

            var changed = false;
            foreach (var type in module.GetTypes())
            {
                foreach (var method in type.Methods)
                {
                    if (!IsCandidate(method)) continue;
                    if (!method.HasBody) continue;
                    if (IsAlreadyMemoized(method)) continue;
                    if (TryWeaveMethod(method, context, ref reach, ref nonSafe, diagnostics))
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
            => NamedFlag(attr, CompilerPropertyName, fallback: true);

        // Memoize = true already suppresses the hookless body's equal-props parent renders. A second props gate
        // inside that body would add a deps array without serving the path the outer gate suppressed.
        private static bool RequestsPropsBail(MethodDefinition method)
        {
            foreach (var attr in method.CustomAttributes)
            {
                if (attr.AttributeType.FullName == ComponentAttrFullName)
                {
                    return NamedFlag(attr, MemoizePropertyName, fallback: false);
                }
            }
            // MUTANT_SURVIVES(unreachable): only TryAnalyze calls this, on a method IsCandidate admitted for its [Component].
            return false;
        }

        private static bool NamedFlag(CustomAttribute attr, string propertyName, bool fallback)
        {
            foreach (var named in attr.Properties)
            {
                if (named.Name == propertyName && named.Argument.Value is bool value)
                {
                    return value;
                }
            }
            return fallback;
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

        // The per-method boundary: a failure while analyzing or weaving one body leaves that body as it was and
        // is reported as a warning, so a weaver defect costs the one component its memoization rather than the
        // user's build. The folds are replaced because the walk that threw leaves them mid-descent.
        private static bool TryWeaveMethod(MethodDefinition method, WeaverContext context, ref HookReachFold reach,
            ref NonSafeHookFold nonSafe, List<DiagnosticMessage> diagnostics)
        {
            var snapshot = new BodySnapshot(method.Body);
            try
            {
                if (!TryAnalyze(method, reach, nonSafe, out var analysis))
                {
                    return false;
                }

                InjectMemoization(method, analysis, context);
                return true;
            }
            catch (System.Exception exception)
            {
                snapshot.Restore();
                reach = new HookReachFold();
                nonSafe = new NonSafeHookFold();
                diagnostics.Add(new DiagnosticMessage
                {
                    DiagnosticType = DiagnosticType.Warning,
                    MessageData = WeaverDiagnostics.FormatMethodFailureWarning(method, exception),
                });
                return false;
            }
        }

        // What InjectMemoization and the macro rewrites around it change in a body: the instruction list, each
        // instruction's opcode and operand, and the variable list. Handlers and debug information refer to the
        // instructions themselves, which are kept.
        private sealed class BodySnapshot
        {
            private readonly MethodBody _body;
            private readonly Instruction[] _instructions;
            private readonly OpCode[] _opCodes;
            private readonly object?[] _operands;
            private readonly int _variableCount;

            public BodySnapshot(MethodBody body)
            {
                _body = body;
                _instructions = new Instruction[body.Instructions.Count];
                body.Instructions.CopyTo(_instructions, 0);
                _opCodes = new OpCode[_instructions.Length];
                _operands = new object?[_instructions.Length];
                for (var i = 0; i < _instructions.Length; i++)
                {
                    _opCodes[i] = _instructions[i].OpCode;
                    _operands[i] = _instructions[i].Operand;
                }
                _variableCount = body.Variables.Count;
            }

            public void Restore()
            {
                _body.Instructions.Clear();
                for (var i = 0; i < _instructions.Length; i++)
                {
                    _instructions[i].OpCode = _opCodes[i];
                    _instructions[i].Operand = _operands[i];
                    _body.Instructions.Add(_instructions[i]);
                }
                // Adding links each instruction to the one before it, so the first keeps the Previous the weave gave it.
                // MUTANT_SURVIVES(unreachable): only a method that HasBody is woven, and such a body holds at least the instruction that ends it, so the snapshot is never empty.
                if (_instructions.Length > 0)
                {
                    _instructions[0].Previous = null;
                }
                while (_body.Variables.Count > _variableCount)
                {
                    _body.Variables.RemoveAt(_body.Variables.Count - 1);
                }
            }
        }

        private static bool TryAnalyze(MethodDefinition method, HookReachFold reach, NonSafeHookFold nonSafe, out HookAnalysis analysis)
        {
            analysis = default!;
            var body = method.Body;
            var instructions = body.Instructions;

            var returns = CollectReturns(instructions);
            if (returns.Count == 0)
            {
                return false;
            }

            if (ReachesAnyNonSafeHook(instructions, nonSafe))
            {
                return false;
            }

            var hookPipedLocals = new List<(VariableDefinition? Local, TypeReference Type, Instruction Stored)>();
            var hookCalls = new List<Instruction>();
            if (!TryScanHookSection(body, reach, hookPipedLocals, hookCalls,
                    out var lastHookBoundary))
            {
                return false;
            }

            if (hookPipedLocals.Count == 0 && method.Parameters.Count == 0)
            {
                // No parameter and no value hook: the deps array would be empty, so TryGetMemoizedVNode would
                // be an unconditional hit and freeze the body after the first render. A component with no reactive
                // input (no props, no value hook) is constant, so leave it unwoven rather than always-hit.
                return false;
            }

            if (lastHookBoundary is null)
            {
                // No hook call: the gate keys on the parameters alone and goes ahead of the first instruction, so
                // it precedes every return and every protected region, and a hit's early return skips no hook
                // call. The checks below have nothing to find.
                analysis = new HookAnalysis(hookPipedLocals, null, returns, 0);
                return !RequestsPropsBail(method);
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

            if (HasOpaqueCallAhead(instructions, lastHookBoundary, reach))
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

            analysis = new HookAnalysis(hookPipedLocals, lastHookBoundary, returns,
                StackDepthAfter(body, lastHookBoundary));
            return true;
        }

        // One walk in instruction order is enough because ECMA-335 III.1.7.5 requires it to be: valid IL lets a
        // single forward pass infer the stack at every instruction, and leaves it empty where an instruction
        // after an unconditional branch is reached by no earlier branch. No handler's entry depth is seeded:
        // TryAnalyze has refused a boundary inside a protected region or a handler before this runs, and
        // control leaves one only through `leave`, which empties the stack, or through `endfinally`,
        // `endfilter`, `throw` or `rethrow`, after each of which the walk restarts at zero.
        private static int StackDepthAfter(MethodBody body, Instruction boundary)
        {
            var depthAtTarget = new Dictionary<Instruction, int>();
            var depth = 0;
            foreach (var instr in body.Instructions)
            {
                if (depthAtTarget.TryGetValue(instr, out var reached)) depth = reached;
                depth = instr.OpCode.StackBehaviourPop == StackBehaviour.PopAll
                    ? 0
                    : depth + Pushes(instr) - Pops(instr);
                if (instr == boundary) break;

                switch (instr.Operand)
                {
                    case Instruction target:
                        depthAtTarget[target] = depth;
                        break;
                    case Instruction[] targets:
                        foreach (var t in targets) depthAtTarget[t] = depth;
                        break;
                }
                switch (instr.OpCode.FlowControl)
                {
                    case FlowControl.Branch:
                    case FlowControl.Throw:
                    case FlowControl.Return:
                        depth = 0;
                        break;
                }
            }
            return depth;
        }

        private static int Pushes(Instruction instr)
        {
            switch (instr.OpCode.StackBehaviourPush)
            {
                case StackBehaviour.Push0:
                    return 0;
                case StackBehaviour.Push1_push1:
                    return 2;
                case StackBehaviour.Varpush:
                    return instr.Operand is IMethodSignature signature ? CallPushes(signature) : 1;
                default:
                    return 1;
            }
        }

        // An init accessor returns `void modreq(IsExternalInit)`, which pushes nothing either.
        private static int CallPushes(IMethodSignature signature)
        {
            var type = signature.ReturnType;
            while (type is IModifierType modified) type = modified.ElementType;
            return type.MetadataType == MetadataType.Void ? 0 : 1;
        }

        private static int Pops(Instruction instr)
        {
            switch (instr.OpCode.StackBehaviourPop)
            {
                case StackBehaviour.Pop0:
                case StackBehaviour.PopAll:
                    return 0;
                case StackBehaviour.Varpop:
                    // `ret` is the one Varpop carrying no signature, and the walk restarts at zero after it.
                    return instr.Operand is IMethodSignature signature ? CallPops(instr.OpCode.Code, signature) : 0;
                case StackBehaviour.Pop1:
                case StackBehaviour.Popi:
                case StackBehaviour.Popref:
                    return 1;
                case StackBehaviour.Popi_popi_popi:
                case StackBehaviour.Popref_popi_popi:
                case StackBehaviour.Popref_popi_popi8:
                case StackBehaviour.Popref_popi_popr4:
                case StackBehaviour.Popref_popi_popr8:
                case StackBehaviour.Popref_popi_popref:
                    return 3;
                default:
                    return 2;
            }
        }

        private static int CallPops(Code code, IMethodSignature signature)
        {
            var pops = signature.Parameters.Count;
            // MUTANT_SURVIVES(unreachable): C# declares no explicit-this method, and Roslyn's reference to one declared
            // in IL elsewhere carries HasThis without ExplicitThis, so no call a C# body makes sets it.
            if (signature.HasThis && !signature.ExplicitThis && code != Code.Newobj) pops++;
            if (code == Code.Calli) pops++;
            return pops;
        }

        private static List<Instruction> CollectReturns(Collection<Instruction> instructions)
        {
            var returns = new List<Instruction>();
            foreach (var instr in instructions)
            {
                if (instr.OpCode == OpCodes.Ret)
                {
                    returns.Add(instr);
                }
            }

            return returns;
        }

        // Allow-list gate. The whole method bails when any call reaches a non-SAFE hook — a known memo-unsafe
        // hook, an unknown / future hook absent from both allow-lists, or a call whose hook safety cannot be
        // confirmed because Resolve() fails. Caching the body when an unguarded hook is in play would
        // short-circuit a re-render the hook drives through a path the deps array does not track.
        private static bool ReachesAnyNonSafeHook(Collection<Instruction> instructions,
            NonSafeHookFold nonSafe)
        {
            foreach (var instr in instructions)
            {
                if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt) continue;
                if (instr.Operand is MethodReference callee && ReachesNonSafeHook(callee, nonSafe))
                {
                    return true;
                }
            }

            return false;
        }

        // Walks the hook section once, recording every call site and the local each hook result is piped
        // into. False means the shape is unweavable and the whole method bails; a true result with a null
        // boundary means the body reached no hook at all.
        private static bool TryScanHookSection(MethodBody body, HookReachFold reach,
            List<(VariableDefinition? Local, TypeReference Type, Instruction Stored)> hookPipedLocals,
            List<Instruction> hookCalls,
            out Instruction? lastHookBoundary)
        {
            lastHookBoundary = null;
            foreach (var instr in body.Instructions)
            {
                if (!IsHookCall(instr, reach, out var hookTarget, out var isDirect)) continue;

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

                if (TryMatchDiscardedHookResult(next))
                {
                    return false;
                }

                var capture = TryCaptureHookResult(next, body, isDirect, out var local, out var boundary);
                if (capture == HookCaptureMatch.Bail)
                {
                    return false;
                }
                if (capture == HookCaptureMatch.Matched)
                {
                    hookPipedLocals.Add((local!, local!.VariableType, boundary!));
                    lastHookBoundary = boundary;
                    continue;
                }
                if (TryCaptureStackValue(instr, next, out var stackType, out var producer))
                {
                    hookPipedLocals.Add((null, stackType!, producer));
                    lastHookBoundary = producer;
                    continue;
                }

                // Hook return consumed by an unsupported pattern. Bail: the deps array would be incomplete.
                return false;
            }

            return true;
        }

        // The shapes are tried in this order because each is a strictly narrower read of the same
        // instruction, and the first one that recognizes the shape owns the verdict — including a Bail,
        // which means the shape matched but is unsound and a later matcher must not get to claim it.
        private static HookCaptureMatch TryCaptureHookResult(Instruction next, MethodBody body, bool isDirect,
            out VariableDefinition? local, out Instruction? boundary)
        {
            var direct = TryMatchDirectCapture(next, body, out local, out boundary);
            if (direct != HookCaptureMatch.NotMatched)
            {
                return direct;
            }

            var item1 = TryMatchItem1Deconstruction(next, body, out local, out boundary);
            if (item1 != HookCaptureMatch.NotMatched)
            {
                return item1;
            }

            return TryMatchTwoElementDeconstruction(next, body, isDirect, out local, out boundary);
        }

        // A hook call can be followed directly by whatever reads its value, with no store between. Such a value
        // is copied where it is produced: at the call, or at the ldfld taking a tuple's Item1. A whole tuple is
        // refused for the reason TryMatchDirectCapture gives, and a later element because only Item1 is a sound
        // dep. A void call, or a value whose type cannot be named in the caller's terms, leaves nothing to copy, and a
        // value stored to a field is left to the body, whose store a hit would skip.
        private static bool TryCaptureStackValue(Instruction call, Instruction next,
            out TypeReference? type, out Instruction producer)
        {
            producer = call;
            var callee = (MethodReference)call.Operand;
            type = InCallerTerms(callee.ReturnType, callee as GenericInstanceMethod,
                callee.DeclaringType as GenericInstanceType);
            if (type == null || type.MetadataType == MetadataType.Void) return false;
            if (IsValueTupleType(type))
            {
                type = null;
                if (next.OpCode != OpCodes.Ldfld) return false;
                // MUTANT_SURVIVES(unreachable): Cecil reads the operand of an ldfld as a field reference.
                if (next.Operand is not FieldReference field) return false;
                if (field.Name != "Item1") return false;
                producer = next;
                type = InCallerTerms(field.FieldType, null, field.DeclaringType as GenericInstanceType);
                if (type == null) return false;
            }
            // A value stored straight to a field is a side effect of the body, which a cache hit would skip.
            var consumer = producer.Next;
            return consumer == null || (consumer.OpCode != OpCodes.Stsfld && consumer.OpCode != OpCodes.Stfld);
        }

        // A callee's signature names its own generic parameters; the copy's local needs the arguments the call
        // site bound them to. Null where a part of the type is not one this substitutes.
        private static TypeReference? InCallerTerms(TypeReference type, GenericInstanceMethod? method,
            GenericInstanceType? declaring)
        {
            switch (type)
            {
                case GenericParameter parameter:
                    var arguments = parameter.Type == GenericParameterType.Method
                        ? method?.GenericArguments
                        : declaring?.GenericArguments;
                    return arguments?[parameter.Position];
                case GenericInstanceType instance:
                    var resolved = new GenericInstanceType(instance.ElementType);
                    foreach (var argument in instance.GenericArguments)
                    {
                        var each = InCallerTerms(argument, method, declaring);
                        // MUTANT_SURVIVES(unreachable, guard removed): an argument comes back null only for a pointer, a by-reference type or an unbound generic parameter, and no generic argument at a call site is any of those.
                        if (each == null) return null;
                        resolved.GenericArguments.Add(each);
                    }
                    return resolved;
                case ArrayType array:
                    var element = InCallerTerms(array.ElementType, method, declaring);
                    return element == null ? null : new ArrayType(element, array.Rank);
                case TypeSpecification:
                    return null;
                default:
                    return type;
            }
        }

        // Outcome of matching the instruction immediately following a hook call against one hook-return
        // consumption shape: NotMatched means this shape was not recognized (the next matcher gets a turn),
        // Bail means the shape was recognized but is unsound to cache (TryAnalyze leaves the whole method
        // unwoven), and Matched means a sound dep was captured and the hook boundary can advance.
        private enum HookCaptureMatch
        {
            NotMatched,
            Bail,
            Matched,
        }

        // A discarded hook result (`Hooks.UseXxx(...);` as a bare statement, IL `call -> pop`) means a
        // reactive input is not captured in the deps array, so the memo would bail re-renders the discarded
        // value should have triggered. There is no valid outcome for this shape other than bail.
        private static bool TryMatchDiscardedHookResult(Instruction next)
            => next.OpCode == OpCodes.Pop;

        // A whole-tuple capture (`var s = Hooks.UseState(...)`) stores the returned ValueTuple and is
        // compared via ValueTuple.Equals — per-element EqualityComparer<T>.Default (structural). For a
        // reference / float Item1 that diverges from the Object.is the reconciler uses to drive a
        // re-render, so a fresh-but-equal value would be a stale hit. Bail: single-element
        // deconstruction (`var (value, _) = ...`) captures Item1 directly and is compared soundly.
        private static HookCaptureMatch TryMatchDirectCapture(
            Instruction next, MethodBody body, out VariableDefinition? capturedLocal, out Instruction? newBoundary)
        {
            capturedLocal = null;
            newBoundary = null;
            if (!TryGetStlocVariable(next, body, out var local))
            {
                return HookCaptureMatch.NotMatched;
            }
            // A `ref var` local holds a managed pointer, which cannot be stored into the object[] of deps.
            if (IsValueTupleType(local.VariableType) || local.VariableType.IsByReference)
            {
                return HookCaptureMatch.Bail;
            }
            capturedLocal = local;
            newBoundary = next;
            return HookCaptureMatch.Matched;
        }

        // Deconstruction shape that keeps only the first tuple element (the value):
        // `var (value, _) = Hooks.UseXxx(...);` emits `call → ldfld <Item1> → stloc`. Only Item1 is
        // a sound dep — capturing a later element (e.g. the stable setter of UseState while the value
        // is discarded) would leave the changing value out of the deps array. Bail on anything but Item1.
        private static HookCaptureMatch TryMatchItem1Deconstruction(
            Instruction next, MethodBody body, out VariableDefinition? capturedLocal, out Instruction? newBoundary)
        {
            capturedLocal = null;
            newBoundary = null;
            if (next.OpCode != OpCodes.Ldfld || next.Operand is not FieldReference field
                || next.Next is not { } afterLdfld
                || !TryGetStlocVariable(afterLdfld, body, out var deconstructedLocal))
            {
                return HookCaptureMatch.NotMatched;
            }
            if (field.Name != "Item1")
            {
                return HookCaptureMatch.Bail;
            }
            capturedLocal = deconstructedLocal;
            newBoundary = afterLdfld;
            return HookCaptureMatch.Matched;
        }

        // Two-element deconstruction that keeps BOTH tuple elements — the idiomatic
        // `var (value, setter) = Hooks.UseState(...)`. Roslyn emits
        //   call -> dup -> ldfld Item1 -> stloc <value> -> ldfld Item2 -> st* <setter>
        // Only Item1 (the value) is a sound dep; Item2 (the reference-stable StateUpdater<T> setter) does
        // not change between renders, so it is NOT a dep. Capture Item1 and advance the boundary PAST the
        // setter store so the cache gate lands after the whole deconstruction (the stack must be balanced:
        // Item2 has to be consumed before the gate runs). Without this branch the leading `dup` matched no
        // shape and bailed the whole component — disabling auto-memo for every `var (x, setX) = ...` site.
        private static HookCaptureMatch TryMatchTwoElementDeconstruction(
            Instruction next, MethodBody body, bool isDirect, out VariableDefinition? capturedLocal,
            out Instruction? newBoundary)
        {
            capturedLocal = null;
            newBoundary = null;
            if (next.OpCode != OpCodes.Dup)
            {
                return HookCaptureMatch.NotMatched;
            }
            var valueStore = next.Next?.Next;
            if (!IsFieldLoad(next.Next, "Item1") || valueStore == null
                || !TryGetStlocVariable(valueStore, body, out var valueLocal))
            {
                // A dup that is not the canonical two-element tuple deconstruction: unknown shape, bail.
                return HookCaptureMatch.Bail;
            }
            // `var (v, set) = ...` with `set` never read leaves the tuple to a pop instead of an Item2 read, so
            // Item2 is discarded and capturing Item1 alone is sound for any hook.
            var rest = valueStore.Next;
            if (rest != null && rest.OpCode == OpCodes.Pop)
            {
                capturedLocal = valueLocal;
                newBoundary = rest;
                return HookCaptureMatch.Matched;
            }
            // Item2 is read, so it is left out of the deps only where it cannot change: the allow-listed pairs
            // (UseState, UseReducer, UseTransition, UseOptimistic) have a reference-stable second element, while
            // a custom hook's, or a non-positional Velvet.Hooks member's, can change between renders.
            var setterStore = rest?.Next;
            // MUTANT_SURVIVES(unreachable, clause removed): a body ends in ret, so an instruction follows the Item2 read and `setterStore` is never null here.
            if (!isDirect || !IsFieldLoad(rest, "Item2") || setterStore == null || !IsValueConsumingStore(setterStore))
            {
                return HookCaptureMatch.Bail;
            }
            capturedLocal = valueLocal;
            newBoundary = setterStore;
            return HookCaptureMatch.Matched;
        }

        private static bool IsFieldLoad(Instruction? instr, string fieldName)
            => instr != null && instr.OpCode == OpCodes.Ldfld
                && instr.Operand is FieldReference field && field.Name == fieldName;

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

        private static bool IsHookCall(Instruction instr, HookReachFold reach, out MethodReference? target, out bool isDirect)
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
            if ((reach.ReachOf(method) & HookReach.Hook) != 0)
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

        // What a call is known to reach, as flags: Hook for a hook the scan sees, Opaque for an open dispatch,
        // whose runtime target the scan cannot enumerate. Whether an Opaque call runs a hook is decided by where
        // it sits: TryAnalyze refuses one ahead of the gate — a custom hook reaching one included — and the
        // gate's runtime check covers one past it.
        [System.Flags]
        private enum HookReach
        {
            None = 0,
            Opaque = 1,
            Hook = 2,
        }

        // A per-weave fold of a property over the call graph a method reaches, each callee's value OR-ed into its
        // caller's and memoized by FullName, because the same V.* DSL factories and store accessors appear in
        // every component body and Resolve() walks metadata each time. Methods calling one another in a cycle are
        // folded as one component (Tarjan's algorithm), so each member takes the value of every member: a
        // provisional answer cached for a member the walk re-enters is read by a member it meets before the
        // cycle closes, and that member keeps the provisional answer as its own.
        private abstract class CallGraphFold
        {
            private readonly Dictionary<string, int> _final = new();
            private readonly Dictionary<string, int> _index = new();
            private readonly Dictionary<string, int> _low = new();
            private readonly Dictionary<string, int> _own = new();
            private readonly Dictionary<string, MethodReference> _members = new();
            private readonly Stack<string> _open = new();

            // A value for a method the fold does not descend; false hands back the definition whose body it does.
            protected abstract bool TryLeaf(MethodReference method, out int value, out MethodDefinition? definition);

            // What a member of a closed component ends up with, given what the component reaches.
            protected virtual int Finish(MethodReference method, int reached) => reached;

            public int Of(MethodReference method) => Fold(null, method);

            // A method the fold has entered and not closed is open, and so shares a component with the caller
            // that reached it again: it lends the caller its index, and its value arrives when the component
            // closes.
            private int Fold(string? caller, MethodReference callee)
            {
                var key = callee.FullName;
                if (_final.TryGetValue(key, out var done)) return done;
                if (_index.TryGetValue(key, out var openIndex))
                {
                    Lower(caller!, openIndex);
                    return 0;
                }
                if (TryLeaf(callee, out var leaf, out var definition))
                {
                    // MUTANT_SURVIVES(equivalent, line removed): the entry only saves resolving the same callee again, which returns the same value.
                    _final[key] = leaf;
                    return leaf;
                }
                Visit(callee, key, definition!);
                if (_final.TryGetValue(key, out done)) return done;
                Lower(caller!, _low[key]);
                return 0;
            }

            private void Lower(string key, int index) => _low[key] = System.Math.Min(_low[key], index);

            private void Visit(MethodReference method, string key, MethodDefinition definition)
            {
                var index = _index[key] = _index.Count;
                _low[key] = index;
                _members[key] = method;
                _open.Push(key);
                var reached = 0;
                foreach (var instr in definition.Body.Instructions)
                {
                    if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt) continue;
                    if (instr.Operand is not MethodReference callee) continue;
                    reached |= Fold(key, callee);
                }
                _own[key] = reached;
                if (_low[key] == index) Close(key);
            }

            private void Close(string root)
            {
                var component = new List<string>();
                var reached = 0;
                string member;
                do
                {
                    member = _open.Pop();
                    component.Add(member);
                    reached |= _own[member];
                } while (member != root);
                foreach (var each in component) _final[each] = Finish(_members[each], reached);
            }
        }

        private sealed class HookReachFold : CallGraphFold
        {
            public HookReach ReachOf(MethodReference method) => (HookReach)Of(method);

            protected override bool TryLeaf(MethodReference method, out int value, out MethodDefinition? definition)
            {
                definition = null;
                value = (int)HookReach.None;
                // A call into a well-known framework namespace (BCL / Unity) cannot reach a Velvet hook, so it needs
                // neither Resolve() nor a body walk. This mirrors the namespace short-circuit NonSafeHookFold relies
                // on, scoping the descent to calls that could plausibly compose a Velvet hook (Velvet DSL /
                // app-defined custom hooks).
                if (CannotReachVelvetHook(method)) return true;
                if (IsDirectHookCall(method))
                {
                    value = (int)HookReach.Hook;
                    return true;
                }
                // Resolve() requires the referenced assembly to be reachable from the IL post-processor.
                // Cross-assembly failures (or body-less non-virtual methods — pinvoke / runtime impls) are
                // treated as "does not call hooks" and never block weaving on their own; NonSafeHookFold is what
                // bails a method on an unverifiable callee.
                try
                {
                    definition = method.Resolve();
                }
                catch (System.Exception)
                {
                    return true;
                }
                if (definition == null) return true;
                if (IsDispatchOpen(definition))
                {
                    // An open virtual / interface dispatch resolves only to the statically declared method — the
                    // runtime override's body is not that one, and that override can be declared in an assembly
                    // that references Velvet even when the statically declared base/interface's own assembly does
                    // not, so checking the DECLARING assembly for a Velvet reference proves nothing about where an
                    // override can live. CannotReachVelvetHook above is the one case this rules out.
                    value = (int)HookReach.Opaque;
                    return true;
                }
                return !definition.HasBody;
            }

            // NonSafeHookFold reads a Velvet.Hooks member by its name and never descends it, so a dispatch inside
            // one does not count here either: any reach makes the member a Hook alone, its value captured ahead of
            // the gate rather than bailing the body there.
            protected override int Finish(MethodReference method, int reached)
                => reached != 0 && method.DeclaringType.FullName == HooksTypeFullName ? (int)HookReach.Hook : reached;
        }

        // A call ahead of the gate runs whether or not the gate hits, so a hook an open dispatch reaches there would
        // feed the body a value the deps array does not capture, and the gate's runtime check reads only what runs
        // past it.
        private static bool HasOpaqueCallAhead(Collection<Instruction> instructions, Instruction boundary,
            HookReachFold reach)
        {
            // The boundary itself is read too: where the hook's value stays on the stack, it is the hook call.
            foreach (var instr in instructions)
            {
                if (IsOpaqueCall(instr, reach)) return true;
                if (instr == boundary) break;
            }
            return false;
        }

        private static bool IsOpaqueCall(Instruction instr, HookReachFold reach)
        {
            if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt) return false;
            return (reach.ReachOf((MethodReference)instr.Operand) & HookReach.Opaque) != 0;
        }

        // True when a Velvet.Hooks.* member is a positional hook NOT on the SAFE allow-list — a known memo-unsafe
        // hook (Use / UseMutation / UseBlocker / UseFallback) or a future positional hook nobody has classified
        // yet. A Hooks.* member that is not a positional hook (framework plumbing such as TryGetMemoizedVNode)
        // is not a reactive hook and is treated as safe.
        private static bool IsDirectNonSafeHookCall(MethodReference method)
            => method.DeclaringType.FullName == HooksTypeFullName
                && PositionalHookMethodNames.Contains(method.Name)
                && !IsDirectSafeHookCall(method);

        // Namespace roots whose members cannot transitively reach a Velvet hook: the runtime / Unity
        // surfaces a component body calls for non-hook work (ToString, string.Concat, V is excluded as it lives
        // under Velvet). A call into one of these is a SAFE leaf, so the weaver neither resolves nor descends it.
        private static readonly string[] NonVelvetNamespaceRoots =
        {
            "System.",
            "Unity.",
            "UnityEngine.",
            "UnityEditor.",
            "Mono.",
        };

        // Returns true when method's declaring type lives in a framework namespace that
        // cannot reach a Velvet hook. The check is conservative: only well-known runtime / Unity
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
        // sealed-type carve-out is what keeps delegate invocations from reading as Opaque: a delegate's Invoke is
        // virtual in metadata but declared on a sealed type. A hook reached only through delegate
        // indirection stays outside this static model (as it always has), which is a rules-of-hooks
        // violation the analyzer layer reports.
        private static bool IsDispatchOpen(MethodDefinition def)
            => def.IsVirtual
                && !def.IsFinal
                && !(def.DeclaringType.IsSealed && !def.DeclaringType.IsInterface);

        // Whether a method reaches a non-SAFE hook, directly or transitively across plain method calls. A hook is
        // non-SAFE when it is a positional hook absent from both allow-lists (known memo-unsafe or unknown /
        // future), or when hook safety cannot be confirmed because Resolve() fails for a callee whose body the
        // weaver must inspect. Treating an unverifiable callee as non-SAFE keeps the allow-list a closed safety
        // contract: the weaver only weaves a body whose every hook the walk can see is proven SAFE. A custom hook
        // that composes UseMutation / Use is caught here.
        private sealed class NonSafeHookFold : CallGraphFold
        {
            protected override bool TryLeaf(MethodReference method, out int value, out MethodDefinition? definition)
            {
                definition = null;
                value = 0;
                // A direct Velvet.Hooks.* call is classified by name alone: never descend into a hook's own body,
                // whose framework internals would be misread as the component's reachable hook set.
                if (method.DeclaringType.FullName == HooksTypeFullName)
                {
                    value = IsDirectNonSafeHookCall(method) ? 1 : 0;
                    return true;
                }
                // A call into a well-known framework namespace (BCL / Unity) cannot reach a Velvet hook, so it is SAFE
                // without resolving. This scopes the conservative Resolve-failure bail below to calls that could
                // plausibly compose a Velvet hook, instead of bailing every component on an unresolvable BCL call
                // like object.ToString or string.Concat.
                if (CannotReachVelvetHook(method)) return true;
                // A failed Resolve() means the weaver cannot inspect the callee to confirm it reaches no non-SAFE
                // hook, so it is treated as reaching one and the whole method is left unwoven rather than woven on
                // an unproven assumption.
                value = 1;
                try
                {
                    definition = method.Resolve();
                }
                catch (System.Exception)
                {
                    return true;
                }
                if (definition == null) return true;
                // An open dispatch is opaque to this walk as it is to HookReachFold: the declared body need not be
                // the one that runs, so it proves nothing either way, and where the call sits decides instead —
                // see HookReach. A body-less non-virtual method (pinvoke / extern / runtime-implemented) has no IL
                // that could call a Velvet.Hooks member.
                value = 0;
                return IsDispatchOpen(definition) || !definition.HasBody;
            }
        }

        private static bool ReachesNonSafeHook(MethodReference method, NonSafeHookFold nonSafe) => nonSafe.Of(method) != 0;

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

            // Each hook value is copied into a local of the weaver's own where it is stored, and the deps array
            // reads the copies: Roslyn gives two nested hooks' results one temp, and a statement's local can be
            // assigned again before the gate, so the local a hook stored into need not hold its value there. A
            // value left on the stack is copied off it with a dup where it is produced.
            var copies = new List<(VariableDefinition? Local, Instruction Stored, VariableDefinition Copy)>();
            foreach (var (local, type, stored) in analysis.HookCaptures)
            {
                var copy = new VariableDefinition(type);
                body.Variables.Add(copy);
                copies.Add((local, stored, copy));
            }

            var insertAfter = analysis.LastHookBoundary;
            // Deps layout: each component parameter (a reactive prop) first, then every value flowing out of a
            // hook call. A prop change is therefore a miss under the same Object.is comparison as a hook input.
            var parameters = method.Parameters;
            var paramCount = parameters.Count;
            var depsCount = paramCount + copies.Count;

            var injected = new List<Instruction>();
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
            var depIndex = paramCount;
            foreach (var (_, _, local) in copies)
            {
                injected.Add(Instruction.Create(OpCodes.Dup));
                injected.Add(Instruction.Create(OpCodes.Ldc_I4, depIndex++));
                injected.Add(Instruction.Create(OpCodes.Ldloc, local));
                if (local.VariableType.IsValueType || local.VariableType.IsGenericParameter)
                {
                    injected.Add(Instruction.Create(OpCodes.Box, local.VariableType));
                }
                injected.Add(Instruction.Create(OpCodes.Stelem_Ref));
            }
            injected.Add(Instruction.Create(OpCodes.Stloc, depsLocal));

            injected.Add(Instruction.Create(OpCodes.Ldtoken, SelfReference(method)));
            injected.Add(Instruction.Create(OpCodes.Ldloc, depsLocal));
            injected.Add(Instruction.Create(OpCodes.Ldloca, slotLocal));
            injected.Add(Instruction.Create(OpCodes.Ldloca, cachedLocal));
            injected.Add(Instruction.Create(OpCodes.Call, context.TryGetMemoizedVNode));

            var afterHitBranch = Instruction.Create(OpCodes.Nop);
            injected.Add(Instruction.Create(OpCodes.Brfalse, afterHitBranch));
            // A hook nested in an argument list (`V.Label(text: Hooks.UseStore(...).ToString())`) leaves the
            // arguments evaluated ahead of it on the stack at the gate, and a `ret` over them is invalid IL.
            for (var i = 0; i < analysis.StackDepthAtGate; i++)
            {
                injected.Add(Instruction.Create(OpCodes.Pop));
            }
            injected.Add(Instruction.Create(OpCodes.Ldloc, cachedLocal));
            injected.Add(Instruction.Create(OpCodes.Ret));
            injected.Add(afterHitBranch);

            if (insertAfter is { } boundary)
            {
                var current = boundary;
                foreach (var ins in injected)
                {
                    il.InsertAfter(current, ins);
                    current = ins;
                }
            }
            else
            {
                var entry = body.Instructions[0];
                foreach (var ins in injected)
                {
                    il.InsertBefore(entry, ins);
                }
            }

            // After the gate, so that a copy lands between its store and the gate where that store is the boundary.
            foreach (var (local, stored, copy) in copies)
            {
                il.InsertAfter(stored, Instruction.Create(OpCodes.Stloc, copy));
                il.InsertAfter(stored, local == null ? Instruction.Create(OpCodes.Dup) : Instruction.Create(OpCodes.Ldloc, local));
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
                // A branch that targets the ret itself would jump past the commit above it and return a tree
                // the slot never stages.
                RetargetBranches(body, returnInstr, preReturn[0]);
            }

            body.OptimizeMacros();
        }

        // The handle the gate matches against the method the rendering fiber renders, which for a generic method or
        // a method of a generic type is one instantiation: so the token names the method over its own generic
        // parameters, which the runtime resolves to the instantiation running.
        private static MethodReference SelfReference(MethodDefinition method)
        {
            MethodReference self = method;
            var declaringType = method.DeclaringType;
            if (declaringType.HasGenericParameters)
            {
                var instance = new GenericInstanceType(declaringType);
                foreach (var parameter in declaringType.GenericParameters)
                    instance.GenericArguments.Add(parameter);
                self = new MethodReference(method.Name, method.ReturnType, instance)
                {
                    HasThis = method.HasThis,
                    ExplicitThis = method.ExplicitThis,
                    CallingConvention = method.CallingConvention,
                };
                foreach (var parameter in method.Parameters)
                {
                    self.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
                }
                foreach (var parameter in method.GenericParameters)
                {
                    self.GenericParameters.Add(new GenericParameter(parameter.Name, self));
                }
            }
            if (method.HasGenericParameters)
            {
                var instance = new GenericInstanceMethod(self);
                foreach (var parameter in method.GenericParameters)
                {
                    instance.GenericArguments.Add(parameter);
                }
                self = instance;
            }
            return self;
        }

        private static void RetargetBranches(MethodBody body, Instruction from, Instruction to)
        {
            foreach (var instr in body.Instructions)
            {
                if (ReferenceEquals(instr.Operand, from))
                {
                    instr.Operand = to;
                }
                else if (instr.Operand is Instruction[] targets)
                {
                    instr.Operand = System.Array.ConvertAll(targets, t => ReferenceEquals(t, from) ? to : t);
                }
            }
        }

        private readonly struct HookAnalysis
        {
            public HookAnalysis(IReadOnlyList<(VariableDefinition? Local, TypeReference Type, Instruction Stored)> hookCaptures,
                Instruction? lastHookBoundary,
                IReadOnlyList<Instruction> returns,
                int stackDepthAtGate)
            {
                HookCaptures = hookCaptures;
                LastHookBoundary = lastHookBoundary;
                Returns = returns;
                StackDepthAtGate = stackDepthAtGate;
            }
            public IReadOnlyList<(VariableDefinition? Local, TypeReference Type, Instruction Stored)> HookCaptures { get; }
            public Instruction? LastHookBoundary { get; }
            public IReadOnlyList<Instruction> Returns { get; }
            public int StackDepthAtGate { get; }
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

            var hooksTypeName = typeof(Velvet.Hooks).FullName;
            var hooksType = module.GetType(hooksTypeName)
                ?? WeaverDiagnostics.ResolveExternal(module, hooksTypeName);
            if (hooksType == null)
            {
                failure = $"the type '{hooksTypeName}' could not be resolved from the assembly's references.";
                return null;
            }

            var vnodeTypeName = typeof(Velvet.VNode).FullName;
            var vnodeType = module.GetType(vnodeTypeName)
                ?? WeaverDiagnostics.ResolveExternal(module, vnodeTypeName);
            if (vnodeType == null)
            {
                failure = $"the type '{vnodeTypeName}' could not be resolved from the assembly's references.";
                return null;
            }

            var tryGet = ResolveHookMethod(hooksType, nameof(Velvet.Hooks.TryGetMemoizedVNode), 4);
            var store = ResolveHookMethod(hooksType, nameof(Velvet.Hooks.StoreMemoizedVNode), 3);
            if (tryGet == null || store == null)
            {
                failure = $"the memoization methods '{hooksTypeName}.{nameof(Velvet.Hooks.TryGetMemoizedVNode)}' /"
                    + $" '{hooksTypeName}.{nameof(Velvet.Hooks.StoreMemoizedVNode)}' could not be resolved on the"
                    + " referenced Velvet assembly.";
                return null;
            }

            return new WeaverContext
            {
                VNode = module.ImportReference(vnodeType),
                TryGetMemoizedVNode = module.ImportReference(tryGet),
                StoreMemoizedVNode = module.ImportReference(store),
            };
        }

        // The arity is asserted so that a future overload is not picked up silently.
        private static MethodDefinition? ResolveHookMethod(TypeDefinition hooks, string name, int arity)
        {
            foreach (var m in hooks.Methods)
            {
                if (m.Name == name && m.Parameters.Count == arity) return m;
            }
            return null;
        }
    }
}

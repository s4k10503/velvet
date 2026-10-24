using Unity.CompilationPipeline.Common.Diagnostics;
using System.Collections.Generic;
using Mono.Collections.Generic;
using Mono.Cecil.Rocks;
using Mono.Cecil.Cil;
using Mono.Cecil;

namespace Velvet.CodeGen
{
    // Reading a body into a plan: whether the body as a whole can be keyed on its props and hook values, and which
    // parts of it can be cached on their own where it cannot, or in addition.
    internal static partial class CompilerWeaver
    {
        // An assembly carrying AssemblyMetadata with this key and value has every note of every component in it
        // reported as a build warning, in addition to the attribute each component carries. It is the opt-in
        // convention Velvet.CodeShape uses.
        private const string ReportMetadataKey = Velvet.MemoizationReportAttribute.MetadataKey;
        private const string ReportMetadataValue = Velvet.MemoizationReportAttribute.WarnValue;
        private const string AssemblyMetadataAttributeName = "System.Reflection.AssemblyMetadataAttribute";

        // A hook value, or one element of a hook's tuple, the gate on the body keys on.
        private sealed class HookCapture
        {
            public HookCapture(VariableDefinition? local, FieldReference? item, TypeReference type, Instruction stored)
            {
                Local = local;
                Item = item;
                Type = type;
                Stored = stored;
            }

            // The local the value was stored into, or null where it is copied off the stack.
            public VariableDefinition? Local { get; }

            // The tuple field read off Local, or null where Local holds the value itself.
            public FieldReference? Item { get; }

            public TypeReference Type { get; }

            public Instruction Stored { get; }
        }

        // Every hook call of a body, the values captured off them and the first reason one could not be.
        private sealed class HookScan
        {
            public List<HookCapture> Captures { get; } = new();

            public List<Instruction> Calls { get; } = new();

            public Instruction? Boundary { get; set; }

            public string? CaptureFailure { get; set; }

            public bool AnyValueHook { get; set; }

            // Set where a hook's value is never read, so the render it causes has nothing to be keyed on.
            public string? UnreadValue { get; set; }

            // The instruction after which every hook has run and its value is held.
            public Instruction? End
            {
                get
                {
                    if (Calls.Count == 0) return null;
                    var last = Calls[Calls.Count - 1];
                    return Boundary != null && Boundary.Offset > last.Offset ? Boundary : last;
                }
            }
        }

        private sealed class HookFindings
        {
            public HookScan Scan { get; } = new();

            public bool Unweavable { get; set; }

            public HookAnalysis? Gate { get; set; }
        }

        private sealed class WeavePlan
        {
            public WeavePlan(HookAnalysis? gate, List<ScopeRegion> scopes)
            {
                Gate = gate;
                Scopes = scopes;
            }

            public HookAnalysis? Gate { get; }

            public List<ScopeRegion> Scopes { get; }
        }

        private static WeavePlan? PlanWeave(MethodDefinition method, WeaverContext context, HookReachFold reach,
            NonSafeHookFold nonSafe, WeaveReport report)
        {
            var findings = AnalyzeHooks(method, reach, nonSafe, report);
            if (findings.Unweavable) return null;
            var scopes = context.HasScopes
                ? PlanScopes(method, findings, new CallClassifier(reach, nonSafe), report)
                : new List<ScopeRegion>();
            if (findings.Gate == null && scopes.Count == 0) return null;
            return new WeavePlan(findings.Gate, scopes);
        }

        private static HookFindings AnalyzeHooks(MethodDefinition method, HookReachFold reach, NonSafeHookFold nonSafe,
            WeaveReport report)
        {
            var body = method.Body;
            var findings = new HookFindings();
            ScanHooks(body, reach, findings.Scan);
            var returns = CollectReturns(body.Instructions);
            var refusal = Refusal(method, findings.Scan, returns) ?? findings.Scan.UnreadValue;
            if (refusal != null)
            {
                report.Add($"The component is not cached: {refusal}.");
                findings.Unweavable = true;
                return findings;
            }
            var blockers = GateBlockers(method, reach, nonSafe, findings.Scan, returns);
            foreach (var blocker in blockers) report.Add($"The body as a whole is not cached: {blocker}.");
            if (blockers.Count == 0) findings.Gate = BuildGate(body, findings.Scan, returns);
            return findings;
        }

        // A body that breaks the Rules of Hooks, or has no input, is left exactly as it is.
        private static string? Refusal(MethodDefinition method, HookScan scan, List<Instruction> returns)
        {
            if (returns.Count == 0) return "the body has no return";
            if (method.Parameters.Count == 0 && !scan.AnyValueHook) return "it reads no prop and no hook value to key a cache on";
            if (scan.Calls.Count == 0)
            {
                return RequestsPropsBail(method) ? "Memoize = true already skips a render whose props are equal" : null;
            }
            return HookOrderRefusal(method.Body, scan, returns);
        }

        // Every return path must come after the hook section: a return before or inside it means hooks are
        // skipped on that path, which would corrupt the position-based slot allocation. Nor may a branch skip or
        // repeat a hook, or a protected region hold one: weaving such a component would also place the cache
        // gate inside a conditional block, producing malformed IL, so the runtime rules-of-hooks backstop reports
        // the violation instead. A benign branch (a ternary in a hook arg or between hooks) converges BEFORE the
        // next hook, so its target is not past a hook and is not flagged.
        private static string? HookOrderRefusal(MethodBody body, HookScan scan, List<Instruction> returns)
        {
            var end = scan.End!;
            foreach (var ret in returns)
            {
                if (ret.Offset <= end.Offset) return "a return comes before a hook call";
            }
            if (HasConditionallySkippedHook(body, scan.Calls)) return "a hook call is skipped or repeated by a branch";
            return HookInsideHandler(body, scan.Calls) ? "a hook call sits inside a try, catch or finally" : null;
        }

        private static bool HookInsideHandler(MethodBody body, List<Instruction> hookCalls)
        {
            foreach (var handler in body.ExceptionHandlers)
            {
                foreach (var call in hookCalls)
                {
                    if (Overlaps(handler.TryStart, handler.TryEnd, call)) return true;
                    if (Overlaps(handler.HandlerStart, handler.HandlerEnd, call)) return true;
                    if (Overlaps(handler.FilterStart, handler.HandlerStart, call)) return true;
                }
            }
            return false;
        }

        // What keeps the body from being keyed on its props and hook values as a whole. Each is a thing the
        // gate's single dependency array cannot stand for; a part of the body that does not read it is still
        // cached on its own.
        private static List<string> GateBlockers(MethodDefinition method, HookReachFold reach, NonSafeHookFold nonSafe,
            HookScan scan, List<Instruction> returns)
        {
            var blockers = new List<string>();
            var instructions = method.Body.Instructions;
            var unsafeHook = FirstNonSafeHook(instructions, nonSafe);
            if (unsafeHook != null) blockers.Add($"it reaches {unsafeHook}, whose result a dependency comparison does not represent");
            if (scan.CaptureFailure != null) blockers.Add(scan.CaptureFailure);
            var end = scan.End;
            if (end != null && HasOpaqueCallAhead(instructions, end, reach))
            {
                blockers.Add("a call whose target the build cannot see runs ahead of the last hook");
            }
            if (end != null && method.Body.HasExceptionHandlers && IsInsideAnyHandler(method.Body, end, returns))
            {
                blockers.Add("a return or the last hook sits inside a protected region");
            }
            if (scan.Captures.Count == 0 && method.Parameters.Count == 0) blockers.Add("no prop or hook value is read");
            return blockers;
        }

        private static HookAnalysis BuildGate(MethodBody body, HookScan scan, List<Instruction> returns)
        {
            if (scan.Calls.Count == 0) return new HookAnalysis(scan.Captures, null, returns, 0);
            return new HookAnalysis(scan.Captures, scan.Boundary, returns, StackDepthAfter(body, scan.Boundary!));
        }

        // The name of a hook some call of the body reaches that is outside both allow-lists, or null.
        private static string? FirstNonSafeHook(Collection<Instruction> instructions, NonSafeHookFold nonSafe)
        {
            foreach (var instr in instructions)
            {
                if (instr.OpCode != OpCodes.Call && instr.OpCode != OpCodes.Callvirt) continue;
                if (instr.Operand is MethodReference callee && ReachesNonSafeHook(callee, nonSafe)) return callee.Name;
            }
            return null;
        }

        // Walks every hook call site once, recording the call and the local each hook result is piped into.
        private static void ScanHooks(MethodBody body, HookReachFold reach, HookScan scan)
        {
            foreach (var instr in body.Instructions)
            {
                if (!IsHookCall(instr, reach, out var hookTarget, out var isDirect)) continue;

                // Every hook call site is recorded so the pass over them can verify none is conditionally skipped.
                scan.Calls.Add(instr);

                // A direct SAFE void hook (UseEffect and friends) runs for its side effect only and pushes no
                // value, so there is no dep to capture. Only a direct hook is treated this way: a void-returning
                // custom hook would hide whatever value hook it composes internally, which the deps array could
                // not capture.
                if (isDirect && IsDirectVoidSafeHookCall(hookTarget!))
                {
                    scan.Boundary = instr;
                    continue;
                }

                scan.AnyValueHook = true;
                scan.CaptureFailure ??= TryCapture(body, scan, instr, isDirect);
            }
        }

        // Null when the hook's value is captured, and otherwise the reason the gate cannot key on it.
        private static string? TryCapture(MethodBody body, HookScan scan, Instruction instr, bool isDirect)
        {
            const string Unsupported = "a hook result is consumed by a shape the cache cannot key on";
            var next = instr.Next;
            if (next == null || TryMatchDiscardedHookResult(next))
            {
                if (!HoldsStableObject(instr, isDirect)) scan.UnreadValue ??= UnreadMessage;
                return "a hook result is discarded";
            }

            var tuple = TryCaptureTupleTemp(body, scan, instr, next, isDirect);
            if (tuple == HookCaptureMatch.Bail) return "a hook result is a tuple the body hands on whole";
            if (tuple == HookCaptureMatch.Matched) return null;

            if (ReadsLaterElementOnly(next) && !HoldsStableObject(instr, isDirect)) scan.UnreadValue ??= UnreadMessage;
            var capture = TryCaptureHookResult(next, body, isDirect, out var local, out var boundary);
            if (capture == HookCaptureMatch.Bail) return Unsupported;
            if (capture == HookCaptureMatch.Matched)
            {
                scan.Captures.Add(new HookCapture(local, null, local!.VariableType, boundary!));
                scan.Boundary = boundary;
                return null;
            }
            if (!TryCaptureStackValue(instr, next, out var stackType, out var producer)) return Unsupported;
            scan.Captures.Add(new HookCapture(null, null, stackType!, producer));
            scan.Boundary = producer;
            return null;
        }

        private const string FirstTupleItem = "Item1";

        // A hook whose value the body never reads leaves the render it causes with nothing to key on, so no part of
        // the body is cached.
        private const string UnreadMessage =
            "a hook's value is never read, so a render it causes has nothing to be keyed on";

        // `Hooks.UseState(0).Item2`: an element other than the first read straight off the returned tuple.
        private static bool ReadsLaterElementOnly(Instruction next)
            => next.OpCode.Code == Code.Ldfld && next.Operand is FieldReference field
                && IsValueTupleType(field.DeclaringType) && field.Name != FirstTupleItem;

        private static bool HoldsStableObject(Instruction call, bool isDirect)
            => isDirect && call.Operand is MethodReference hook && ContainerHookNames.Contains(hook.Name);

        // A tuple a hook returned is stored whole and read one element at a time, as a lambda capturing the
        // deconstructed names makes the compiler write it. Each element read is a dep of its own, so the tuple
        // is never compared as a tuple; a tuple read in any other way is not captured.
        private static HookCaptureMatch TryCaptureTupleTemp(MethodBody body, HookScan scan, Instruction call,
            Instruction stored, bool isDirect)
        {
            if (!TryGetStlocVariable(stored, body, out var tuple) || !IsValueTupleType(tuple.VariableType))
            {
                return HookCaptureMatch.NotMatched;
            }
            var fields = TupleElementsRead(body, stored, tuple);
            if (fields == null) return HookCaptureMatch.Bail;
            if (!fields.Exists(field => field.Name == FirstTupleItem) && !HoldsStableObject(call, isDirect))
            {
                scan.UnreadValue ??= UnreadMessage;
            }
            var captures = new List<HookCapture>();
            foreach (var field in fields)
            {
                var type = InCallerTerms(field.FieldType, null, field.DeclaringType as GenericInstanceType);
                if (type == null) return HookCaptureMatch.Bail;
                captures.Add(new HookCapture(tuple, field, type, stored));
            }
            scan.Captures.AddRange(captures);
            scan.Boundary = stored;
            return HookCaptureMatch.Matched;
        }

        // The fields read off the tuple the store at `stored` put into `tuple`, up to the next store into that
        // local, or null where it is read as anything but a field.
        private static List<FieldReference>? TupleElementsRead(MethodBody body, Instruction stored, VariableDefinition tuple)
        {
            var fields = new List<FieldReference>();
            var names = new HashSet<string>();
            for (var instr = stored.Next; instr != null; instr = instr.Next)
            {
                if (TryGetStlocVariable(instr, body, out var written) && written == tuple) break;
                if (IsLocalAddress(instr, body) == tuple) return null;
                if (LoadedLocal(instr, body) != tuple) continue;
                var read = instr.Next;
                if (read == null || read.OpCode.Code != Code.Ldfld) return null;
                var field = (FieldReference)read.Operand;
                if (names.Add(field.Name)) fields.Add(field);
            }
            return fields;
        }

        private static VariableDefinition? LoadedLocal(Instruction instr, MethodBody body)
        {
            switch (instr.OpCode.Code)
            {
                case Code.Ldloc_0:
                    return body.Variables[0];
                case Code.Ldloc_1:
                    return body.Variables[1];
                case Code.Ldloc_2:
                    return body.Variables[2];
                case Code.Ldloc_3:
                    return body.Variables[3];
                case Code.Ldloc:
                case Code.Ldloc_S:
                    return instr.Operand as VariableDefinition;
                default:
                    return null;
            }
        }

        private static VariableDefinition? IsLocalAddress(Instruction instr, MethodBody body)
            => instr.OpCode.Code == Code.Ldloca || instr.OpCode.Code == Code.Ldloca_S ? instr.Operand as VariableDefinition : null;

        // The tail of a body is what runs after its last hook has returned its value: only there can a part of
        // the body be skipped without skipping a hook.
        private static List<ScopeRegion> PlanScopes(MethodDefinition method, HookFindings hooks, CallClassifier calls,
            WeaveReport report)
        {
            var body = method.Body;
            body.SimplifyMacros();
            var flow = ReactiveScopeFlow.Run(body);
            if (flow.Failed)
            {
                report.Add("No part of the body is cached on its own: its instructions use a stack shape the build does not follow.");
                return new List<ScopeRegion>();
            }
            var volatility = new ReactiveScopeVolatility(flow, calls);
            volatility.Solve();
            var end = hooks.Scan.End;
            var tailStart = end == null ? 0 : flow.IndexOf(end) + 1;
            var regions = new ReactiveScopeRegions(body, flow, volatility, tailStart, VNodeFullName);
            var found = regions.Find();
            report.AddRange(regions.Declined);
            var notes = new List<string>();
            var kept = ReactiveScopePlan.Select(found, hooks.Gate != null, notes);
            ReactiveScopePlan.Number(kept, hooks.Gate != null ? 1 : 0);
            report.AddRange(notes);
            return kept;
        }

        private static void InjectPlan(MethodDefinition method, WeavePlan plan, WeaverContext context)
        {
            var body = method.Body;
            body.SimplifyMacros();
            VariableDefinition? firstSlot = null;
            if (plan.Scopes.Count > 0)
            {
                firstSlot = new VariableDefinition(method.Module.TypeSystem.Int32);
                body.Variables.Add(firstSlot);
                new ReactiveScopeInjector(method, context, firstSlot).Gate(plan.Scopes);
            }
            if (plan.Gate is { } gate) InjectMemoization(method, gate, context, firstSlot);
            if (firstSlot != null) InsertReservation(method, context, firstSlot, (plan.Gate == null ? 0 : 1) + plan.Scopes.Count);
            body.OptimizeMacros();
        }

        // First of everything the method does, ahead of the gates it feeds.
        private static void InsertReservation(MethodDefinition method, WeaverContext context, VariableDefinition firstSlot,
            int slotCount)
        {
            var body = method.Body;
            var il = body.GetILProcessor();
            var entry = body.Instructions[0];
            il.InsertBefore(entry, Instruction.Create(OpCodes.Ldtoken, SelfReference(method)));
            il.InsertBefore(entry, Instruction.Create(OpCodes.Ldc_I4, slotCount));
            il.InsertBefore(entry, Instruction.Create(OpCodes.Call, context.ReserveScopes!));
            il.InsertBefore(entry, Instruction.Create(OpCodes.Stloc, firstSlot));
        }

        private static bool WarnsOnReport(AssemblyDefinition assembly)
        {
            foreach (var attribute in assembly.CustomAttributes)
            {
                if (!IsReportOptIn(attribute)) continue;
                return true;
            }
            return false;
        }

        private static bool IsReportOptIn(CustomAttribute attribute)
            => attribute.AttributeType.FullName == AssemblyMetadataAttributeName
                && attribute.ConstructorArguments.Count == 2
                && (attribute.ConstructorArguments[0].Value as string) == ReportMetadataKey
                && (attribute.ConstructorArguments[1].Value as string) == ReportMetadataValue;

        // Records the outcome on the method itself, where a test or a tool can read it back, and as a build
        // warning where the assembly asks for it.
        private static void Publish(MethodDefinition method, WeaverContext context, WeaveReport report,
            List<DiagnosticMessage> diagnostics)
        {
            context.AttachReport(method, report);
            if (report.Notes.Count == 0) return;
            if (!WarnsOnReport(method.Module.Assembly)) return;
            diagnostics.Add(new DiagnosticMessage
            {
                DiagnosticType = DiagnosticType.Warning,
                MessageData = WeaverDiagnostics.FormatMemoizationReport(method, report),
            });
        }
    }
}

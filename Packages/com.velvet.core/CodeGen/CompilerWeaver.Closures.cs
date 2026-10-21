using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Velvet.CodeGen
{
    // Display classes: the class the compiler hoists a variable into when a lambda captures it. A hook value a
    // lambda captures is stored into a field of one, so the shapes below are what keeps a component with a
    // handler woven at all, and the reads of those fields are what a scope compares.
    internal static partial class CompilerWeaver
    {
        private const string ClosureClassPrefix = "<>c__DisplayClass";
        private const string ClosureParentFieldPrefix = "CS$<>8__locals";

        // Where one hook value was stored. A local or a display class field is read again to copy it into the
        // gate's dependencies; neither is set for a value left on the stack, which is copied with a `dup`.
        private readonly record struct HookCapture(VariableDefinition? Local, FieldReference? Field,
            VariableDefinition? Receiver, TypeReference Type, Instruction Stored);

        private readonly record struct HookCaptureOutcome(HookCaptureMatch Match, HookCapture Capture,
            Instruction? Boundary, object? StableKey);

        // OnStack: the element is read and left for whatever uses it next, which the gate copies with a `dup`.
        private readonly record struct TupleElementStore(int Item, Instruction Store, VariableDefinition? Local,
            FieldReference? Field, VariableDefinition? Receiver, TypeReference Type, bool OnStack = default);

        private static readonly HookCaptureOutcome BailOutcome = new(HookCaptureMatch.Bail, default, null, null);

        // The value of a hook that no comparison stands for: what UseService or a volatile hook returns, and what a
        // custom hook returns unless it is a value, which may be an instance it holds from inside.
        private static bool IsUncomparableHookValue(MethodReference hook, bool isDirect, TypeReference type)
            => isDirect ? hook.Name is nameof(Velvet.Hooks.UseService) || !IsDirectSafeHookCall(hook) : !IsValueLike(type);

        private static TypeReference? ElementOf(TypeReference? type)
            => type is GenericInstanceType instance ? ElementOf(instance.ElementType) : type;

        private static bool IsClosureClass(TypeReference? type)
            => ElementOf(type) is { IsValueType: false, Name: var name } && name.StartsWith(ClosureClassPrefix, System.StringComparison.Ordinal);

        private static bool IsClosureField(FieldReference field) => IsClosureClass(field.DeclaringType);

        private static bool IsParentLink(FieldReference field)
            => field.Name.StartsWith(ClosureParentFieldPrefix, System.StringComparison.Ordinal);

        // A field is named by its definition, so that two references to it from different instructions agree.
        private static object FieldKey(FieldReference field)
            => (object?)TryResolve(() => field.Resolve()) ?? field.FullName;

        // The one local of a display class type. A body holding two of one class (a scope entered twice) has no
        // local to say which a read means, and no weave names it.
        private static VariableDefinition? FindClosureLocal(MethodBody body, TypeReference closure)
            => body.Variables.Where(local => ElementOf(local.VariableType)!.FullName.Equals(ElementOf(closure)!.FullName)).ToList() is [var only] ? only : null;

        private static VariableDefinition? LoadedLocal(Instruction? instr, MethodBody body)
            => instr?.OpCode.Code switch
            {
                Code.Ldloc_0 => body.Variables[0],
                Code.Ldloc_1 => body.Variables[1],
                Code.Ldloc_2 => body.Variables[2],
                Code.Ldloc_3 => body.Variables[3],
                Code.Ldloc or Code.Ldloc_S => (VariableDefinition)instr.Operand,
                _ => null,
            };

        // The instructions from `from` on, in order.
        private static IEnumerable<Instruction> Following(Instruction? from)
        {
            for (var instr = from; instr is not null; instr = instr.Next) yield return instr;
        }

        // `ldloc display; <the hook call>; stfld field`: a captured variable declared and initialised by a hook.
        private static HookCaptureOutcome TryMatchFieldCapture(Instruction store, MethodBody body)
        {
            if (store.Operand is not FieldReference field || !IsClosureField(field) || IsParentLink(field)) return default;
            var receiver = FindClosureLocal(body, field.DeclaringType);
            var type = InCallerTerms(field.FieldType, null, field.DeclaringType as GenericInstanceType);
            // MUTANT_SURVIVES(unreachable, clause removed): the type of a display class field is never one InCallerTerms answers null for.
            return receiver is not null && type is not null && !IsValueTupleType(type)
                ? new HookCaptureOutcome(HookCaptureMatch.Matched, new HookCapture(null, field, receiver, type, store), store, null)
                : BailOutcome;
        }

        // The deconstruction of a hook's tuple when a target is a captured variable: Roslyn keeps the tuple in a
        // temporary and then reads one element for each target, `ldloc tuple; ldfld ItemN; stloc x` for a local and
        // `ldloc display; ldloc tuple; ldfld ItemN; stfld f` for a captured variable. The temporary must hold
        // nothing else: a tuple read again later is a hook value the gate could not compare.
        private static HookCaptureOutcome TryMatchTupleTemp(Instruction stored, MethodBody body, bool isDirect, VariableDefinition tuple)
        {
            var elements = new List<TupleElementStore>();
            var cursor = stored.Next;
            while (cursor is not null && ReadTupleElement(cursor, body, tuple) is { } element)
            {
                elements.Add(element);
                cursor = element.Store.Next;
            }
            if (!TupleElementsAreSound(elements, isDirect) || IsTupleReadAgain(cursor, tuple, body)) return BailOutcome;
            var value = elements.First(element => element.Item is 1);
            var setter = elements.FirstOrDefault(element => element.Item is 2);
            return new HookCaptureOutcome(HookCaptureMatch.Matched, new HookCapture(value.Local, value.Field, value.Receiver, value.Type, value.Store),
                elements[^1].Store, setter.Local ?? (setter.Field is null ? null : FieldKey(setter.Field)));
        }

        private static TupleElementStore? ReadTupleElement(Instruction first, MethodBody body, VariableDefinition tuple)
        {
            var head = LoadedLocal(first, body);
            var receiver = head is not null && !ReferenceEquals(head, tuple) ? head : null;
            var load = receiver is null ? first : first.Next;
            if (!ReferenceEquals(LoadedLocal(load, body), tuple) || load?.Next is not { OpCode.Code: Code.Ldfld, Operand: FieldReference item, Next: { } store } read) return null;
            var number = item.Name.StartsWith("Item", System.StringComparison.Ordinal) && int.TryParse(item.Name[4..], out var parsed) ? parsed : 0;
            return (receiver, number) switch
            {
                (_, 0) => null,
                (null, 1 or 2) when !IsElementStore(store, body) => OnStackElement(read, item, number),
                (null, _) => LocalElement(store, body, number),
                _ => ClosureElement(store, receiver, number),
            };
        }

        private static bool IsElementStore(Instruction store, MethodBody body)
            => store.OpCode.Code is Code.Stsfld || TryGetStlocVariable(store, body, out _);

        // `ldloc tuple; ldfld Item1` that nothing stores yet: Roslyn keeps a value read once on the stack until its
        // use, which can be past the next hook call.
        private static TupleElementStore? OnStackElement(Instruction read, FieldReference item, int number)
            => InCallerTerms(item.FieldType, null, item.DeclaringType as GenericInstanceType) is { } type
                ? new TupleElementStore(number, read, null, null, null, type, OnStack: true)
                : null;

        // A static field takes an element straight off the stack where a setter is assigned the line after it is
        // declared; nothing of it is read back.
        private static TupleElementStore? LocalElement(Instruction store, MethodBody body, int number)
            => store is { OpCode.Code: Code.Stsfld, Operand: FieldReference { FieldType: var globalType } }
                ? new TupleElementStore(number, store, null, null, null, globalType)
                : TryGetStlocVariable(store, body, out var target) ? new TupleElementStore(number, store, target, null, null, target.VariableType) : null;

        private static TupleElementStore? ClosureElement(Instruction store, VariableDefinition receiver, int number)
            => store is { OpCode.Code: Code.Stfld, Operand: FieldReference field } && IsClosureClass(receiver.VariableType)
                && InCallerTerms(field.FieldType, null, field.DeclaringType as GenericInstanceType) is { } type
                ? new TupleElementStore(number, store, null, field, receiver, type)
                : null;

        // The value is captured once. A setter is the stable second element of the hook's pair, which only a
        // direct hook promises; a later element is not captured by anything here.
        private static bool TupleElementsAreSound(List<TupleElementStore> elements, bool isDirect)
            => elements.All(element => element.Item is 1 or 2)
                && elements.Count(element => element.Item is 2) is var setters && (setters is 0 || (isDirect && setters is 1))
                && elements.FirstOrDefault(element => element.Item is 1) is { Type: { IsByReference: false } kept } value
                && !IsValueTupleType(kept) && (value.Local is not null || value.Field is not null || value.OnStack);

        private static bool References(Instruction instr, VariableDefinition local, MethodBody body)
            => ReferenceEquals(instr.Operand, local) || ReferenceEquals(LoadedLocal(instr, body), local) || (TryGetStlocVariable(instr, body, out var stored) && ReferenceEquals(stored, local));

        // The temporary is rewritten by the next deconstruction that reuses it, which is not a read.
        private static bool IsTupleReadAgain(Instruction? from, VariableDefinition tuple, MethodBody body)
            => Following(from).FirstOrDefault(instr => References(instr, tuple, body)) is { } reference
                && !TryGetStlocVariable(reference, body, out _);

        private static bool IsThisLoad(Instruction instr, MethodDefinition lambda)
            => instr.OpCode.Code is Code.Ldarg_0 || (instr.OpCode.Code is Code.Ldarg or Code.Ldarg_S && ReferenceEquals(instr.Operand, lambda.Body.ThisParameter));

        // The fields of the component's display classes a lambda reads when it runs, taken through the lambdas it
        // creates in turn; null where it does something with the display class that this does not follow, such as
        // writing a captured variable, passing the class along or calling a method on it. A lambda is entered once,
        // so one creating itself reads as unsupported.
        private static HashSet<FieldDefinition>? CapturedReadsOf(MethodDefinition lambda, Dictionary<MethodDefinition, HashSet<FieldDefinition>?> known)
        {
            if (known.TryGetValue(lambda, out var done)) return done;
            known[lambda] = null;
            var instructions = lambda.HasBody ? lambda.Body.Instructions.ToList() : new List<Instruction>();
            var created = instructions.Where(IsClosureCreation).Select(instr => TryResolve(() => ((MethodReference)instr.Operand).Resolve()))
                .Select(method => method is null ? null : CapturedReadsOf(method, known)).ToList();
            var fields = instructions.Where(instr => instr is { OpCode.Code: Code.Ldfld or Code.Ldflda, Operand: FieldReference { } field } && IsClosureField(field) && !IsParentLink(field))
                .Select(instr => TryResolve(() => ((FieldReference)instr.Operand).Resolve())).ToList();
            known[lambda] = lambda.HasThis && created.All(each => each is not null) && fields.All(each => each is not null) && !instructions.Any(instr => BreaksClosureReads(instr, lambda))
                ? fields.OfType<FieldDefinition>().Concat(created.SelectMany(each => each!)).ToHashSet()
                : null;
            return known[lambda];
        }

        private static bool IsClosureCreation(Instruction instr)
            => instr is { OpCode.Code: Code.Ldftn or Code.Ldvirtftn, Operand: MethodReference { DeclaringType: var owner } } && IsClosureClass(owner);

        // What a lambda can do with its display class that the reads above do not account for: write a captured
        // variable, or hand the class on anywhere but a field read, a lambda it creates or a link from a class it creates.
        private static bool BreaksClosureReads(Instruction instr, MethodDefinition lambda)
            => IsThisLoad(instr, lambda) ? !IsReceiverOfClosureAccess(instr.Next) : instr switch
            {
                { Operand: ParameterDefinition parameter } when ReferenceEquals(parameter, lambda.Body.ThisParameter) => instr.OpCode.Code is Code.Starg or Code.Starg_S or Code.Ldarga or Code.Ldarga_S,
                { OpCode.Code: Code.Stfld, Operand: FieldReference field } => IsClosureField(field) && !IsParentLink(field),
                _ => default,
            };

        private static bool IsReceiverOfClosureAccess(Instruction? next)
            => next is { OpCode.Code: Code.Ldfld or Code.Ldflda or Code.Ldftn or Code.Ldvirtftn } || (next is { OpCode.Code: Code.Stfld, Operand: FieldReference link } && IsParentLink(link));

        // The fields any lambda writes. A captured variable a lambda writes is shared between the lambdas of one
        // render and the body, so a lambda kept from an earlier render would write one the later renders do not read.
        private static HashSet<FieldDefinition> FieldsWrittenByLambdas(MethodDefinition component)
            => component.DeclaringType.NestedTypes.Where(IsClosureClass).SelectMany(type => type.Methods).Where(method => method.HasBody)
                .SelectMany(method => method.Body.Instructions)
                .Where(instr => instr is { OpCode.Code: Code.Stfld, Operand: FieldReference field } && !IsParentLink(field))
                .Select(instr => TryResolve(() => ((FieldReference)instr.Operand).Resolve())).OfType<FieldDefinition>().ToHashSet();
    }
}

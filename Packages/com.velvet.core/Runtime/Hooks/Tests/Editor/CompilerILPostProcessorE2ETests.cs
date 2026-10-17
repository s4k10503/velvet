using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the default-on inner auto-memoization weaver (the <c>Unity.Velvet.CodeGen</c> ILPostProcessor)
    /// at the IL level: which <c>[Component]</c> bodies it weaves and which it leaves untouched.
    /// <list type="bullet">
    /// <item>Weaving a body means injecting both the <c>TryGetMemoizedVNode</c> gate and the
    /// <c>StoreMemoizedVNode</c> commit; an unwoven body has neither.</item>
    /// <item>An analyzable body — one with a hook whose captured value keys the cache — is woven independently
    /// of the props-bail flag. Single-element deconstruction of a hook result, multiple returns after the hook
    /// section, props prepended to the deps array, a captured <c>UseContext</c> value, a captured
    /// <c>UseMemo</c> value, a safe void effect hook alongside a value hook, and the stable references from
    /// <c>UseRef</c> / <c>UseService</c> are all analyzable shapes. The cache gate is injected after the whole
    /// hook section, so no hook call is skipped on a cache hit. A hook nested in an argument list is analyzable
    /// too, and its gate pops the operands evaluated ahead of the hook before returning a cached VNode. Each hook
    /// value keys the cache on its own, even where Roslyn stores two hooks' results into one local.</item>
    /// <item>A props-only body — parameters and no hook — is woven too, keyed on its parameters alone with the
    /// gate at method entry, unless it sets <c>Memoize = true</c>.</item>
    /// <item>An open virtual / interface dispatch outside the BCL / Unity carve-out — made directly or through a
    /// helper — is woven past the gate: after the last hook call, or anywhere in a body with no hook.</item>
    /// <item>A body the weaver cannot prove correct is left unwoven (graceful bailout): neither a parameter nor a
    /// hook to key a cache on, a props-only body left to its props bail, a discarded hook value, a whole-tuple
    /// capture (compared structurally, not by reference, so a fresh-but-equal record would be a stale hit), a
    /// body with void hooks alone and no parameter (empty deps would freeze it on an unconditional hit), a body
    /// that reaches the suspend-unsafe <c>Use</c> hook or
    /// <c>UseMutation</c> — directly or transitively through a custom hook — a hook inside a loop (head-tested
    /// or do-while), a hook section overlapping a try/catch region, and an open dispatch — made directly or
    /// through a helper — ahead of a hook call, where a hook its runtime target composes would feed the body a
    /// value the deps array does not capture. A delegate invocation (virtual Invoke on a sealed type) is not an
    /// open dispatch.</item>
    /// <item>Below the gate, each child of a children list gets a scope gate (<c>TryGetMemoizedScope</c>) of its own
    /// where everything it reads is a value compared at its start, a child inside another child included. A child
    /// reading a <c>MutableRef</c>, a mutable static, a method the weaver cannot read or the clock, or writing a local
    /// a later child reads, gets none, and neither does any child of a body with a loop around the children or a
    /// <c>try</c> in it. <c>ReactiveScopeWovenBehaviorE2ETests</c> asserts what the gates do.</item>
    /// <item>A body opting out with <c>[Component(Compiler = false)]</c> is left unwoven even when it is provably
    /// analyzable; the opt-out is honored ahead of analysis.</item>
    /// <item>Every component — woven, opted-out, or bailed — still renders normally and produces visible output
    /// on the first render.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class CompilerILPostProcessorE2ETests
    {
        private VisualElement _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
        }

        [Component]
        public static VNode WeavedComponent()
        {
            var (count, _) = Hooks.UseState(0);
            return V.Label(text: count.ToString());
        }

        // Same analyzable shape as WeavedComponent (a captured UseState value), but [Component(Compiler = false)]
        // opts the component out of the transform. The weaver honors the opt-out ahead of analysis, so the body
        // is left unwoven even though it could be proven correct.
        [Component(Compiler = false)]
        public static VNode CompilerOptOutComponent()
        {
            var (count, _) = Hooks.UseState(0);
            return V.Label(text: count.ToString());
        }

        [Component]
        public static VNode DeconstructedComponent()
        {
            var (value, _) = Hooks.UseState(42);
            return V.Label(text: value.ToString());
        }

        [Component]
        public static VNode MultiReturnComponent()
        {
            var (count, _) = Hooks.UseState(7);
            if (count < 0)
            {
                return V.Label(text: "negative");
            }
            return V.Label(text: count.ToString());
        }

        public sealed record TupleStateRecord(int Value);

        // Capturing the whole UseState tuple (no deconstruction) whose value is a record: the boxed tuple is
        // compared structurally per element, diverging from the reference equality the reconciler uses to drive a
        // re-render, so a fresh-but-equal record set would be a stale hit. The weaver must bail; single-element
        // deconstruction is required.
        [Component]
        public static VNode WholeTupleStateComponent()
        {
            var state = Hooks.UseState(new TupleStateRecord(0));
            return V.Label(text: state.Item1.Value.ToString());
        }

        // Neither a hook call nor a parameter: nothing to key a cache on, so the weaver has no inner memo to
        // inject -> bailout.
        [Component]
        public static VNode NoHookComponent()
        {
            return V.Label(text: "no-hook");
        }

        public sealed record GreetProps(string Name);

        // Props-receiving component with no hook: the prop is the whole deps array, and the gate goes at
        // method entry.
        [Component]
        public static VNode PropsComponent(GreetProps p)
        {
            return V.Label(text: p.Name);
        }

        // The same body with Memoize = true. The props bail already decides when a parent render reaches it,
        // so the weaver leaves it to that bail.
        [Component(Memoize = true)]
        public static VNode MemoizedPropsComponent(GreetProps p)
        {
            return V.Label(text: p.Name);
        }

        // A hook body keeps its gate whatever Memoize says: its hook values can change while its props do not.
        [Component(Memoize = true)]
        public static VNode MemoizedPropsWithHookComponent(GreetProps p)
        {
            var (suffix, _) = Hooks.UseState("!");
            return V.Label(text: p.Name + suffix);
        }

        // Compiler = false behind another flag on the same attribute: the opt-out is read by its name.
        [Component(Memoize = true, Compiler = false)]
        public static VNode MemoizedOptOutComponent()
        {
            var (count, _) = Hooks.UseState(0);
            return V.Label(text: count.ToString());
        }

        public interface IPropsFormatter
        {
            string Format(string name);
        }

        private static readonly IPropsFormatter? s_propsFormatter = null;

        // A props-only body making an open interface dispatch.
        [Component]
        public static VNode PropsInterfaceDispatchComponent(GreetProps p)
        {
            return V.Label(text: s_propsFormatter?.Format(p.Name) ?? p.Name);
        }

        // Props-receiving component with a hook. The prop is prepended to the deps array alongside the
        // hook-derived value, so this is an analyzable shape the weaver must weave.
        [Component]
        public static VNode PropsWithHookComponent(GreetProps p)
        {
            var (suffix, _) = Hooks.UseState("!");
            return V.Label(text: p.Name + suffix);
        }

        private static readonly ComponentContext<string> NameContext =
            ComponentContext<string>.Create("default");

        // UseContext captures the live context value into the deps array; the memo compares it with the same
        // strictness the Provider uses to drive a re-render, so the body is analyzable and must be woven.
        [Component]
        public static VNode ContextComponent()
        {
            var name = Hooks.UseContext(NameContext);
            return V.Label(text: name);
        }

        private static readonly int s_externalCount = 3;

        private static System.Action ExternalSubscribe(System.Action onStoreChange) => () => { };

        // UseSyncExternalStore returns the snapshot the body renders from, and it is captured into the deps array,
        // so the body is analyzable and must be woven.
        [Component]
        public static VNode SyncExternalStoreComponent()
        {
            var count = Hooks.UseSyncExternalStore(ExternalSubscribe, () => s_externalCount);
            return V.Label(text: count.ToString());
        }

        // A discarded hook result (UseState whose value element is dropped via '_') leaves the changing value
        // out of the deps array, so the weaver must bail rather than cache against the stable setter alone.
        [Component]
        public static VNode DiscardedValueComponent()
        {
            var (_, setValue) = Hooks.UseState(0);
            _ = setValue;
            return V.Label(text: "discarded");
        }

        // UseMutation returns a MutationResult whose reference is stable across renders. Mutate() mutates the
        // status / data in place and requests a re-render, but the captured reference stays equal, so a woven
        // memo would hit and return a stale (Idle) VNode after the mutation. The allow-list excludes UseMutation,
        // so the weaver must bail.
        [Component]
        public static VNode UseMutationComponent()
        {
            var mutation = Hooks.UseMutation(new MutationOptions<int, int>(
                MutationFn: (v, _) => VelvetTask.FromResult(v * 2)));
            return V.Label(text: mutation.Status.ToString());
        }

        // A void effect hook (UseEffect) captures no dep, but the allow-list admits it: it runs for its side
        // effect only and cannot drive a re-render through a return value. UseState supplies the captured dep,
        // and the gate is injected after the whole hook section (past the UseEffect call), so the body is woven.
        [Component]
        public static VNode VoidEffectComponent()
        {
            var (value, _) = Hooks.UseState(0);
            Hooks.UseEffect(() => () => { }, System.Array.Empty<object>());
            return V.Label(text: value.ToString());
        }

        // A void effect hook alone — no value hook, no parameter — leaves the deps array empty. Weaving it would
        // make TryGetMemoizedVNode an unconditional hit that freezes the body after the first render, so the
        // weaver must leave it unwoven.
        [Component]
        public static VNode VoidOnlyComponent()
        {
            Hooks.UseEffect(() => () => { }, System.Array.Empty<object>());
            return V.Label(text: "void-only");
        }

        // UseFrame is a memo-safe void hook: its per-frame callback flows through a ref slot the hook
        // overwrites every render, never through a captured return value, so it advances the hook
        // boundary without contributing a dep while the prop supplies the deps entry. A hook missing
        // from the weaver's allow-lists bails the whole body, so this shape pins the registration —
        // losing it would silently cost every UseFrame component its auto-memo.
        [Component]
        public static VNode UseFrameComponent(GreetProps p)
        {
            Hooks.UseFrame(_ => { });
            return V.Label(text: p.Name);
        }

        // UseRef returns a stable reference that does not self-trigger a re-render. It is on the value allow-list,
        // so capturing the stable reference as a constant dep is sound and the body is woven.
        [Component]
        public static VNode UseRefComponent()
        {
            var reference = Hooks.UseRef<object>();
            _ = reference;
            return V.Label(text: "ref");
        }

        public interface IWovenService
        {
            string Name();
        }

        // UseService returns a stable service reference (DI-resolved) that does not self-trigger a re-render. It
        // is on the value allow-list, so the body is woven, and the stable reference itself is the captured dep.
        [Component]
        public static VNode UseServiceComponent()
        {
            var service = Hooks.UseService<IWovenService>();
            return V.Label(text: service != null ? "resolved" : "missing");
        }

        // Custom hook that transitively reaches the suspend-unsafe Use hook. A component calling it must bail.
        private static System.Func<VelvetTask<string>> s_factory =
            () => VelvetTask.FromResult("x");

        private static string UseSuspendingResource() => Hooks.Use(s_factory);

        [Component]
        public static VNode TransitiveUseComponent()
        {
            var data = UseSuspendingResource();
            return V.Label(text: data);
        }

        // Custom hook that transitively reaches UseMutation. A component calling it must bail.
        private static MutationResult<int, int> UseDoubler()
            => Hooks.UseMutation(new MutationOptions<int, int>(
                MutationFn: (v, _) => VelvetTask.FromResult(v * 2)));

        [Component]
        public static VNode TransitiveUseMutationComponent()
        {
            var mutation = UseDoubler();
            return V.Label(text: mutation.Status.ToString());
        }

        // UseMemo's captured return value changes only when its deps change, so it is on the value
        // allow-list; a body whose ONLY hook is UseMemo is analyzable and must be woven.
        [Component]
        public static VNode UseMemoOnlyComponent()
        {
            var memoized = Hooks.UseMemo(() => "memo", "stable");
            return V.Label(text: memoized);
        }

        // UseMemo placed after another value hook. The whole hook section — including the UseMemo call —
        // must run on every render, so the injected cache gate has to land after the UseMemo call; a gate
        // anchored between UseState and UseMemo would skip the UseMemo call outright on a cache hit and
        // leave its changing value out of the deps array.
        [Component]
        public static VNode UseMemoAfterStateComponent()
        {
            var (count, _) = Hooks.UseState(3);
            var memoized = Hooks.UseMemo(() => "value", "key");
            return V.Label(text: memoized + count.ToString());
        }

        public interface IDispatchService
        {
            string Value();
        }

        private sealed class ConstantDispatchService : IDispatchService
        {
            public string Value() => "svc";
        }

        private static readonly IDispatchService? s_dispatchService = new ConstantDispatchService();

        // A call through an interface declared in a Velvet-referencing assembly, after the only hook call.
        [Component]
        public static VNode InterfaceDispatchComponent(GreetProps p)
        {
            var (count, _) = Hooks.UseState(0);
            var extra = s_dispatchService?.Value() ?? "none";
            return V.Label(text: extra + count.ToString() + p.Name);
        }

        // The same dispatch ahead of the hook call.
        [Component]
        public static VNode InterfaceDispatchAheadOfHookComponent()
        {
            var extra = s_dispatchService?.Value() ?? "none";
            var (count, _) = Hooks.UseState(0);
            return V.Label(text: extra + count.ToString());
        }

        // The dispatch sits in a static helper rather than in the body.
        private static string DescribeDispatchService() => s_dispatchService?.Value() ?? "none";

        [Component]
        public static VNode HelperDispatchComponent()
        {
            var (count, _) = Hooks.UseState(0);
            return V.Label(text: DescribeDispatchService() + count.ToString());
        }

        [Component]
        public static VNode HelperDispatchAheadOfHookComponent()
        {
            var extra = DescribeDispatchService();
            var (count, _) = Hooks.UseState(0);
            return V.Label(text: extra + count.ToString());
        }

        // A custom hook composing a hook and the dispatch: a hook call, so it runs ahead of the gate.
        private static string UseNameWithDispatch()
        {
            var (name, _) = Hooks.UseState("name");
            return name + DescribeDispatchService();
        }

        [Component]
        public static VNode CustomHookWithDispatchComponent()
        {
            var name = UseNameWithDispatch();
            return V.Label(text: name);
        }

        // The lambda's body makes the dispatch when it is called, not while the component body runs.
        [Component]
        public static VNode DispatchInsideHookArgumentLambdaComponent()
        {
            var describe = Hooks.UseCallback<System.Func<string>>(() => s_dispatchService?.Value() ?? "none",
                System.Array.Empty<object>());
            return V.Label(text: describe());
        }

        // A hook value passed straight as an argument, with no local of its own.
        private static int s_stackBuilds;

        private static string CountStackBuild()
        {
            s_stackBuilds++;
            return "stack";
        }

        [Component]
        public static VNode StoreValueAsArgumentComponent()
            => V.Label(text: Hooks.UseStore(s_firstStore, value => value.ToString()), name: CountStackBuild());

        private static System.Action<int> s_stackParentSetTick = null!;

        [Component]
        public static VNode StoreValueAsArgumentParent()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_stackParentSetTick = setTick;
            return V.Component(StoreValueAsArgumentComponent, key: "stack");
        }

        // Item1 read off the returned tuple without storing either.
        [Component]
        public static VNode StateItem1AsArgumentComponent()
            => V.Label(text: Hooks.UseState("first").Item1, name: CountStackBuild());

        private static System.Func<VNode> s_stackChild = null!;

        // Renders whichever component a case names and re-renders it with nothing about it changed.
        [Component]
        public static VNode StackChildParent()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_stackParentSetTick = setTick;
            return V.Component(s_stackChild, key: "stack-child");
        }

        internal static StateUpdater<int> s_stackSetter;

        // Only the setter is read off the tuple: the changing value is never captured.
        [Component]
        public static VNode StateSetterOffTheStackComponent()
        {
            s_stackSetter = Hooks.UseState(0).Item2;
            return V.Label(text: "setter");
        }

        private static string Describe(object state) => state.ToString();

        // The whole tuple handed on boxed, never stored.
        [Component]
        public static VNode WholeTupleOffTheStackComponent()
            => V.Label(text: Describe(Hooks.UseState("whole")));

        // A custom hook composing a dispatch, its value passed straight as an argument: the hook call is the
        // boundary itself.
        [Component]
        public static VNode CustomHookWithDispatchAsArgumentComponent()
            => V.Label(text: UseNameWithDispatch());

        // A static field that happens to be named like the tuple's value element.
        private static class TupleSlot
        {
            internal static (string value, StateUpdater<string> setValue) Item1;
        }

        // The whole tuple stored straight into that field, so the instruction after the call names a field Item1.
        [Component]
        public static VNode TupleStoredToAFieldNamedItem1Component()
        {
            TupleSlot.Item1 = Hooks.UseState("slot");
            return V.Label(text: TupleSlot.Item1.value);
        }

        private static string s_storedHookValue = "";

        // The hook value stored straight to a static field.
        [Component]
        public static VNode HookValueStoredToAFieldComponent()
        {
            s_storedHookValue = Hooks.UseStore(s_firstStore, value => value.ToString());
            return V.Label(text: "stored");
        }

        private sealed class StoredValueHolder
        {
            public string Value = "";
        }

        private static readonly StoredValueHolder s_storedValueHolder = new();

        // The hook value stored to an instance field.
        [Component]
        public static VNode HookValueStoredToAnInstanceFieldComponent()
        {
            s_storedValueHolder.Value = Hooks.UseStore(s_firstStore, value => value.ToString());
            return V.Label(text: "stored");
        }

        // A custom hook returning nothing: no value to capture, however the call is followed.
        private static void UseMountLog()
        {
            Hooks.UseEffect(() => () => { }, System.Array.Empty<object>());
        }

        [Component]
        public static VNode VoidCustomHookComponent()
        {
            UseMountLog();
            var (count, _) = Hooks.UseState(0);
            return V.Label(text: count.ToString());
        }

        private static int s_countSlot;

        // A custom hook returning a reference: the caller reads through it, so there is no value to copy.
        private static ref int UseCountSlot()
        {
            _ = Hooks.UseRef<object>();
            return ref s_countSlot;
        }

        [Component]
        public static VNode RefReturningCustomHookComponent()
        {
            var count = UseCountSlot();
            var (offset, _) = Hooks.UseState(0);
            return V.Label(text: (count + offset).ToString());
        }

        // A reference local over that hook's result: a managed pointer, which the deps array cannot hold.
        [Component]
        public static VNode RefLocalFromCustomHookComponent()
        {
            ref var count = ref UseCountSlot();
            var (offset, _) = Hooks.UseState(0);
            return V.Label(text: (count + offset).ToString());
        }

        // A custom hook returning a pair whose second element changes between renders.
        private static (int count, int total) UseCountAndTotal()
        {
            var count = Hooks.UseStore(s_firstStore, value => value);
            var total = Hooks.UseStore(s_secondStore, value => value);
            return (count, total);
        }

        [Component]
        public static VNode CustomPairDeconstructedComponent()
        {
            var (count, total) = UseCountAndTotal();
            return V.Label(text: count.ToString() + "/" + total.ToString());
        }

        // A custom hook returning an array, its value indexed straight off the stack.
        private static string[] UseNames()
        {
            var name = Hooks.UseStore(s_firstStore, value => value.ToString());
            return Hooks.UseMemo(() => new[] { name }, name);
        }

        [Component]
        public static VNode ArrayFromCustomHookComponent()
            => V.Label(text: UseNames()[0], name: CountStackBuild());

        // A Ref<object> read straight off the stack: the copy's type is a generic instance built from the call.
        [Component]
        public static VNode RefValueOffTheStackComponent()
            => V.Label(text: Hooks.UseRef<object>() != null ? "ref" : "none", name: CountStackBuild());

        // A helper calling itself: the walk classifying it meets it again before it has finished reading it.
        private static string Repeat(string text, int times) => times <= 0 ? text : Repeat(text + "!", times - 1);

        [Component]
        public static VNode RecursiveHelperComponent()
        {
            var (count, _) = Hooks.UseState(0);
            return V.Label(text: Repeat(count.ToString(), 2));
        }

        // A custom hook composing a SAFE hook: a hook call, whose value is captured.
        private static int UseCount()
        {
            var (count, _) = Hooks.UseState(5);
            return count;
        }

        [Component]
        public static VNode CustomHookOnlyComponent()
        {
            var count = UseCount();
            return V.Label(text: count.ToString());
        }

        private static readonly object s_boxedLabel = "label";

        // An override of object.ToString reached ahead of the hook: the carve-out reads it without resolving.
        [Component]
        public static VNode BclVirtualAheadOfHookComponent()
        {
            var label = s_boxedLabel.ToString();
            var (count, _) = Hooks.UseState(0);
            return V.Label(text: label + count.ToString());
        }

        // Building the delegate makes no call into the lambda that dispatches.
        private static System.Func<string> MakeDescriber() => () => s_dispatchService?.Value() ?? "none";

        [Component]
        public static VNode DescriberAheadOfHookComponent()
        {
            var describe = MakeDescriber();
            var (count, _) = Hooks.UseState(0);
            return V.Label(text: describe() + count.ToString());
        }

        public class MutatingFormatter
        {
            public virtual string Format(int value)
            {
                var mutation = Hooks.UseMutation(new MutationOptions<int, int>(
                    MutationFn: (v, _) => VelvetTask.FromResult(v)));
                return mutation.Status.ToString() + value;
            }
        }

        private static readonly MutatingFormatter s_mutatingFormatter = new();

        // The declared body of the virtual reaches UseMutation, but an override need not.
        [Component]
        public static VNode VirtualWithNonSafeDeclaredBodyComponent()
        {
            var (count, _) = Hooks.UseState(0);
            return V.Label(text: s_mutatingFormatter.Format(count));
        }

        private static readonly bool s_recurse = false;

        // Three custom hooks calling one another in a ring, the first reaching its own hook only after the call
        // into the second.
        private static string UseLoopFirst(int depth)
        {
            var rest = depth > 0 ? UseLoopSecond(depth - 1) : "";
            var (own, _) = Hooks.UseState("first");
            return rest + own;
        }

        private static string UseLoopSecond(int depth) => UseLoopThird(depth);

        private static string UseLoopThird(int depth) => UseLoopFirst(depth);

        // Declared ahead of the case below so the walk enters the cycle at UseLoopFirst.
        [Component]
        public static VNode LoopEntryComponent()
        {
            var value = UseLoopFirst(0);
            return V.Label(text: value);
        }

        [Component]
        public static VNode LoopMemberBehindABranchComponent()
        {
            var value = s_recurse ? UseLoopSecond(0) : "";
            var (count, _) = Hooks.UseState(0);
            return V.Label(text: value + count.ToString());
        }

        // The same cycle reaching UseMutation.
        private static string UseMutatingLoopFirst(int depth)
        {
            var rest = depth > 0 ? UseMutatingLoopSecond(depth - 1) : "";
            var mutation = Hooks.UseMutation(new MutationOptions<int, int>(
                MutationFn: (v, _) => VelvetTask.FromResult(v)));
            return rest + mutation.Status;
        }

        private static string UseMutatingLoopSecond(int depth) => UseMutatingLoopFirst(depth);

        // Declared ahead of the case below so the safety walk enters the cycle at UseMutatingLoopFirst.
        [Component]
        public static VNode MutatingLoopEntryComponent()
        {
            var value = UseMutatingLoopFirst(0);
            return V.Label(text: value);
        }

        [Component]
        public static VNode MutatingLoopMemberComponent()
        {
            var value = UseMutatingLoopSecond(0);
            return V.Label(text: value);
        }

        public class OverridableFormatter
        {
            public virtual string Format(int value) => value.ToString();
        }

        private static readonly OverridableFormatter s_formatter = new();

        // A virtual method on a non-sealed class in a Velvet-referencing assembly, after the only hook call.
        [Component]
        public static VNode VirtualDispatchComponent(GreetProps p)
        {
            var (count, _) = Hooks.UseState(0);
            return V.Label(text: s_formatter.Format(count) + p.Name);
        }

        private static System.Func<GreetProps, VNode> s_propChild = null!;
        private static System.Action<string> s_propParentSetName = null!;

        // Renders whichever props component a case names, with a name the case can change.
        [Component]
        public static VNode PropChildParent()
        {
            var (name, setName) = Hooks.UseState("a");
            s_propParentSetName = setName;
            return V.Component(s_propChild, new GreetProps(name), key: "prop-child");
        }

        public delegate string TextProvider();

        private static readonly TextProvider s_textProvider = static () => "delegate";

        // Invoking a delegate declared in a Velvet-referencing assembly: a delegate type is sealed and its
        // runtime-implemented Invoke cannot be overridden by user code, so the call is not an open dispatch.
        [Component]
        public static VNode DelegateInvokeComponent()
        {
            var (count, _) = Hooks.UseState(0);
            return V.Label(text: s_textProvider() + count.ToString());
        }

        // A hook inside a head-tested loop: the loop is entered through a forward jump over the body, so the
        // hook can be skipped entirely (zero iterations) or repeated. The weaver must bail. The loop runs
        // exactly once at runtime so mounting still satisfies the rules of hooks.
        [Component]
        public static VNode WhileLoopHookComponent()
        {
            var text = "";
            var i = 0;
            while (i < 1)
            {
                var (count, _) = Hooks.UseState(11);
                text = count.ToString();
                i++;
            }
            return V.Label(text: text);
        }

        // A hook inside a do-while loop: the body is entered without any forward jump — only a backward
        // conditional branch closes the loop — so forward-skip detection alone cannot see it. The hook can
        // repeat within one render, and a cache gate anchored after it would sit inside the loop, returning
        // from mid-loop on a hit. The weaver must bail. The loop runs exactly once at runtime so mounting
        // still satisfies the rules of hooks.
        [Component]
        public static VNode DoWhileLoopHookComponent()
        {
            var text = "";
            var i = 0;
            do
            {
                var (count, _) = Hooks.UseState(13);
                text = count.ToString();
                i++;
            } while (i < 1);
            return V.Label(text: text);
        }

        // A hook and a hook-derived return inside a try region: the injected early return (cache hit) and
        // the commit before the real returns would need the Leave protocol required for protected regions,
        // which the weaver does not emit. The weaver must bail.
        [Component]
        public static VNode TryCatchHookComponent()
        {
            try
            {
                var (count, _) = Hooks.UseState(17);
                return V.Label(text: count.ToString());
            }
            catch (System.Exception)
            {
                return V.Label(text: "error");
            }
        }

        private sealed class CountStore : Store<int>
        {
            public CountStore(int initial) : base(initial) { }
            protected override void ResetCore() => SetState(_ => 0);
        }

        private static readonly CountStore s_countStore = new(4);

        // `className` is pushed for V.Label before the hook's argument is evaluated, so the gate after the hook
        // lands with that operand on the stack.
        [Component]
        public static VNode ExpressionBodiedNestedHookComponent()
            => V.Label(text: Hooks.UseStore(s_countStore, value => value).ToString());

        [Component]
        public static VNode BlockBodiedNestedHookComponent()
        {
            return V.Label(text: Hooks.UseStore(s_countStore, value => value + 1).ToString());
        }

        [Component]
        public static VNode ChildArgumentHookComponent()
            => V.Div(className: "row", children: new VNode[]
            {
                V.Label(text: Hooks.UseStore(s_countStore, value => value + 2).ToString()),
            });

        public sealed record OffsetProps(int Offset);

        [Component]
        public static VNode CapturingSelectorHookComponent(OffsetProps p)
            => V.Label(text: Hooks.UseStore(s_countStore, value => value + p.Offset).ToString());

        private static readonly string s_rowClass = " row ";

        [Component]
        public static VNode CatchAheadOfNestedHookComponent()
        {
            string className;
            try
            {
                className = s_rowClass.Trim();
            }
            catch (System.NullReferenceException)
            {
                className = "fallback";
            }
            return V.Label(className: className, text: Hooks.UseStore(s_countStore, value => value + 5).ToString());
        }

        public sealed class ClassHolder
        {
            public string Name { get; init; } = "";
        }

        [Component]
        public static VNode InitSetterAheadOfNestedHookComponent()
            => V.Label(className: new ClassHolder { Name = "row" }.Name,
                text: Hooks.UseStore(s_countStore, value => value + 6).ToString());

        private static readonly bool s_wide = true;

        [Component]
        public static VNode ConditionalAheadOfNestedHookComponent()
            => V.Label(className: "row",
                text: (s_wide ? "wide " : "narrow ") + Hooks.UseStore(s_countStore, value => value + 7).ToString());

        private sealed class SettableStore : Store<int>
        {
            public SettableStore(int initial) : base(initial) { }
            public void Set(int value) => SetState(_ => value);
            protected override void ResetCore() => SetState(_ => 0);
        }

        private static SettableStore s_firstStore = null!;
        private static SettableStore s_secondStore = null!;

        [Component]
        public static VNode TwoNestedHooksComponent()
            => V.Label(text: Hooks.UseStore(s_firstStore, value => value).ToString() + "/"
                + Hooks.UseStore(s_secondStore, value => value).ToString());

        [Component]
        public static VNode TwoHookStatementsComponent()
        {
            var first = Hooks.UseStore(s_firstStore, value => value).ToString();
            var second = Hooks.UseStore(s_secondStore, value => value).ToString();
            return V.Label(text: first + "/" + second);
        }

        private static System.Action<int> s_rowParentSetTick = null!;
        private static int s_rowBuilds;

        private static string CountRowBuild()
        {
            s_rowBuilds++;
            return "value";
        }

        // The list ahead of the hook rents a node array, and its button a props bag and an event array; a memo
        // hit drops all three with the gate's pops.
        [Component]
        public static VNode SiblingAheadOfNestedHookComponent()
            => V.Div("row", V.Div(children: V.List(new[] { "add" }, id => id, id => V.Button(text: id, onClick: () => { }))),
                V.Label(text: Hooks.UseStore(s_countStore, value => value).ToString(), name: CountRowBuild()));

        [Component]
        public static VNode SiblingRowParent()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_rowParentSetTick = setTick;
            return V.Component(SiblingAheadOfNestedHookComponent, key: "row");
        }

        #region Woven shapes (gate + commit injected)

        [Test]
        public void Given_PlainComponentWithHook_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(WeavedComponent))), Is.True,
                "An analyzable [Component] with a captured hook value is woven");
        }

        [Test]
        public void Given_DeconstructionPattern_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(DeconstructedComponent))), Is.True,
                "Single-element deconstruction of a hook result is an analyzable shape");
        }

        [Test]
        public void Given_MultipleReturnsAfterHookSection_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(MultiReturnComponent))), Is.True,
                "Multiple returns after the hook section are analyzable");
        }

        [Test]
        public void Given_PropsReceivingComponentWithHook_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(PropsWithHookComponent))), Is.True,
                "Props are prepended to the deps array; a props body with a hook is analyzable");
        }

        [Test]
        public void Given_PropsOnlyComponent_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(PropsComponent))), Is.True,
                "A props-only body keys the cache on its parameter alone, gated at method entry");
        }

        // GREEN_ON_BASE(characterization): the base weaves a hook body whatever Memoize says, and so does this change.
        // Only a body with no hook is left to its props bail; applying `RequestsPropsBail` to every body is what
        // reddens this.
        [Test]
        public void Given_MemoizedPropsComponentWithHook_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(MemoizedPropsWithHookComponent))), Is.True,
                "A hook body with Memoize = true keeps its gate: its hook values change while its props hold");
        }

        [Test]
        public void Given_UseContextComponent_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(ContextComponent))), Is.True,
                "UseContext captures the live value into the deps array; the body is woven");
        }

        [Test]
        public void Given_UseSyncExternalStoreComponent_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(SyncExternalStoreComponent))), Is.True,
                "UseSyncExternalStore captures its snapshot into the deps array; the body is woven");
        }

        [Test]
        public void Given_SafeVoidEffectAlongsideValueHook_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(VoidEffectComponent))), Is.True,
                "A safe void effect hook advances the hook boundary while UseState supplies the captured dep");
        }

        [Test]
        public void Given_UseFrameAlongsideAProp_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert — a hook absent from the weaver's allow-lists bails the whole body, so this
            // pins UseFrame's registration: the body must weave, not silently lose its auto-memo.
            Assert.That(IsWoven(LoadMethod(nameof(UseFrameComponent))), Is.True,
                "UseFrame is a memo-safe void hook; with a prop supplying the deps entry the body is woven");
        }

        [Test]
        public void Given_UseRefComponent_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(UseRefComponent))), Is.True,
                "UseRef returns a stable reference captured as a constant dep; the body is woven");
        }

        [Test]
        public void Given_UseServiceComponent_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(UseServiceComponent))), Is.True,
                "UseService returns a stable DI reference captured as a constant dep; the body is woven");
        }

        [Test]
        public void Given_UseMemoOnlyComponent_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(UseMemoOnlyComponent))), Is.True,
                "UseMemo is a value hook whose captured result changes only when its deps change; a body whose"
                + " only hook is UseMemo is analyzable and woven");
        }

        [Test]
        public void Given_UseMemoAfterValueHook_When_Woven_Then_GateIsInjectedAfterTheUseMemoCall()
        {
            // Arrange
            var method = LoadMethod(nameof(UseMemoAfterStateComponent));
            Assume.That(IsWoven(method), Is.True, "Precondition: the UseState + UseMemo body is woven");

            // Act
            var useMemoIndex = IndexOfHookCall(method, nameof(Hooks.UseMemo));
            var gateIndex = IndexOfHookCall(method, nameof(Hooks.TryGetMemoizedVNode));

            // Assert — a gate before the UseMemo call would skip the hook outright on a cache hit,
            // violating the invariant that hooks run on every render.
            Assert.That(gateIndex, Is.GreaterThan(useMemoIndex),
                "The cache gate must land after the whole hook section, including the trailing UseMemo call");
        }

        [Test]
        public void Given_DelegateInvokeComponent_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(DelegateInvokeComponent))), Is.True,
                "A delegate's Invoke is virtual on a sealed type — not an open dispatch — so invoking a"
                + " user-declared delegate does not bail the component");
        }

        [Test]
        public void Given_PropsOnlyComponentWithInterfaceDispatch_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(PropsInterfaceDispatchComponent))), Is.True,
                "A body with no hook is gated at entry, so its interface dispatch runs past the gate");
        }

        [Test]
        public void Given_InterfaceDispatchAfterTheHook_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(InterfaceDispatchComponent))), Is.True,
                "An interface dispatch after the last hook call runs past the gate");
        }

        [Test]
        public void Given_VirtualDispatchAfterTheHook_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(VirtualDispatchComponent))), Is.True,
                "A call to an overridable virtual method after the last hook call runs past the gate");
        }

        [Test]
        public void Given_HelperDispatchingAfterTheHook_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(HelperDispatchComponent))), Is.True,
                "A helper whose only reach is a dispatch is not a hook call, so its value needs no capture");
        }

        // GREEN_ON_BASE(characterization): the base reads only call instructions too, so it weaves this body.
        // What it pins is the ahead-of-gate check doing the same: also accepting `OpCodes.Ldftn` in the opcode
        // test of `IsOpaqueCall` reads the lambda the `ldftn` names, and reddens it.
        [Test]
        public void Given_DispatchInsideALambdaHandedToAHook_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(DispatchInsideHookArgumentLambdaComponent))), Is.True,
                "A lambda's body does not run while the component body builds, so its dispatch is not ahead of the gate");
        }

        // GREEN_ON_BASE(characterization): the base classifies a recursive helper as well.
        // What it pins is the fold's cycle guard: deleting the open-method branch from `CallGraphFold.Fold`
        // leaves the walk recursing through `Repeat` without end.
        [Test]
        public void Given_RecursiveHelperAfterTheHook_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(RecursiveHelperComponent))), Is.True,
                "A helper that calls itself is classified once, and reaches no hook");
        }

        // GREEN_ON_BASE(characterization): the base reads a custom hook composing UseState as a hook call too.
        // What it pins is the direct hook ending the fold: deleting the `IsDirectHookCall` branch from
        // `HookReachFold.TryLeaf` sends the fold into UseState's own body, which reaches no positional hook.
        [Test]
        public void Given_CustomHookComposingASafeHook_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(CustomHookOnlyComponent))), Is.True,
                "A custom hook's value is captured like a direct hook's, so the body has a dep to key on");
        }

        // GREEN_ON_BASE(characterization): the base skips a BCL / Unity callee unresolved too, so it weaves this.
        // What it pins is the reach fold keeping that carve-out: deleting the `CannotReachVelvetHook` line
        // from `HookReachFold.TryLeaf` reads object.ToString as an open dispatch ahead of the gate.
        [Test]
        public void Given_BclVirtualCallAheadOfTheHook_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(BclVirtualAheadOfHookComponent))), Is.True,
                "A BCL virtual signature is read as hook-free without resolving it");
        }

        // GREEN_ON_BASE(characterization): the base reads only call instructions in a callee's body too.
        // What it pins is the fold doing the same: dropping the call-opcode filter from `CallGraphFold.Visit`
        // walks the lambda MakeDescriber's `ldftn` names and reads the helper as Opaque.
        [Test]
        public void Given_HelperBuildingADispatchingLambdaAheadOfTheHook_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(DescriberAheadOfHookComponent))), Is.True,
                "Building a delegate makes no call into its body, so the helper reaches no dispatch");
        }

        [Test]
        public void Given_VirtualWhoseDeclaredBodyReachesUseMutation_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(VirtualWithNonSafeDeclaredBodyComponent))), Is.True,
                "An open dispatch's declared body need not be the one that runs, so the safety walk does not read it");
        }

        [Test]
        public void Given_AHookValuePassedStraightAsAnArgument_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(StoreValueAsArgumentComponent))), Is.True,
                "A hook value left on the stack is copied where the call produces it");
        }

        [Test]
        public void Given_Item1ReadOffTheStack_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(StateItem1AsArgumentComponent))), Is.True,
                "Item1 read straight off the returned tuple is copied where the ldfld produces it");
        }

        [Test]
        public void Given_AnArrayFromACustomHookIndexedOffTheStack_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(ArrayFromCustomHookComponent))), Is.True,
                "An array value left on the stack is copied into a local of the array type");
        }

        [Test]
        public void Given_HookNestedInArgumentList_When_Woven_Then_HitPathPopsTheOperandUnderTheGate()
        {
            // Act
            var pops = HitPathPops(LoadMethod(nameof(ExpressionBodiedNestedHookComponent)));

            // Assert
            Assert.That(pops, Is.EqualTo(1),
                "The body stays memoized, and its hit path pops V.Label's className before returning the cached"
                + " VNode");
        }

        #endregion

        #region Hook values held by display classes

        // A hook value a handler captures is hoisted by the compiler into a field of a display class, so the hook
        // call stores into a field and the value is read from it.
        [Component]
        public static VNode HandlerCapturingAHookValueComponent()
        {
            var (count, setCount) = Hooks.UseState(0);
            return V.Button(text: "add", onClick: () => setCount.Invoke(count + 1));
        }

        [Component]
        public static VNode HandlerCapturingAMemoValueComponent()
        {
            var label = Hooks.UseMemo(() => "x", System.Array.Empty<object>());
            return V.Button(text: "go", onClick: () => System.Console.Write(label));
        }

        [Component]
        public static VNode HandlerCapturingAWholeTupleComponent()
        {
            var pair = Hooks.UseState(0);
            return V.Button(text: "go", onClick: () => pair.Item2.Invoke(pair.Item1 + 1));
        }

        private sealed class HookValueHolder
        {
            public int Count;
            public StateUpdater<int> Setter = null!;
            public string Label = "";
        }

        [Component]
        public static VNode HookTupleDeconstructedIntoAnObjectComponent()
        {
            var holder = new HookValueHolder();
            (holder.Count, holder.Setter) = Hooks.UseState(0);
            return V.Label(text: holder.Count.ToString());
        }

        [Component]
        public static VNode HookTupleElementStoredToAnObjectFieldComponent()
        {
            var holder = new HookValueHolder();
            var pair = Hooks.UseState(0);
            holder.Count = pair.Item1;
            return V.Label(text: holder.Count.ToString());
        }

        [Component]
        public static VNode HookValueStoredToAnObjectFieldComponent()
        {
            var holder = new HookValueHolder();
            holder.Label = Hooks.UseMemo(() => "x", System.Array.Empty<object>());
            return V.Label(text: holder.Label);
        }

        [Component]
        public static VNode TupleHeldThenDeconstructedComponent()
        {
            var pair = Hooks.UseState(0);
            var count = pair.Item1;
            var setCount = pair.Item2;
            if (count < 0) setCount.Invoke(0);
            return V.Label(text: count.ToString());
        }

        [Component]
        public static VNode TupleElementPassedStraightToAHookComponent()
        {
            var pair = Hooks.UseState(0);
            var deferred = Hooks.UseDeferredValue(pair.Item1);
            return V.Label(text: deferred.ToString());
        }

        private static (int First, int Second) UsePairHook()
        {
            var (first, _) = Hooks.UseState(0);
            return (first, 1);
        }

        [Component]
        public static VNode HandlerCapturingACustomHookPairComponent()
        {
            var (first, second) = UsePairHook();
            return V.Button(text: "go", onClick: () => System.Console.Write(first + second));
        }

        [Component]
        public static VNode HandlerCapturingATupleValueComponent()
        {
            var (pair, setPair) = Hooks.UseState((1, 2));
            return V.Button(text: "go", onClick: () => setPair.Invoke(pair));
        }

        [Component]
        public static VNode HandlerCapturingTwoHookValuesComponent()
        {
            var (a, setA) = Hooks.UseState(0);
            var (b, setB) = Hooks.UseState(0);
            return V.Button(text: "go", onClick: () => { setA.Invoke(b); setB.Invoke(a); });
        }

        [Component]
        public static VNode HandlerCapturingOnlyASetterComponent()
        {
            var (_, setCount) = Hooks.UseState(0);
            return V.Button(text: "go", onClick: () => setCount.Invoke(1));
        }

        [Component]
        public static VNode ValueAndSetterComponent()
        {
            var (count, setCount) = Hooks.UseState(0);
            if (count < 0) setCount.Invoke(0);
            return V.Label(text: count.ToString());
        }

        [Test]
        public void Given_AHookValueAHandlerCaptures_When_Woven_Then_ItIsWoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(HandlerCapturingAHookValueComponent))), Is.True);
        }

        [Test]
        public void Given_AHookValueAHandlerCaptures_When_Woven_Then_TheGateIsKeyedOnTheValueAlone()
        {
            // Act
            var dependencies = GateDependencyCount(LoadMethod(nameof(HandlerCapturingAHookValueComponent)));

            // Assert
            Assert.That(dependencies, Is.EqualTo(1), "The setter is the one object for the life of the component and is no dependency");
        }

        [Test]
        public void Given_AMemoValueAHandlerCaptures_When_Woven_Then_TheGateIsKeyedOnTheValue()
        {
            // Act
            var dependencies = GateDependencyCount(LoadMethod(nameof(HandlerCapturingAMemoValueComponent)));

            // Assert
            Assert.That(dependencies, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base leaves a body whose handler captures a whole tuple unwoven too,
        // as it reads no hook result stored to a field.
        // What it pins is the new reading declining a tuple held whole: dropping `&& !IsValueTupleType(type)` from
        // `TryMatchFieldCapture` weaves it and the gate then compares the tuple structurally.
        [Test]
        public void Given_AWholeTupleAHandlerCaptures_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(HandlerCapturingAWholeTupleComponent))), Is.False,
                "A tuple held whole compares structurally rather than by reference");
        }

        // GREEN_ON_BASE(characterization): the base leaves a hook tuple deconstructed into the fields of an object
        // unwoven too.
        // What it pins is a tuple temporary that anything but the element stores of a local or a display class reads
        // counting as read again: dropping `|| IsTupleReadAgain(cursor, tuple, body)` from `TryMatchTupleTemp` weaves
        // it.
        [Test]
        public void Given_AHookTupleDeconstructedIntoAnObject_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(HookTupleDeconstructedIntoAnObjectComponent))), Is.False,
                "Only a display class field is read back for the gate; the fields of any other object are not");
        }

        // GREEN_ON_BASE(characterization): the base leaves a hook tuple element stored to the field of an object
        // unwoven too.
        // What it pins is a field store counting as a capture only on a display class: dropping `&&
        // IsClosureClass(receiver.VariableType)` from `ClosureElement` weaves it.
        [Test]
        public void Given_AHookTupleElementStoredToAnObjectField_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(HookTupleElementStoredToAnObjectFieldComponent))), Is.False,
                "A hit would skip the store, leaving the field at an earlier render's value");
        }

        // GREEN_ON_BASE(characterization): the base leaves a hook value stored to the field of an object unwoven too.
        // What it pins is a field store counting as a capture only on a display class: dropping `||
        // !IsClosureField(field)` from `TryMatchFieldCapture` weaves it.
        [Test]
        public void Given_AHookValueStoredToAnObjectField_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(HookValueStoredToAnObjectFieldComponent))), Is.False,
                "A hit would skip the store, leaving the field at an earlier render's value");
        }

        [Test]
        public void Given_ATupleHeldThenDeconstructed_When_Woven_Then_TheGateIsKeyedOnTheValueAlone()
        {
            // Act
            var dependencies = GateDependencyCount(LoadMethod(nameof(TupleHeldThenDeconstructedComponent)));

            // Assert
            Assert.That(dependencies, Is.EqualTo(1));
        }

        [Test]
        public void Given_ATupleHeldThenDeconstructed_When_Woven_Then_TheGateLeavesNothingOnTheStack()
        {
            // Act
            var pops = HitPathPops(LoadMethod(nameof(TupleHeldThenDeconstructedComponent)));

            // Assert
            Assert.That(pops, Is.EqualTo(0), "The gate sits after the store of the last element, not between the read and the store");
        }

        [Test]
        public void Given_ATupleElementPassedStraightToAHook_When_Woven_Then_TheGateIsKeyedOnTheElementAndTheMemo()
        {
            // Act
            var dependencies = GateDependencyCount(LoadMethod(nameof(TupleElementPassedStraightToAHookComponent)));

            // Assert
            Assert.That(dependencies, Is.EqualTo(2));
        }

        // GREEN_ON_BASE(characterization): the base leaves a body whose handler captures the pair of a custom hook
        // unwoven too.
        // What it pins is the second element being taken for a setter only from a hook of the library: dropping `&&
        // (setters is 0 || (stableSecond && setters is 1))` from `TupleElementsAreSound` weaves it.
        [Test]
        public void Given_ACustomHookPairAHandlerCaptures_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(HandlerCapturingACustomHookPairComponent))), Is.False,
                "Only a hook of the library promises its second element stable; a custom hook's is a value the gate does not compare");
        }

        // GREEN_ON_BASE(characterization): the base leaves a body whose handler captures a tuple held in state
        // unwoven too.
        // What it pins is a tuple value being refused as the captured value: dropping `&& !IsValueTupleType(kept)`
        // from `TupleElementsAreSound` weaves it.
        [Test]
        public void Given_ATupleValueAHandlerCaptures_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(HandlerCapturingATupleValueComponent))), Is.False,
                "A tuple value compares structurally rather than by reference");
        }

        // GREEN_ON_BASE(characterization): the base leaves a body whose handler captures only a setter unwoven too.
        // What it pins is a pair being captured only with its first element: taking the first element found in
        // `TupleElementsAreSound` and element 0 in `TryMatchTupleTemp` in place of the element numbered 1 weaves it.
        [Test]
        public void Given_OnlyTheSetterAHandlerCaptures_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(HandlerCapturingOnlyASetterComponent))), Is.False,
                "The setter is stable while the value it sets is never captured");
        }

        [Test]
        public void Given_TwoHookValuesAHandlerCaptures_When_Woven_Then_TheGateIsKeyedOnBoth()
        {
            // Act
            var dependencies = GateDependencyCount(LoadMethod(nameof(HandlerCapturingTwoHookValuesComponent)));

            // Assert
            Assert.That(dependencies, Is.EqualTo(2));
        }

        // GREEN_ON_BASE(characterization): the base keys the gate of a body that deconstructs its pair into locals on
        // the value alone too.
        // What it pins is the recording of the hook result for the gate: removing
        // `hookPipedLocals.Add(outcome.Capture);` from `TryScanHookSection` reddens it.
        [Test]
        public void Given_AValueAndItsSetter_When_Woven_Then_TheGateIsKeyedOnTheValueAlone()
        {
            // Act
            var dependencies = GateDependencyCount(LoadMethod(nameof(ValueAndSetterComponent)));

            // Assert
            Assert.That(dependencies, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base weaves the gate of a body that deconstructs its pair into locals
        // clear of the stack too.
        // What it pins is the same recording: removing `hookPipedLocals.Add(outcome.Capture);` from
        // `TryScanHookSection` reddens it.
        [Test]
        public void Given_AValueAndItsSetter_When_Woven_Then_TheGateLeavesNothingOnTheStack()
        {
            // Act
            var pops = HitPathPops(LoadMethod(nameof(ValueAndSetterComponent)));

            // Assert
            Assert.That(pops, Is.EqualTo(0), "The gate sits after the store of the last element, not between the read and the store");
        }

        [Component]
        public static VNode StoreTupleSelectorComponent()
        {
            var (first, second) = Hooks.UseStore(s_firstStore, value => (value, value + 1));
            return V.Label(text: first.ToString() + second);
        }

        [Component]
        public static VNode HandlerCapturingAStoreTupleComponent()
        {
            var (first, second) = Hooks.UseStore(s_firstStore, value => (value, value + 1));
            return V.Button(text: "go", onClick: () => System.Console.Write(first + second));
        }

        [Component]
        public static VNode HandlerReassigningAHookValueComponent()
        {
            var (count, _) = Hooks.UseState(0);
            return V.Button(text: "add", onClick: () => { count++; });
        }

        [Test]
        public void Given_AStoreWhoseSelectorReturnsAPair_When_Woven_Then_IsLeftUnwoven()
        {
            // Act
            var woven = IsWoven(LoadMethod(nameof(StoreTupleSelectorComponent)));

            // Assert
            Assert.That(woven, Is.False, "The second element of a store's selected pair is a value of the selector's, not a setter");
        }

        // GREEN_ON_BASE(characterization): the base leaves a body whose handler captures a hook value unwoven,
        // whatever the hook returns.
        // What it pins is the second element of a pair being left out of the gate for four hooks only: `isDirect` in
        // place of `isDirect && StableSecondElementHookNames.Contains(hookTarget!.Name)` in `TryScanHookSection`
        // weaves it.
        [Test]
        public void Given_AHandlerCapturingAStoreWhosePairIsSelected_When_Woven_Then_IsLeftUnwoven()
        {
            // Act
            var woven = IsWoven(LoadMethod(nameof(HandlerCapturingAStoreTupleComponent)));

            // Assert
            Assert.That(woven, Is.False, "The second element of a store's selected pair is a value of the selector's, not a setter");
        }

        // GREEN_ON_BASE(characterization): the base leaves a body whose handler captures a hook value unwoven,
        // whatever the handler does to it.
        // What it pins is a handler that assigns the captured value being refused: `if (false)` in place of the
        // `LambdaWritesACapturedHookValue` test in `TryAnalyze` weaves it.
        [Test]
        public void Given_AHandlerReassigningAHookValue_When_Woven_Then_IsLeftUnwoven()
        {
            // Act
            var woven = IsWoven(LoadMethod(nameof(HandlerReassigningAHookValueComponent)));

            // Assert
            Assert.That(woven, Is.False, "A handler that assigns the variable makes it other than what the gate compared");
        }

        #endregion

        #region First reactive scopes

        private static string s_scopeText = "";

        private static string DescribeText(string text) => s_scopeText + text;

        private sealed class RefCarrier : System.Collections.Generic.Dictionary<string, string>
        {
            public RefCarrier(MutableRef<string> reference)
            {
                reference.Current = "carried";
            }
        }

        private sealed class ServiceCarrier : System.Collections.Generic.Dictionary<string, string>
        {
            public ServiceCarrier(System.Collections.Generic.List<string> items)
            {
                items.Add("carried");
            }
        }

        private static System.Collections.Generic.List<string> UseTextItems() => new();

        [Component]
        public static VNode TwoTextChildrenComponent()
        {
            var (left, _) = Hooks.UseState("");
            var (right, _) = Hooks.UseState("");
            return V.Div("root", V.Label(text: left), V.Label(text: right));
        }

        [Component]
        public static VNode NestedTextChildrenComponent()
        {
            var (a, _) = Hooks.UseState("");
            var (b, _) = Hooks.UseState("");
            return V.Div("root", V.Div("group", V.Label(text: a), V.Label(text: b)), V.Label(text: b));
        }

        [Component]
        public static VNode ConditionalTextChildComponent()
        {
            var (flag, _) = Hooks.UseState(false);
            var (a, _) = Hooks.UseState("");
            var (b, _) = Hooks.UseState("");
            return V.Div("root", flag ? V.Label(text: a) : V.Label(text: b), V.Label(text: b));
        }

        [Component]
        public static VNode TextChildAfterAHookComponent()
        {
            var (a, _) = Hooks.UseState("");
            return V.Div("root", V.Label(text: a), V.Label(text: Hooks.UseId()));
        }

        [Component]
        public static VNode TextChildOmittingAnOptionalNullableComponent()
        {
            var (a, _) = Hooks.UseState("");
            return V.Div("root", V.Button(text: a), V.Label(text: a));
        }

        [Component]
        public static VNode TextChildOfAParameterComponent(string title)
        {
            var (a, _) = Hooks.UseState("");
            return V.Div("root", V.Label(text: title), V.Label(text: a));
        }

        [Component]
        public static VNode FixedTextChildComponent()
        {
            var (a, _) = Hooks.UseState("");
            return V.Div("root", V.Label(text: "fixed"), V.Label(text: a));
        }

        [Component]
        public static VNode TextChildCallingAFixtureMethodComponent()
        {
            var (a, _) = Hooks.UseState("");
            return V.Div("root", V.Label(text: DescribeText(a)), V.Label(text: a));
        }

        [Component]
        public static VNode FixtureMethodChildReadingNothingComponent()
        {
            var (a, _) = Hooks.UseState("");
            return V.Div("root", V.Label(text: DescribeText("fixed")), V.Label(text: a));
        }

        [Component]
        public static VNode TextChildReadingAStaticComponent()
        {
            var (a, _) = Hooks.UseState("");
            return V.Div("root", V.Label(text: s_scopeText), V.Label(text: a));
        }

        [Component]
        public static VNode TextChildrenBuiltInALoopComponent()
        {
            var (a, _) = Hooks.UseState("");
            VNode? last = null;
            for (var i = 0; i < 2; i++)
            {
                last = V.Div("row", V.Label(text: a), V.Label(text: a));
            }
            return last!;
        }

        [Component]
        public static VNode TextChildrenAfterATryFinallyComponent()
        {
            var (a, _) = Hooks.UseState("");
            try
            {
                s_scopeText = a;
            }
            finally
            {
                s_scopeText += "x";
            }
            return V.Div("root", V.Label(text: a), V.Label(text: a));
        }

        [Component]
        public static VNode CarrierChildWritingALocalComponent()
        {
            var (a, _) = Hooks.UseState(new System.Collections.Generic.List<string>());
            var written = new System.Collections.Generic.List<string>();
            return V.Div("root", V.Label(data: new ServiceCarrier(written = a)), V.Label(data: new ServiceCarrier(written)));
        }

        [Component]
        public static VNode TextChildCarryingARefComponent()
        {
            var counter = Hooks.UseMutableRef("");
            var (a, _) = Hooks.UseState("");
            return V.Div("root", V.Label(data: new RefCarrier(counter)), V.Label(text: a));
        }

        [Component]
        public static VNode TextChildReadingAServiceValueComponent()
        {
            var items = UseTextItems();
            var (a, _) = Hooks.UseState("");
            return V.Div("root", V.Label(data: new ServiceCarrier(items)), V.Label(text: a));
        }

        [Component]
        public static VNode TextChildReadingAReferenceStateComponent()
        {
            var (items, _) = Hooks.UseState(new System.Collections.Generic.List<string>());
            var (a, _) = Hooks.UseState("");
            return V.Div("root", V.Label(data: new ServiceCarrier(items)), V.Label(text: a));
        }

        [Component]
        public static VNode TextChildAssigningAParameterComponent(string title)
        {
            var (a, _) = Hooks.UseState("");
            return V.Div("root", V.Label(text: title = a), V.Label(text: a));
        }

        [Component]
        public static VNode CarrierChildReadingAReassignedHookValueComponent()
        {
            var (a, _) = Hooks.UseState(new System.Collections.Generic.List<string>());
            var (b, _) = Hooks.UseState(new System.Collections.Generic.List<string>());
            a = b;
            return V.Div("root", V.Label(data: new ServiceCarrier(a)), V.Label(data: new ServiceCarrier(b)));
        }

        private static System.Collections.Generic.IReadOnlyDictionary<string, string> UseTextTable()
        {
            var (table, _) = Hooks.UseState<System.Collections.Generic.IReadOnlyDictionary<string, string>>(
                new System.Collections.Generic.Dictionary<string, string>());
            return table;
        }

        [Component]
        public static VNode TextChildPassingAServiceTableComponent()
        {
            var table = Hooks.UseService<System.Collections.Generic.IReadOnlyDictionary<string, string>>();
            var (a, _) = Hooks.UseState("");
            return V.Div("root", V.Label(data: table), V.Label(text: a));
        }

        [Component]
        public static VNode TextChildPassingACustomHookTableComponent()
        {
            var table = UseTextTable();
            var (a, _) = Hooks.UseState("");
            return V.Div("root", V.Label(data: table), V.Label(text: a));
        }

        [Component]
        public static VNode TextChildReadingAGenericParameterComponent<T>(T value)
        {
            var (a, _) = Hooks.UseState("");
            return V.Div("root", V.Label(text: value as string), V.Label(text: a));
        }

        private static System.ReadOnlySpan<char> UseTextSpan()
        {
            var (text, _) = Hooks.UseState("");
            return System.MemoryExtensions.AsSpan(text);
        }

        [Component]
        public static VNode TextChildReadingARefStructHookValueComponent()
        {
            var span = UseTextSpan();
            var (a, _) = Hooks.UseState("");
            return V.Div("root", V.Label(text: new string(span)), V.Label(text: a));
        }

        private static int ScopeGatesOf(string component) => CountHookCalls(LoadMethod(component), ScopeGateName);

        [Test]
        public void Given_TwoChildrenReadingDifferentHookValues_When_Woven_Then_EachGetsItsOwnScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(TwoTextChildrenComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(2));
        }

        [Test]
        public void Given_AChildReadingOneHookValue_When_Woven_Then_ItsScopeIsKeyedOnThatValue()
        {
            // Act
            var dependencies = DependencyCountOfCall(LoadMethod(nameof(TwoTextChildrenComponent)), ScopeGateName, 0);

            // Assert
            Assert.That(dependencies, Is.EqualTo(1));
        }

        [Test]
        public void Given_ChildrenNestedInAChildReadingBothValues_When_Woven_Then_EveryChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(NestedTextChildrenComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(4));
        }

        [Test]
        public void Given_AChildChoosingBetweenTwoCalls_When_Woven_Then_TheChoiceIsOneScopeKeyedOnWhatItReads()
        {
            // Act
            var dependencies = DependencyCountOfCall(LoadMethod(nameof(ConditionalTextChildComponent)), ScopeGateName, 0);

            // Assert
            Assert.That(dependencies, Is.EqualTo(3));
        }

        [Test]
        public void Given_AChildChoosingBetweenTwoCalls_When_Woven_Then_TheChoiceItsFirstArmAndItsSiblingGetAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(ConditionalTextChildComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(3));
        }

        // GREEN_ON_BASE(characterization): the base weaves the component gate of this body.
        // It holds the next case to account: a body left unwoven has no scope gate either.
        [Test]
        public void Given_AChildAheadOfALaterHook_When_Woven_Then_TheComponentIsWoven()
        {
            // Act
            var woven = IsWoven(LoadMethod(nameof(TextChildAfterAHookComponent)));

            // Assert
            Assert.That(woven, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base weaves no scope gate, so it counts none here either.
        // What it pins is a child ahead of a later hook staying out of every scope: `true` in place of
        // `each.Span.Start > boundary` in `PlanScopes` gives it one.
        [Test]
        public void Given_AChildAheadOfALaterHook_When_Woven_Then_NoChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(TextChildAfterAHookComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(0));
        }

        [Test]
        public void Given_AChildOmittingAnOptionalNullableParameter_When_Woven_Then_EachChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(TextChildOmittingAnOptionalNullableComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(2));
        }

        [Test]
        public void Given_AChildReadingAParameter_When_Woven_Then_EachChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(TextChildOfAParameterComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(2));
        }

        [Test]
        public void Given_AChildReadingAParameter_When_Woven_Then_ItsScopeIsKeyedOnThatParameter()
        {
            // Act
            var dependencies = DependencyCountOfCall(LoadMethod(nameof(TextChildOfAParameterComponent)), ScopeGateName, 0);

            // Assert
            Assert.That(dependencies, Is.EqualTo(1));
        }

        [Test]
        public void Given_AHookValueTheBodyReassigns_When_Woven_Then_OnlyTheChildNotReadingItGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(CarrierChildReadingAReassignedHookValueComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildReadingNothing_When_Woven_Then_ItGetsAScopeGateBesideTheOneReadingTheValue()
        {
            // Act
            var gates = ScopeGatesOf(nameof(FixedTextChildComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(2));
        }

        [Test]
        public void Given_ATextChildCallingAMethodOfTheFixture_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(TextChildCallingAFixtureMethodComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildReadingNothingButCallingAMethodOfTheFixture_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(FixtureMethodChildReadingNothingComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildReadingAStaticField_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(TextChildReadingAStaticComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base weaves no child gate, so it counts none here either.
        // What it pins is a loop around the children keeping them out of every scope: replacing the last line of
        // `BranchBreaksRegion` with `return false;` gives them one.
        [Test]
        public void Given_TextChildrenBuiltInALoop_When_Woven_Then_NoChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(TextChildrenBuiltInALoopComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base weaves no child gate, so it counts none here either.
        // What it pins is a protected region keeping every child out of every scope: `DepthsBefore(instructions)` in
        // place of `body.HasExceptionHandlers ? null : DepthsBefore(instructions)` in `PlanScopes` gives them some.
        [Test]
        public void Given_TextChildrenAfterATryFinally_When_Woven_Then_NoChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(TextChildrenAfterATryFinallyComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base weaves no child gate, so it counts none here either.
        // What it pins is a local that is no hook value staying out of the scopes: removing `||
        // !facts.CanBeCompared(local, local.VariableType, region)` from `ScanLocal` gives the children some.
        [Test]
        public void Given_AChildWritingALocalALaterChildReads_When_Woven_Then_NoChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(CarrierChildWritingALocalComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(0));
        }

        [Test]
        public void Given_AChildHandedAMutableRef_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(TextChildCarryingARefComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildHandedAServiceValue_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(TextChildReadingAServiceValueComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildHandedAReferenceState_When_Woven_Then_EachChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(TextChildReadingAReferenceStateComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(2));
        }

        [Test]
        public void Given_ATextChildAssigningAParameter_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(TextChildAssigningAParameterComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildPassingAServiceValue_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(TextChildPassingAServiceTableComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildPassingAReferenceFromACustomHook_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(TextChildPassingACustomHookTableComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildReadingARefStructFromACustomHook_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(TextChildReadingARefStructHookValueComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildCastingAGenericParameter_When_Woven_Then_TheParameterIsBoxedForTheGateTheScopeAndTheCast()
        {
            // Act
            var method = LoadMethod(nameof(TextChildReadingAGenericParameterComponent));
            var boxes = method.Body.Instructions.Count(
                instruction => instruction.OpCode == OpCodes.Box && instruction.Operand is GenericParameter);

            // Assert
            Assert.That(boxes, Is.EqualTo(3));
        }

        // GREEN_ON_BASE(characterization): the base weaves no scope gate, so no jump passes a store there and the
        // count of jumps equals the count of gates, nought.
        // What it pins is the weave of a scope's store: removing `RetargetBranches(body, plan.Store, store[0]);` from
        // `InjectScope` reddens it, and so does removing the `foreach` there that inserts the store.
        [Test]
        public void Given_AChildChoosingBetweenTwoCalls_When_Woven_Then_OnlyEachGatesHitPathJumpsPastTheStoreOfItsScope()
        {
            // Act
            var method = LoadMethod(nameof(ConditionalTextChildComponent));
            var gates = ScopeGatesOf(nameof(ConditionalTextChildComponent));
            var jumpsPastTheStore = method.Body.Instructions.Count(instruction => instruction.Operand is Instruction target
                && target.Previous?.Previous?.Operand is MethodReference call && call.Name == ScopeStoreName);

            // Assert
            Assert.That(jumpsPastTheStore, Is.EqualTo(gates));
        }

        #endregion

        #region Lambdas and derived values

        private static void Print(string text) => System.Console.Write(text);

        [Component]
        public static VNode HandlerReadingAStateComponent()
        {
            var (count, setCount) = Hooks.UseState(0);
            return V.Div("root", V.Button(text: "add", onClick: () => setCount.Invoke(count + 1)), V.Label(text: count.ToString()));
        }

        [Component]
        public static VNode HandlerReadingTwoStatesComponent()
        {
            var (a, setA) = Hooks.UseState(0);
            var (b, setB) = Hooks.UseState(0);
            return V.Div("root", V.Button(text: "swap", onClick: () => { setA.Invoke(b); setB.Invoke(a); }), V.Label(text: a.ToString()));
        }

        [Component]
        public static VNode HandlerWritingACapturedVariableComponent()
        {
            var (a, _) = Hooks.UseState(0);
            var total = 0;
            return V.Div("root", V.Button(text: a.ToString(), onClick: () => total += a), V.Label(text: a.ToString()));
        }

        [Component]
        public static VNode HandlerCapturingAParameterComponent(string title)
        {
            var (a, _) = Hooks.UseState(0);
            return V.Div("root", V.Button(text: "show", onClick: () => Print(title)), V.Label(text: a.ToString()));
        }

        [Component]
        public static VNode HandlerInsideAHandlerComponent()
        {
            var (a, setA) = Hooks.UseState(0);
            var (b, _) = Hooks.UseState(0);
            return V.Div("root", V.Button(text: "go", onClick: () => { System.Action inner = () => setA.Invoke(a + b); inner(); }), V.Label(text: b.ToString()));
        }

        [Component]
        public static VNode DerivedNumberComponent()
        {
            var (n, _) = Hooks.UseState(0);
            var (other, _) = Hooks.UseState(0);
            var doubled = n * 2;
            return V.Div("root", V.Label(text: doubled.ToString()), V.Label(text: other.ToString()));
        }

        [Component]
        public static VNode DerivedSequenceComponent()
        {
            var (n, _) = Hooks.UseState(0);
            var (items, _) = Hooks.UseState(new System.Collections.Generic.List<int>());
            var visible = System.Linq.Enumerable.Where(items, item => item > n);
            return V.Div("root", V.Label(text: System.Linq.Enumerable.Count(visible).ToString()), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode DerivedListComponent()
        {
            var (n, _) = Hooks.UseState(0);
            var (other, _) = Hooks.UseState(0);
            var list = new System.Collections.Generic.List<int> { n };
            return V.Div("root", V.Label(text: list.Count.ToString()), V.Label(text: other.ToString()));
        }

        [Component]
        public static VNode DerivedTwiceComponent()
        {
            var (n, _) = Hooks.UseState(0);
            var (other, _) = Hooks.UseState(0);
            var doubled = n * 2;
            doubled = other * 3;
            return V.Div("root", V.Label(text: doubled.ToString()), V.Label(text: other.ToString()));
        }

        [Component]
        public static VNode DerivedDelegateComponent()
        {
            var (n, _) = Hooks.UseState(0);
            var (other, _) = Hooks.UseState(0);
            System.Func<int> read = () => n;
            return V.Div("root", V.Label(text: read().ToString()), V.Label(text: other.ToString()));
        }

        private static int s_parsedSink;

        private sealed class HolderOfCount
        {
            public int Count;
        }

        private sealed class SetterCarrier : System.Collections.Generic.Dictionary<string, string>
        {
            public SetterCarrier(StateUpdater<int> setter)
            {
                Add("setter", setter.ToString());
            }
        }

        [Component]
        public static VNode FourParameterLambdaComponent()
        {
            var (n, _) = Hooks.UseState(0);
            var (other, _) = Hooks.UseState(0);
            System.Func<int, int, int, int, int> sum = (a, b, c, d) => a + b + c + d + n;
            return V.Div("root", V.Label(text: sum(1, 2, 3, 4).ToString()), V.Label(text: other.ToString()));
        }

        [Component]
        public static VNode SelfCreatingLocalFunctionComponent()
        {
            var (n, _) = Hooks.UseState(0);
            var (other, _) = Hooks.UseState(0);
            void Tick()
            {
                System.Action again = Tick;
                Print(n.ToString());
                again();
            }
            return V.Div("root", V.Button(text: "go", onClick: Tick), V.Label(text: other.ToString()));
        }

        [Component]
        public static VNode HandlerCreatingAWritingHandlerComponent()
        {
            var (a, _) = Hooks.UseState(0);
            var total = 0;
            void Add() => total += a;
            System.Action outer = () =>
            {
                System.Action inner = Add;
                inner();
                Print(a.ToString());
            };
            return V.Div("root", V.Button(text: "go", onClick: outer), V.Label(text: a.ToString()));
        }

        [Component]
        public static VNode HandlerCreatingAMethodGroupComponent()
        {
            var (a, _) = Hooks.UseState(0);
            var (other, _) = Hooks.UseState(0);
            System.Action outer = () =>
            {
                System.Action printer = ScopeHandler;
                printer();
                Print(a.ToString());
            };
            return V.Div("root", V.Button(text: "go", onClick: outer), V.Label(text: other.ToString()));
        }

        [Component]
        public static VNode HandlerWithANestedCaptureComponent()
        {
            var (a, _) = Hooks.UseState(0);
            var (other, _) = Hooks.UseState(0);
            System.Action outer = () =>
            {
                int local;
                int.TryParse("1", out local);
                System.Action inner = () => Print((local + a).ToString());
                inner();
            };
            return V.Div("root", V.Button(text: "go", onClick: outer), V.Label(text: other.ToString()));
        }

        [Component]
        public static VNode HandlerReadingAnObjectFieldComponent()
        {
            var (a, _) = Hooks.UseState(0);
            var (other, _) = Hooks.UseState(0);
            var holder = new HolderOfCount { Count = 3 };
            System.Action show = () => Print(holder.Count.ToString() + a);
            return V.Div("root", V.Button(text: "go", onClick: show), V.Label(text: other.ToString()));
        }

        [Component]
        public static VNode HandlerReadingAVariableSetLaterComponent()
        {
            var (a, _) = Hooks.UseState(0);
            var (other, _) = Hooks.UseState(0);
            var late = 0;
            System.Action show = () => Print(late.ToString());
            late = a;
            return V.Div("root", V.Button(text: "go", onClick: show), V.Label(text: other.ToString()));
        }

        [Component]
        public static VNode HandlerReadingAVariableSetByAnOutArgumentComponent()
        {
            var (a, _) = Hooks.UseState(0);
            var (other, _) = Hooks.UseState(0);
            var parsed = 0;
            System.Action show = () => Print(parsed.ToString() + a);
            s_parsedSink = int.TryParse("1", out parsed) ? parsed : 0;
            return V.Div("root", V.Button(text: "go", onClick: show), V.Label(text: other.ToString()));
        }

        [Component]
        public static VNode ChildHoldingASetterComponent()
        {
            var (a, setA) = Hooks.UseState(0);
            var (other, _) = Hooks.UseState(0);
            System.Action show = () => Print(a.ToString());
            return V.Div("root", V.Label(data: new SetterCarrier(setA)), V.Button(text: "go", onClick: show), V.Label(text: other.ToString()));
        }

        [Component]
        public static VNode DerivedSequenceCapturedByAHandlerComponent()
        {
            var (n, _) = Hooks.UseState(0);
            var (other, _) = Hooks.UseState(0);
            var visible = System.Linq.Enumerable.Where(new[] { 1, 2, 3 }, item => item > n);
            System.Action show = () => Print(System.Linq.Enumerable.Count(visible).ToString());
            return V.Div("root", V.Button(text: "go", onClick: show), V.Label(text: other.ToString()));
        }

        [Component]
        public static VNode DerivedArrayComponent()
        {
            var (items, _) = Hooks.UseState(new System.Collections.Generic.List<int>());
            var (other, _) = Hooks.UseState(0);
            var copy = System.Linq.Enumerable.ToArray(items);
            return V.Div("root", V.Label(text: copy.Length.ToString()), V.Label(text: other.ToString()));
        }

        [Component]
        public static VNode HandlerAndASiblingReadingTheSameValueComponent()
        {
            var (count, setCount) = Hooks.UseState(0);
            return V.Div("root", V.Button(text: "add", onClick: () => setCount.Invoke(count + 1)), V.Label(text: (count + 1).ToString()));
        }

        public sealed class ScopeRunner
        {
            public void Run()
            {
            }
        }

        [Component]
        public static VNode DerivedBoundDelegateComponent(ScopeRunner runner)
        {
            var (n, _) = Hooks.UseState(0);
            System.Action run = runner.Run;
            return V.Div("root", V.Button(text: n.ToString(), onClick: run), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode DerivedCopiedListComponent()
        {
            var (items, _) = Hooks.UseState(new System.Collections.Generic.List<int>());
            var (other, _) = Hooks.UseState(0);
            var copy = new System.Collections.Generic.List<int>(items);
            return V.Div("root", V.Label(text: copy.Count.ToString()), V.Label(text: other.ToString()));
        }

        [Component]
        public static VNode DerivedNewSpanComponent()
        {
            var (chars, _) = Hooks.UseState(new char[0]);
            var (other, _) = Hooks.UseState(0);
            var span = new System.ReadOnlySpan<char>(chars);
            return V.Div("root", V.Label(text: span.Length.ToString()), V.Label(text: other.ToString()));
        }

        [Component]
        public static VNode ChildReadingThroughAnOuterDisplayClassComponent()
        {
            var (n, _) = Hooks.UseState(0);
            var (other, _) = Hooks.UseState(0);
            System.Action outer = () => Print(n.ToString());
            {
                var inner = other;
                System.Action nested = () => Print(inner.ToString() + n);
                return V.Div("root", V.Label(text: n.ToString()), V.Label(text: inner.ToString()));
            }
        }

        [Component]
        public static VNode ChildReadingACapturedReferenceComponent()
        {
            var (n, _) = Hooks.UseState(0);
            var tags = new System.Collections.Generic.List<int>();
            System.Action add = () => tags.Add(n);
            return V.Div("root", V.Button(text: "add", onClick: add), V.Label(text: tags.Count.ToString()), V.Label(text: n.ToString()));
        }

        private static string DescribePair((int, int) pair) => pair.Item1.ToString();

        [Component]
        public static VNode DerivedPairComponent()
        {
            var (n, _) = Hooks.UseState(0);
            var (other, _) = Hooks.UseState(0);
            var pair = (n, other);
            System.Action show = () => Print(DescribePair(pair));
            return V.Div("root", V.Button(text: "go", onClick: show), V.Label(text: other.ToString()));
        }

        private static string DescribeNodes((ElementNode, ElementNode) pair) => pair.Item1.ToString();

        [Component]
        public static VNode DerivedNodePairComponent()
        {
            var (n, _) = Hooks.UseState(0);
            var (other, _) = Hooks.UseState(0);
            var pair = (V.Label(text: n.ToString()), V.Label(text: "second"));
            System.Action show = () => Print(DescribeNodes(pair));
            return V.Div("root", V.Button(text: "go", onClick: show), V.Label(text: other.ToString()));
        }

        [Component]
        public static VNode DerivedLookupOfNodesComponent()
        {
            var (n, _) = Hooks.UseState(0);
            var (items, _) = Hooks.UseState(new System.Collections.Generic.List<int>());
            var grouped = System.Linq.Enumerable.ToLookup(items, item => item % 2, item => V.Label(text: item.ToString()));
            return V.Div("root", V.Label(text: grouped.Count.ToString()), V.Label(text: n.ToString()));
        }

        public readonly struct NodeHolder
        {
            public NodeHolder(ElementNode node) => Node = node;

            public ElementNode Node { get; }
        }

        [Component]
        public static VNode DerivedStructHoldingANodeComponent()
        {
            var (n, _) = Hooks.UseState(0);
            var (other, _) = Hooks.UseState(0);
            var holder = new NodeHolder(V.Label(text: n.ToString()));
            System.Action show = () => Print(holder.Node.ToString());
            return V.Div("root", V.Button(text: "go", onClick: show), V.Label(text: other.ToString()));
        }

        [Component]
        public static VNode HandlerWritingACapturedVariableThroughOutComponent()
        {
            var (text, _) = Hooks.UseState("1");
            var (other, _) = Hooks.UseState(0);
            var parsed = 0;
            System.Action parse = () => int.TryParse(text, out parsed);
            return V.Div("root", V.Button(text: "parse", onClick: parse), V.Label(text: parsed.ToString()), V.Label(text: other.ToString()));
        }

        [Component]
        public static VNode HandlerWritingACapturedVariableReadByAChildComponent()
        {
            var (a, _) = Hooks.UseState(0);
            var total = 0;
            return V.Div("root", V.Button(text: "add", onClick: () => total += a), V.Label(text: total.ToString()), V.Label(text: a.ToString()));
        }

        [Component]
        public static VNode HandlerWritingACapturedParameterThroughOutComponent(int start)
        {
            var (other, _) = Hooks.UseState(0);
            System.Action parse = () => int.TryParse("5", out start);
            return V.Div("root", V.Button(text: "parse", onClick: parse), V.Label(text: start.ToString()), V.Label(text: other.ToString()));
        }

        [Component]
        public static VNode DerivedSequenceOfListsComponent()
        {
            var (n, _) = Hooks.UseState(0);
            var (items, _) = Hooks.UseState(new System.Collections.Generic.List<int>());
            var rows = System.Linq.Enumerable.Select(items, item => (System.Collections.Generic.IList<int>)new System.Collections.Generic.List<int> { item });
            return V.Div("root", V.Label(text: System.Linq.Enumerable.Count(rows).ToString()), V.Label(text: n.ToString()));
        }

        private const string ValueGateName = "TryGetMemoizedValue";

        private static int ValueGatesOf(string component) => CountHookCalls(LoadMethod(component), ValueGateName);

        [Test]
        public void Given_AHandlerReadingState_When_Woven_Then_TheButtonAndItsSiblingGetAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(HandlerReadingAStateComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(2));
        }

        // GREEN_ON_BASE(characterization): the base weaves no value gate, so it leaves the lambda to the button here
        // too.
        // What it pins is a scope repeating the inputs of the one around it being left out: `return false;` in place
        // of the comparison at the end of `RepeatsEnclosingScope` gives the lambda a value gate of its own.
        [Test]
        public void Given_AHandlerReadingState_When_Woven_Then_TheLambdaIsLeftToTheButtonReadingTheSame()
        {
            // Act
            var gates = ValueGatesOf(nameof(HandlerReadingAStateComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(0));
        }

        [Test]
        public void Given_ALambdaHandedToTheRoot_When_Woven_Then_ItGetsAValueGate()
        {
            // Act
            var gates = ValueGatesOf(nameof(HandlerCapturingAHookValueComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base gives only the other child a scope gate too.
        // What it pins is a variable a lambda writes through an out argument being no value to compare: `false` in
        // place of `WritesThroughAddress(instr, field)` in `FieldsWrittenByLambdas` gives the child a scope gate.
        [Test]
        public void Given_AHandlerWritingACapturedVariableThroughAnOutArgument_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(HandlerWritingACapturedVariableThroughOutComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AHandlerWritingACapturedVariableReadByAChild_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(HandlerWritingACapturedVariableReadByAChildComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base gives only the other child a scope gate too.
        // What it pins is a parameter a lambda writes through an out argument being no parameter to compare: `false` in
        // place of `WritesThroughAddress(instr, field)` in `FieldsWrittenByLambdas` gives the child a scope gate.
        [Test]
        public void Given_AHandlerWritingACapturedParameterThroughAnOutArgument_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(HandlerWritingACapturedParameterThroughOutComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AHandlerWritingACapturedVariable_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(HandlerWritingACapturedVariableComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AHandlerCapturingAParameter_When_Woven_Then_TheButtonAndItsSiblingGetAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(HandlerCapturingAParameterComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(2));
        }

        [Test]
        public void Given_AHandlerBuiltInsideAHandler_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(HandlerInsideAHandlerComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_ASequenceDerivedOnceFromState_When_Woven_Then_TheLambdaAndTheSequenceGetAValueGate()
        {
            // Act
            var gates = ValueGatesOf(nameof(DerivedSequenceComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(2));
        }

        // GREEN_ON_BASE(characterization): the base gives the child reading the list no scope gate either.
        // What it pins is a local that is no hook value staying out of the scopes: `true` in place of the hook and
        // stable key test that ends `CanBeCompared` gives that child a scope gate.
        [Test]
        public void Given_AListDerivedOnceFromState_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(DerivedListComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_ADelegateDerivedOnceFromState_When_Woven_Then_ItGetsAValueGate()
        {
            // Act
            var gates = ValueGatesOf(nameof(DerivedDelegateComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base weaves no value gate, so it counts none here either.
        // What it pins is an expression calling nothing being left out of the scopes: removing `!scan.HasCall ||`
        // from `TryPlanCandidate` gives the number a value gate.
        [Test]
        public void Given_ANumberDerivedFromState_When_Woven_Then_NoValueGateIsWoven()
        {
            // Act
            var gates = ValueGatesOf(nameof(DerivedNumberComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(0), "An arithmetic expression calls nothing, so there is nothing to keep");
        }

        // GREEN_ON_BASE(characterization): the base gives each child reading the number a scope gate too.
        // What it pins is a local of a value type being compared as it stands: removing `if (IsValueLike(type))
        // return true;` from `CanBeCompared` leaves the children reading it without one.
        [Test]
        public void Given_ANumberDerivedFromState_When_Woven_Then_EveryChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(DerivedNumberComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(3));
        }

        // GREEN_ON_BASE(characterization): the base gives each child reading the number a scope gate too.
        // What it pins is the same comparison of a value type: removing `if (IsValueLike(type)) return true;` from
        // `CanBeCompared` leaves the children reading it without one.
        [Test]
        public void Given_ANumberAssignedTwice_When_Woven_Then_EveryChildStillGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(DerivedTwiceComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(3));
        }

        [Test]
        public void Given_ALambdaOfFourParameters_When_Woven_Then_ItGetsAValueGate()
        {
            // Act
            var gates = ValueGatesOf(nameof(FourParameterLambdaComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base gives the button no scope gate here either.
        // What it pins is a lambda that creates itself reading as unsupported: `return done ?? new
        // HashSet<FieldDefinition>();` in place of `return done;` at the top of `CapturedReadsOf` gives the button
        // one.
        [Test]
        public void Given_ALocalFunctionCreatingItself_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(SelfCreatingLocalFunctionComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1), "What a lambda that creates itself reads is never followed to its end");
        }

        [Test]
        public void Given_AHandlerCreatingAHandlerThatWrites_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(HandlerCreatingAWritingHandlerComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1), "The outer handler runs what the inner one writes");
        }

        [Test]
        public void Given_AHandlerCreatingAMethodGroup_When_Woven_Then_ItGetsAValueGate()
        {
            // Act
            var gates = ValueGatesOf(nameof(HandlerCreatingAMethodGroupComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1), "A method group is a delegate over code this does not read, not a display class lambda");
        }

        [Test]
        public void Given_AHandlerWhoseInnerHandlerCapturesItsLocal_When_Woven_Then_ItGetsAValueGate()
        {
            // Act
            var gates = ValueGatesOf(nameof(HandlerWithANestedCaptureComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1), "The link to the display class an inner handler captures through is no write");
        }

        [Test]
        public void Given_AHandlerReadingAFieldOfACapturedObject_When_Woven_Then_ItGetsAValueGate()
        {
            // Act
            var gates = ValueGatesOf(nameof(HandlerReadingAnObjectFieldComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base gives the button no scope gate here either.
        // What it pins is a captured variable stored after the lambda being no dependency: removing `&&
        // StoresPrecede(field, region.Start)` from `FieldDependency` gives the button one.
        [Test]
        public void Given_AHandlerReadingAVariableSetAfterIt_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(HandlerReadingAVariableSetLaterComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1), "The variable holds something else when the handler runs than when the scope began");
        }

        // GREEN_ON_BASE(characterization): the base gives the button no scope gate here either.
        // What it pins is the same rule for a store made through an out argument: removing `&& StoresPrecede(field,
        // region.Start)` from `FieldDependency` gives the button one.
        [Test]
        public void Given_AHandlerReadingAVariableSetByAnOutArgumentAfterIt_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(HandlerReadingAVariableSetByAnOutArgumentComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildHoldingASetter_When_Woven_Then_EveryChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(ChildHoldingASetterComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(4), "A setter is the one object for the life of the component, so it is never a change");
        }

        [Test]
        public void Given_ASequenceDerivedFromStateAndCapturedByAHandler_When_Woven_Then_TheLambdaAndTheSequenceGetAValueGate()
        {
            // Act
            var gates = ValueGatesOf(nameof(DerivedSequenceCapturedByAHandlerComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(2));
        }

        // GREEN_ON_BASE(characterization): the base weaves no value gate, so it counts none here either.
        // What it pins is an array being no value to keep: `true` in place of `false` in the `IsArrayOrIndirection`
        // test of `IsImmutableType` gives it a value gate.
        [Test]
        public void Given_AnArrayDerivedOnceFromState_When_Woven_Then_NoValueGateIsWoven()
        {
            // Act
            var gates = ValueGatesOf(nameof(DerivedArrayComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(0), "The body may write to an array afterwards, so it is no value to keep");
        }

        [Test]
        public void Given_AHandlerAndASiblingReadingTheSameVariable_When_Woven_Then_BothGetAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(HandlerAndASiblingReadingTheSameValueComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(2), "A read of a captured variable after the handler's scope is no write that handler has to stand before");
        }

        [Test]
        public void Given_ADelegateBoundToAParameterAndDerivedOnce_When_Woven_Then_ItGetsAValueGate()
        {
            // Act
            var gates = ValueGatesOf(nameof(DerivedBoundDelegateComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base weaves no value gate, so it counts none here either.
        // What it pins is a list being no value to keep: `true` in place of the final `false` of `IsImmutableType`
        // gives the copy a value gate.
        [Test]
        public void Given_AListCopiedOnceFromState_When_Woven_Then_NoValueGateIsWoven()
        {
            // Act
            var gates = ValueGatesOf(nameof(DerivedCopiedListComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the compiler builds the span the same way whatever the weaver does.
        // What it pins is the compiler output the declaration in `IsImmutableType` rests on: no `newobj` builds the
        // span.
        [Test]
        public void Given_ASpanBuiltOnceFromState_When_Woven_Then_NoInstructionBuildsItOnTheStack()
        {
            // Act
            var builds = LoadMethod(nameof(DerivedNewSpanComponent)).Body.Instructions.Count(
                instruction => instruction.OpCode == OpCodes.Newobj && instruction.Operand is MethodReference { DeclaringType.Name: "ReadOnlySpan`1" });

            // Assert
            Assert.That(builds, Is.EqualTo(0));
        }

        [Test]
        public void Given_ADelegateDerivedOnceFromState_When_Woven_Then_TheCachedObjectIsCastBackToTheDelegate()
        {
            // Act
            var casts = LoadMethod(nameof(DerivedDelegateComponent)).Body.Instructions.Count(
                instruction => instruction.OpCode == OpCodes.Castclass && instruction.Operand is TypeReference { Name: "Func`1" });

            // Assert
            Assert.That(casts, Is.EqualTo(1));
        }

        [Test]
        public void Given_ChildrenReadingThroughAnOuterDisplayClass_When_Woven_Then_EachGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(ChildReadingThroughAnOuterDisplayClassComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(3));
        }

        [Test]
        public void Given_AChildReadingACapturedReference_When_Woven_Then_OnlyTheOtherChildrenGetAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(ChildReadingACapturedReferenceComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(2));
        }

        // GREEN_ON_BASE(characterization): the base weaves no value gate, so it counts none here either.
        // What it pins is a tuple of nodes being no value to keep: replacing the `HoldsNodes(type)` test in
        // `IsImmutableType` with `false` boxes the pair for a value gate.
        [Test]
        public void Given_APairOfNodesCapturedByAHandler_When_Woven_Then_TheBuiltValueIsNotBoxedForTheCache()
        {
            // Act
            var boxes = LoadMethod(nameof(DerivedNodePairComponent)).Body.Instructions.Count(
                instruction => instruction.OpCode == OpCodes.Box && instruction.Previous.OpCode == OpCodes.Dup);

            // Assert
            Assert.That(boxes, Is.EqualTo(0), "A pooled node the slot holds is recycled by the sweep, which marks no tuple");
        }

        // GREEN_ON_BASE(characterization): the base weaves no value gate, so it counts none here either.
        // What it pins is a sequence of an interface the weaver does not know being no value to keep: `false` in place of
        // the `!ReadOnlyInterfaces.Contains(definition.FullName)` test in `HoldsNodes` gives the sequence a value gate.
        [Test]
        public void Given_ASequenceOfListsDerivedOnce_When_Woven_Then_NoValueGateIsWoven()
        {
            // Act
            var gates = ValueGatesOf(nameof(DerivedSequenceOfListsComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(0), "An interface the weaver does not know may be implemented by a type holding nodes");
        }

        // GREEN_ON_BASE(characterization): the base weaves no value gate, so it counts none here either.
        // What it pins is a lookup of nodes being no value to keep: replacing the `HoldsNodes(type)` test in
        // `IsImmutableType` with `false` gives the lookup a value gate.
        [Test]
        public void Given_ALookupOfNodesDerivedOnce_When_Woven_Then_NoValueGateIsWoven()
        {
            // Act
            var gates = ValueGatesOf(nameof(DerivedLookupOfNodesComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(0), "The nodes a lookup groups are no node the slot marks");
        }

        // GREEN_ON_BASE(characterization): the base weaves no value gate, so it counts none here either.
        // What it pins is a struct with a node field being no value to keep: `field.IsStatic` in place of
        // `!field.IsStatic` in `HoldsNodes` boxes the struct for a value gate.
        [Test]
        public void Given_AStructHoldingANodeCapturedByAHandler_When_Woven_Then_TheBuiltValueIsNotBoxedForTheCache()
        {
            // Act
            var boxes = LoadMethod(nameof(DerivedStructHoldingANodeComponent)).Body.Instructions.Count(
                instruction => instruction.OpCode == OpCodes.Box && instruction.Previous.OpCode == OpCodes.Dup);

            // Assert
            Assert.That(boxes, Is.EqualTo(0), "A node in a struct field is no node the slot marks");
        }

        [Test]
        public void Given_APairCapturedByAHandlerAndDerivedOnce_When_Woven_Then_TheBuiltValueIsBoxedForTheCache()
        {
            // Act
            var boxes = LoadMethod(nameof(DerivedPairComponent)).Body.Instructions.Count(
                instruction => instruction.OpCode == OpCodes.Box && instruction.Previous.OpCode == OpCodes.Dup);

            // Assert
            Assert.That(boxes, Is.EqualTo(1));
        }

        [Test]
        public void Given_APairCapturedByAHandlerAndDerivedOnce_When_Woven_Then_TheCachedObjectIsUnboxedOnAHit()
        {
            // Act
            var unboxes = LoadMethod(nameof(DerivedPairComponent)).Body.Instructions.Count(
                instruction => instruction.OpCode == OpCodes.Unbox_Any);

            // Assert
            Assert.That(unboxes, Is.EqualTo(1));
        }

        #endregion

        #region More reactive scopes

        private sealed class TextCarrier : System.Collections.Generic.Dictionary<string, string>
        {
            public TextCarrier(string text)
            {
                Add("text", text);
            }
        }

        [Component]
        public static VNode ChildComparingAStructFieldComponent()
        {
            var (point, _) = Hooks.UseState(new ScopePoint { X = 3 });
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: point.X > 1 ? "big" : "small"), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildMatchingAPatternComponent()
        {
            var (value, _) = Hooks.UseState<object>("x");
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: value is System.Collections.Generic.List<int> list ? list.Count.ToString() : ""), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildCarryingATextComponent()
        {
            var (left, _) = Hooks.UseState("");
            var (right, _) = Hooks.UseState("");
            return V.Div("root", V.Label(text: right, data: new TextCarrier(left)));
        }

        [Test]
        public void Given_AChildComparingAStructField_When_Woven_Then_EachChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(ChildComparingAStructFieldComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(2));
        }

        [Test]
        public void Given_AChildMatchingAPatternIntoALocal_When_Woven_Then_EachChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(ChildMatchingAPatternComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(2));
        }

        // GREEN_ON_BASE(characterization): the base gives the label one scope gate here too.
        // What it pins is a constructor being a scope of its own only where it builds a node: `true` in place of the
        // final `false` of `IsNodeType` gives the carrier's constructor one.
        [Test]
        public void Given_AChildHoldingAConstructorOfNoNode_When_Woven_Then_OnlyTheChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(ChildCarryingATextComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1), "What a constructor builds is cached only where it is a node");
        }

        #endregion

        #region Reactive scopes (a gate per child)

        private static int s_scopeMutable;

        private static string DescribeScopeInput(int value) => s_scopeMutable + ":" + value;

        [Component]
        public static VNode TwoIndependentChildrenComponent()
        {
            var (left, _) = Hooks.UseState(0);
            var (right, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: left.ToString()), V.Label(text: right.ToString()));
        }

        [Component]
        public static VNode NestedIndependentChildrenComponent()
        {
            var (a, _) = Hooks.UseState(0);
            var (b, _) = Hooks.UseState(0);
            return V.Div("root",
                V.Div("group", V.Label(text: a.ToString()), V.Label(text: b.ToString())),
                V.Label(text: b.ToString()));
        }

        [Component]
        public static VNode ChildReadingAMutableRefComponent()
        {
            var (n, _) = Hooks.UseState(0);
            var counter = Hooks.UseMutableRef(0);
            return V.Div("root", V.Label(text: counter.Current.ToString()), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildReadingAMutableStaticComponent()
        {
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: s_scopeMutable + ":" + n), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildCallingAFixtureMethodComponent()
        {
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: DescribeScopeInput(n)), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildReadingTheClockComponent()
        {
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: System.DateTime.Now.Ticks + ":" + n), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildReadingARandomSourceComponent()
        {
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: new System.Random(n).Next().ToString()), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildReadingTheProcessComponent()
        {
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: (System.Environment.TickCount + n).ToString()), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildReadingAGeneratedGuidComponent()
        {
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: System.Guid.NewGuid().ToString() + n), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildReadingTheTimeZoneComponent()
        {
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: System.TimeZoneInfo.Local.Id + n), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildReadingTheOffsetClockComponent()
        {
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: System.DateTimeOffset.UtcNow.Ticks + ":" + n), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildrenBuiltInALoopComponent()
        {
            var (n, _) = Hooks.UseState(0);
            VNode? last = null;
            for (var i = 0; i < 2; i++)
            {
                last = V.Div("row", V.Label(text: i.ToString() + n), V.Label(text: n.ToString()));
            }
            return last!;
        }

        [Component]
        public static VNode ChildrenAfterATryFinallyComponent()
        {
            var (n, _) = Hooks.UseState(0);
            var text = "x";
            try
            {
                text = "t" + n;
            }
            finally
            {
                s_scopeMutable++;
            }
            return V.Div("root", V.Label(text: text), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildWritingALocalALaterChildReadsComponent()
        {
            var (n, _) = Hooks.UseState(0);
            var written = 0;
            return V.Div("root", V.Label(text: (written = n + 1).ToString()), V.Label(text: written.ToString()));
        }

        [Component]
        public static VNode ChildReadingNothingComponent()
        {
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: "fixed"), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode PropsOnlyChildrenComponent(GreetProps p)
        {
            return V.Div("root", V.Label(text: p.Name), V.Label(text: "fixed"));
        }

        private static void ScopeHandler()
        {
        }

        [Component]
        public static VNode ChildWithAMethodGroupHandlerComponent()
        {
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Button(text: n.ToString(), onClick: ScopeHandler), V.Label(text: n.ToString()));
        }

        public interface IScopeService
        {
            string Title { get; }
        }

        private static System.Collections.Generic.List<int> UseScopeItems()
        {
            var (items, _) = Hooks.UseState(new System.Collections.Generic.List<int>());
            return items;
        }

        [Component]
        public static VNode ChildReadingAStopwatchComponent()
        {
            var watch = Hooks.UseMemo(() => new System.Diagnostics.Stopwatch(), "stable");
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: watch.ElapsedTicks.ToString()), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildReadingATaskGetterComponent()
        {
            var task = Hooks.UseMemo(() => System.Threading.Tasks.Task.CompletedTask, "stable");
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: task.IsCompleted.ToString()), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildReadingAWeakReferenceComponent()
        {
            var weak = Hooks.UseMemo(() => new System.WeakReference(new object()), "stable");
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: weak.IsAlive.ToString()), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildReadingALazyValueComponent()
        {
            var lazy = Hooks.UseMemo(() => new System.Lazy<int>(() => 1), "stable");
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: lazy.Value.ToString()), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildReadingAnAutoPropertyOfAnotherAssemblyComponent()
        {
            var (node, _) = Hooks.UseState<ElementNode>(V.Label(text: "x"));
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: node.Name), V.Label(text: n.ToString()));
        }

        private sealed class CaptionedCard : System.Collections.Generic.Dictionary<string, string>
        {
            public CaptionedCard(VNode label, string caption)
            {
                Add("caption", caption);
            }
        }

        [Component]
        public static VNode ChildStoringALocalItsNextArgumentReadsComponent()
        {
            var (title, _) = Hooks.UseState("");
            var (n, _) = Hooks.UseState(0);
            string caption;
            return V.Div("root",
                V.Label(data: new CaptionedCard(V.Label(text: caption = title.ToUpper()), caption + n)),
                V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode SuspenseReadingOneStateComponent()
        {
            var (a, _) = Hooks.UseState("");
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Suspense(V.Label(text: a), new VNode?[] { V.Label(text: a) }), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildAddingToASetComponent()
        {
            var (seen, _) = Hooks.UseState(new System.Collections.Generic.HashSet<int>());
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: seen.Add(n).ToString()), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildReadingAServiceValueComponent()
        {
            var service = Hooks.UseService<IScopeService>();
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: service.Title), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildReadingACustomHookValueComponent()
        {
            var items = UseScopeItems();
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: items.Count.ToString()), V.Label(text: n.ToString()));
        }

        public struct ScopePoint
        {
            public int X;

            public int Magnitude => X;
        }

        public enum ScopeMode
        {
            Off,
            On,
        }

        private static readonly string s_scopeReadOnly = "fixed";

        private sealed class RefReader
        {
            private readonly MutableRef<int> _reference;

            public RefReader(MutableRef<int> reference)
            {
                _reference = reference;
            }

            public string Text => _reference.Current.ToString();
        }

        [Component]
        public static VNode ChildReadingAReferenceStateComponent()
        {
            var (n, _) = Hooks.UseState(0);
            var (greeting, _) = Hooks.UseState(new GreetProps("hello"));
            return V.Div("root", V.Label(text: greeting.Name), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildReadingAReplacedReferenceStateComponent()
        {
            var (n, _) = Hooks.UseState(0);
            var (greeting, _) = Hooks.UseState(new GreetProps("hello"));
            greeting = new GreetProps(greeting.Name + "!");
            return V.Div("root", V.Label(text: greeting.Name), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildWritingALocalThroughAnOutArgumentComponent(
            System.Collections.Generic.Dictionary<string, int> table)
        {
            var (n, _) = Hooks.UseState(0);
            var found = 0;
            return V.Div("root",
                V.Label(text: table.TryGetValue("key", out found) ? "found" : "none"),
                V.Label(text: found.ToString()),
                V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildReadingARefStructComponent()
        {
            var (n, _) = Hooks.UseState(0);
            System.ReadOnlySpan<char> span = System.MemoryExtensions.AsSpan("span");
            return V.Div("root", V.Label(text: new string(span)), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildReadingAStructFieldComponent()
        {
            var (point, _) = Hooks.UseState(new ScopePoint { X = 3 });
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: point.X.ToString()), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildReadingAStructPropertyComponent()
        {
            var (point, _) = Hooks.UseState(new ScopePoint { X = 3 });
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: point.Magnitude.ToString()), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildReadingAnEnumComponent()
        {
            var (mode, _) = Hooks.UseState(ScopeMode.On);
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: mode.ToString()), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildReadingAReadOnlyStaticComponent()
        {
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: s_scopeReadOnly + n.ToString()), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildBuiltFromAMutableRefComponent()
        {
            var (n, _) = Hooks.UseState(0);
            var counter = Hooks.UseMutableRef(0);
            return V.Div("root", V.Label(text: new RefReader(counter).Text), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildAheadOfALaterHookComponent()
        {
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: n.ToString()), V.Label(text: Hooks.UseId()));
        }

        [Component]
        public static VNode ChildReadingAPrimitiveParameterComponent(int count)
        {
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: count.ToString()), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildReadingAGenericParameterComponent<T>(T value)
        {
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: System.Convert.ToString(value)), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildAssigningAParameterComponent(int count)
        {
            var (n, _) = Hooks.UseState(0);
            return V.Div("root", V.Label(text: (count = count + 1).ToString()), V.Label(text: n.ToString()));
        }

        [Component]
        public static VNode ChildrenRepeatingTheirParentsInputsComponent()
        {
            var (a, _) = Hooks.UseState(0);
            var (b, _) = Hooks.UseState(0);
            return V.Div("root",
                V.Div("group", V.Label(text: a.ToString()), V.Label(text: a.ToString())),
                V.Label(text: b.ToString()));
        }

        [Test]
        public void Given_TwoChildrenReadingDifferentStates_When_Woven_Then_EachGetsItsOwnScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(TwoIndependentChildrenComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(2));
        }

        [Test]
        public void Given_ChildrenNestedInAChildReadingBoth_When_Woven_Then_EveryChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(NestedIndependentChildrenComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(4), "The group, the two labels in it and the label beside it");
        }

        [Test]
        public void Given_AChildReadingAMutableRef_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildReadingAMutableRefComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildReadingAMutableStatic_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildReadingAMutableStaticComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildCallingAMethodOfTheFixture_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildCallingAFixtureMethodComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildReadingAServiceValue_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildReadingAServiceValueComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildReadingAStopwatchProperty_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildReadingAStopwatchComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildReadingATaskProperty_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(ChildReadingATaskGetterComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildReadingAWeakReferenceProperty_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(ChildReadingAWeakReferenceComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildReadingALazyValue_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(ChildReadingALazyValueComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildReadingAnAutoPropertyOfAnotherAssembly_When_Woven_Then_EachChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(ChildReadingAnAutoPropertyOfAnotherAssemblyComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(2));
        }

        [Test]
        public void Given_AChildStoringALocalTheNextArgumentReads_When_Woven_Then_OnlyTheCardAndTheOtherChildGetAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(ChildStoringALocalItsNextArgumentReadsComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(2));
        }

        // GREEN_ON_BASE(characterization): the base counts the same here.
        // What it pins is a scope that begins where its enclosing scope begins being left to it: `<` in place of `<=` on `each.Region.Start` in `RepeatsEnclosingScope` gives the fallback a gate of its own.
        [Test]
        public void Given_ASuspenseWhoseChildrenReadWhatItReads_When_Woven_Then_OnlyItAndTheOtherChildGetAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(SuspenseReadingOneStateComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(2));
        }

        [Test]
        public void Given_AChildAddingToASetInState_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = ScopeGatesOf(nameof(ChildAddingToASetComponent));

            // Assert
            Assert.That(gates, Is.EqualTo(1), "A hit would skip the Add");
        }

        [Test]
        public void Given_AChildReadingAReferenceFromACustomHook_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildReadingACustomHookValueComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildReadingAReferenceState_When_Woven_Then_EachChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildReadingAReferenceStateComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(2));
        }

        [Test]
        public void Given_AChildReadingAReferenceStateTheBodyReplaces_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildReadingAReplacedReferenceStateComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildWritingALocalThroughAnOutArgument_When_Woven_Then_OnlyTheChildrenNotWritingItGetAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildWritingALocalThroughAnOutArgumentComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(2));
        }

        [Test]
        public void Given_AChildReadingARefStruct_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildReadingARefStructComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildReadingAFieldOfAStructState_When_Woven_Then_EachChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildReadingAStructFieldComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(2));
        }

        [Test]
        public void Given_AChildReadingAPropertyOfAStructState_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildReadingAStructPropertyComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildReadingAnEnumState_When_Woven_Then_EachChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildReadingAnEnumComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(2));
        }

        [Test]
        public void Given_AChildReadingAReadOnlyStatic_When_Woven_Then_EachChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildReadingAReadOnlyStaticComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(2));
        }

        [Test]
        public void Given_AChildHandingOnAMethodGroup_When_Woven_Then_EachChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildWithAMethodGroupHandlerComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(2), "The button, whose handler is read from the compiler's cache field, and the label");
        }

        [Test]
        public void Given_AChildBuiltFromAMutableRef_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildBuiltFromAMutableRefComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base weaves the component gate of this body.
        // It holds the next case to account: a body left unwoven has no scope gate either.
        [Test]
        public void Given_AChildAheadOfALaterHook_When_Woven_Then_ItIsWovenWithItsOwnGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildAheadOfALaterHookComponent)), nameof(Hooks.TryGetMemoizedVNode));

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base weaves no child gate, so it counts none here either.
        // What it pins is a child ahead of the last hook staying out of every scope: `-1` in place of the conditional that
        // sets `boundary` in `PlanScopes` gives the label reading `n` a gate that runs before the component's own.
        [Test]
        public void Given_AChildReadingAStateAheadOfALaterHook_When_Woven_Then_NoChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildAheadOfALaterHookComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(0));
        }

        [Test]
        public void Given_AChildReadingAPrimitiveParameter_When_Woven_Then_EachChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildReadingAPrimitiveParameterComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(2));
        }

        [Test]
        public void Given_AChildReadingAGenericParameter_When_Woven_Then_TheScopeBoxesTheParameterBesideTheGateAndTheSource()
        {
            // Act
            var method = LoadMethod(nameof(ChildReadingAGenericParameterComponent));
            var boxes = method.Body.Instructions.Count(
                instruction => instruction.OpCode == OpCodes.Box && instruction.Operand is GenericParameter);

            // Assert
            Assert.That(boxes, Is.EqualTo(3));
        }

        [Test]
        public void Given_AChildAssigningAParameter_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildAssigningAParameterComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_ChildrenReadingWhatTheirParentReads_When_Woven_Then_OnlyTheParentAndItsSiblingGetAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildrenRepeatingTheirParentsInputsComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(2), "The group and the label beside it; the two labels in the group read what it reads");
        }

        [Test]
        public void Given_AChildReadingTheClock_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildReadingTheClockComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildReadingARandomSource_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildReadingARandomSourceComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildReadingTheProcess_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildReadingTheProcessComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildReadingAGeneratedGuid_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildReadingAGeneratedGuidComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildReadingTheTimeZone_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildReadingTheTimeZoneComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildReadingTheOffsetClock_When_Woven_Then_OnlyTheOtherChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildReadingTheOffsetClockComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildWritingALocalALaterChildReads_When_Woven_Then_OnlyTheReadingChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildWritingALocalALaterChildReadsComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(1));
        }

        [Test]
        public void Given_AChildReadingNothing_When_Woven_Then_ItGetsAScopeGateBesideTheOneReadingTheState()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildReadingNothingComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(2));
        }

        [Test]
        public void Given_APropsOnlyBody_When_Woven_Then_EachChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(PropsOnlyChildrenComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(2), "The label reading the prop and the label reading nothing");
        }

        // GREEN_ON_BASE(characterization): the base weaves no child gate, so it counts none here either.
        // What it pins is a loop around the children keeping them out of every scope: replacing the last line of
        // `BranchBreaksRegion` with `return false;` gives the pair of labels inside the loop a gate each.
        [Test]
        public void Given_ChildrenBuiltInALoop_When_Woven_Then_NoChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildrenBuiltInALoopComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base weaves no child gate, so it counts none here either.
        // What it pins is a protected region keeping every child out of every scope: `DepthsBefore(instructions)` in
        // place of `body.HasExceptionHandlers ? null : DepthsBefore(instructions)` in `PlanScopes` gives both labels a gate.
        [Test]
        public void Given_ChildrenAfterATryFinally_When_Woven_Then_NoChildGetsAScopeGate()
        {
            // Act
            var gates = CountHookCalls(LoadMethod(nameof(ChildrenAfterATryFinallyComponent)), ScopeGateName);

            // Assert
            Assert.That(gates, Is.EqualTo(0));
        }

        #endregion

        #region Bailed shapes (left unwoven)

        [Test]
        public void Given_CompilerFalseComponent_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(CompilerOptOutComponent))), Is.False,
                "[Component(Compiler = false)] opts out ahead of analysis; the analyzable body is left unwoven");
        }

        // GREEN_ON_BASE(characterization): the base reads Compiler by name as well. Cutting the
        // `named.Name == propertyName` clause from `NamedFlag` reads the first flag instead, and reddens this.
        [Test]
        public void Given_CompilerFalseBehindAnotherFlag_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(MemoizedOptOutComponent))), Is.False,
                "Compiler = false is read by its name, whatever flag precedes it on the attribute");
        }

        // GREEN_ON_BASE(characterization): the base weaves no body without a hook. What this pins is the
        // props-bail skip on the new path: the hookless branch's `return !RequestsPropsBail(method)` turned
        // into `return true` reddens it.
        [Test]
        public void Given_MemoizedPropsOnlyComponent_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(MemoizedPropsComponent))), Is.False,
                "A props-only body with Memoize = true is left to the props bail");
        }

        // GREEN_ON_BASE(characterization): the base leaves this body unwoven too, and this change keeps it so.
        // What changed is the message, which gave the missing hook as the reason where a body with a parameter
        // and no hook is woven now. Removing the guard in `TryAnalyze` that refuses an empty deps array reddens it.
        [Test]
        public void Given_NoHookComponent_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(NoHookComponent))), Is.False,
                "A body with neither a hook nor a parameter has no deps to key a cache on; the weaver leaves it"
                + " untouched");
        }

        [Test]
        public void Given_DiscardedHookValue_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(DiscardedValueComponent))), Is.False,
                "Discarding the state value leaves the changing input out of the deps array; the weaver bails");
        }

        [Test]
        public void Given_UseMutationComponent_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(UseMutationComponent))), Is.False,
                "UseMutation returns a stable reference mutated in place; a woven memo would return a stale VNode, so the weaver bails");
        }
        [Test]
        public void Given_VoidOnlyComponentWithoutProps_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(VoidOnlyComponent))), Is.False,
                "A void-only body has an empty deps array; weaving would freeze it on an unconditional hit, so the weaver bails");
        }

        [Test]
        public void Given_WholeTupleCapture_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(WholeTupleStateComponent))), Is.False,
                "A whole-tuple capture is compared structurally, diverging from the reference equality the reconciler uses, so the weaver bails and requires single-element deconstruction");
        }

        [Test]
        public void Given_TransitiveUseComponent_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(TransitiveUseComponent))), Is.False,
                "A custom hook that transitively reaches the suspend-unsafe Use hook forces the component to bail");
        }
        [Test]
        public void Given_TransitiveUseMutationComponent_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(TransitiveUseMutationComponent))), Is.False,
                "A custom hook that transitively reaches UseMutation forces the component to bail");
        }
        // GREEN_ON_BASE(characterization): the base bails a body making an open dispatch, and this one stays bailed.
        // What it pins is the ahead-of-gate refusal: removing the `HasOpaqueCallAhead` check from `TryAnalyze`
        // reddens it.
        [Test]
        public void Given_InterfaceDispatchAheadOfTheHook_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(InterfaceDispatchAheadOfHookComponent))), Is.False,
                "A dispatch ahead of the gate runs whether or not the gate hits, and a hook it reached would go uncaptured");
        }
        // GREEN_ON_BASE(characterization): the base bails a body whose helper makes an open dispatch, as this one.
        // What it pins is the helper taking Opaque from its dispatch: masking the Opaque flag off what
        // `CallGraphFold.Visit` folds from a callee reddens it.
        [Test]
        public void Given_HelperDispatchingAheadOfTheHook_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(HelperDispatchAheadOfHookComponent))), Is.False,
                "A helper whose only reach is a dispatch is Opaque, and Opaque ahead of the gate bails the body");
        }
        // GREEN_ON_BASE(characterization): the base bails a custom hook reaching a dispatch, and so does this change.
        // What it pins is a hook call still carrying its dispatch: testing `IsOpaqueCall` for a reach of exactly
        // Opaque instead of for the Opaque flag reddens it.
        [Test]
        public void Given_CustomHookComposingADispatch_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(CustomHookWithDispatchComponent))), Is.False,
                "A custom hook runs ahead of the gate, so a dispatch inside it bails the body as a direct one does");
        }
        [Test]
        public void Given_HookCycleEnteredAtItsOtherMember_When_ThatMemberSitsBehindABranch_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(LoopMemberBehindABranchComponent))), Is.False,
                "UseLoopSecond reaches UseState through UseLoopThird and UseLoopFirst, so it is a hook call the branch can skip");
        }

        [Test]
        public void Given_UseMutationCycleEnteredAtItsOtherMember_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(MutatingLoopMemberComponent))), Is.False,
                "UseMutatingLoopSecond reaches UseMutation through UseMutatingLoopFirst, so the body bails");
        }
        // GREEN_ON_BASE(characterization): the base leaves a setter-only read unwoven, and so does this change.
        // What it pins is the stack capture taking Item1 alone: deleting the `field.Name != "Item1"` check from
        // `TryCaptureStackValue` captures the stable setter and reddens it.
        [Test]
        public void Given_OnlyTheSetterReadOffTheStack_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(StateSetterOffTheStackComponent))), Is.False,
                "The setter is stable while the value it sets is never captured, so the weaver bails");
        }

        // GREEN_ON_BASE(characterization): the base leaves a whole tuple unwoven, and so does this change.
        // What it pins is the stack capture refusing one too: replacing `if (!IsValueTupleType(type)) return true;`
        // in `TryCaptureStackValue` with `return true;` reddens it.
        [Test]
        public void Given_AWholeTupleHandedOnOffTheStack_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(WholeTupleOffTheStackComponent))), Is.False,
                "A whole tuple compares structurally rather than by reference, so the weaver bails");
        }

        // GREEN_ON_BASE(characterization): the base bails a custom hook reaching a dispatch, and so does this change.
        // What it pins is the ahead-of-gate scan reading its boundary: with the value on the stack the hook call
        // is the boundary, and moving `if (instr == boundary) break;` ahead of the `IsOpaqueCall` test in
        // `HasOpaqueCallAhead` reddens it.
        [Test]
        public void Given_CustomHookComposingADispatchPassedStraightAsAnArgument_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(CustomHookWithDispatchAsArgumentComponent))), Is.False,
                "The custom hook is the boundary and runs ahead of the gate, so its dispatch bails the body");
        }
        // GREEN_ON_BASE(characterization): the base bails a whole tuple stored to a field, and so does this change.
        // What it pins is the stack capture reading Item1 only through an ldfld: deleting the `OpCodes.Ldfld`
        // check from `TryCaptureStackValue` takes the stsfld naming a field Item1 for the value element.
        [Test]
        public void Given_AWholeTupleStoredToAFieldNamedItem1_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(TupleStoredToAFieldNamedItem1Component))), Is.False,
                "The field holds the whole tuple, which compares structurally, so the weaver bails");
        }

        // GREEN_ON_BASE(characterization): the base bails a hook value stored to a field, and so does this change.
        // What it pins is the stack capture leaving that store to the body: deleting the `Stsfld` clause from the
        // consumer test at the end of `TryCaptureStackValue` weaves it.
        [Test]
        public void Given_AHookValueStoredToAStaticField_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(HookValueStoredToAFieldComponent))), Is.False,
                "A hit would skip the store, leaving the field at an earlier render's value");
        }

        // GREEN_ON_BASE(characterization): the base bails a hook value stored to an instance field, and so does this change.
        // What it pins is the consumer test reading a store to an instance field too: deleting the `Stfld` clause
        // at the end of `TryCaptureStackValue` weaves it.
        [Test]
        public void Given_AHookValueStoredToAnInstanceField_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(HookValueStoredToAnInstanceFieldComponent))), Is.False,
                "A hit would skip the store, leaving the field at an earlier render's value");
        }

        // GREEN_ON_BASE(characterization): the base bails a void custom hook, and so does this change.
        // What it pins is the stack capture refusing a void call: deleting the `MetadataType.Void` clause from
        // `TryCaptureStackValue` declares a void local for it, and reddens this.
        [Test]
        public void Given_AVoidCustomHook_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(VoidCustomHookComponent))), Is.False,
                "A void custom hook hides whatever value hook it composes, and pushes nothing to capture");
        }

        // GREEN_ON_BASE(characterization): the base bails a ref-returning custom hook, and so does this change.
        // What it pins is the stack capture refusing a type it cannot name: deleting the `type == null` clause
        // from `TryCaptureStackValue` reads the null `InCallerTerms` returns for a by-reference type.
        [Test]
        public void Given_ARefReturningCustomHook_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(RefReturningCustomHookComponent))), Is.False,
                "The caller reads through the returned reference, so there is no value to copy");
        }

        // Not green on the base: the base captures a reference local like any other, and boxes nothing for it.
        [Test]
        public void Given_AReferenceLocalOverACustomHook_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(RefLocalFromCustomHookComponent))), Is.False,
                "A managed pointer cannot be stored into the deps array");
        }

        // Not green on the base: the base captures only the first element of a custom hook's pair.
        [Test]
        public void Given_ACustomHookPairDeconstructedWhole_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(CustomPairDeconstructedComponent))), Is.False,
                "The second element of a custom hook's pair can change, and only the first would key the cache");
        }

        [Test]
        public void Given_HookInsideWhileLoop_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(WhileLoopHookComponent))), Is.False,
                "A hook inside a head-tested loop can be skipped or repeated within a render, so the weaver bails");
        }

        [Test]
        public void Given_HookInsideDoWhileLoop_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(DoWhileLoopHookComponent))), Is.False,
                "A do-while loop closes with only a backward branch across the hook; weaving it would anchor"
                + " the cache gate inside the loop, so the weaver bails");
        }

        [Test]
        public void Given_HookInsideTryCatch_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(TryCatchHookComponent))), Is.False,
                "A hook section overlapping a protected region would require the Leave protocol the weaver"
                + " does not emit, so the weaver bails");
        }

        #endregion

        #region Render normally regardless of weave outcome

        [Test]
        public void Given_WovenComponent_When_FirstRender_Then_ProducesVisibleOutput()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(WeavedComponent, key: "weaved"));

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("0"), "A woven component renders normally on first render");
        }

        [Test]
        public void Given_CompilerOptOutComponent_When_FirstRender_Then_ProducesVisibleOutput()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(CompilerOptOutComponent, key: "compiler-optout"));

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("0"), "An opted-out component still renders normally");
        }

        [Test]
        public void Given_DeconstructedComponent_When_FirstRender_Then_ProducesVisibleOutput()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(DeconstructedComponent, key: "deconstructed"));

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("42"), "The label reflects the initial state value");
        }

        [Test]
        public void Given_MultiReturnComponent_When_FirstRender_Then_ProducesVisibleOutput()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(MultiReturnComponent, key: "multi-return"));

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("7"), "The initial state value takes the second return path");
        }

        // GREEN_ON_BASE(characterization): the base renders the same body unwoven. Weaving it moves the first
        // render onto the gate's miss path, which is what this reads: dropping the entry insertion
        // `il.InsertBefore(entry, ins)` leaves the commit a null deps array to store, and reddens it.
        [Test]
        public void Given_WovenPropsComponent_When_FirstRender_Then_ProducesVisibleOutput()
        {
            // Act
            using var mounted = V.Mount(_root,
                V.Component(PropsComponent, new GreetProps("hello"), key: "props"));

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("hello"),
                "The first render misses the freshly allocated slot and runs the body");
        }

        [Test]
        public void Given_WovenPropsWithHookComponent_When_FirstRender_Then_ProducesVisibleOutput()
        {
            // Act
            using var mounted = V.Mount(_root,
                V.Component(PropsWithHookComponent, new GreetProps("hello"), key: "props-hook"));

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("hello!"),
                "The output combines the prop and the hook value; the woven gate misses on first render");
        }

        [Test]
        public void Given_WovenUseContextComponent_When_FirstRender_Then_ProducesVisibleOutput()
        {
            // Act
            using var mounted = V.Mount(_root,
                V.Provider(NameContext, "provided", new VNode[]
                {
                    V.Component(ContextComponent, key: "ctx"),
                }));

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("provided"),
                "The captured context value drives the output; the woven gate misses on first render");
        }

        [Test]
        public void Given_WovenUseMemoOnlyComponent_When_FirstRender_Then_ProducesVisibleOutput()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(UseMemoOnlyComponent, key: "use-memo"));

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("memo"),
                "A woven UseMemo-only component renders normally on first render");
        }

        [Test]
        public void Given_BailedWhileLoopHookComponent_When_FirstRender_Then_ProducesVisibleOutput()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(WhileLoopHookComponent, key: "while-loop"));

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("11"),
                "A bailed loop component still renders normally (the loop body runs exactly once)");
        }

        [Test]
        public void Given_BailedDoWhileLoopHookComponent_When_FirstRender_Then_ProducesVisibleOutput()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(DoWhileLoopHookComponent, key: "do-while-loop"));

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("13"),
                "A bailed do-while component still renders normally (the loop body runs exactly once)");
        }

        [Test]
        public void Given_BailedTryCatchHookComponent_When_FirstRender_Then_ProducesVisibleOutput()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(TryCatchHookComponent, key: "try-catch"));

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("17"),
                "A bailed try/catch component still renders normally through the non-throwing path");
        }

        [Test]
        public void Given_HookNestedInExpressionBodiedArgumentList_When_FirstRender_Then_ProducesVisibleOutput()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(ExpressionBodiedNestedHookComponent, key: "expr-nested"));

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("4"),
                "A hook nested in V.Label's argument list leaves a woven body the runtime accepts");
        }

        [Test]
        public void Given_HookNestedInBlockBodiedReturn_When_FirstRender_Then_ProducesVisibleOutput()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(BlockBodiedNestedHookComponent, key: "block-nested"));

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("5"),
                "A hook nested in a return statement's argument list leaves a woven body the runtime accepts");
        }

        [Test]
        public void Given_HookNestedInChildArgumentList_When_FirstRender_Then_ProducesVisibleOutput()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(ChildArgumentHookComponent, key: "child-nested"));

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("6"),
                "A hook nested in a child's argument list leaves a woven body the runtime accepts");
        }

        [Test]
        public void Given_HookWithCapturingSelectorNestedInArgumentList_When_FirstRender_Then_ProducesVisibleOutput()
        {
            // Act
            using var mounted = V.Mount(_root,
                V.Component(CapturingSelectorHookComponent, new OffsetProps(3), key: "capturing-nested"));

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("7"),
                "A nested hook whose selector closes over a prop leaves a woven body the runtime accepts");
        }

        [Test]
        public void Given_CatchAheadOfNestedHook_When_FirstRender_Then_ProducesVisibleOutput()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(CatchAheadOfNestedHookComponent, key: "catch-nested"));

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("9"),
                "A try/catch ahead of a nested hook leaves a woven body the runtime accepts");
        }

        [Test]
        public void Given_InitSetterAheadOfNestedHook_When_FirstRender_Then_ProducesVisibleOutput()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(InitSetterAheadOfNestedHookComponent, key: "init-nested"));

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("10"),
                "An init accessor called ahead of a nested hook leaves a woven body the runtime accepts");
        }

        [Test]
        public void Given_ConditionalAheadOfNestedHook_When_FirstRender_Then_ProducesVisibleOutput()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(ConditionalAheadOfNestedHookComponent, key: "conditional-nested"));

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("wide 11"),
                "A conditional operand evaluated ahead of a nested hook leaves a woven body the runtime accepts");
        }

        [Test]
        public void Given_TwoNestedHooks_When_OnlyTheFirstStoreChanges_Then_TheLabelShowsItsNewValue()
        {
            // Arrange
            using var first = new SettableStore(1);
            using var second = new SettableStore(2);
            s_firstStore = first;
            s_secondStore = second;
            using var mounted = V.Mount(_root, V.Component(TwoNestedHooksComponent, key: "two-nested"));

            // Act
            first.Set(9);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("9/2"),
                "Each nested hook keys the memo on its own value");
        }

        [Test]
        public void Given_TwoHookStatements_When_OnlyTheFirstStoreChanges_Then_TheLabelShowsItsNewValue()
        {
            // Arrange
            using var first = new SettableStore(1);
            using var second = new SettableStore(2);
            s_firstStore = first;
            s_secondStore = second;
            using var mounted = V.Mount(_root, V.Component(TwoHookStatementsComponent, key: "two-statements"));

            // Act
            first.Set(9);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("9/2"),
                "Each hook statement keys the memo on its own value");
        }

        // GREEN_ON_BASE(characterization): the base renders this body unwoven, so it shows the new value too.
        // What it pins is the copy holding the call's value: putting `ldnull` where `InjectMemoization` inserts
        // the `dup` keys every render on null, so the second render hits and shows the first value.
        [Test]
        public void Given_AStoreValuePassedStraightAsAnArgument_When_TheStoreChanges_Then_TheLabelShowsItsNewValue()
        {
            // Arrange
            using var first = new SettableStore(1);
            s_firstStore = first;
            using var mounted = V.Mount(_root, V.Component(StoreValueAsArgumentComponent, key: "stack"));

            // Act
            first.Set(9);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("9"), "The copied store value keys the memo");
        }

        [Test]
        public void Given_AStoreValuePassedStraightAsAnArgument_When_TheParentReRendersWithItUnchanged_Then_TheBodyDoesNotRun()
        {
            // Arrange
            using var first = new SettableStore(1);
            s_firstStore = first;
            s_stackBuilds = 0;
            using var mounted = V.Mount(_root, V.Component(StoreValueAsArgumentParent, key: "stack-parent"));

            // Act
            s_stackParentSetTick(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_stackBuilds, Is.EqualTo(1), "An equal copied value is a hit, so the counter past the gate runs once");
        }

        // GREEN_ON_BASE(characterization): the base renders this body unwoven, and it renders the same woven.
        // What it pins is the copy's type naming the call's arguments: deleting `resolved.GenericArguments.Add(each);`
        // from `InCallerTerms` declares the copy as a Ref`1 instance with no type argument.
        [Test]
        public void Given_ARefValueReadOffTheStack_When_FirstRender_Then_ProducesVisibleOutput()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(RefValueOffTheStackComponent, key: "ref-stack"));

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("ref"), "The copy of the Ref<object> keys the memo");
        }

        [Test]
        public void Given_Item1ReadOffTheStack_When_TheParentReRendersWithItUnchanged_Then_TheBodyDoesNotRun()
        {
            // Arrange
            s_stackBuilds = 0;
            s_stackChild = StateItem1AsArgumentComponent;
            using var mounted = V.Mount(_root, V.Component(StackChildParent, key: "stack-parent"));

            // Act
            s_stackParentSetTick(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_stackBuilds, Is.EqualTo(1), "An equal copied Item1 is a hit, so the counter past the gate runs once");
        }

        [Test]
        public void Given_AnArrayFromACustomHookIndexedOffTheStack_When_TheParentReRendersWithItUnchanged_Then_TheBodyDoesNotRun()
        {
            // Arrange
            using var first = new SettableStore(1);
            s_firstStore = first;
            s_stackBuilds = 0;
            s_stackChild = ArrayFromCustomHookComponent;
            using var mounted = V.Mount(_root, V.Component(StackChildParent, key: "stack-parent"));

            // Act
            s_stackParentSetTick(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_stackBuilds, Is.EqualTo(1), "The same array is an equal copy, so the counter past the gate runs once");
        }

        // GREEN_ON_BASE(characterization): the base renders this body unwoven, so it shows the new value too.
        // What it pins is the copy holding the call's value: putting `ldnull` where `InjectMemoization` inserts
        // the `dup` keys every render on null, so the second render hits and shows the first value.
        [Test]
        public void Given_AnArrayFromACustomHookIndexedOffTheStack_When_TheStoreChanges_Then_TheLabelShowsItsNewValue()
        {
            // Arrange
            using var first = new SettableStore(1);
            s_firstStore = first;
            using var mounted = V.Mount(_root, V.Component(ArrayFromCustomHookComponent, key: "array-stack"));

            // Act
            first.Set(9);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("9"), "The copied array keys the memo");
        }

        [Test]
        public void Given_ARefValueReadOffTheStack_When_TheParentReRendersWithItUnchanged_Then_TheBodyDoesNotRun()
        {
            // Arrange
            s_stackBuilds = 0;
            s_stackChild = RefValueOffTheStackComponent;
            using var mounted = V.Mount(_root, V.Component(StackChildParent, key: "stack-parent"));

            // Act
            s_stackParentSetTick(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_stackBuilds, Is.EqualTo(1), "The same Ref is an equal copy, so the counter past the gate runs once");
        }

        // GREEN_ON_BASE(characterization): the base renders this body unwoven, and it renders the same woven.
        // What it pins is the gate's branch: `Brtrue` in place of `Brfalse` in `InjectMemoization` sends the
        // first render down the hit path, which returns a null tree.
        [Test]
        public void Given_InterfaceDispatchAfterTheHook_When_FirstRender_Then_ProducesVisibleOutput()
        {
            // Arrange
            s_propChild = InterfaceDispatchComponent;

            // Act
            using var mounted = V.Mount(_root, V.Component(PropChildParent, key: "prop-parent"));

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("svc0a"), "The dispatch runs on the first render");
        }

        // GREEN_ON_BASE(characterization): the base renders this body unwoven, so it shows the new prop too.
        // What it pins is the prop keying the memo: emptying the loop over `parameters` in `InjectMemoization`
        // leaves the prop out of the deps, so the second render hits and shows the first name.
        [Test]
        public void Given_InterfaceDispatchAfterTheHook_When_ThePropChanges_Then_TheLabelShowsItsNewValue()
        {
            // Arrange
            s_propChild = InterfaceDispatchComponent;
            using var mounted = V.Mount(_root, V.Component(PropChildParent, key: "prop-parent"));

            // Act
            s_propParentSetName("b");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("svc0b"), "The prop keys the memo ahead of the dispatch");
        }

        // GREEN_ON_BASE(characterization): the base renders this body unwoven, and it renders the same woven.
        // What it pins is the gate's branch: `Brtrue` in place of `Brfalse` in `InjectMemoization` sends the
        // first render down the hit path, which returns a null tree.
        [Test]
        public void Given_VirtualDispatchAfterTheHook_When_FirstRender_Then_ProducesVisibleOutput()
        {
            // Arrange
            s_propChild = VirtualDispatchComponent;

            // Act
            using var mounted = V.Mount(_root, V.Component(PropChildParent, key: "prop-parent"));

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("0a"), "The dispatch runs on the first render");
        }

        // GREEN_ON_BASE(characterization): the base renders this body unwoven, so it shows the new prop too.
        // What it pins is the prop keying the memo: emptying the loop over `parameters` in `InjectMemoization`
        // leaves the prop out of the deps, so the second render hits and shows the first name.
        [Test]
        public void Given_VirtualDispatchAfterTheHook_When_ThePropChanges_Then_TheLabelShowsItsNewValue()
        {
            // Arrange
            s_propChild = VirtualDispatchComponent;
            using var mounted = V.Mount(_root, V.Component(PropChildParent, key: "prop-parent"));

            // Act
            s_propParentSetName("b");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("0b"), "The prop keys the memo ahead of the dispatch");
        }

        [Test]
        public void Given_ACustomHookPairDeconstructedWhole_When_TheSecondElementChanges_Then_TheLabelShowsItsNewValue()
        {
            // Arrange
            using var first = new SettableStore(1);
            using var second = new SettableStore(2);
            s_firstStore = first;
            s_secondStore = second;
            using var mounted = V.Mount(_root, V.Component(CustomPairDeconstructedComponent, key: "custom-pair"));

            // Act
            second.Set(9);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("1/9"), "The second element of the pair reaches the label");
        }

        [Test]
        public void Given_SiblingBuiltAheadOfNestedHook_When_ParentReRendersWithEqualDeps_Then_NoRentedPropsAreLeftBehind()
        {
            // Arrange
            s_rowBuilds = 0;
            using var mounted = V.Mount(_root, V.Component(SiblingRowParent, key: "row-parent"));
            var before = VNodePoolTestAccess.RentedOutCountsForTest();

            // Act
            s_rowParentSetTick(1);
            mounted.FlushStateForTest();

            // Assert — one build means the second render hit the memo, which is the render that drops the sibling.
            var after = VNodePoolTestAccess.RentedOutCountsForTest();
            Assert.That(
                (s_rowBuilds, after.Props - before.Props, after.EventArrays - before.EventArrays,
                    after.NodeArrays - before.NodeArrays),
                Is.EqualTo((1, 0, 0, 0)),
                "A memo hit leaves nothing the sibling rented ahead of the gate in the pool's rented sets");
        }

        #endregion

        #region Helpers

        // A body is woven iff both the gate (TryGetMemoizedVNode) and the commit (StoreMemoizedVNode) are
        // injected; an unwoven body has neither.
        private static bool IsWoven(MethodDefinition method) =>
            InjectsHookCall(method, nameof(Hooks.TryGetMemoizedVNode))
            && InjectsHookCall(method, nameof(Hooks.StoreMemoizedVNode));

        private static MethodDefinition LoadMethod(string name)
        {
            var assemblyPath = typeof(CompilerILPostProcessorE2ETests).Assembly.Location;
            var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
            var fixtureType = assembly.MainModule.GetType(typeof(CompilerILPostProcessorE2ETests).FullName);
            Assume.That(fixtureType, Is.Not.Null, "Precondition: the fixture type is in the assembly");
            return fixtureType.Methods.Single(m => m.Name == name);
        }

        // The run of `pop` between the branch on TryGetMemoizedVNode's result and the cached VNode's load, or -1
        // for a body without the gate.
        private static int HitPathPops(MethodDefinition method)
        {
            var gate = IndexOfHookCall(method, nameof(Hooks.TryGetMemoizedVNode));
            if (gate < 0) return -1;
            var instructions = method.Body.Instructions;
            var pops = 0;
            while (instructions[gate + 2 + pops].OpCode == OpCodes.Pop) pops++;
            return pops;
        }

        private const string ScopeGateName = "TryGetMemoizedScope";
        private const string ScopeStoreName = "StoreMemoizedScope";

        private static int CountHookCalls(MethodDefinition method, string hookMethodName) =>
            method.Body.Instructions.Count(IsHookCallTo(hookMethodName));

        // The length of the dependency array handed to the `occurrence`th call to a gate: the `ldc.i4 n; newarr object`
        // that builds it, ahead of the call.
        private static int DependencyCountOfCall(MethodDefinition method, string gateName, int occurrence)
        {
            var gate = method.Body.Instructions.Where(IsHookCallTo(gateName)).ElementAt(occurrence);
            var array = gate;
            while (array.OpCode != OpCodes.Newarr) array = array.Previous;
            var length = array.Previous;
            return length.OpCode.Code switch
            {
                Code.Ldc_I4_0 => 0,
                Code.Ldc_I4_1 => 1,
                Code.Ldc_I4_2 => 2,
                Code.Ldc_I4_3 => 3,
                Code.Ldc_I4_4 => 4,
                Code.Ldc_I4_5 => 5,
                Code.Ldc_I4_6 => 6,
                Code.Ldc_I4_7 => 7,
                Code.Ldc_I4_8 => 8,
                Code.Ldc_I4_S => (sbyte)length.Operand,
                _ => (int)length.Operand,
            };
        }

        private static int GateDependencyCount(MethodDefinition method) =>
            DependencyCountOfCall(method, nameof(Hooks.TryGetMemoizedVNode), 0);

        private static bool InjectsHookCall(MethodDefinition method, string hookMethodName) =>
            method.Body.Instructions.Any(IsHookCallTo(hookMethodName));

        // Index (in instruction order) of the first call to the given Velvet.Hooks method, or -1 when absent.
        // Instruction order is what places the injected cache gate relative to the hook section.
        private static int IndexOfHookCall(MethodDefinition method, string hookMethodName)
        {
            var instructions = method.Body.Instructions;
            var isHookCall = IsHookCallTo(hookMethodName);
            for (var i = 0; i < instructions.Count; i++)
            {
                if (isHookCall(instructions[i])) return i;
            }
            return -1;
        }

        private static System.Func<Instruction, bool> IsHookCallTo(string methodName) => instr =>
            instr.OpCode == OpCodes.Call
            && instr.Operand is MethodReference methodRef
            && methodRef.DeclaringType.FullName == typeof(Hooks).FullName
            && methodRef.Name == methodName;

        #endregion
    }
}

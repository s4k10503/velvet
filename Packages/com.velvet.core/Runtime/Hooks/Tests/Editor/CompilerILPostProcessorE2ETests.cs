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

        // Deliberately never assigned: analysis is static, so the outcome does not depend on the runtime value.
        private static readonly IDispatchService? s_dispatchService = null;

        // A call through an interface declared in a Velvet-referencing assembly, after the only hook call.
        [Component]
        public static VNode InterfaceDispatchComponent()
        {
            var (count, _) = Hooks.UseState(0);
            var extra = s_dispatchService?.Value() ?? "none";
            return V.Label(text: extra + count.ToString());
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

        // A helper calling itself: the walk classifying it meets it again before it has finished reading it.
        private static string Repeat(string text, int times) => times <= 0 ? text : Repeat(text + "!", times - 1);

        [Component]
        public static VNode RecursiveHelperComponent()
        {
            var (count, _) = Hooks.UseState(0);
            return V.Label(text: Repeat(count.ToString(), 2));
        }

        public class OverridableFormatter
        {
            public virtual string Format(int value) => value.ToString();
        }

        private static readonly OverridableFormatter s_formatter = new();

        // A virtual method on a non-sealed class in a Velvet-referencing assembly, after the only hook call.
        [Component]
        public static VNode VirtualDispatchComponent()
        {
            var (count, _) = Hooks.UseState(0);
            return V.Label(text: s_formatter.Format(count));
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
        // What it pins is the ahead-of-gate check doing the same: dropping the call-opcode filter from
        // `HasOpaqueCallAhead` lets it walk the lambda the `ldftn` names, and reddens it.
        [Test]
        public void Given_DispatchInsideALambdaHandedToAHook_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(DispatchInsideHookArgumentLambdaComponent))), Is.True,
                "A lambda's body does not run while the component body builds, so its dispatch is not ahead of the gate");
        }

        // GREEN_ON_BASE(characterization): the base classifies a recursive helper as well.
        // What it pins is the reach walk's cycle guard: removing the provisional None that `ReachOf` writes
        // before reading a body leaves the walk recursing through `Repeat` without end.
        [Test]
        public void Given_RecursiveHelperAfterTheHook_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(RecursiveHelperComponent))), Is.True,
                "A helper that calls itself is classified once, and reaches no hook");
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
        // What it pins is the helper taking Opaque from its dispatch: masking the Opaque flag off a callee's reach
        // in `ReachOfDefinition` reddens it.
        [Test]
        public void Given_HelperDispatchingAheadOfTheHook_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(HelperDispatchAheadOfHookComponent))), Is.False,
                "A helper whose only reach is a dispatch is Opaque, and Opaque ahead of the gate bails the body");
        }

        // GREEN_ON_BASE(characterization): the base bails a custom hook reaching a dispatch, and so does this change.
        // What it pins is a hook call still carrying its dispatch: testing `HasOpaqueCallAhead` for a reach of
        // exactly Opaque instead of for the Opaque flag reddens it.
        [Test]
        public void Given_CustomHookComposingADispatch_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(CustomHookWithDispatchComponent))), Is.False,
                "A custom hook runs ahead of the gate, so a dispatch inside it bails the body as a direct one does");
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

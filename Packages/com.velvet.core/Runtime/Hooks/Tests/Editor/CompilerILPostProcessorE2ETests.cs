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
    /// hook section, so no hook call is skipped on a cache hit.</item>
    /// <item>A body the weaver cannot prove correct is left unwoven (graceful bailout): no hook to key a cache
    /// on, a props-only body, a discarded hook value, a whole-tuple capture (compared structurally, not by
    /// reference, so a fresh-but-equal record would be a stale hit), a void-only body (empty deps would freeze
    /// it on an unconditional hit), a body that reaches the suspend-unsafe <c>Use</c> hook or
    /// <c>UseMutation</c> — directly or transitively through a custom hook — a hook inside a loop (head-tested
    /// or do-while), a hook section overlapping a try/catch region, and an open virtual / interface dispatch
    /// outside the BCL / Unity / UniTask carve-out (the runtime override could compose a hook the static
    /// target does not show, regardless of whether the declaring assembly itself references Velvet — an
    /// override can live in a third assembly that does). A delegate invocation (virtual Invoke on a sealed
    /// type) is not an open dispatch and does not bail.</item>
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

        // No hook call: nothing to key a cache on, so the weaver has no inner memo to inject -> bailout.
        [Component]
        public static VNode NoHookComponent()
        {
            return V.Label(text: "no-hook");
        }

        public sealed record GreetProps(string Name);

        // Props-receiving component with no hook. A prop is a reactive input the deps array would capture, but
        // there is no hook to establish a cache boundary, so the weaver has no inner memo to inject -> bailout.
        [Component]
        public static VNode PropsComponent(GreetProps p)
        {
            return V.Label(text: p.Name);
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
                MutationFn: (v, _) => Cysharp.Threading.Tasks.UniTask.FromResult(v * 2)));
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

        // A void-only body (only a void effect hook, no value hook, no props) has an empty deps array. Weaving it
        // would make TryGetMemoizedVNode an unconditional hit that freezes the body after the first render, so the
        // weaver must leave it unwoven.
        [Component]
        public static VNode VoidOnlyComponent()
        {
            Hooks.UseEffect(() => () => { }, System.Array.Empty<object>());
            return V.Label(text: "void-only");
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
        // is on the value allow-list, so the body is woven. The body deliberately does not CALL a member
        // through the interface-typed reference: an open interface dispatch in a Velvet-referencing assembly
        // is unverifiable and would bail (see InterfaceDispatchComponent) — the stable reference itself is
        // the captured dep.
        [Component]
        public static VNode UseServiceComponent()
        {
            var service = Hooks.UseService<IWovenService>();
            return V.Label(text: service != null ? "resolved" : "missing");
        }

        // Custom hook that transitively reaches the suspend-unsafe Use hook. A component calling it must bail.
        private static System.Func<Cysharp.Threading.Tasks.UniTask<string>> s_factory =
            () => Cysharp.Threading.Tasks.UniTask.FromResult("x");

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
                MutationFn: (v, _) => Cysharp.Threading.Tasks.UniTask.FromResult(v * 2)));

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

        // Deliberately never assigned: analysis is static, so the bail happens regardless of the runtime
        // value, and the null-propagated call keeps the render test NPE-free.
        private static readonly IDispatchService? s_dispatchService = null;

        // A call through an interface declared in a Velvet-referencing assembly: the statically resolved
        // target has no body, but the runtime implementation could compose any hook, so the weaver must
        // treat the call as unverifiable and bail rather than weave against the declared (empty) target.
        [Component]
        public static VNode InterfaceDispatchComponent()
        {
            var (count, _) = Hooks.UseState(0);
            var extra = s_dispatchService?.Value() ?? "none";
            return V.Label(text: extra + count.ToString());
        }

        public class OverridableFormatter
        {
            public virtual string Format(int value) => value.ToString();
        }

        private static readonly OverridableFormatter s_formatter = new();

        // A virtual method on a non-sealed class in a Velvet-referencing assembly: an override could call a
        // hook the statically resolved body does not show, so the call is unverifiable and the weaver bails.
        [Component]
        public static VNode VirtualDispatchComponent()
        {
            var (count, _) = Hooks.UseState(0);
            return V.Label(text: s_formatter.Format(count));
        }

        public delegate string TextProvider();

        private static readonly TextProvider s_textProvider = static () => "delegate";

        // Invoking a delegate declared in a Velvet-referencing assembly: a delegate type is sealed and its
        // runtime-implemented Invoke cannot be overridden by user code, so the call is not an open dispatch
        // and must not bail the component. Pins that the open-dispatch bail does not over-reach into
        // delegate invocations — the callback pattern component bodies use everywhere.
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
        public void Given_UseContextComponent_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(ContextComponent))), Is.True,
                "UseContext captures the live value into the deps array; the body is woven");
        }

        [Test]
        public void Given_SafeVoidEffectAlongsideValueHook_When_Woven_Then_InjectsBothMemoCalls()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(VoidEffectComponent))), Is.True,
                "A safe void effect hook advances the hook boundary while UseState supplies the captured dep");
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

        #endregion

        #region Bailed shapes (left unwoven)

        [Test]
        public void Given_CompilerFalseComponent_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(CompilerOptOutComponent))), Is.False,
                "[Component(Compiler = false)] opts out ahead of analysis; the analyzable body is left unwoven");
        }

        [Test]
        public void Given_NoHookComponent_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(NoHookComponent))), Is.False,
                "A hook-less body has no deps to key a cache on; the weaver leaves it untouched");
        }

        [Test]
        public void Given_PropsOnlyComponent_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(PropsComponent))), Is.False,
                "A props-only body has no hook to key a cache on; the weaver leaves it untouched");
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

        [Test]
        public void Given_InterfaceDispatchComponent_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(InterfaceDispatchComponent))), Is.False,
                "An interface dispatch in a Velvet-referencing assembly resolves only to the body-less"
                + " declaration; the runtime implementation could compose a hook, so the weaver bails");
        }

        [Test]
        public void Given_VirtualDispatchComponent_When_Analyzed_Then_IsLeftUnwoven()
        {
            // Act + Assert
            Assert.That(IsWoven(LoadMethod(nameof(VirtualDispatchComponent))), Is.False,
                "A virtual call on a non-sealed class in a Velvet-referencing assembly may dispatch to an"
                + " override that composes a hook, so the weaver bails");
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

        [Test]
        public void Given_BailedPropsComponent_When_FirstRender_Then_ProducesVisibleOutput()
        {
            // Act
            using var mounted = V.Mount(_root,
                V.Component(PropsComponent, new GreetProps("hello"), key: "props"));

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("hello"), "A bailed props component still renders normally");
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
        public void Given_BailedInterfaceDispatchComponent_When_FirstRender_Then_ProducesVisibleOutput()
        {
            // Act — the service field is left null, so the null-propagated call yields the fallback text.
            using var mounted = V.Mount(_root, V.Component(InterfaceDispatchComponent, key: "iface"));

            // Assert
            Assert.That(_root.Q<Label>()?.text, Is.EqualTo("none0"),
                "A bailed interface-dispatch component still renders normally");
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

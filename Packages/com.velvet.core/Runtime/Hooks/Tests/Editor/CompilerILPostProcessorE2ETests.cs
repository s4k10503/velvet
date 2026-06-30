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
    /// section, props prepended to the deps array, a captured <c>UseContext</c> value, a safe void effect hook
    /// alongside a value hook, and the stable references from <c>UseRef</c> / <c>UseService</c> are all
    /// analyzable shapes.</item>
    /// <item>A body the weaver cannot prove correct is left unwoven (graceful bailout): no hook to key a cache
    /// on, a props-only body, a discarded hook value, a whole-tuple capture (compared structurally, not by
    /// reference, so a fresh-but-equal record would be a stale hit), a void-only body (empty deps would freeze
    /// it on an unconditional hit), and a body that reaches the suspend-unsafe <c>Use</c> hook or
    /// <c>UseMutation</c> — directly or transitively through a custom hook.</item>
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
        // is on the value allow-list, so the body is woven.
        [Component]
        public static VNode UseServiceComponent()
        {
            var service = Hooks.UseService<IWovenService>();
            return V.Label(text: service.Name());
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

        private static System.Func<Instruction, bool> IsHookCallTo(string methodName) => instr =>
            instr.OpCode == OpCodes.Call
            && instr.Operand is MethodReference methodRef
            && methodRef.DeclaringType.FullName == typeof(Hooks).FullName
            && methodRef.Name == methodName;

        #endregion
    }
}

using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// A memoized (<c>[Component(Memoize = true)]</c>) forwardRef component bails a parent-driven re-render on
    /// shallow-equal props. But the forwarded ref is part of the component's identity, not its props: when the
    /// parent passes a DIFFERENT <see cref="Ref{T}"/> instance (equal — here null — props), the child must still
    /// re-run <see cref="Hooks.UseImperativeHandle"/> so the NEW ref receives the handle. Otherwise the props-bail
    /// swallows the ref change and the new ref is never populated. GWT, one assert.
    /// </summary>
    [TestFixture]
    internal sealed class MemoizedForwardRefIdentityTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_refA = new Ref<IHandle>();
            s_refB = new Ref<IHandle>();
            s_setUseB = default;
        }

        private interface IHandle { }

        private sealed class Handle : IHandle { }

        private static Ref<IHandle> s_refA;
        private static Ref<IHandle> s_refB;
        private static StateUpdater<bool> s_setUseB;

        // Memoized so a parent re-render with equal (null) props bails its body — the ref change must override that.
        [Component(Memoize = true)]
        private static VNode MemoHandleChild()
        {
            var handleRef = Hooks.ForwardedRef<IHandle>();
            Hooks.UseImperativeHandle(handleRef, () => new Handle(), System.Array.Empty<object>());
            return V.Label(text: "child");
        }

        [Component]
        private static VNode MemoRefParent()
        {
            var (useB, setUseB) = Hooks.UseState(false);
            s_setUseB = setUseB;
            var currentRef = useB ? s_refB : s_refA;
            return V.Div(children: new VNode[]
            {
                V.Button(name: "swap-ref", onClick: () => setUseB.Invoke(_ => true)),
                V.Component<IHandle>(MemoHandleChild, currentRef, key: "child"),
            });
        }

        [Test]
        public void Given_AMemoizedForwardRefChild_When_TheParentPassesADifferentRefWithEqualProps_Then_TheNewRefIsPopulated()
        {
            // Arrange — a memoized forwardRef child whose handle landed in ref A.
            using var mounted = V.Mount(_root, V.Component(MemoRefParent, key: "memo-ref"));
            Assume.That(s_refA.Current, Is.Not.Null, "Precondition: the child's handle populated ref A on mount");
            Assume.That(s_refB.Current, Is.Null, "Precondition: ref B is empty before the swap");

            // Act — the parent re-renders passing a DIFFERENT ref instance (ref B); props are unchanged (null).
            _root.Q<Button>("swap-ref").SimulateClick();
            mounted.FlushStateForTest();

            // Assert — the ref change re-runs the imperative handle, so the new ref receives the handle.
            Assert.That(s_refB.Current, Is.Not.Null,
                "A changed forwarded ref must re-run UseImperativeHandle even when the memoized props bail.");
        }
    }
}

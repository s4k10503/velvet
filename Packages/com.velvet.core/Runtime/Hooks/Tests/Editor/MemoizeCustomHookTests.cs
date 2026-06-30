using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how the <c>[Component(Memoize = true)]</c> props-bail gate composes with a Store subscription
    /// taken through a custom hook (a plain static method that internally calls Velvet hooks).
    /// <list type="bullet">
    /// <item>The host renders once on mount and produces the value derived by the custom hook.</item>
    /// <item>The props-bail gate only suppresses parent-driven re-renders on shallow-equal props; it does not
    /// suppress a re-render demanded by a hook. When the subscribed Store changes, the subscription marks the
    /// host fiber dirty, so the host re-renders and observes the new derived value even though its props are
    /// unchanged.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class MemoizeCustomHookTests
    {
        private sealed class IntStore : Store<int>
        {
            public IntStore(int initial) : base(initial) { }
            public void PublicSet(int next) => SetState(_ => next);
            protected override void ResetCore() => SetState(_ => 0);
        }

        private VisualElement _root = null!;
        private static IntStore s_store = null!;
        private static int s_renderCount;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = new IntStore(0);
            s_renderCount = 0;
        }

        // Custom hook: a plain static method composing Hooks.UseStore. The subscription it sets up marks the
        // host fiber dirty when the Store changes, which is what drives the re-render.
        private static int UseDoubledValue(IntStore store)
            => Hooks.UseStore(store, s => s * 2);

        [Component(Memoize = true)]
        private static VNode MemoizedCustomHookHostRender()
        {
            var doubled = UseDoubledValue(s_store);
            s_renderCount++;
            return V.Label(name: "host", text: doubled.ToString());
        }

        [Test]
        public void Given_MemoizedHost_When_FirstRender_Then_ProducesValueFromCustomHook()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(MemoizedCustomHookHostRender, key: "host"));

            // Assert
            Assert.That(_root.Q<Label>(name: "host")?.text, Is.EqualTo("0"),
                "The custom hook derives the displayed value (store 0, doubled = 0)");
        }

        [Test]
        public void Given_MemoizedHost_When_FirstRender_Then_RendersExactlyOnce()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(MemoizedCustomHookHostRender, key: "host"));

            // Assert
            Assert.That(s_renderCount, Is.EqualTo(1));
        }

        [Test]
        public void Given_MemoizedHost_When_SubscribedStoreChanges_Then_ObservesNewDerivedValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(MemoizedCustomHookHostRender, key: "host"));
            Assume.That(_root.Q<Label>(name: "host")?.text, Is.EqualTo("0"), "Precondition: initial derived value is 0");

            // Act
            s_store.PublicSet(7);
            mounted.FlushStateForTest();

            // Assert — the subscription drives a re-render despite the props-bail (store 7, doubled = 14)
            Assert.That(_root.Q<Label>(name: "host")?.text, Is.EqualTo("14"),
                "A Store change observed via the custom hook re-renders the memoized host");
        }

        [Test]
        public void Given_MemoizedHost_When_SubscribedStoreChanges_Then_HostReRenders()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(MemoizedCustomHookHostRender, key: "host"));
            Assume.That(s_renderCount, Is.EqualTo(1), "Precondition: only the initial render has happened");

            // Act
            s_store.PublicSet(7);
            mounted.FlushStateForTest();

            // Assert — the props-bail gate does not suppress a hook-demanded re-render
            Assert.That(s_renderCount, Is.GreaterThanOrEqualTo(2),
                "Marking the fiber dirty forces a re-render past the props-bail gate");
        }
    }
}

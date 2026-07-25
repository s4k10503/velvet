using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how the <c>[Component(Memoize = true)]</c> props-bail gate interacts with other render-triggering
    /// mechanisms: a custom hook's Store subscription, an Error Boundary catching a child exception, and a
    /// Suspense boundary's fallback-to-resolved transition.
    /// <list type="bullet">
    /// <item>The props-bail gate only suppresses a parent-driven re-render when props are shallow-equal; it never
    /// suppresses a re-render demanded by a hook, so a Store change observed through a custom hook still
    /// re-renders the memoized host with the newly derived value.</item>
    /// <item>Combining <c>IsErrorBoundary = true</c> with <c>Memoize = true</c> registers the same method as both
    /// a props-bail gate and an Error Boundary; the boundary still catches a child render exception and its
    /// fallback still receives the thrown exception.</item>
    /// <item>A memoized Suspense boundary with no props never caches a stale subtree, so once the awaited
    /// resource resolves, the subtree is re-walked and the resolved child replaces the fallback.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class MemoizePropsBailInteractionTests
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
        private static Func<CancellationToken, UniTask<string>> s_factory;
        private static UniTaskCompletionSource<string> s_source;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = new IntStore(0);
            s_renderCount = 0;
            ComboState.Reset();
            s_source = new UniTaskCompletionSource<string>();
            s_factory = _ => s_source.Task;
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

        [Component]
        private static VNode SuspendedAsyncChildRender()
        {
            var data = Hooks.Use(s_factory);
            return V.Label(name: "loaded", text: data);
        }

        // Memoized boundary with no props: the props-bail never triggers a stale cached subtree, so the Suspense
        // commit's re-render is free to re-walk the children once the resource resolves.
        [Component(Memoize = true)]
        private static VNode MemoizedSuspenseHostRender()
            => V.Suspense(
                fallback: V.Label(name: "loading", text: "loading..."),
                children: new VNode[] { V.Component(SuspendedAsyncChildRender, key: "child") });

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

        [Test]
        public void Given_BoundaryWithMemoize_When_ChildThrows_Then_FallbackFires()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(CombinedBoundary.Render, key: "boundary"));
            Assume.That(ComboState.FallbackShown, Is.False, "Precondition: the normal child rendered without firing fallback");
            Assume.That(ComboState.SetTick, Is.Not.Null, "Precondition: the child rendered and wired SetTick");

            // Act
            ComboState.ThrowOnNextRender = true;
            ComboState.SetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(ComboState.FallbackShown, Is.True,
                "The boundary still catches the child exception even with the props-bail flag set");
        }

        [Test]
        public void Given_BoundaryWithMemoize_When_ChildThrows_Then_FallbackReceivesTheThrownException()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(CombinedBoundary.Render, key: "boundary"));
            Assume.That(ComboState.SetTick, Is.Not.Null, "Precondition: the child rendered and wired SetTick");

            // Act
            ComboState.ThrowOnNextRender = true;
            ComboState.SetTick.Invoke(1);
            mounted.FlushStateForTest();
            Assume.That(ComboState.FallbackShown, Is.True, "Precondition: the fallback fired");

            // Assert
            Assert.That(ComboState.LastCaughtMessage, Is.EqualTo("Combo throw"),
                "The fallback receives the exact exception thrown by the child");
        }

        [Test]
        public void Given_MemoizedBoundary_When_ChildSuspends_Then_DisplaysFallback()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(MemoizedSuspenseHostRender, key: "host"));

            // Assert
            Assert.That(_root.Q<Label>(name: "loading"), Is.Not.Null,
                "While the child suspends, the boundary displays its fallback");
        }

        [Test]
        public void Given_MemoizedBoundary_When_ChildSuspends_Then_DoesNotDisplayChild()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(MemoizedSuspenseHostRender, key: "host"));

            // Assert
            Assert.That(_root.Q<Label>(name: "loaded"), Is.Null,
                "The suspended child is not rendered while awaiting");
        }

        [Test]
        public void Given_MemoizedBoundary_When_ResourceResolves_Then_FallbackIsRemoved()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(MemoizedSuspenseHostRender, key: "host"));
            Assume.That(_root.Q<Label>(name: "loading"), Is.Not.Null, "Precondition: the fallback is shown while suspended");

            // Act
            s_source.TrySetResult("ready");
            mounted.FlushStateForTest();

            // Assert — the props-bail caches nothing, so the subtree re-walks and drops the fallback
            Assert.That(_root.Q<Label>(name: "loading"), Is.Null,
                "After the resource resolves, the Suspense subtree is re-walked and the fallback is removed");
        }

        [Test]
        public void Given_MemoizedBoundary_When_ResourceResolves_Then_ChildDisplaysResolvedValue()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(MemoizedSuspenseHostRender, key: "host"));
            Assume.That(_root.Q<Label>(name: "loaded"), Is.Null, "Precondition: the child is not yet rendered");

            // Act
            s_source.TrySetResult("ready");
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Q<Label>(name: "loaded")?.text, Is.EqualTo("ready"),
                "After resolve, the child renders with the resolved value");
        }
    }

    internal static class ComboState
    {
        public static bool FallbackShown;
        public static string LastCaughtMessage;
        public static bool ThrowOnNextRender;
        public static Action<int> SetTick;

        public static void Reset()
        {
            FallbackShown = false;
            LastCaughtMessage = null;
            ThrowOnNextRender = false;
            SetTick = null;
        }
    }

    internal static class CombinedBoundary
    {
        [Component(IsErrorBoundary = true, Memoize = true)]
        public static VNode Render()
        {
            Hooks.UseFallback(ex =>
            {
                ComboState.FallbackShown = true;
                ComboState.LastCaughtMessage = ex.Message;
                return V.Label(text: "error");
            });
            return V.Component(ComboChildRenderer.Render, key: "child");
        }
    }

    internal static class ComboChildRenderer
    {
        [Component]
        public static VNode Render()
        {
            var (_, setTick) = Hooks.UseState(0);
            ComboState.SetTick = setTick;
            if (ComboState.ThrowOnNextRender) throw new InvalidOperationException("Combo throw");
            return V.Label(text: "ok");
        }
    }
}

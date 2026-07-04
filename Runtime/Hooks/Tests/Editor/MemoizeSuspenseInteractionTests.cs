using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a Suspense boundary annotated with <c>[Component(Memoize = true)]</c> drives the
    /// fallback → children transition normally.
    /// <list type="bullet">
    /// <item>While the suspended child is awaiting, the boundary displays its fallback and not the child.</item>
    /// <item>The props-bail gate never caches the boundary's body, so once the awaited resource resolves the
    /// Suspense subtree is re-walked and the resolved child replaces the fallback, displaying the resolved
    /// value.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class MemoizeSuspenseInteractionTests
    {
        private VisualElement _root;
        private static Func<CancellationToken, UniTask<string>> s_factory;
        private static UniTaskCompletionSource<string> s_source;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_source = new UniTaskCompletionSource<string>();
            s_factory = _ => s_source.Task;
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
}

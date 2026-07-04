using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that error-boundary identity resolves through <see cref="ComponentMethodRegistry"/> even when the
    /// boundary component is declared inside a closed generic class.
    /// <list type="bullet">
    /// <item>A boundary declared in a closed generic class catches a descendant's render exception, because the
    /// registry rebuilds the open-form lookup key when the live type name carries a type-argument suffix.</item>
    /// <item>A boundary declared in a type nested inside a closed generic class likewise catches, because the
    /// registry walks the declaring chain to rebuild the open form when the live type name is null.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. The throw is
    /// driven by a child-side <c>setTick</c> setter so it fires on an update.
    /// </remarks>
    [TestFixture]
    internal sealed class GenericClassErrorBoundaryTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_fallbackShown = false;
            s_throwOnNextRender = false;
            s_setTick = null;
        }

        [Test]
        public void Given_BoundaryInClosedGenericClass_When_ChildThrows_Then_FallbackFires()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(GenericBoundary<int>.Render, key: "boundary"));
            Assume.That(s_fallbackShown, Is.False, "Precondition: the initial mount renders the child without fallback");
            Assume.That(s_setTick, Is.Not.Null, "Precondition: the child wired its setter on the initial mount");
            s_throwOnNextRender = true;

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_fallbackShown, Is.True,
                "A boundary in a closed generic class resolves through the registry's open-form key and catches");
        }

        [Test]
        public void Given_BoundaryInNestedTypeOfClosedGeneric_When_ChildThrows_Then_FallbackFires()
        {
            // Arrange — the live type name of a type nested in a closed generic is null, so the registry must
            // walk the declaring chain to rebuild the open-form key.
            using var mounted = V.Mount(_root, V.Component(GenericOuter<int>.NestedBoundary.Render, key: "boundary"));
            Assume.That(s_fallbackShown, Is.False, "Precondition: the initial mount renders the child without fallback");
            Assume.That(s_setTick, Is.Not.Null, "Precondition: the child wired its setter on the initial mount");
            s_throwOnNextRender = true;

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_fallbackShown, Is.True,
                "A boundary nested inside a closed generic resolves through the chain-walking open-form key and catches");
        }

        private static bool s_fallbackShown;
        private static bool s_throwOnNextRender;
        private static Action<int> s_setTick;

        private static class GenericBoundary<T>
        {
            [Component(IsErrorBoundary = true)]
            public static VNode Render()
            {
                Hooks.UseFallback(_ =>
                {
                    s_fallbackShown = true;
                    return V.Label(text: "error");
                });
                return V.Component(ChildRender, key: "child");
            }
        }

        private static class GenericOuter<T>
        {
            public static class NestedBoundary
            {
                [Component(IsErrorBoundary = true)]
                public static VNode Render()
                {
                    Hooks.UseFallback(_ =>
                    {
                        s_fallbackShown = true;
                        return V.Label(text: "error");
                    });
                    return V.Component(NestedChildRender, key: "child");
                }
            }
        }

        [Component]
        private static VNode ChildRender()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            if (s_throwOnNextRender) throw new InvalidOperationException("Generic boundary throw");
            return V.Label(text: "ok");
        }

        [Component]
        private static VNode NestedChildRender()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            if (s_throwOnNextRender) throw new InvalidOperationException("Nested generic boundary throw");
            return V.Label(text: "ok");
        }
    }
}

using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies parent-to-child ref forwarding via <c>componentRef:</c> together with
    /// <see cref="Hooks.UseImperativeHandle"/>.
    /// <list type="bullet">
    /// <item>A child that exposes a handle through <see cref="Hooks.UseImperativeHandle"/> populates the
    /// parent-forwarded ref synchronously by the time the mount returns, so the parent can invoke the
    /// handle immediately.</item>
    /// <item>The handle factory runs in the layout phase, after every child ref in the subtree has been
    /// attached, so a factory that reads a descendant element ref observes the already-attached element.</item>
    /// <item>A deps change on re-render re-invokes the factory, and the re-invoked factory still observes the
    /// attached child element ref.</item>
    /// <item>Unmounting the parent unmounts the child and null-clears the forwarded ref.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. Per-region
    /// static fields are reset together in <see cref="SetUp"/> via <c>Reset{Region}()</c> helpers; the deps-change
    /// region resets inside its own test because it tracks a call count that the test asserts from zero.
    /// </remarks>
    [TestFixture]
    internal sealed class ComponentRefForwardingTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetParent();
            ResetChildRefForwarding();
        }

        [Test]
        public void Given_ChildExposesHandle_When_Mounted_Then_ForwardedRefIsPopulated()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(ParentRender, key: "parent"));

            // Assert
            Assert.That(s_parentChildHandleRef.Current, Is.Not.Null,
                "The forwarded ref holds the child's handle by the time Mount returns");
        }

        [Test]
        public void Given_ForwardedHandle_When_ParentInvokesIt_Then_HandleReceivesTheCall()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ParentRender, key: "parent"));
            Assume.That(s_parentChildHandleRef.Current, Is.Not.Null, "Precondition: the handle is populated after mount");

            // Act
            s_parentChildHandleRef.Current.Focus();

            // Assert
            Assert.That(s_parentChildHandleRef.Current.FocusCallCount, Is.EqualTo(1),
                "Invoking the forwarded handle reaches the child's implementation");
        }

        [Test]
        public void Given_FactoryReadsDescendantElementRef_When_Mounted_Then_ObservesAttachedElement()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(ChildRefForwardingParentRender, key: "parent"));

            // Assert
            Assert.That(s_observedChildElementAtFactory, Is.Not.Null,
                "The layout-phase factory runs after subtree refs are attached, so it observes the child element");
        }

        [Test]
        public void Given_FactoryReadsDescendantElementRef_When_Mounted_Then_ParentRefHoldsBuiltHandle()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(ChildRefForwardingParentRender, key: "parent"));

            // Assert
            Assert.That(s_parentHandleRefForChildAttach.Current, Is.Not.Null,
                "The parent ref is populated with the handle that wraps the child element");
        }

        [Test]
        public void Given_DepsChangeOnReRender_When_Flushed_Then_FactoryReInvokes()
        {
            // Arrange
            ResetDepsChange();
            using var mounted = V.Mount(_root, V.Component(DepsChangeParentRender, key: "parent"));
            Assume.That(s_depsChangeFactoryCallCount, Is.EqualTo(1), "Precondition: the factory ran once on mount");

            // Act
            s_depsChangeSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_depsChangeFactoryCallCount, Is.EqualTo(2),
                "A deps change (0 to 1) re-invokes the layout-phase factory");
        }

        [Test]
        public void Given_DepsChangeReInvokesFactory_When_Flushed_Then_FactoryStillObservesChildElement()
        {
            // Arrange
            ResetDepsChange();
            using var mounted = V.Mount(_root, V.Component(DepsChangeParentRender, key: "parent"));
            Assume.That(s_depsChangeParentRef.Current?.Element, Is.Not.Null,
                "Precondition: the factory observed the attached child element on first commit");

            // Act
            s_depsChangeSetTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_depsChangeParentRef.Current?.Element, Is.Not.Null,
                "The re-invoked factory still observes the attached child element ref through the flush commit path");
        }

        [Test]
        public void Given_MountedParentWithChildRef_When_ParentUnmounted_Then_ChildRefIsNullCleared()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ParentRender, key: "parent"));
            Assume.That(s_parentChildHandleRef.Current, Is.Not.Null, "Precondition: the ref is populated before unmount");

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(s_parentChildHandleRef.Current, Is.Null,
                "Unmounting the parent unmounts the child and null-clears the forwarded ref");
        }

        private interface IFocusable
        {
            void Focus();
            int FocusCallCount { get; }
        }

        private sealed class FocusableHandle : IFocusable
        {
            public int FocusCallCount { get; private set; }
            public void Focus() => FocusCallCount++;
        }

        #region Parent component (forwards Ref<IFocusable> to child via componentRef:)

        private static Ref<IFocusable> s_parentChildHandleRef;

        private static void ResetParent()
        {
            s_parentChildHandleRef = new Ref<IFocusable>();
        }

        [Component]
        private static VNode ParentRender()
            => V.Component(ChildRender, componentRef: s_parentChildHandleRef, key: "child");

        [Component]
        private static VNode ChildRender()
        {
            var handleRef = Hooks.ForwardedRef<IFocusable>();
            Hooks.UseImperativeHandle(handleRef, () => new FocusableHandle(), Array.Empty<object>());
            return V.Label(text: "child");
        }

        #endregion

        #region Parent reads a descendant element ref inside UseImperativeHandle factory

        private interface IElementHandle
        {
            VisualElement Element { get; }
        }

        private sealed class ElementHandle : IElementHandle
        {
            public ElementHandle(VisualElement element) { Element = element; }
            public VisualElement Element { get; }
        }

        private static Ref<IElementHandle> s_parentHandleRefForChildAttach;
        private static Ref<VisualElement> s_childElementRef;
        private static VisualElement s_observedChildElementAtFactory;

        private static void ResetChildRefForwarding()
        {
            s_parentHandleRefForChildAttach = new Ref<IElementHandle>();
            s_childElementRef = new Ref<VisualElement>();
            s_observedChildElementAtFactory = null;
        }

        [Component]
        private static VNode ChildRefForwardingParentRender()
        {
            Hooks.UseImperativeHandle(
                s_parentHandleRefForChildAttach,
                () =>
                {
                    s_observedChildElementAtFactory = s_childElementRef.Current;
                    return new ElementHandle(s_childElementRef.Current);
                },
                Array.Empty<object>());
            return V.TextField(refCallback: s_childElementRef.SetElement);
        }

        #endregion

        #region Parent factory re-runs on deps change and observes child element ref

        private static int s_depsChangeFactoryCallCount;
        private static Action<int> s_depsChangeSetTick;
        private static Ref<IElementHandle> s_depsChangeParentRef;
        private static Ref<VisualElement> s_depsChangeChildElementRef;

        private static void ResetDepsChange()
        {
            s_depsChangeFactoryCallCount = 0;
            s_depsChangeSetTick = null;
            s_depsChangeParentRef = new Ref<IElementHandle>();
            s_depsChangeChildElementRef = new Ref<VisualElement>();
        }

        [Component]
        private static VNode DepsChangeParentRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_depsChangeSetTick = setTick;
            Hooks.UseImperativeHandle(
                s_depsChangeParentRef,
                () =>
                {
                    s_depsChangeFactoryCallCount++;
                    return new ElementHandle(s_depsChangeChildElementRef.Current);
                },
                new object[] { tick });
            return V.TextField(refCallback: s_depsChangeChildElementRef.SetElement);
        }

        #endregion
    }
}

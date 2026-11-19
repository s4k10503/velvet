using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies a ref callback written as an inline lambda, a new delegate at every render, the way React's
    /// <c>ref={el => register("t", el)}</c> is. React runs the old one's cleanup and the new one together in
    /// its commit, so a portal patched in that render still finds the id its target registered, and
    /// registering the same element again changes nothing for the portal. The cases count
    /// the declaring component's renders, since a portal that never finds its id asks for one render after
    /// another.
    /// </summary>
    internal sealed class InlineRefCallbackTests
    {
        private MountedTree _mounted;
        private VisualElement _root;
        private static int s_renders;

        [SetUp]
        public void SetUp()
        {
            RuntimeStateProbe.ClearPortalRegistry();
            _root = new VisualElement();
            s_renders = 0;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            RuntimeStateProbe.ClearPortalRegistry();
        }

        private static Action Register(string id, VisualElement element)
        {
            FiberPortalRegistry.Register(id, element);
            return () => FiberPortalRegistry.Unregister(id);
        }

        [Component]
        private static VNode DivTargetRender()
        {
            s_renders++;
            var (round, _) = Hooks.UseState(0);
            return V.Fragment(children: new VNode[]
            {
                // Captures round, so every render hands the container a new callback.
                V.Div(name: "container", refCallback: element =>
                {
                    _ = round;
                    return Register("inline-div", element);
                }),
                V.Portal("inline-div", children: new VNode[] { V.Div(name: "portal-child") }),
            });
        }

        [Component]
        private static VNode ScrollTargetRender()
        {
            s_renders++;
            var (round, _) = Hooks.UseState(0);
            return V.Fragment(children: new VNode[]
            {
                // Captures round, so every render hands the ScrollView a new callback.
                V.ScrollView(name: "scroll", refCallback: element =>
                {
                    _ = round;
                    return Register("inline-scroll", element);
                }),
                V.Portal("inline-scroll", children: new VNode[] { V.Div(name: "portal-child") }),
            });
        }

        [Test]
        public void Given_APortalIntoADivRegisteredFromAnInlineRef_When_TheTreeSettles_Then_ItsChildMountsAfterABoundedNumberOfRenders()
        {
            // Act
            _mounted = V.Mount(_root, V.Component(DivTargetRender, key: "host"));
            _mounted.FlushStateForTest();

            // Assert — the mount and the render the registration asks for.
            Assert.That(
                (_root.Q<VisualElement>("container")?.Q<VisualElement>("portal-child") != null, s_renders),
                Is.EqualTo((true, 2)));
        }

        [Test]
        public void Given_APortalIntoAScrollViewRegisteredFromAnInlineRef_When_TheTreeSettles_Then_ItsChildMountsAfterABoundedNumberOfRenders()
        {
            // Act
            _mounted = V.Mount(_root, V.Component(ScrollTargetRender, key: "host"));
            _mounted.FlushStateForTest();

            // Assert — the mount and the render the registration asks for.
            Assert.That(
                (_root.Q<VisualElement>("scroll")?.contentContainer.Q<VisualElement>("portal-child") != null, s_renders),
                Is.EqualTo((true, 2)));
        }
    }
}

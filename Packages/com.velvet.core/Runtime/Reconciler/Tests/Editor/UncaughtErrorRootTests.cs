using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what an error no error boundary catches does to the tree <see cref="V.Mount(VisualElement, VNode)"/>
    /// mounted. <see cref="UncaughtErrorHandlerTests"/> holds what it reports.
    /// <list type="bullet">
    /// <item>A render error, on mount or on update, and a passive effect's error unmount the whole tree: the target
    /// is left without its elements and the effects of the components that unmount are cleaned up.</item>
    /// <item>So does an error the reconcile of the tree the root was handed raises below no component.</item>
    /// <item>A click handler's error unmounts nothing.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class UncaughtErrorRootTests
    {
        internal const string RenderMessage = "uncaught render boom";
        internal const string CleanupMessage = "uncaught cleanup boom";

        internal static bool s_throws;
        internal static int s_cleanups;
        internal static StateUpdater<int> s_setTick;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetComponents();
        }

        internal static void ResetComponents()
        {
            s_throws = false;
            s_cleanups = 0;
            s_setTick = default;
        }

        [Test]
        public void Given_NoBoundary_When_ARenderThrowsOnMount_Then_TheTargetHoldsNothing()
        {
            // Arrange
            s_throws = true;
            LogAssert.Expect(LogType.Exception, $"InvalidOperationException: {RenderMessage}");

            // Act
            using var mounted = V.Mount(_root, V.Component(ThrowerBesideProbeRender, key: "host"));

            // Assert
            Assert.That(_root.childCount, Is.EqualTo(0));
        }

        [Test]
        public void Given_NoBoundary_When_AChildsUpdateRenderThrows_Then_ASiblingsEffectIsCleanedUp()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ThrowerBesideProbeRender, key: "host"));
            mounted.FlushEffectsForTest();
            LogAssert.Expect(LogType.Exception, $"InvalidOperationException: {RenderMessage}");
            s_throws = true;

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_cleanups, Is.EqualTo(1));
        }

        [Test]
        public void Given_NoBoundary_When_APassiveEffectThrows_Then_TheTargetHoldsNothing()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Div(children: new VNode[]
            {
                V.Component(ThrowingEffectRender, key: "effect"),
            }));
            LogAssert.Expect(LogType.Exception, $"InvalidOperationException: {RenderMessage}");

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(_root.childCount, Is.EqualTo(0));
        }

        [Test]
        public void Given_AMountedTree_When_TheRootRendersANodeTheReconcileRefuses_Then_TheTargetHoldsNothing()
        {
            // Arrange — a node with no body is refused while the root's own rows are reconciled, below no component
            using var mounted = V.Mount(_root, V.Div(name: "kept"));
            LogAssert.Expect(LogType.Exception,
                new System.Text.RegularExpressions.Regex("ComponentNode.Body must not be null"));

            // Act
            mounted.Render(V.Div(name: "kept", children: new VNode[]
            {
                new ComponentNode { Body = null, Identity = "no-method" },
            }));

            // Assert
            Assert.That(_root.childCount, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): a click handler's error unmounts nothing on the base, and must not after it.
        // Routing the handler's exception to the boundary search reddens this.
        [Test]
        public void Given_NoBoundary_When_AClickHandlerThrows_Then_TheTreeStaysMounted()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Div(children: new VNode[]
            {
                V.Button(name: "button", onClick: () => throw new InvalidOperationException(RenderMessage)),
            }));

            // Act
            try
            {
                _root.Q<Button>("button").SimulateClick();
            }
            catch (InvalidOperationException exception) when (exception.Message == RenderMessage)
            {
            }

            // Assert
            Assert.That(_root.Q<Button>("button"), Is.Not.Null);
        }

        [Component(Compiler = false)]
        private static VNode ThrowingRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            if (s_throws) throw new InvalidOperationException(RenderMessage);
            return V.Label(text: "thrower:" + tick);
        }

        [Component(Compiler = false)]
        private static VNode CleanupProbeRender()
        {
            Hooks.UseEffect((Func<Action>)(() => () => s_cleanups++), Array.Empty<object>());
            return V.Label(text: "probe");
        }

        [Component(Compiler = false)]
        internal static VNode ThrowingCleanupRender()
        {
            Hooks.UseEffect((Func<Action>)(() => () => throw new InvalidOperationException(CleanupMessage)),
                Array.Empty<object>());
            return V.Label(text: "cleanup");
        }

        [Component(Compiler = false)]
        private static VNode ThrowingEffectRender()
        {
            Hooks.UseEffect((Func<Action>)(() => throw new InvalidOperationException(RenderMessage)),
                Array.Empty<object>());
            return V.Label(text: "effect");
        }

        [Component(Compiler = false)]
        internal static VNode ThrowerBesideProbeRender()
            => V.Div(children: new VNode[]
            {
                V.Component(CleanupProbeRender, key: "probe"),
                V.Component(ThrowingRender, key: "thrower"),
            });

        [Component(Compiler = false)]
        internal static VNode ThrowerBesideThrowingCleanupRender()
            => V.Div(children: new VNode[]
            {
                V.Component(ThrowingCleanupRender, key: "cleanup"),
                V.Component(ThrowingRender, key: "thrower"),
            });
    }
}

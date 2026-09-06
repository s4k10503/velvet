using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the inline-mount contract for a function component placed under a container node (Element /
    /// Motion / Provider / Portal).
    /// <list type="bullet">
    /// <item>The component contributes its rendered output as a direct sibling of the parent container — never
    /// wrapped in the container the ComponentNode single-instance fallback uses.</item>
    /// <item>N keyed components under one container produce N direct sibling elements in render order, and each
    /// of those elements is the component's own output rather than a wrapper holding it.</item>
    /// <item>An inline-expanded output carries no Provider wrapper class.</item>
    /// <item>A Provider inline-expands within a container's children: its descendant components become direct
    /// siblings of that container, not nested inside a Provider wrapper element.</item>
    /// <item>Components under a Portal inline-expand into the portal target in order; multiple portals to a
    /// shared target append after one another, preserving cross-portal order.</item>
    /// <item>Each component body runs exactly once per mount.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ComponentInlineMountTests : ReconcilerTestFixture
    {
        private static int s_buttonInstanceCounter;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            s_buttonInstanceCounter = 0;
        }

        private readonly record struct ButtonProps(int Index, string Label);

        [Component]
        private static VNode RenderButton(ButtonProps p)
        {
            s_buttonInstanceCounter++;
            return V.Button(name: $"btn-{p.Index}", text: p.Label, className: "swatch");
        }

        [Component]
        private static VNode RenderText(int index)
        {
            return V.Label(name: $"label-{index}", text: $"item-{index}");
        }

        [Test]
        public void Given_NComponentsAsElementChildren_When_Mounted_Then_EachOutputIsADirectChild()
        {
            // Arrange
            var children = ElementHostWithThreeButtons();

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), children);

            // Assert
            var host = Root.Q(name: "host");
            Assert.That(host.childCount, Is.EqualTo(3),
                "Each V.Component contributes its rendered Button directly — three direct children, not one wrapper");
        }

        // GREEN_ON_BASE(characterization): the base already puts a Component's own output in the container.
        // This case is what still separates that from a wrapper holding the output, now that the
        // wrapper-mount container carries no absolute position for a sibling case to read.
        [Test]
        public void Given_NComponentsAsElementChildren_When_Mounted_Then_EachDirectChildIsTheComponentOutput()
        {
            // Arrange
            var children = ElementHostWithThreeButtons();

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), children);

            // Assert
            var host = Root.Q(name: "host");
            var names = new[] { host.ElementAt(0).name, host.ElementAt(1).name, host.ElementAt(2).name };
            Assert.That(names, Is.EqualTo(new[] { "btn-0", "btn-1", "btn-2" }),
                "Each direct child is the Button emitted by the component, not a wrapper holding it");
        }

        [Test]
        public void Given_NComponentsAsElementChildren_When_Mounted_Then_NoOutputCarriesAProviderWrapperClass()
        {
            // Arrange
            var children = ElementHostWithThreeButtons();

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), children);

            // Assert
            var host = Root.Q(name: "host");
            var anyWrapped = false;
            for (var i = 0; i < host.childCount; i++)
            {
                if (host.ElementAt(i).ClassListContains(FiberNodeFactory.ContextProviderClassName)) anyWrapped = true;
            }
            Assert.That(anyWrapped, Is.False, "No Provider wrapper class appears on inline-expanded Component output");
        }

        [Test]
        public void Given_NComponentsAsElementChildren_When_Mounted_Then_EachBodyRanExactlyOnce()
        {
            // Arrange
            var children = ElementHostWithThreeButtons();

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), children);

            // Assert
            Assert.That(s_buttonInstanceCounter, Is.EqualTo(3), "Each of the three component bodies ran exactly once on mount");
        }

        [Test]
        public void Given_NComponentsAsMotionChildren_When_Mounted_Then_EachOutputIsADirectSibling()
        {
            // Arrange
            var children = new VNode[]
            {
                V.Motion(
                    "motion-host",
                    children: new VNode[]
                    {
                        V.Component(RenderButton, new ButtonProps(0, "A"), key: "k0"),
                        V.Component(RenderButton, new ButtonProps(1, "B"), key: "k1"),
                    }),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), children);

            // Assert
            var host = Root.ElementAt(0);
            Assert.That(
                (host.childCount, host.ElementAt(0).name, host.ElementAt(1).name),
                Is.EqualTo((2, "btn-0", "btn-1")),
                "MotionNode children render like ElementNode children — N components produce N direct VE siblings");
        }

        [Test]
        public void Given_NComponentsInsideProvider_When_Mounted_Then_TheyBecomeDirectHostSiblings()
        {
            // Arrange
            var children = new VNode[]
            {
                V.Div(name: "host", children: new VNode[]
                {
                    V.Provider(ComponentContext<string>.Create("default"), "scoped", new VNode[]
                    {
                        V.Component(RenderButton, new ButtonProps(0, "A"), key: "k0"),
                        V.Component(RenderButton, new ButtonProps(1, "B"), key: "k1"),
                    }),
                }),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), children);

            // Assert
            var host = Root.Q(name: "host");
            Assert.That(
                (host.childCount, host.ElementAt(0).name, host.ElementAt(1).name),
                Is.EqualTo((2, "btn-0", "btn-1")),
                "Provider inline-expands within the host's children — descendant Components become direct host siblings");
        }

        [Test]
        public void Given_NComponentsInsidePortal_When_Mounted_Then_TargetReceivesNDirectSiblingsInOrder()
        {
            // Arrange
            var target = new VisualElement { name = "portal-target" };
            FiberPortalRegistry.Register("test-portal", target);
            try
            {
                var children = new VNode[]
                {
                    V.Portal("test-portal", children: new VNode[]
                    {
                        V.Component(RenderText, 0, key: "t0"),
                        V.Component(RenderText, 1, key: "t1"),
                        V.Component(RenderText, 2, key: "t2"),
                    }),
                };

                // Act
                Reconciler.Reconcile(Root, Array.Empty<VNode>(), children);

                // Assert
                var names = new[]
                {
                    target.ElementAt(0).name, target.ElementAt(1).name, target.ElementAt(2).name,
                };
                Assert.That(
                    (target.childCount, names[0], names[1], names[2]),
                    Is.EqualTo((3, "label-0", "label-1", "label-2")),
                    "PortalNode children inline-expand into the target as direct siblings in order");
            }
            finally
            {
                FiberPortalRegistry.Unregister("test-portal");
            }
        }

        [Test]
        public void Given_TwoPortalsToSharedTarget_When_Mounted_Then_SecondAppendsAfterFirstPreservingOrder()
        {
            // Arrange
            var target = new VisualElement { name = "shared-target" };
            FiberPortalRegistry.Register("shared", target);
            try
            {
                var children = new VNode[]
                {
                    V.Portal("shared", key: "portal-1", children: new VNode[]
                    {
                        V.Component(RenderText, 0, key: "p1-0"),
                        V.Component(RenderText, 1, key: "p1-1"),
                    }),
                    V.Portal("shared", key: "portal-2", children: new VNode[]
                    {
                        V.Component(RenderText, 2, key: "p2-0"),
                        V.Component(RenderText, 3, key: "p2-1"),
                    }),
                };

                // Act
                Reconciler.Reconcile(Root, Array.Empty<VNode>(), children);

                // Assert
                var names = new[]
                {
                    target.ElementAt(0).name, target.ElementAt(1).name,
                    target.ElementAt(2).name, target.ElementAt(3).name,
                };
                Assert.That(
                    (target.childCount, names[0], names[1], names[2], names[3]),
                    Is.EqualTo((4, "label-0", "label-1", "label-2", "label-3")),
                    "Each Portal contributes its inline-expanded Components; Portal-2 appends after Portal-1's range");
            }
            finally
            {
                FiberPortalRegistry.Unregister("shared");
            }
        }

        [Test]
        public void Given_ListOfComponentsInHStack_When_Mounted_Then_EachIsADirectSibling()
        {
            // Arrange
            var children = HStackOfSevenSwatches();

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), children);

            // Assert
            var hstack = Root.Q(name: "hstack");
            Assert.That(hstack.childCount, Is.EqualTo(7),
                "V.List of 7 V.Component entries produces 7 direct sibling VEs — not 7 stacked wrappers");
        }

        // GREEN_ON_BASE(characterization): the base already puts each V.List entry's own output in the row.
        // It replaces a case that read the wrapper's absolute position, which this change takes away, and
        // the sibling case counting children is satisfied by as many wrappers.
        [Test]
        public void Given_ListOfComponentsInHStack_When_Mounted_Then_EachDirectChildIsTheComponentOutput()
        {
            // Arrange
            var children = HStackOfSevenSwatches();

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), children);

            // Assert
            var hstack = Root.Q(name: "hstack");
            var names = new string[hstack.childCount];
            for (var i = 0; i < hstack.childCount; i++)
            {
                names[i] = hstack.ElementAt(i).name;
            }
            Assert.That(string.Join(",", names), Is.EqualTo("btn-0,btn-1,btn-2,btn-3,btn-4,btn-5,btn-6"),
                "Each direct child is the Button the V.List entry emitted, not a wrapper holding it");
        }

        private VNode[] ElementHostWithThreeButtons()
            => new VNode[]
            {
                V.Div(
                    name: "host",
                    children: new VNode[]
                    {
                        V.Component(RenderButton, new ButtonProps(0, "A"), key: "k0"),
                        V.Component(RenderButton, new ButtonProps(1, "B"), key: "k1"),
                        V.Component(RenderButton, new ButtonProps(2, "C"), key: "k2"),
                    }),
            };

        private VNode[] HStackOfSevenSwatches()
        {
            var data = new[] { "red", "green", "blue", "yellow", "purple", "orange", "cyan" };
            return new VNode[]
            {
                V.Div(
                    name: "hstack",
                    children: V.List(data, (label, i) => $"swatch-{i}", (label, i) =>
                        V.Component(RenderButton, new ButtonProps(i, label), key: $"swatch-{i}"))),
            };
        }
    }
}

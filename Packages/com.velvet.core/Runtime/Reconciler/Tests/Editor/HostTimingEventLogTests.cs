using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins where a commit's host writes land against its layout effects, as one <see cref="EventLog"/> string per
    /// case: each layout effect reads one fact off an element through a ref. react-reconciler 19.1 makes a commit's
    /// insertions, removals and text writes in its mutation phase (<c>commitMutationEffects</c>), which it runs to
    /// the end before its layout phase (<c>commitLayoutEffects</c>).
    /// </summary>
    [TestFixture]
    internal sealed class HostTimingEventLogTests
    {
        private sealed record Placed(string Name, VisualElement Container, VNode?[]? Children = null);

        private static string PlacementOf(VisualElement? element, VisualElement container)
        {
            if (element == null) return "unset";
            for (var ancestor = element; ancestor != null; ancestor = ancestor.hierarchy.parent)
            {
                if (ReferenceEquals(ancestor, container)) return "attached";
            }
            return "detached";
        }

        [Component]
        private static VNode ReadsItsPlacement(Placed props)
        {
            var log = EventLog.UseLog();
            var element = Hooks.UseRef<VisualElement>();
            Hooks.UseLayoutEffect(() =>
            {
                log.Add("lay:" + props.Name + ":" + PlacementOf(element.Current, props.Container));
                return (Action?)null;
            }, Array.Empty<object>());
            return V.Div(refCallback: element.SetElement, children: props.Children);
        }

        [Component]
        private static VNode MountsASubtree(VisualElement container)
        {
            var (shown, _) = EventLog.UseState("Shown", false);
            return V.Div(children: new VNode?[]
            {
                shown
                    ? V.Component(ReadsItsPlacement, new Placed("P", container, new VNode?[]
                    {
                        V.Component(ReadsItsPlacement, new Placed("C1", container), key: "c1"),
                        V.Component(ReadsItsPlacement, new Placed("C2", container), key: "c2"),
                    }), key: "p")
                    : null,
            });
        }

        [Component]
        private static VNode Text(Ref<Label> label)
        {
            var (value, _) = EventLog.UseState("Text", 0);
            return V.Label(text: "text" + value, refCallback: label.SetElement);
        }

        [Component]
        private static VNode ReadsItsChildsText()
        {
            var log = EventLog.UseLog();
            var (value, _) = EventLog.UseState("Reader", 0);
            var label = Hooks.UseRef<Label>();
            Hooks.UseLayoutEffect(() =>
            {
                log.Add("lay:P:" + (label.Current == null ? "unset" : label.Current.text));
                return (Action?)null;
            }, new object[] { value });
            return V.Div(children: new VNode[] { V.Component(Text, label, key: "text") });
        }

        private sealed class Captured
        {
            internal VisualElement? Element;
        }

        private sealed record Sibling(Captured Removed, bool Shown);

        [Component]
        private static VNode ReadsItsRemovedSibling(Sibling props)
        {
            var log = EventLog.UseLog();
            Hooks.UseLayoutEffect(() =>
            {
                var removed = props.Removed.Element;
                log.Add("lay:B:" + (removed == null ? "unset" : removed.hierarchy.parent == null ? "parentless" : "parented"));
                return (Action?)null;
            }, new object[] { props.Shown });
            return V.Div();
        }

        [Component]
        private static VNode DropsASibling(Captured removed)
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            // A cleanup, so that React detaches this ref by calling it, and the case reads the removal alone.
            var capture = Hooks.UseCallback<Func<VisualElement, Action>>(element =>
            {
                removed.Element = element;
                return () => { };
            }, Array.Empty<object>());
            return V.Div(children: new VNode?[]
            {
                shown ? V.Label(text: "a", key: "a", refCallback: capture) : null,
                V.Component(ReadsItsRemovedSibling, new Sibling(removed, shown), key: "b"),
            });
        }

        // GREEN_ON_BASE(characterization): the base inserts the subtree in its walk, ahead of the commit's layout effects.
        [Test]
        public void Given_AMountedElement_When_ASubtreeWithRefsAndLayoutEffectsMountsUnderIt_Then_EachLayoutEffectSeesItsElementUnderTheContainer()
        {
            // Arrange
            var log = new EventLog();
            var container = new VisualElement();
            using var mounted = V.Mount(container, log.Provide(V.Component(MountsASubtree, container)));
            mounted.DrainForTest();

            // Act
            log.Setter<bool>("Shown").Invoke(true);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("lay:C1:attached lay:C2:attached lay:P:attached"));
        }

        // GREEN_ON_BASE(characterization): the base writes the child's text in its walk, ahead of the commit's layout effects.
        [Test]
        public void Given_AParentAndItsChildUpdatedInOneBatch_When_TheyCommit_Then_TheParentsLayoutEffectReadsTheChildsNewText()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(ReadsItsChildsText)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<int>("Text").Invoke(1);
            log.Setter<int>("Reader").Invoke(1);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("lay:P:text1"));
        }

        // GREEN_ON_BASE(characterization): the base removes the sibling in its walk, ahead of the commit's layout effects.
        [Test]
        public void Given_TwoSiblings_When_TheFirstIsRemovedAsTheSecondUpdates_Then_TheSecondsLayoutEffectSeesTheRemovedElementWithNoParent()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(DropsASibling, new Captured())));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("lay:B:parentless"));
        }
    }
}

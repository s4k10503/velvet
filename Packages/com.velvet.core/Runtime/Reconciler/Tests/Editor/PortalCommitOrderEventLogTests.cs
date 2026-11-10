using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the commit sequence of a tree holding a <c>V.Portal</c> as one <see cref="EventLog"/> string: React 19's
    /// <c>ReactFiberCommitWork</c> walks a portal's children where the portal sits among its siblings, whichever
    /// container they mount into.
    /// </summary>
    [TestFixture]
    internal sealed class PortalCommitOrderEventLogTests
    {
        private sealed record Named(string Name);

        private sealed record ParentProps(VisualElement Target, bool LastRendersNothing);

        [Component]
        private static VNode Probed(Named props)
        {
            EventLog.UseEffects(props.Name, Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback(props.Name));
        }

        [Component]
        private static VNode Empty(Named props)
        {
            EventLog.UseEffects(props.Name, Array.Empty<object>());
            return V.Fragment(Array.Empty<VNode>());
        }

        [Component]
        private static VNode PortalParent(ParentProps props)
        {
            EventLog.UseEffects("P", Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback("P"), children: new VNode[]
            {
                V.Component(Probed, new Named("A"), key: "a"),
                V.Portal(props.Target, new VNode[] { V.Component(Probed, new Named("PC"), key: "pc") }, key: "portal"),
                props.LastRendersNothing
                    ? V.Component(Empty, new Named("N"), key: "n")
                    : V.Component(Probed, new Named("B"), key: "b"),
            });
        }

        private static string MountAndLog(bool lastRendersNothing)
        {
            var log = new EventLog();
            using var mounted = V.Mount(new VisualElement(),
                log.Provide(V.Component(PortalParent, new ParentProps(new VisualElement(), lastRendersNothing))));
            mounted.DrainForTest();
            return log.ToString();
        }

        [Test]
        public void Given_APortalBetweenTwoSiblings_When_Mounted_Then_ThePortalsChildCommitsAtThePortalsPositionInEveryPhase()
        {
            // Arrange
            const bool lastRendersNothing = false;

            // Act
            var logged = MountAndLog(lastRendersNothing);

            // Assert
            Assert.That(logged, Is.EqualTo(
                "ins:A ins:PC ins:B ins:P ref:A lay:A ref:PC lay:PC ref:B lay:B ref:P lay:P eff:A eff:PC eff:B eff:P"));
        }

        [Test]
        public void Given_APortalFollowedByAComponentRenderingNothing_When_Mounted_Then_ThePortalsChildCommitsAheadOfThatComponent()
        {
            // Arrange
            const bool lastRendersNothing = true;

            // Act
            var logged = MountAndLog(lastRendersNothing);

            // Assert
            Assert.That(logged, Is.EqualTo(
                "ins:A ins:PC ins:N ins:P ref:A lay:A ref:PC lay:PC lay:N ref:P lay:P eff:A eff:PC eff:N eff:P"));
        }
    }
}

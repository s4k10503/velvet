using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a play heads for on a slot only its from-side names: what the element shows there when it already
    /// carries the pose the play heads for.
    /// </summary>
    [TestFixture]
    internal sealed class MotionRestingTargetTests
    {
        private static string Lengths(MotionSpringClassParser.SpringPlan plan)
            => string.Join(",", (plan.Lengths ?? new System.Collections.Generic.List<MotionSpringClassParser.LengthChannelPlan>())
                .Select(l => $"{l.Property}={l.From}->{l.To}").OrderBy(x => x, System.StringComparer.Ordinal));

        [Test]
        public void Given_APaddingOnlyTheLeavingSideNames_When_TheElementCarriesTheTargetPose_Then_ItHeadsForThePaddingTheElementShows()
        {
            // Arrange
            using var host = new HeadlessEditorPanelHost();
            var element = new VisualElement();
            element.AddToClassList("bg-red-500");
            host.Root.Add(element);
            EditorPanelTestHelpers.ForcePanelUpdate(host.Panel);
            var (from, to) = (new[] { "p-8" }, new[] { "bg-red-500" });
            var context = MotionSlotContext.Read(element, from, to, _ => false);

            // Act
            var plan = MotionSpringClassParser.Resolve(from, to, 0f, 0f, context);

            // Assert
            Assert.That(Lengths(plan),
                Is.EqualTo("PaddingBottom=32->0,PaddingLeft=32->0,PaddingRight=32->0,PaddingTop=32->0"));
        }

        // GREEN_ON_BASE(characterization): the base planned no channel here either; the case pins that reading the
        // padding the element shows does not start one while the leaving pose is still on it.
        [Test]
        public void Given_APaddingOnlyTheLeavingSideNames_When_TheElementStillCarriesTheLeavingPose_Then_NoChannelIsPlanned()
        {
            // Arrange
            using var host = new HeadlessEditorPanelHost();
            var element = new VisualElement();
            element.AddToClassList("p-8");
            host.Root.Add(element);
            EditorPanelTestHelpers.ForcePanelUpdate(host.Panel);
            var (from, to) = (new[] { "p-8" }, new[] { "bg-red-500" });
            var context = MotionSlotContext.Read(element, from, to, _ => false);

            // Act
            var plan = MotionSpringClassParser.Resolve(from, to, 0f, 0f, context);

            // Assert
            Assert.That(Lengths(plan), Is.EqualTo(string.Empty));
        }
    }
}

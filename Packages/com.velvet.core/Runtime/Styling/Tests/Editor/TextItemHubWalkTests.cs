using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the walk a parent's geometry event makes over the text leaves of its hub when one leaf's step takes another
    /// leaf of the hub out of it.
    /// </summary>
    [TestFixture]
    internal sealed class TextItemHubWalkTests
    {
        // A text-balance leaf and a flex-min leaf on one label, in that order of joining, so the walk reaches the balance
        // leaf first. The label is moved to another parent off a panel, where no event tells either leaf, and the balance
        // leaf's signature is moved and its shown string owed again: its step then leaves the first parent's hub, and
        // re-resolving the text re-derives the flex-min leaf, which leaves it as well. Both gone, no leaf is left at
        // the index the walk goes on to.
        // GREEN_ON_BASE(characterization): the base walks past a hub emptied under it. Change `i < hub.Leaves.Count` to `<=` in s_onParentGeometry and
        // the walk reads an index the list no longer holds.
        [Test]
        public void Given_ABalanceStepThatAlsoTakesTheFlexMinLeafOutOfTheHub_When_TheParentsGeometryEventFires_Then_TheWalkThrowsNothing()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var ctx = ReconcilerContextProbe.Of(scope);
            var first = new VisualElement();
            var second = new VisualElement();
            var label = new Label("hello");
            first.Add(label);
            StyleTextEffectResolver.CaptureRaw(ctx, label, "hello");
            StyleTextEffectResolver.Apply(ctx, label, new[] { "uppercase" });
            var flexMin = new StyleFlexMinSizeManipulator(ctx, Array.Empty<string>());
            ctx.FlexMinSizeManipulators[label] = flexMin;
            label.AddManipulator(flexMin);
            var balance = new StyleTextBalanceManipulator(ctx, TextWrapStyle.Balance);
            label.AddManipulator(balance);
            second.Add(label);
            typeof(StyleTextBalanceManipulator).GetField("_laidOut", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(balance, true);
            ctx.TextRendered.Remove(label);
            using var evt = EventBase<GeometryChangedEvent>.GetPooled();

            // Act / Assert
            Assert.That(() => first.SimulateEvent(evt), Throws.Nothing);
        }
    }
}

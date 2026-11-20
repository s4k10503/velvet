using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a text leaf whose percentage length comes from a class, with no inline value on that longhand,
    /// derives its automatic minimum again on its parent's geometry event. Arranged as
    /// <see cref="TextItemParentEventTests"/> is: once the leaf's text has changed, only the parent's event is
    /// fired, with no layout pass.
    /// </summary>
    [TestFixture]
    internal sealed class TextItemPercentClassParentEventTests : PanelTestBase
    {
        private const string LongWord = "internationalization";
        private const string Sentence = LongWord + " is long";
        private const string LongerWord = "antidisestablishmentarianism";
        private const string Longer = LongerWord + " is long";
        private const string WideRow = "flex flex-row w-[2000px]";

        protected override void LoadStyleSheets() => VelvetStyleUtilities.AttachTo(_window.rootVisualElement);

        private Label MountLabel(ReconcilerScope scope, string containerClass, string labelClass, string text)
        {
            _window.rootVisualElement.Add(scope.Root);
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(),
                new VNode[] { V.Div(containerClass, V.Label(className: labelClass, text: text)) });
            var label = scope.Root.Q<Label>();
            ForcePanelUpdate(label.panel);
            FireGeometry(label);
            ForcePanelUpdate(label.panel);
            return label;
        }

        private static void FireGeometry(VisualElement element)
        {
            using var evt = EventBase<GeometryChangedEvent>.GetPooled();
            element.SimulateEvent(evt);
        }

        private static void SetTextQuietly(Label label, string text)
            => ((INotifyValueChanged<string>)label).SetValueWithoutNotify(text);

        private static float WidestWordMinimum(TextElement e, string word)
            => Mathf.Ceil(e.MeasureTextSize(
                    word, float.NaN, VisualElement.MeasureMode.Undefined,
                    float.NaN, VisualElement.MeasureMode.Undefined).x)
                + e.resolvedStyle.paddingLeft + e.resolvedStyle.paddingRight
                + e.resolvedStyle.borderLeftWidth + e.resolvedStyle.borderRightWidth;

        // GREEN_ON_BASE(characterization): the base reads a max-w-full class as a length that reads its parent's box.
        // Change `inline.keyword == StyleKeyword.Null` to `!=` in the class branch of IsPercent and the parent event
        // leaves the label at the minimum it settled at.
        [Test]
        public void Given_ALabelWithAPercentMaxWidthClass_When_ItsTextChangesAndOnlyItsParentsGeometryEventFires_Then_ItsMinWidthFollowsTheText()
        {
            // Arrange — in a row this wide the class's cap stays above either text's widest word, and shrink-0 keeps
            // a label with room for its text measured, so the minimum is the widest word of the text last derived.
            using var scope = new ReconcilerScope();
            var label = MountLabel(scope, WideRow, "shrink-0 max-w-full", Sentence);
            var parent = label.parent;
            FireGeometry(parent);
            SetTextQuietly(label, Longer);

            // Act
            FireGeometry(parent);

            // Assert
            Assert.That(label.style.minWidth.value.value, Is.EqualTo(WidestWordMinimum(label, LongerWord)));
        }
    }
}

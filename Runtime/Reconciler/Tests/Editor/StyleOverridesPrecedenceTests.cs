using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// A <see cref="StyleOverrides"/> member against an arbitrary-value utility writing the same inline slot,
    /// ranked as React ranks the <c>style</c> prop against <c>className</c>: the override wins whichever was
    /// written last, an important utility wins over it as an important rule wins over a style attribute, and
    /// an override that goes away hands the slot back to the utility.
    /// </summary>
    [TestFixture]
    internal sealed class StyleOverridesPrecedenceTests
    {
        [Test]
        public void Given_ABackgroundColorOverride_When_TheUtilityBesideItChanges_Then_TheOverrideStillPaints()
        {
            // Arrange — the same override on both sides, so the patch writes the utility and not the override.
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var styles = new StyleOverrides { BackgroundColor = Color.blue };
            var oldTree = new VNode[] { V.Div(className: "bg-[#ff0000]", styles: styles) };
            var newTree = new VNode[] { V.Div(className: "bg-[#00ff00]", styles: styles) };
            reconciler.Reconcile(root, Array.Empty<VNode>(), oldTree);

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert
            Assert.That(root.ElementAt(0).style.backgroundColor.value, Is.EqualTo(Color.blue));
        }

        [Test]
        public void Given_AKeywordBackgroundColorOverride_When_TheUtilityBesideItChanges_Then_TheKeywordStands()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var styles = new StyleOverrides { BackgroundColor = new StyleColor(StyleKeyword.Initial) };
            var oldTree = new VNode[] { V.Div(className: "bg-[#ff0000]", styles: styles) };
            var newTree = new VNode[] { V.Div(className: "bg-[#00ff00]", styles: styles) };
            reconciler.Reconcile(root, Array.Empty<VNode>(), oldTree);

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert
            Assert.That(root.ElementAt(0).style.backgroundColor.keyword, Is.EqualTo(StyleKeyword.Initial));
        }

        [Test]
        public void Given_ATextColorOverride_When_AHoverUtilityTurnsOn_Then_TheOverrideStillPaints()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            reconciler.Reconcile(root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "hover:text-[#00ff00]", styles: new StyleOverrides { Color = Color.blue }),
            });
            var leaf = root.ElementAt(0);

            // Act
            using (var over = PointerOverEvent.GetPooled())
            {
                leaf.SimulateEvent(over);
            }

            // Assert
            Assert.That(leaf.style.color.value, Is.EqualTo(Color.blue));
        }

        [Test]
        public void Given_ABackgroundColorOverride_When_AnImportantUtilityWritesTheSameSlot_Then_TheUtilityPaints()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            reconciler.Reconcile(root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "!bg-[#00ff00]", styles: new StyleOverrides { BackgroundColor = Color.blue }),
            });

            // Assert
            Assert.That(root.ElementAt(0).style.backgroundColor.value, Is.EqualTo(Color.green));
        }

        [Test]
        public void Given_ABackgroundColorOverrideOverAUtility_When_TheOverrideIsRemoved_Then_TheUtilityPaintsAgain()
        {
            // Arrange — the override is read while present, so a utility that never yielded cannot pass.
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var oldTree = new VNode[]
            {
                V.Div(className: "bg-[#ff0000]", styles: new StyleOverrides { BackgroundColor = Color.blue }),
            };
            var newTree = new VNode[] { V.Div(className: "bg-[#ff0000]") };
            reconciler.Reconcile(root, Array.Empty<VNode>(), oldTree);
            var overridden = root.ElementAt(0).style.backgroundColor.value;

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert
            Assert.That((overridden, root.ElementAt(0).style.backgroundColor.value),
                Is.EqualTo((Color.blue, Color.red)));
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds where a reader's payload stands in the class array the reconciler composes for an element, as the
    /// reader's place and the payload's family slot decide it, by composing the array for a mounted reader directly.
    /// </summary>
    internal sealed class CustomPropertyPlacementTests : PanelTestBase
    {
        // The reader is the first of the classes given, so its place among them is the lowest.
        private string[] Composed(string parentClasses, string[] classes)
        {
            _mounted = V.Mount(_window.rootVisualElement,
                V.Div(className: parentClasses, children: new VNode?[]
                {
                    V.Div(name: "inner", className: string.Join(" ", classes)),
                }));
            var inner = _window.rootVisualElement.Q<VisualElement>("inner");
            var table = typeof(StyleCustomProperties)
                .GetField("s_consumers", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var args = new object?[] { inner, null };
            table.GetType().GetMethod("TryGetValue")!.Invoke(table, args);
            var entries = (IEnumerable)args[1]!.GetType().GetField("Entries")!.GetValue(args[1])!;
            var tokens = new List<string>();
            foreach (var consumer in entries)
            {
                var applied = (string[]?)consumer.GetType().GetField("Applied")!.GetValue(consumer);
                if (applied != null) tokens.AddRange(applied);
            }
            return (string[])typeof(FiberNodePatcher)
                .GetMethod("ComposePlacedClasses", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object?[] { inner, classes, tokens, new List<string>(), null })!;
        }

        [Test]
        public void Given_APaddingReaderOfPlaceZeroBeforeALiteralOfALaterPlace_When_TheClassesAreComposed_Then_ThePayloadPrecedesTheLiteral()
        {
            // Arrange / Act
            var composed = Composed("[--pad:20px]", new[] { "p-(--pad)", "pt-[9px]" });

            // Assert — the literal paints after the payload, so pt wins the top as Tailwind emits it.
            Assert.AreEqual("p-(--pad) p-[20px] pt-[9px]", string.Join(" ", composed));
        }

        [Test]
        public void Given_ATextReaderOfFamilySlotZeroBeforeALiteralOfAnotherFamily_When_TheClassesAreComposed_Then_ThePayloadWaitsForItsOwnFamily()
        {
            // Arrange / Act — opacity is a later place than text in Tailwind's order and belongs to no sorted family.
            var composed = Composed("[--sz:14px]", new[] { "text-(--sz)", "opacity-50" });

            // Assert
            Assert.AreEqual("text-(--sz) opacity-50 text-[14px]", string.Join(" ", composed));
        }
    }
}

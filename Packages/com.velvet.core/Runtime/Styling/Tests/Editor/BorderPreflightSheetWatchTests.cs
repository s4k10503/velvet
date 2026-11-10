using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the ancestors a panel's stylesheet watch compares on BorderPreflight's behalf: each ancestor of an
    /// element listening for a rule's border width once, for as long as it stays on the panel.
    /// </summary>
    internal sealed class BorderPreflightSheetWatchTests : PanelTestBase
    {
        private const BindingFlags Statics = BindingFlags.NonPublic | BindingFlags.Static;

        private static void WatchSheets(VisualElement element)
            => typeof(BorderPreflight).GetMethod("WatchSheets", Statics).Invoke(null, new object[] { element });

        private static object WatchOf(IPanel panel)
        {
            var table = typeof(BorderPreflight).GetField("s_sheetWatches", Statics).GetValue(null);
            var args = new object[] { panel, null };
            table.GetType().GetMethod("TryGetValue").Invoke(table, args);
            return args[1];
        }

        private static int AncestorsWatched(IPanel panel)
        {
            var watch = WatchOf(panel);
            return ((IList)watch.GetType().GetField("Ancestors").GetValue(watch)).Count;
        }

        private static void Tick(IPanel panel)
            => typeof(BorderPreflight).GetMethod("TickSheets", Statics).Invoke(null, new[] { panel, WatchOf(panel) });

        private VisualElement Container()
        {
            var container = new VisualElement();
            _window.rootVisualElement.Add(container);
            return container;
        }

        // GREEN_ON_BASE(characterization): the base watches each ancestor of the preflight's elements once.
        [Test]
        public void Given_AWatchedElement_When_ASiblingIsWatched_Then_NoAncestorIsWatchedTwice()
        {
            // Arrange
            var container = Container();
            var first = new VisualElement();
            var second = new VisualElement();
            container.Add(first);
            container.Add(second);
            WatchSheets(first);
            var before = AncestorsWatched(container.panel);

            // Act
            WatchSheets(second);

            // Assert
            Assert.That(AncestorsWatched(container.panel), Is.EqualTo(before));
        }

        // GREEN_ON_BASE(characterization): the base stops watching an ancestor that left its panel.
        [Test]
        public void Given_AWatchedAncestor_When_ItLeavesThePanelAndTheWatchTicks_Then_ItIsNoLongerWatched()
        {
            // Arrange
            var container = Container();
            var element = new VisualElement();
            container.Add(element);
            WatchSheets(element);
            var panel = container.panel;
            var before = AncestorsWatched(panel);
            container.RemoveFromHierarchy();

            // Act
            Tick(panel);

            // Assert
            Assert.That(AncestorsWatched(panel), Is.EqualTo(before - 1));
        }
    }
}

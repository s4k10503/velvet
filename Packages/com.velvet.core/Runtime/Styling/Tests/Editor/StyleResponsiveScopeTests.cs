using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which element answers each kind of width-driven variant, and when a class change tells
    /// the variants already attached to look again. Neither needs a panel: the ancestor walk reads class lists,
    /// and the change signal reads class arrays.
    /// </summary>
    [TestFixture]
    internal sealed class StyleResponsiveScopeTests
    {
        // A chain of elements each holding the classes given, outermost first, returning the innermost.
        private static VisualElement Chain(params string[] classesOutermostFirst)
        {
            VisualElement? parent = null;
            VisualElement element = null!;
            foreach (var classes in classesOutermostFirst)
            {
                element = new VisualElement();
                foreach (var cls in classes.Split(' ', System.StringSplitOptions.RemoveEmptyEntries))
                {
                    element.AddToClassList(cls);
                }
                parent?.Add(element);
                parent = element;
            }
            return element;
        }

        [Test]
        public void Given_NestedContainers_When_AnUnnamedQueryResolves_Then_TheNearestIsFound()
        {
            // Arrange
            var leaf = Chain("@container", "@container", "");

            // Act
            var container = StyleResponsiveScope.ResolveContainer(leaf, "");

            // Assert
            Assert.That(container, Is.SameAs(leaf.parent));
        }

        [Test]
        public void Given_ANamedContainerBelowAnUnnamedOne_When_AnUnnamedQueryResolves_Then_TheNearestOfAnyNameIsFound()
        {
            // Arrange
            var leaf = Chain("@container", "@container/side", "");

            // Act
            var container = StyleResponsiveScope.ResolveContainer(leaf, "");

            // Assert
            Assert.That(container, Is.SameAs(leaf.parent));
        }

        [Test]
        public void Given_ANamedContainerAsTheOnlyOne_When_AnUnnamedQueryResolves_Then_ItIsFound()
        {
            // Arrange
            var leaf = Chain("@container/side", "");

            // Act
            var container = StyleResponsiveScope.ResolveContainer(leaf, "");

            // Assert
            Assert.That(container, Is.SameAs(leaf.parent));
        }

        [Test]
        public void Given_TwoNamedContainers_When_ANamedQueryResolves_Then_ThatNamesContainerIsFound()
        {
            // Arrange
            var leaf = Chain("@container/a", "@container/b", "");

            // Act
            var container = StyleResponsiveScope.ResolveContainer(leaf, "a");

            // Assert
            Assert.That(container, Is.SameAs(leaf.parent!.parent));
        }

        [Test]
        public void Given_OnlyAnUnnamedContainer_When_ANamedQueryResolves_Then_NothingIsFound()
        {
            // Arrange
            var leaf = Chain("@container", "");

            // Act
            var container = StyleResponsiveScope.ResolveContainer(leaf, "a");

            // Assert
            Assert.That(container, Is.Null);
        }

        [Test]
        public void Given_ALeafThatIsItselfAContainer_When_AQueryResolves_Then_ItDoesNotAnswerItself()
        {
            // Arrange
            var leaf = Chain("@container");

            // Act
            var container = StyleResponsiveScope.ResolveContainer(leaf, "");

            // Assert
            Assert.That(container, Is.Null);
        }

        [Test]
        public void Given_AClassThatOnlyStartsLikeTheMarker_When_AQueryResolves_Then_ItIsNotAContainer()
        {
            // Arrange
            var leaf = Chain("@container-normal @containers", "");

            // Act
            var container = StyleResponsiveScope.ResolveContainer(leaf, "");

            // Assert
            Assert.That(container, Is.Null);
        }

        [Test]
        public void Given_AViewportScopeAndAContainer_When_AViewportVariantResolves_Then_TheViewportScopeIsFound()
        {
            // Arrange
            var leaf = Chain(VelvetResponsive.ViewportClass, VelvetResponsive.ContainerClass, "");
            var panelRoot = new VisualElement();

            // Act
            var source = StyleResponsiveScope.ResolveWidthSource(leaf, panelRoot);

            // Assert
            Assert.That(source, Is.SameAs(leaf.parent!.parent));
        }

        [Test]
        public void Given_OnlyAContainer_When_AViewportVariantResolves_Then_ThePanelRootIsFound()
        {
            // Arrange
            var leaf = Chain(VelvetResponsive.ContainerClass, "");
            var panelRoot = new VisualElement();

            // Act
            var source = StyleResponsiveScope.ResolveWidthSource(leaf, panelRoot);

            // Assert
            Assert.That(source, Is.SameAs(panelRoot));
        }

        private static bool Signalled(string[] before, string[] after)
        {
            var signalled = false;
            void OnChanged() => signalled = true;
            StyleResponsiveScope.ScopesChanged += OnChanged;
            try
            {
                StyleResponsiveScope.OnClassesChanged(before, after);
            }
            finally
            {
                StyleResponsiveScope.ScopesChanged -= OnChanged;
            }
            return signalled;
        }

        [TestCase("p-2", "p-2 @container")]
        [TestCase("p-2 @container", "p-2")]
        [TestCase("@container", "@container/a")]
        [TestCase("@container/a", "@container/b")]
        [TestCase("@container/a", "@container/a @container/b")]
        [TestCase("p-2", "p-2 velvet-viewport")]
        [TestCase("velvet-viewport p-2", "p-2")]
        public void Given_AMarkerGainedLostOrRenamed_When_TheClassesChange_Then_TheScopesAreSignalled(
            string before, string after)
        {
            // Act
            var signalled = Signalled(before.Split(' '), after.Split(' '));

            // Assert
            Assert.That(signalled, Is.True);
        }

        [TestCase("p-2", "p-4")]
        [TestCase("@container p-2", "p-4 @container")]
        [TestCase("@container/a p-2", "@container/a p-4")]
        [TestCase("velvet-viewport p-2", "velvet-viewport p-4")]
        [TestCase("@container-normal", "@containers")]
        public void Given_NoMarkerGainedLostOrRenamed_When_TheClassesChange_Then_NothingIsSignalled(
            string before, string after)
        {
            // Act
            var signalled = Signalled(before.Split(' '), after.Split(' '));

            // Assert
            Assert.That(signalled, Is.False);
        }
    }
}

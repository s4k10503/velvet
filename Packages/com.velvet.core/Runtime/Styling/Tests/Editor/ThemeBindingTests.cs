using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what binding a root to <see cref="VelvetTheme"/> does and what it holds: the theme event is static,
    /// so whatever the binding keeps reachable from it lives as long as the domain.
    /// </summary>
    /// <remarks>
    /// Collection is counted over several roots rather than asserted of one, the way RouteLoaderRunnerTests
    /// counts it.
    /// </remarks>
    [TestFixture]
    internal sealed class ThemeBindingTests
    {
        private const int Roots = 10;

        private static readonly FieldInfo BoundRoots = typeof(VelvetStyleUtilities)
            .GetField("s_themeRoots", BindingFlags.NonPublic | BindingFlags.Static);

        // -1 where the field is absent, so a tree without it disagrees rather than throws.
        private static int BoundCount() => (BoundRoots?.GetValue(null) as ICollection)?.Count ?? -1;

        private bool _darkBefore;
        private HeadlessEditorPanelHost _host;

        [SetUp]
        public void SetUp() => _darkBefore = VelvetTheme.IsDark;

        [TearDown]
        public void TearDown()
        {
            VelvetTheme.IsDark = _darkBefore;
            _host?.Dispose();
            _host = null;
        }

        private static void Collect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        // GREEN_ON_BASE(characterization): the base applies the theme to a root bound on a panel.
        [Test]
        public void Given_DarkModeOn_When_ARootOnAPanelIsBound_Then_ItCarriesTheDarkClass()
        {
            // Arrange
            VelvetTheme.IsDark = true;
            _host = new HeadlessEditorPanelHost();

            // Act
            VelvetStyleUtilities.BindThemeTo(_host.Root);

            // Assert
            Assert.That(_host.Root.ClassListContains(VelvetStyleUtilities.DarkThemeClass), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base keeps every root bound on a panel following the theme.
        [Test]
        public void Given_TwoRootsBound_When_DarkModeTurnsOn_Then_TheFirstStillCarriesTheDarkClass()
        {
            // Arrange — binding the second must leave the first's binding in place.
            _host = new HeadlessEditorPanelHost();
            using var second = new HeadlessEditorPanelHost();
            VelvetStyleUtilities.BindThemeTo(_host.Root);
            VelvetStyleUtilities.BindThemeTo(second.Root);

            // Act
            VelvetTheme.IsDark = true;

            // Assert
            Assert.That(_host.Root.ClassListContains(VelvetStyleUtilities.DarkThemeClass), Is.True);
        }

        [Test]
        public void Given_TheSheetAttachedToTheRootsOfPanelsSinceDisposed_When_CollectionRuns_Then_MostOfThoseRootsAreCollected()
        {
            // Arrange
            var roots = AttachToRootsOfDisposedPanels();

            // Act
            Collect();

            // Assert
            Assert.That(roots.Count(root => root.IsAlive), Is.LessThan(Roots / 2));
        }

        [Test]
        public void Given_ARootBoundOnce_When_ItIsBoundAgain_Then_ItIsStillHeldOnce()
        {
            // Arrange — collected first, so the bind below drops every binding a collection could drop before the
            // count is taken.
            var root = new VisualElement();
            Collect();
            VelvetStyleUtilities.BindThemeTo(root);
            var once = BoundCount();

            // Act
            VelvetStyleUtilities.BindThemeTo(root);

            // Assert — a first count of none says no bindings were counted at all.
            Assert.That((once > 0, BoundCount() - once), Is.EqualTo((true, 0)));
        }

        [Test]
        public void Given_BoundRootsSinceCollected_When_AnotherRootIsBound_Then_MostOfTheirBindingsAreDropped()
        {
            // Arrange — collected first, for the reason the case above is.
            Collect();
            var kept = new VisualElement();
            VelvetStyleUtilities.BindThemeTo(kept);
            var before = BoundCount();
            BindRootsNothingElseHolds();
            Collect();

            // Act
            VelvetStyleUtilities.BindThemeTo(new VisualElement());
            GC.KeepAlive(kept);

            // Assert — the root bound last is one of the bindings counted.
            Assert.That(BoundCount() - before, Is.InRange(1, Roots / 2));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static List<WeakReference> AttachToRootsOfDisposedPanels()
        {
            var roots = new List<WeakReference>();
            for (var i = 0; i < Roots; i++)
            {
                var host = new HeadlessEditorPanelHost();
                VelvetStyleUtilities.AttachTo(host.Root);
                roots.Add(new WeakReference(host.Root));
                host.Dispose();
            }

            return roots;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void BindRootsNothingElseHolds()
        {
            for (var i = 0; i < Roots; i++)
            {
                VelvetStyleUtilities.BindThemeTo(new VisualElement());
            }
        }
    }
}

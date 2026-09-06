using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Plays the starter sample's scene the way a developer does — load it, let it run — and reads the
    /// result off the <c>UIDocument</c> panel. Every element is reached by the name the scene renders it
    /// under rather than through the sample assembly, so a rename has to move both sides.
    /// <para>
    /// The scene is loaded through <c>SceneManager</c> and therefore through the build settings, which is
    /// what lets the same fixture run against a built player (<c>-testPlatform StandaloneOSX</c>); there,
    /// the utility-sheet case below also answers whether the sheet survived the build.
    /// </para>
    /// <para>
    /// Each case settles on a condition rather than a frame count. Several suites share this machine, so a
    /// budget large enough to be reliable under load would be most of the wall time when it is idle.
    /// </para>
    /// <para>
    /// One case reads geometry rather than presence: where the route's first row sits relative to the
    /// chrome's header. It compares the two measured rects against each other rather than against a pixel
    /// budget, so the font metrics and panel scale of whichever machine runs it do not decide the outcome.
    /// </para>
    /// <para>
    /// One case counts rather than finds: the task route's keyed rows over two swaps, since what a swap
    /// appending rather than replacing leaves behind is a copy of the departed route's children.
    /// </para>
    /// </summary>
    [Timeout(600000)]
    internal sealed class StarterSampleSceneTests
    {
        private const string ScenePath = "Assets/VelvetStarterSample/StarterApp.unity";
        private const string RootName = "starter-app";
        private const string HeaderName = "starter-header";
        private const string AboutLinkName = "nav-about";
        private const string BackLinkName = "back-link";

        // The nav sits in the layout and survives both routes, so the departure term has to come from
        // the task route's own body.
        private const string DraftFieldName = "draft-field";

        // The task route's keyed rows, which sit under its AnimatePresence rather than beside the field
        // above: the field's departure is the ordinary diff's and says nothing about theirs.
        private const string TaskRowName = "task-row";

        private const double SettleSeconds = 20;

        private bool _loaded;

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            if (_loaded)
            {
                _loaded = false;
                yield return SceneManager.UnloadSceneAsync(ScenePath);
            }
        }

        private IEnumerator PlaySampleScene()
        {
            yield return SceneManager.LoadSceneAsync(ScenePath, LoadSceneMode.Additive);
            _loaded = true;
            // Settle on the host having mounted something that the panel has laid out, which is a strictly
            // earlier event than any route rendering — so what each case asserts is still its own to prove.
            yield return WaitUntil(
                () =>
                {
                    var root = PanelRoot();
                    return root != null && root.childCount > 0 && root[0].resolvedStyle.width > 0f;
                },
                "the UIDocument panel holding a laid-out child");
        }

        /// <summary>
        /// Waits for <paramref name="condition"/>, and fails naming <paramref name="what"/> if it never holds.
        /// </summary>
        /// <remarks>
        /// A timeout used to fall through to the assertion, which then dereferenced an element the scene had
        /// not produced — so the run reported a <c>NullReferenceException</c> and a null button rather than
        /// a scene that mounted nothing. Reading those as a defect in the change under test cost several
        /// suite runs and a wrong diagnosis before a control checkout cleared it; the failure now says which
        /// wait ran out, which is true whatever caused it.
        /// </remarks>
        private static IEnumerator WaitUntil(Func<bool> condition, string what)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + SettleSeconds;
            while (!condition() && Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
            }

            if (!condition())
            {
                Assert.Fail($"{what} did not hold within {SettleSeconds}s of playing {ScenePath}. "
                            + "Nothing below this point measured anything. If the rest of the suite is "
                            + "green, suspect this checkout's imported state before the change under test — "
                            + "a cloned or restored Library reaches the sample's assets in the same way.");
            }
        }

        private static VisualElement PanelRoot()
        {
            var document = UnityEngine.Object.FindFirstObjectByType<UIDocument>();
            return document == null ? null : document.rootVisualElement;
        }

        private static VisualElement Find(string name) => PanelRoot()?.Q<VisualElement>(name);

        private static int CountOf(string name)
        {
            var root = PanelRoot();
            return root == null ? 0 : root.Query<VisualElement>(name).ToList().Count;
        }

        [UnityTest]
        public IEnumerator Given_TheStarterSampleScene_When_Played_Then_TheMountedTreeReachesTheDocumentPanel()
        {
            // Arrange — the scene's own host attaches the sheet, builds the router and mounts.
            yield return PlaySampleScene();

            // Act
            yield return WaitUntil(() => Find(RootName) != null, $"an element named {RootName}");

            // Assert
            Assert.That(Find(RootName), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator Given_TheStarterSampleScene_When_Played_Then_TheBundledUtilitiesResolveOnItsPanel()
        {
            // Arrange
            yield return PlaySampleScene();

            // Act — a laid-out header is what the reading needs; its direction is not part of the wait.
            yield return WaitUntil(() => Find(HeaderName) is { } header && header.resolvedStyle.width > 0f,
                $"a laid-out element named {HeaderName}");

            // Assert — the header's direction is read rather than an arbitrary-value class, which would
            // land as inline style and so pass against an entirely unstyled panel.
            // BundledStyleUtilitiesRuntimeTests pins both poles of this reading on a bare panel.
            Assert.That(Find(HeaderName).resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.Row));
        }

        [UnityTest]
        public IEnumerator Given_TheStarterSampleScene_When_Played_Then_TheRouteBodyIsLaidOutBelowTheChromesHeader()
        {
            // Arrange — the chrome is a column holding the header and then the Outlet, so the route's first
            // row belongs under the header rather than over it.
            yield return PlaySampleScene();

            // Act
            yield return WaitUntil(
                () => Find(HeaderName) is { } header && header.worldBound.height > 0f
                      && Find(DraftFieldName) is { } draft && draft.worldBound.height > 0f,
                $"laid-out elements named {HeaderName} and {DraftFieldName}");

            // Assert — read in panel space, since the two sit at different depths and a local rect would
            // compare them in different coordinate systems. Both heights ride along, because two elements
            // that never laid out would satisfy the ordering term at y = 0 and say nothing about where
            // either one went.
            var chromeHeader = Find(HeaderName);
            var routeField = Find(DraftFieldName);
            Assert.That(
                (chromeHeader.worldBound.height > 0f, routeField.worldBound.height > 0f,
                 chromeHeader.worldBound.yMax <= routeField.worldBound.y),
                Is.EqualTo((true, true, true)),
                $"header {chromeHeader.worldBound}, route field {routeField.worldBound}");
        }

        [UnityTest]
        public IEnumerator Given_TheStarterSampleScene_When_TheAboutLinkIsClicked_Then_TheOutletSwapsRoute()
        {
            // Arrange
            yield return PlaySampleScene();
            yield return WaitUntil(() => PanelRoot()?.Q<Button>(AboutLinkName) != null,
                $"a Button named {AboutLinkName}");
            var tasksBefore = Find(DraftFieldName) != null;

            // Act
            PanelRoot().Q<Button>(AboutLinkName).SimulateClick();
            yield return WaitUntil(() => PanelRoot()?.Q<Button>(BackLinkName) != null,
                $"a Button named {BackLinkName}");

            // Assert — arrival alone is satisfied by an outlet that appends rather than replaces, and the
            // departure term alone is satisfied by a task route that never rendered. All three in one
            // comparison is what makes this case pin the swap its name claims.
            Assert.That(
                (tasksBefore, Find(DraftFieldName) != null, PanelRoot().Q<Button>(BackLinkName) != null),
                Is.EqualTo((true, false, true)));
        }

        [UnityTest]
        public IEnumerator Given_TheStarterSampleScene_When_TheRouteIsSwappedTwice_Then_NoTaskRowSurvivesTheDeparture()
        {
            // Arrange — the rows reach a diff's old side only through their AnimatePresence's committed
            // composition, which is held against the boundary that rendered it.
            yield return PlaySampleScene();
            yield return WaitUntil(() => CountOf(TaskRowName) > 0, $"at least one element named {TaskRowName}");
            var rowsOnTheTaskRoute = CountOf(TaskRowName);

            // Act — twice, so the return to the task route is read as a swap of its own rather than left
            // to the departure.
            PanelRoot().Q<Button>(AboutLinkName).SimulateClick();
            yield return WaitUntil(() => PanelRoot()?.Q<Button>(BackLinkName) != null, $"a Button named {BackLinkName}");
            PanelRoot().Q<Button>(BackLinkName).SimulateClick();
            yield return WaitUntil(() => Find(DraftFieldName) != null, $"an element named {DraftFieldName}");
            PanelRoot().Q<Button>(AboutLinkName).SimulateClick();
            yield return WaitUntil(() => PanelRoot()?.Q<Button>(BackLinkName) != null, $"a Button named {BackLinkName}");

            // Assert — the count on the task route rides along, since a scene that rendered no row would
            // satisfy the zero on the about route while saying nothing about a departure.
            Assert.That(
                (rowsOnTheTaskRoute > 0, CountOf(TaskRowName)),
                Is.EqualTo((true, 0)),
                $"{rowsOnTheTaskRoute} row(s) on the task route");
        }
    }
}

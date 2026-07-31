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
    /// </summary>
    [Timeout(600000)]
    internal sealed class StarterSampleSceneTests
    {
        private const string ScenePath = "Assets/VelvetStarterSample/StarterApp.unity";
        private const string RootName = "starter-app";
        private const string HeaderName = "starter-header";
        private const string AboutLinkName = "nav-about";
        private const string BackLinkName = "back-link";

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
            yield return WaitUntil(() =>
            {
                var root = PanelRoot();
                return root != null && root.childCount > 0 && root[0].resolvedStyle.width > 0f;
            });
        }

        private static IEnumerator WaitUntil(Func<bool> condition)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + SettleSeconds;
            while (!condition() && Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
            }
        }

        private static VisualElement PanelRoot()
        {
            var document = UnityEngine.Object.FindFirstObjectByType<UIDocument>();
            return document == null ? null : document.rootVisualElement;
        }

        private static VisualElement Find(string name) => PanelRoot()?.Q<VisualElement>(name);

        [UnityTest]
        public IEnumerator Given_TheStarterSampleScene_When_Played_Then_TheMountedTreeReachesTheDocumentPanel()
        {
            // Arrange — the scene's own host attaches the sheet, builds the router and mounts.
            yield return PlaySampleScene();

            // Act
            yield return WaitUntil(() => Find(RootName) != null);

            // Assert
            Assert.That(Find(RootName), Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator Given_TheStarterSampleScene_When_Played_Then_TheBundledUtilitiesResolveOnItsPanel()
        {
            // Arrange
            yield return PlaySampleScene();

            // Act — a laid-out header is what the reading needs; its direction is not part of the wait.
            yield return WaitUntil(() => Find(HeaderName) is { } header && header.resolvedStyle.width > 0f);

            // Assert — the header's direction is read rather than an arbitrary-value class, which would
            // land as inline style and so pass against an entirely unstyled panel.
            // BundledStyleUtilitiesRuntimeTests pins both poles of this reading on a bare panel.
            Assert.That(Find(HeaderName).resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.Row));
        }

        [UnityTest]
        public IEnumerator Given_TheStarterSampleScene_When_TheAboutLinkIsClicked_Then_TheOutletSwapsRoute()
        {
            // Arrange
            yield return PlaySampleScene();
            yield return WaitUntil(() => PanelRoot()?.Q<Button>(AboutLinkName) != null);

            // Act
            PanelRoot().Q<Button>(AboutLinkName).SimulateClick();
            yield return WaitUntil(() => PanelRoot()?.Q<Button>(BackLinkName) != null);

            // Assert — an element only the About route renders.
            Assert.That(PanelRoot().Q<Button>(BackLinkName), Is.Not.Null);
        }
    }
}

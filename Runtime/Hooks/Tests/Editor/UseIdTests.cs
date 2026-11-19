using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="Hooks.UseId"/> in a function component.
    /// <list type="bullet">
    /// <item>The generated ID is stable across re-renders of the same component instance.</item>
    /// <item>Distinct component instances receive independent IDs.</item>
    /// <item>Each call site owns its own slot, so two calls in one component yield different IDs.</item>
    /// <item>Without a prefix the ID has the form <c>:r{hex}:</c>; with a prefix it is <c>{prefix}:r{hex}:</c>.</item>
    /// <item>Only the first render reflects the prefix; later prefix changes are ignored, like the initial value of UseState.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. Per-region static
    /// fields are reset together in <see cref="SetUp"/> via <c>Reset{Region}()</c> helpers.
    /// </remarks>
    [TestFixture]
    internal sealed class UseIdTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetCapture();
        }

        [Test]
        public void Given_MountedComponent_When_ReRendered_Then_IdIsStable()
        {
            // Arrange
            s_capturePrefix = null;
            using var mounted = V.Mount(_root, V.Component(CaptureIdRender, key: "id-stable"));
            var firstId = s_capturedId;

            // Act
            s_forceUpdateSetter.Invoke(s_forceUpdateValue + 1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(s_capturedId, Is.EqualTo(firstId),
                "Re-renders of the same component instance return the same id");
        }

        [Test]
        public void Given_TwoComponentInstances_When_Mounted_Then_IdsDiffer()
        {
            // Arrange
            s_capturePrefix = null;

            // Act — mount the first, snapshot its id before the second mount overwrites the captured field
            using var mountedA = V.Mount(_root, V.Component(CaptureIdRender, key: "id-a"));
            var idA = s_capturedId;
            using var mountedB = V.Mount(new VisualElement(), V.Component(CaptureIdRender, key: "id-b"));
            var idB = s_capturedId;

            // Assert
            Assert.That(idA, Is.Not.EqualTo(idB), "Distinct component instances return independent ids");
        }

        [Test]
        public void Given_TwoCallSites_When_Mounted_Then_IdsDiffer()
        {
            // Arrange + Act
            using var mounted = V.Mount(_root, V.Component(TwoIdsRender, key: "two-ids"));

            // Assert
            Assert.That(s_twoIdsFirst, Is.Not.EqualTo(s_twoIdsSecond),
                "Distinct call sites within the same component yield different ids");
        }

        [Test]
        public void Given_NoPrefix_When_Mounted_Then_IdHasStandardForm()
        {
            // Arrange
            s_capturePrefix = null;

            // Act
            using var mounted = V.Mount(_root, V.Component(CaptureIdRender, key: "id-standard"));

            // Assert
            Assert.That(s_capturedId, Does.StartWith(":r").And.EndWith(":"),
                "Without a prefix the id has the :r{hex}: form");
        }

        [Test]
        public void Given_Prefix_When_Mounted_Then_PrefixIsPrependedToStandardForm()
        {
            // Arrange
            s_capturePrefix = "myform";

            // Act
            using var mounted = V.Mount(_root, V.Component(CaptureIdRender, key: "id-prefixed"));

            // Assert
            Assert.That(s_capturedId, Does.StartWith("myform:r").And.EndWith(":"),
                "A prefix is prepended to the :r{hex}: form");
        }

        #region CaptureId component (UseId + UseState to trigger re-render)

        private static string s_capturePrefix;
        private static string s_capturedId;
        private static System.Action<int> s_forceUpdateSetter;
        private static int s_forceUpdateValue;

        private static void ResetCapture()
        {
            s_capturePrefix = null;
            s_capturedId = null;
            s_forceUpdateSetter = null;
            s_forceUpdateValue = 0;
            s_twoIdsFirst = null;
            s_twoIdsSecond = null;
        }

        [Component]
        private static VNode CaptureIdRender()
        {
            var (value, setValue) = Hooks.UseState(0);
            s_forceUpdateValue = value;
            s_forceUpdateSetter = setValue;
            s_capturedId = Hooks.UseId(s_capturePrefix);
            return V.Label(text: s_capturedId);
        }

        #endregion

        #region TwoIds component (two UseId calls in the same component)

        private static string s_twoIdsFirst;
        private static string s_twoIdsSecond;

        [Component]
        private static VNode TwoIdsRender()
        {
            s_twoIdsFirst = Hooks.UseId();
            s_twoIdsSecond = Hooks.UseId();
            return V.Label(text: s_twoIdsFirst);
        }

        #endregion
    }
}

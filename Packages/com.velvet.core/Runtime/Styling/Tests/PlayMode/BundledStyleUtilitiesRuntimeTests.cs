using System.Collections;
using NUnit.Framework;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using Velvet.TestUtilities;
using static Velvet.TestUtilities.PlayModeRealtimeTestHelpers;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that runtime code can put Velvet's bundled utilities onto a panel with no editor API in reach.
    /// Every case drives a <c>UIDocument</c>-backed panel, the one a player has.
    /// <para>
    /// The sheet is probed with a PLAIN class, never an arbitrary-value one: <c>w-[120px]</c> resolves to
    /// inline style and lands whether or not a stylesheet is attached, so it would pass against an entirely
    /// unstyled panel. The <c>gap-*</c> case is the opposite pole and is here on purpose — it is a family
    /// with no USS payload at all, so it holds without the sheet, and pinning both poles is what keeps the
    /// documented split between them honest.
    /// </para>
    /// <para>
    /// Run this fixture against a built player (<c>-testPlatform StandaloneOSX</c>) to exercise the part the
    /// editor cannot: whether the asset survives the build at all.
    /// </para>
    /// </summary>
    [Timeout(600000)]
    internal sealed class BundledStyleUtilitiesRuntimeTests
    {
        private RenderTexturePanelHost _host;
        private MountedTree _mounted;

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            yield return null;
        }

        private IEnumerator MountProbe(string name, bool attachUtilities)
        {
            _host = new RenderTexturePanelHost(name, 64, 64);
            if (attachUtilities)
            {
                VelvetStyleUtilities.AttachTo(_host.Root);
            }

            _mounted = V.Mount(_host.Root, V.Div(name: "probe", className: "flex-row"));
            return WaitRealtime(0.5);
        }

        private FlexDirection ProbeDirection() => _host.Root.Q<VisualElement>("probe").resolvedStyle.flexDirection;

        [UnityTest]
        public IEnumerator Given_ARuntimePanel_When_NoStyleSheetIsAttached_Then_APlainUtilityClassIsInert()
        {
            // Arrange / Act — the state a user gets today: mount, build, ship.
            yield return MountProbe("NoUtilities", attachUtilities: false);

            // Assert — the engine's own default survives, so `flex-row` did nothing. This is what makes the
            // attached case below discriminating rather than a reading of UI Toolkit's default.
            Assert.That(ProbeDirection(), Is.EqualTo(FlexDirection.Column));
        }

        [UnityTest]
        public IEnumerator Given_ARuntimePanel_When_TheBundledUtilitiesAreAttached_Then_APlainUtilityClassResolves()
        {
            // Arrange / Act
            yield return MountProbe("WithUtilities", attachUtilities: true);

            // Assert
            Assert.That(ProbeDirection(), Is.EqualTo(FlexDirection.Row));
        }

        [UnityTest]
        public IEnumerator Given_ARuntimePanelWithNoStyleSheet_When_AGapUtilityIsUsed_Then_ItStillSpacesChildren()
        {
            // Arrange — gap-* carries no USS payload at all: `_gap.uss` declares no rules and
            // StyleGapManipulator writes the inter-child margin from C#. It stands here for the whole set of
            // families a missing sheet leaves working, which is why the failure misreads; it is one of many,
            // not the counterpart to the sheet.
            _host = new RenderTexturePanelHost("GapWithoutSheet", 64, 64);
            _mounted = V.Mount(_host.Root, V.Div(className: "gap-4", children: new[]
            {
                V.Div(name: "first"),
                V.Div(name: "second"),
            }));

            // Act
            yield return WaitRealtime(0.5);

            // Assert — 4 units on the shared spacing scale, 4px each, on the leading edge of the column the
            // container resolves to without a direction class.
            Assert.That(_host.Root.Q<VisualElement>("second").resolvedStyle.marginTop, Is.EqualTo(16f));
        }

        [Test]
        public void Given_NoEditorApi_When_TheBundledUtilitiesAreResolved_Then_TheSheetLoads()
        {
            // Arrange / Act / Assert — asserted as "does not throw" rather than "is not null", because the
            // property's contract on a missing asset is to throw: a null check there can only ever pass.
            Assert.That(() => VelvetStyleUtilities.Sheet, Throws.Nothing);
        }
    }
}

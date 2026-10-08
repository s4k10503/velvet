using NUnit.Framework;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how far a registered family reaches a label that carries no font class, read through
    /// <c>resolvedStyle</c> on a real panel so inheritance takes part: <see cref="VelvetFonts.DefaultFamily"/>
    /// reaches only an element with a weight or style class, and a <c>font-&lt;name&gt;</c> class on a
    /// container is what reaches the unadorned labels under it. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class DefaultFamilyScopePanelTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;
        private FontAsset _sans;

        [SetUp]
        public void SetUp()
        {
            VelvetFonts.Clear();
            _sans = FontAsset.CreateFontAsset(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            VelvetFonts.Register(new VelvetFontFamily("sans",
                new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, upright = _sans }));
            _host = new HeadlessEditorPanelHost();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            VelvetFonts.Clear();
            Object.DestroyImmediate(_sans);
        }

        private bool ResolvesSans(string name) =>
            _host.Root.Q<Label>(name).resolvedStyle.unityFontDefinition.fontAsset == _sans;

        // GREEN_ON_BASE(characterization): the scope of DefaultFamily that the setup guide now states.
        [Test]
        public void Given_ADefaultFamily_When_LabelsWithAndWithoutAWeightClassMount_Then_OnlyTheWeightedLabelResolvesIt()
        {
            // Arrange
            VelvetFonts.DefaultFamily = "sans";

            // Act
            _mounted = V.Mount(_host.Root, V.Div(children: new VNode[]
            {
                V.Label(text: "plain", name: "plain"),
                V.Label(className: "font-normal", text: "weighted", name: "weighted"),
            }));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);

            // Assert
            Assert.That((ResolvesSans("plain"), ResolvesSans("weighted")), Is.EqualTo((false, true)));
        }

        // GREEN_ON_BASE(characterization): the root-level opt-in that the setup guide recommends.
        [Test]
        public void Given_AFamilyClassOnAContainer_When_AnUnadornedLabelMountsInsideIt_Then_TheLabelResolvesThatFamily()
        {
            // Arrange — "sans" is registered by the setup, with no DefaultFamily.

            // Act — the outside label separates inheritance from the family reaching every label.
            _mounted = V.Mount(_host.Root, V.Div(children: new VNode[]
            {
                V.Div(className: "font-sans", children: new VNode[] { V.Label(text: "inside", name: "inside") }),
                V.Label(text: "outside", name: "outside"),
            }));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);

            // Assert
            Assert.That((ResolvesSans("inside"), ResolvesSans("outside")), Is.EqualTo((true, false)));
        }
    }
}

using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what <see cref="VelvetFonts.DefaultFamily"/> does where the engine's matcher cannot be built: the rules
    /// that match an element are not read, so the family is written on the element a tree is mounted on as though no
    /// author's rule gave it a font. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class DefaultFamilyMatcherFallbackTests
    {
        private const string AuthorSheet = "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/DefaultFamilyAuthorFont.uss";

        private static readonly FieldInfo s_extractor =
            typeof(BorderPreflight).GetField("s_extractor", BindingFlags.NonPublic | BindingFlags.Static);

        // Created once and never destroyed, for the reason FontRelativeUnitPanelTests gives its own.
        private static FontAsset s_sans;

        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;
        private object _extractor;

        [SetUp]
        public void SetUp()
        {
            VelvetFonts.Clear();
            if (s_sans == null)
            {
                s_sans = FontAsset.CreateFontAsset(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            }
            VelvetFonts.Register(new VelvetFontFamily("sans",
                new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, upright = s_sans }));
            _host = new HeadlessEditorPanelHost();
            _extractor = s_extractor.GetValue(null);
        }

        [TearDown]
        public void TearDown()
        {
            s_extractor.SetValue(null, _extractor);
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            VelvetFonts.Clear();
        }

        [Test]
        public void Given_NoMatcher_When_ATreeMountsOnARootAnAuthorsRuleGivesAFont_Then_ItsLabelResolvesTheDefaultFamily()
        {
            // Arrange
            s_extractor.SetValue(null, null);
            VelvetFonts.DefaultFamily = "sans";
            _host.Root.styleSheets.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(AuthorSheet));
            var mountRoot = new VisualElement();
            mountRoot.AddToClassList("author-font");
            _host.Root.Add(mountRoot);

            // Act
            _mounted = V.Mount(mountRoot, V.Div(children: new VNode[] { V.Label(text: "plain", name: "plain") }));
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);

            // Assert
            Assert.That(mountRoot.Q<Label>("plain").resolvedStyle.unityFontDefinition.fontAsset, Is.SameAs(s_sans));
        }
    }
}

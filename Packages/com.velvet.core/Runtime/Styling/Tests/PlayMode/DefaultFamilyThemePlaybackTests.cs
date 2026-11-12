#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that <see cref="VelvetFonts.DefaultFamily"/> reaches a label with no font class mounted on a
    /// document's root under the runtime theme this project ships, which gives such a label a font of its own.
    /// </summary>
    internal sealed class DefaultFamilyThemePlaybackTests
    {
        private const string ThemePath = "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss";

        // Created once and never destroyed, for the reason FontRelativeUnitPanelTests gives its own.
        private static FontAsset s_sans;

        private GameObject _host;
        private PanelSettings _settings;
        private MountedTree _mounted;

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            if (_host != null)
            {
                Object.Destroy(_host);
                _host = null;
            }
            if (_settings != null)
            {
                Object.Destroy(_settings);
                _settings = null;
            }
            VelvetFonts.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Given_ADefaultFamilyUnderTheRuntimeTheme_When_ALabelWithNoFontClassMountsOnTheDocumentRoot_Then_ItResolvesTheDefaultFamily()
        {
            // Arrange
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (s_sans == null)
            {
                s_sans = FontAsset.CreateFontAsset(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            }
            VelvetFonts.Register(new VelvetFontFamily("sans",
                new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, upright = s_sans }));
            VelvetFonts.DefaultFamily = "sans";
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.themeStyleSheet = theme;
            _host = new GameObject(nameof(DefaultFamilyThemePlaybackTests));
            var document = _host.AddComponent<UIDocument>();
            document.panelSettings = _settings;
            yield return null;

            // Act
            _mounted = V.Mount(document.rootVisualElement, V.Div(children: new VNode[]
            {
                V.Label(text: "plain", name: "plain"),
            }));
            yield return null;
            yield return null;

            // Assert — null where the theme is missing, so a missing theme fails the case rather than passing.
            var resolved = document.rootVisualElement.Q<Label>("plain").resolvedStyle.unityFontDefinition.fontAsset;
            Assert.That(theme != null ? resolved : null, Is.SameAs(s_sans));
        }
    }
}
#endif

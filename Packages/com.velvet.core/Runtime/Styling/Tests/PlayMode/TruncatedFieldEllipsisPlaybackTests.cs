#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that <c>truncate</c> on a field elides the input's text while the field is unfocused, as CSS's
    /// text-overflow does on an <c>&lt;input&gt;</c>. Mounted on the runtime theme this project ships rather
    /// than the empty test theme, since the theme is what lays out the text element a single-line field draws.
    /// </summary>
    internal sealed class TruncatedFieldEllipsisPlaybackTests
    {
        private const string ThemePath = "Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss";

        private const string LongValue =
            "Velvet renders declarative user interfaces for Unity UI Toolkit with hooks and components";

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
            yield return null;
        }

        [UnityTest]
        public IEnumerator Given_ATruncatedNarrowFieldHoldingALongValue_When_ItIsNotFocused_Then_ItsTextIsElided()
        {
            // Arrange
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            Assume.That(theme, Is.Not.Null, "Precondition: the project's runtime theme is at " + ThemePath);
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _settings.themeStyleSheet = theme;
            _host = new GameObject(nameof(TruncatedFieldEllipsisPlaybackTests));
            var document = _host.AddComponent<UIDocument>();
            document.panelSettings = _settings;
            yield return null;
            var root = document.rootVisualElement;
            VelvetStyleUtilities.AttachTo(root);

            // Act
            _mounted = V.Mount(root, V.TextField(name: "field", className: "truncate w-[60px]", value: LongValue));
            for (var frame = 0; frame < 5; frame++)
            {
                yield return null;
            }
            var text = root.Q<TextField>("field").Q<TextElement>();

            // Assert
            Assert.That(text.isElided, Is.True,
                $"text width {text.layout.width}, box width {text.parent.layout.width}, "
                + $"text-overflow {text.resolvedStyle.textOverflow}, "
                + $"white-space {text.resolvedStyle.whiteSpace}");
        }
    }
}
#endif

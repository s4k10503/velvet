using System;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <see cref="MotionPresetClasses"/>, the mirror of the bundled <c>anim-*</c> preset classes a Tween a
    /// mount's clock drives reads its plan from, against the stylesheet: each mirrored class gives the values the
    /// sheet declares for it, and the sheet declares no preset class, nor a property of one, the mirror leaves out.
    /// </summary>
    internal sealed class MotionPresetClassesTests
    {
        private const string StyleSheetPath = "Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss";

        private static string Line(string cls, float opacity, float x, float y, float scaleX, float scaleY)
            => string.Join(" ", cls, Number(opacity), Number(x), Number(y), Number(scaleX), Number(scaleY));

        private static string Number(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        // The values a class's tokens give, each slot they leave unnamed at its identity. A uniform scale token
        // gives both axes.
        private static string Mirrored(string cls)
        {
            var (opacity, x, y, scale) = (1f, 0f, 0f, 1f);
            foreach (var token in MotionPresetClasses.Tokens[cls])
            {
                MotionSpringClassParser.TryParseAxisValue(token, out var axis, out var value);
                switch (axis)
                {
                    case SpringAxis.Opacity: opacity = value; break;
                    case SpringAxis.TranslateX: x = value; break;
                    case SpringAxis.TranslateY: y = value; break;
                    case SpringAxis.Scale: scale = value; break;
                }
            }
            return Line(cls, opacity, x, y, scale, scale);
        }

        [Test]
        public void Given_EachMirroredPresetClass_When_TheStylesheetResolvesIt_Then_ItsTokensGiveTheValuesTheSheetDeclares()
        {
            // Arrange
            using var host = new HeadlessEditorPanelHost();
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleSheetPath);
            Assume.That(sheet, Is.Not.Null, "Precondition: the bundled StyleUtilities.uss loads");
            host.Root.styleSheets.Add(sheet);
            var classes = MotionPresetClasses.Tokens.Keys.OrderBy(cls => cls, StringComparer.Ordinal).ToArray();
            var elements = classes.Select(cls =>
            {
                var element = new VisualElement();
                element.AddToClassList(cls);
                host.Root.Add(element);
                return element;
            }).ToArray();

            // Act
            EditorPanelTestHelpers.ForcePanelUpdate(host.Panel);
            var declared = classes.Select((cls, i) =>
            {
                var resolved = elements[i].resolvedStyle;
                return Line(cls, resolved.opacity, resolved.translate.x, resolved.translate.y, resolved.scale.value.x,
                    resolved.scale.value.y);
            }).ToArray();

            // Assert
            Assert.That(classes.Select(Mirrored).ToArray(), Is.EqualTo(declared));
        }

        [Test]
        public void Given_TheBundledStylesheets_When_TheirPresetClassesAreListed_Then_EachIsMirroredAndWritesNothingTheMirrorLeavesOut()
        {
            // Arrange
            var mirroredLonghands = StyleLonghandSet.Of(StyleLonghand.Opacity)
                .Union(StyleLonghandSet.Of(StyleLonghand.Translate))
                .Union(StyleLonghandSet.Of(StyleLonghand.Scale))
                .Union(StyleLonghandSet.Of(StyleLonghand.TransitionProperty));
            var declared = StyleUtilityTableProbe.ClassNames().Where(cls => cls.StartsWith("anim-", StringComparison.Ordinal))
                .ToArray();

            // Act
            var unmirrored = declared.Where(cls => !MotionPresetClasses.Tokens.ContainsKey(cls)
                    || (StyleUtilityProperties.TryGet(cls, out var rule) && !IsWithin(rule.Properties, mirroredLonghands)))
                .Concat(MotionPresetClasses.Tokens.Keys.Where(cls => !declared.Contains(cls)))
                .ToArray();

            // Assert
            Assert.That(unmirrored, Is.Empty);
        }

        private static bool IsWithin(StyleLonghandSet set, StyleLonghandSet bound) => set.Union(bound) == bound;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <see cref="DrivenTweenTiming.Shorthands"/> against UI Toolkit: a transition named by each shorthand
    /// runs every longhand the table says it names, as the native Tween a mount's clock replaces would run it.
    /// </summary>
    internal sealed class DrivenTweenShorthandTests
    {
        // Writes the longhand to a value at the given fraction of its 0..1 (colour) or 0..100 (length) range, and
        // reads back the fraction it resolves at.
        private static readonly Dictionary<StyleLonghand, (Action<IStyle, float> write, Func<IResolvedStyle, float> read)> s_longhands =
            new()
            {
                [StyleLonghand.PaddingTop] = ((s, f) => s.paddingTop = 100f * f, r => r.paddingTop / 100f),
                [StyleLonghand.PaddingRight] = ((s, f) => s.paddingRight = 100f * f, r => r.paddingRight / 100f),
                [StyleLonghand.PaddingBottom] = ((s, f) => s.paddingBottom = 100f * f, r => r.paddingBottom / 100f),
                [StyleLonghand.PaddingLeft] = ((s, f) => s.paddingLeft = 100f * f, r => r.paddingLeft / 100f),
                [StyleLonghand.MarginTop] = ((s, f) => s.marginTop = 100f * f, r => r.marginTop / 100f),
                [StyleLonghand.MarginRight] = ((s, f) => s.marginRight = 100f * f, r => r.marginRight / 100f),
                [StyleLonghand.MarginBottom] = ((s, f) => s.marginBottom = 100f * f, r => r.marginBottom / 100f),
                [StyleLonghand.MarginLeft] = ((s, f) => s.marginLeft = 100f * f, r => r.marginLeft / 100f),
                [StyleLonghand.BorderTopWidth] = ((s, f) => s.borderTopWidth = 100f * f, r => r.borderTopWidth / 100f),
                [StyleLonghand.BorderRightWidth] = ((s, f) => s.borderRightWidth = 100f * f, r => r.borderRightWidth / 100f),
                [StyleLonghand.BorderBottomWidth] = ((s, f) => s.borderBottomWidth = 100f * f, r => r.borderBottomWidth / 100f),
                [StyleLonghand.BorderLeftWidth] = ((s, f) => s.borderLeftWidth = 100f * f, r => r.borderLeftWidth / 100f),
                [StyleLonghand.BorderTopColor] = ((s, f) => s.borderTopColor = new Color(f, f, f), r => r.borderTopColor.r),
                [StyleLonghand.BorderRightColor] = ((s, f) => s.borderRightColor = new Color(f, f, f), r => r.borderRightColor.r),
                [StyleLonghand.BorderBottomColor] = ((s, f) => s.borderBottomColor = new Color(f, f, f), r => r.borderBottomColor.r),
                [StyleLonghand.BorderLeftColor] = ((s, f) => s.borderLeftColor = new Color(f, f, f), r => r.borderLeftColor.r),
                [StyleLonghand.BorderTopLeftRadius] = ((s, f) => s.borderTopLeftRadius = 100f * f, r => r.borderTopLeftRadius / 100f),
                [StyleLonghand.BorderTopRightRadius] = ((s, f) => s.borderTopRightRadius = 100f * f, r => r.borderTopRightRadius / 100f),
                [StyleLonghand.BorderBottomRightRadius] = ((s, f) => s.borderBottomRightRadius = 100f * f, r => r.borderBottomRightRadius / 100f),
                [StyleLonghand.BorderBottomLeftRadius] = ((s, f) => s.borderBottomLeftRadius = 100f * f, r => r.borderBottomLeftRadius / 100f),
                [StyleLonghand.FlexBasis] = ((s, f) => s.flexBasis = 100f * f, r => r.flexBasis.value / 100f),
            };

        [Test]
        public void Given_ATransitionNamingEachShorthand_When_EachLonghandItNamesChanges_Then_TheEngineRunsTheChangeOnThatTransition()
        {
            // Arrange — one element per shorthand and longhand, each with a linear second-long transition on the
            // shorthand alone.
            using var host = new HeadlessEditorPanelHost();
            var now = 100.0;
            EditorPanelTestHelpers.SetPanelTimeFunction(host.Panel, () => now);
            var cases = DrivenTweenTiming.Shorthands
                .SelectMany(entry => s_longhands.Keys.Where(longhand => entry.Value.Contains(longhand))
                    .Select(longhand => (shorthand: entry.Key, longhand, element: new VisualElement())))
                .ToArray();
            foreach (var (shorthand, longhand, element) in cases)
            {
                host.Root.Add(element);
                element.style.transitionProperty = new List<StylePropertyName> { new(shorthand) };
                element.style.transitionDuration = new List<TimeValue> { new(1f) };
                element.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.Linear) };
                s_longhands[longhand].write(element.style, 0f);
            }
            EditorPanelTestHelpers.ForcePanelUpdate(host.Panel);

            // Act — every longhand changes, and the panel paints half a second in.
            foreach (var (_, longhand, element) in cases)
            {
                s_longhands[longhand].write(element.style, 1f);
            }
            EditorPanelTestHelpers.ForcePanelUpdate(host.Panel);
            now += 0.5;
            EditorPanelTestHelpers.DriveAnimationsOnce(host.Panel);
            EditorPanelTestHelpers.ForcePanelUpdate(host.Panel);

            // Assert — every one half way; a longhand the shorthand does not name lands at 1. The table's every
            // longhand has an entry above, so none is left out of the cases.
            var fractions = cases.Select(c => s_longhands[c.longhand].read(c.element.resolvedStyle)).ToArray();
            var covered = DrivenTweenTiming.Shorthands.Values.All(set =>
                Enum.GetValues(typeof(StyleLonghand)).Cast<StyleLonghand>()
                    .Where(set.Contains).All(s_longhands.ContainsKey));
            Assert.That(covered ? fractions : new[] { float.NaN },
                Is.EqualTo(Enumerable.Repeat(0.5f, cases.Length).ToArray()).Within(0.01f));
        }
    }
}

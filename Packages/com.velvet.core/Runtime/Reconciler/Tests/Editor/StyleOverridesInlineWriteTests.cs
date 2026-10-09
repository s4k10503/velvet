using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Every member <see cref="StyleOverrides"/> declares reaches the inline style slot of the same name, on
    /// mount and on both directions of a patch. <c>StyleOverridesLayer.Diff</c> writes the members one by one, so
    /// a member added to the type without its write there compiles and is ignored; these cases enumerate the
    /// type instead.
    /// </summary>
    [TestFixture]
    internal sealed class StyleOverridesInlineWriteTests
    {
        private Texture2D _texture;

        [SetUp]
        public void SetUp()
        {
            _texture = new Texture2D(2, 2);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_texture);
        }

        // GREEN_ON_BASE(construction): compares the type's members with what mounting writes for them.
        // Its perturbation: deleting the `UnitySliceLeft` branch of `StyleOverridesLayer.Diff`.
        [Test]
        public void Given_EveryStyleOverridesMemberSet_When_Mounted_Then_EachLandsInItsInlineSlot()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var problems = new List<string>();
            var overrides = Populated(problems);

            // Act
            reconciler.Reconcile(root, Array.Empty<VNode>(), new VNode[] { V.Div(styles: overrides) });

            // Assert
            problems.AddRange(Mismatches(root.ElementAt(0), overrides, written: true));
            Assert.That(string.Join("; ", problems), Is.Empty);
        }

        // GREEN_ON_BASE(construction): compares the type's members with what a patch adding them writes.
        // Its perturbation: deleting the `UnitySliceType` branch of `StyleOverridesLayer.Diff`.
        [Test]
        public void Given_EveryStyleOverridesMemberAddedByAPatch_When_Reconciled_Then_EachLandsInItsInlineSlot()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var problems = new List<string>();
            var overrides = Populated(problems);
            var oldTree = new VNode[] { V.Div() };
            reconciler.Reconcile(root, Array.Empty<VNode>(), oldTree);

            // Act
            reconciler.Reconcile(root, oldTree, new VNode[] { V.Div(styles: overrides) });

            // Assert
            problems.AddRange(Mismatches(root.ElementAt(0), overrides, written: true));
            Assert.That(string.Join("; ", problems), Is.Empty);
        }

        // GREEN_ON_BASE(construction): compares the type's members with what a patch removing them clears.
        // Its perturbation: deleting the `StyleArbitraryValueResolver.Clear` call from `StyleOverridesLayer.Write`.
        [Test]
        public void Given_EveryStyleOverridesMemberRemovedByAPatch_When_Reconciled_Then_EachInlineSlotIsUnset()
        {
            // Arrange — the slots are read once written as well, so a member that never landed cannot pass for
            // one that was cleared.
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var problems = new List<string>();
            var overrides = Populated(problems);
            var oldTree = new VNode[] { V.Div(styles: overrides) };
            reconciler.Reconcile(root, Array.Empty<VNode>(), oldTree);
            problems.AddRange(Mismatches(root.ElementAt(0), overrides, written: true));

            // Act
            reconciler.Reconcile(root, oldTree, new VNode[] { V.Div() });

            // Assert
            problems.AddRange(Mismatches(root.ElementAt(0), overrides, written: false));
            Assert.That(string.Join("; ", problems), Is.Empty);
        }

        // GREEN_ON_BASE(construction): compares the type's members with what mounting writes for a keyword value.
        // Its perturbation: writing `new StyleInt((int)value)` in place of `new StyleInt(style.Keyword)` in
        // `StyleArbitraryValueResolver.SliceInset`.
        [Test]
        public void Given_EveryStyleOverridesMemberSetToAKeyword_When_Mounted_Then_EachSlotCarriesTheKeyword()
        {
            // Arrange — a keyword reaches each slot through the keyword arm of its write, not the value's.
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var overrides = new StyleOverrides();
            foreach (var member in Members())
            {
                var type = Nullable.GetUnderlyingType(member.PropertyType) ?? member.PropertyType;
                member.SetValue(overrides, Activator.CreateInstance(type, StyleKeyword.Initial));
            }

            // Act
            reconciler.Reconcile(root, Array.Empty<VNode>(), new VNode[] { V.Div(styles: overrides) });

            // Assert — the two members whose inline getters cannot report a keyword are read through the resolved
            // style by the case below.
            var problems = new List<string>();
            foreach (var member in Members())
            {
                if (Array.IndexOf(s_keywordBlindSlots, member.Name) >= 0)
                {
                    continue;
                }
                var slot = typeof(IStyle).GetProperty(char.ToLowerInvariant(member.Name[0]) + member.Name.Substring(1));
                var actual = slot?.GetValue(root.ElementAt(0).style);
                var keyword = actual?.GetType().GetProperty("keyword").GetValue(actual);
                if (!Equals(keyword, StyleKeyword.Initial))
                {
                    problems.Add($"{member.Name}: reads {keyword}");
                }
            }
            Assert.That(string.Join("; ", problems), Is.Empty);
        }

        // Given_AKeywordWrittenToTheImageAndRepeatSlots_When_ReadBack_Then_NeitherReportsIt pins why these two are
        // read through the resolved style instead.
        private static readonly string[] s_keywordBlindSlots =
            { nameof(StyleOverrides.BackgroundImage), nameof(StyleOverrides.BackgroundRepeat) };

        [Test]
        public void Given_AKeywordWrittenToTheImageAndRepeatSlots_When_ReadBack_Then_NeitherReportsIt()
        {
            // Arrange
            var element = new VisualElement();

            // Act
            element.style.backgroundImage = new StyleBackground(StyleKeyword.Initial);
            element.style.backgroundRepeat = new StyleBackgroundRepeat(StyleKeyword.Initial);

            // Assert
            Assert.That((element.style.backgroundImage.keyword, element.style.backgroundRepeat.keyword),
                Is.EqualTo((StyleKeyword.Null, StyleKeyword.Undefined)));
        }

        [Test]
        public void Given_InitialImageAndRepeatOverridesOverAGradient_When_Mounted_Then_TheImageIsHiddenAndTheRepeatWritten()
        {
            // Arrange — a gradient under the image, so an image override that never landed leaves its bake showing.
            // An initial repeat and the zero value its keyword arm would otherwise write resolve alike, so of the
            // repeat this asks only that it was written.
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var overrides = new StyleOverrides
            {
                BackgroundImage = new StyleBackground(StyleKeyword.Initial),
                BackgroundRepeat = new StyleBackgroundRepeat(StyleKeyword.Initial),
            };

            // Act
            reconciler.Reconcile(root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "w-[100px] h-[40px] bg-gradient-to-r from-red-500 to-blue-500", styles: overrides),
            });

            // Assert — the bake exists, so a hidden image is the override's doing.
            var element = root.ElementAt(0);
            var baked = reconciler.Context.GradientBackgrounds.TryGetValue(element, out var gradient)
                && gradient.Texture != null;
            Assert.That((baked, element.resolvedStyle.backgroundImage.texture == null,
                    element.style.backgroundRepeat.keyword != StyleKeyword.Null),
                Is.EqualTo((true, true, true)));
        }

        [Test]
        public void Given_SliceOverrides_When_TheSliceUtilitiesBesideThemChange_Then_TheOverridesKeepTheirSlots()
        {
            // Arrange — the same overrides on both sides, so the patch writes the utilities and not the overrides.
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var styles = new StyleOverrides { UnitySliceTop = 12, UnitySliceScale = 3f };
            var oldTree = new VNode[] { V.Div(className: "slice-[4] slice-scale-[1]", styles: styles) };
            var newTree = new VNode[] { V.Div(className: "slice-[6] slice-scale-[2]", styles: styles) };
            reconciler.Reconcile(root, Array.Empty<VNode>(), oldTree);

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert — top, right, bottom, left, scale: the overrides' top and scale, the utility's other edges.
            var style = root.ElementAt(0).style;
            var read = string.Join(",", style.unitySliceTop.value, style.unitySliceRight.value,
                style.unitySliceBottom.value, style.unitySliceLeft.value,
                style.unitySliceScale.value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.That(read, Is.EqualTo("12,6,6,6,3"));
        }

        private static PropertyInfo[] Members() =>
            typeof(StyleOverrides).GetProperties(BindingFlags.Public | BindingFlags.Instance);

        // A StyleOverrides with every member set to a value distinct from the slot's unset reading. A member of a
        // type this cannot sample is reported rather than skipped.
        private StyleOverrides Populated(List<string> problems)
        {
            var overrides = new StyleOverrides();
            foreach (var member in Members())
            {
                var sample = SampleOf(Nullable.GetUnderlyingType(member.PropertyType) ?? member.PropertyType);
                if (sample == null)
                {
                    problems.Add($"{member.Name}: no sample for {member.PropertyType}");
                    continue;
                }
                member.SetValue(overrides, sample);
            }
            return overrides;
        }

        private object SampleOf(Type type)
        {
            if (type == typeof(StyleBackground)) return new StyleBackground(_texture);
            if (type == typeof(StyleColor)) return new StyleColor(Color.red);
            if (type == typeof(StyleInt)) return new StyleInt(7);
            if (type == typeof(StyleFloat)) return new StyleFloat(2f);
            if (type == typeof(StyleEnum<SliceType>)) return new StyleEnum<SliceType>(SliceType.Tiled);
            if (type == typeof(StyleBackgroundRepeat))
                return new StyleBackgroundRepeat(new BackgroundRepeat(Repeat.Repeat, Repeat.NoRepeat));
            return null;
        }

        // Each member whose inline slot does not read back its sample (written) or the unset keyword (not).
        private static IEnumerable<string> Mismatches(VisualElement element, StyleOverrides overrides, bool written)
        {
            foreach (var member in Members())
            {
                var expected = member.GetValue(overrides);
                if (expected == null)
                {
                    continue;
                }
                var slot = typeof(IStyle).GetProperty(char.ToLowerInvariant(member.Name[0]) + member.Name.Substring(1));
                if (slot == null)
                {
                    yield return $"{member.Name}: no IStyle slot of that name";
                    continue;
                }
                var actual = slot.GetValue(element.style);
                var keyword = (StyleKeyword)actual.GetType().GetProperty("keyword").GetValue(actual);
                if (written ? !Equals(actual, expected) : keyword != StyleKeyword.Null)
                {
                    yield return $"{member.Name}: {(written ? "written" : "cleared")} reads {actual}";
                }
            }
        }
    }
}

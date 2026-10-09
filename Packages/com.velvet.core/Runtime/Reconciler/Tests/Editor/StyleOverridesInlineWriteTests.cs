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
    /// mount (<c>FiberElementFactory.ApplyStyles</c>) and on both directions of a patch
    /// (<c>FiberNodePatcher.DiffStyles</c>). Each of those writes the members one by one, so a member added to
    /// the type without its write there compiles and is ignored; these cases enumerate the type instead.
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
        // Deleting the `UnitySliceLeft` branch of `FiberElementFactory.ApplyStyles` reddens it.
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
        // Deleting the `UnitySliceType` branch of `FiberNodePatcher.DiffStyles` reddens it.
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
        // Writing `newStyles.UnitySliceScale ?? 1f` in the `UnitySliceScale` branch of `FiberNodePatcher.DiffStyles`
        // reddens it.
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

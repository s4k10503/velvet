using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    // A reset helper is a hand-written mirror of the widget's writable surface, and an unpinned mirror
    // drifts silently — the same reason the utility-to-longhand table is derived from the stylesheets
    // rather than restated. So the surface is reflected rather than listed: every public settable
    // instance property the widget or its field/text base chain declares is set away from its value,
    // the helper runs, and nothing may still read what was written. VisualElement's own properties are
    // out of scope because FiberElementPoolReset.ResetCommonState owns them.
    //
    // A property that refuses the write would make its case vacuous, so the refusals are declared and
    // compared both ways: one that starts accepting a write fails here rather than quietly leaving a
    // hole, and so does a new one that refuses.
    internal sealed class PooledElementSurfaceResetTests
    {
        private static readonly Dictionary<string, string> UnsettableOffPanel = new()
        {
            ["TextField.cursorIndex"] = "a caret index does not move off a panel",
            ["TextField.selectIndex"] = "a selection index does not move off a panel",
        };

        private Texture2D _texture;

        [OneTimeSetUp]
        public void CreateTexture() => _texture = new Texture2D(2, 2);

        [OneTimeTearDown]
        public void DestroyTexture() => UnityEngine.Object.DestroyImmediate(_texture);

        private sealed class StubBinding : IBinding
        {
            public void PreUpdate() { }
            public void Update() { }
            public void Release() { }
        }

        private static void NoOp<T>(T _) { }

        // The floors come from reflecting each type, and they are floors rather than counts because a
        // surface that grows is the case this exists for. A surface that shrinks to nothing would make
        // every assertion below trivially true, which is what they guard.
        [TestCase("Button", 10)]
        [TestCase("Label", 8)]
        [TestCase("Toggle", 7)]
        [TestCase("Slider", 12)]
        [TestCase("TextField", 23)]
        public void Given_a_pooled_widget_dirtied_across_its_writable_surface_When_its_reset_helper_runs_Then_no_property_keeps_what_was_written(
            string widget, int floor)
        {
            // Arrange
            var element = Construct(widget);
            var surface = Surface(element.GetType()).ToList();
            var problems = new List<string>();
            var written = Dirty(element, widget, surface, problems);

            // Act
            Reset(widget, element);

            // Assert
            problems.AddRange(surface
                .Where(p => written.TryGetValue(p.Name, out var value) && Equals(Read(p, element), value))
                .Select(p => $"{widget}.{p.Name} kept {Read(p, element)}"));
            Assert.That(
                (string.Join(" | ", problems), surface.Count >= floor),
                Is.EqualTo((string.Empty, true)));
        }

        private Dictionary<string, object> Dirty(
            VisualElement element, string widget, List<PropertyInfo> surface, List<string> problems)
        {
            var written = new Dictionary<string, object>();
            foreach (var property in surface)
            {
                // Chosen off the pristine instance and reused below. Deriving it a second time from an
                // already-dirtied element walks every bool straight back to its default, which then reads
                // as a reset that worked.
                var value = DirtyValue(property, Read(property, element));
                if (value == null) problems.Add($"{widget}.{property.Name} has no dirty value for {property.PropertyType.Name}");
                else written[property.Name] = value;
            }

            // Written twice: a clamped property (Slider.value against lowValue/highValue) only takes once
            // its bounds have moved.
            Write(element, surface, written, problems, widget, report: false);
            Write(element, surface, written, problems, widget, report: true);

            var refused = surface
                .Where(p => written.TryGetValue(p.Name, out var value) && !Equals(Read(p, element), value))
                .Select(p => $"{widget}.{p.Name}")
                .ToList();
            problems.AddRange(refused
                .Where(name => !UnsettableOffPanel.ContainsKey(name))
                .Select(name => $"{name} refused the write, so its case proves nothing"));
            problems.AddRange(UnsettableOffPanel.Keys
                .Where(name => name.StartsWith(widget + ".", StringComparison.Ordinal) && !refused.Contains(name))
                .Select(name => $"{name} now accepts a write and must be scrubbed rather than declared unsettable"));
            return written;
        }

        private static void Write(
            VisualElement element, List<PropertyInfo> surface, Dictionary<string, object> written,
            List<string> problems, string widget, bool report)
        {
            foreach (var property in surface)
            {
                if (!written.TryGetValue(property.Name, out var value)) continue;
                try { property.SetValue(element, value); }
                catch (Exception e)
                {
                    if (report) problems.Add($"{widget}.{property.Name} threw {e.InnerException?.GetType().Name ?? e.GetType().Name}");
                }
            }
        }

        private static VisualElement Construct(string widget) => widget switch
        {
            "Button" => new Button(),
            "Label" => new Label(),
            "Toggle" => new Toggle(),
            "Slider" => new Slider(),
            "TextField" => new TextField(),
            _ => throw new ArgumentOutOfRangeException(nameof(widget), widget, null),
        };

        private static void Reset(string widget, VisualElement element)
        {
            switch (widget)
            {
                case "Button": FiberButtonPoolHelper.ResetButtonForReuse((Button)element); break;
                case "Label": FiberLabelPoolHelper.ResetLabelForReuse((Label)element); break;
                case "Toggle": FiberTogglePoolHelper.ResetToggleForReuse((Toggle)element); break;
                case "Slider": FiberSliderPoolHelper.ResetSliderForReuse((Slider)element); break;
                case "TextField": FiberTextFieldPoolHelper.ResetTextFieldForReuse((TextField)element); break;
                default: throw new ArgumentOutOfRangeException(nameof(widget), widget, null);
            }
        }

        private static IEnumerable<PropertyInfo> Surface(Type type)
        {
            var seen = new HashSet<string>();
            var found = new List<PropertyInfo>();
            for (var current = type; current != null && current != typeof(VisualElement); current = current.BaseType)
            {
                found.AddRange(current
                    .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(p => Eligible(p) && seen.Add(p.Name)));
            }
            return found;
        }

        private static bool Eligible(PropertyInfo property) =>
            property.GetIndexParameters().Length == 0
            && property.GetMethod is { IsPublic: true }
            && property.SetMethod is { IsPublic: true };

        private static object Read(PropertyInfo property, object target)
        {
            try { return property.GetValue(target); }
            catch (Exception e) { return "threw " + e.GetType().Name; }
        }

        private object DirtyValue(PropertyInfo property, object current)
        {
            var type = property.PropertyType;
            if (type == typeof(bool)) return !(bool)current;
            if (type == typeof(string)) return "velvet-dirty";
            if (type == typeof(int)) return (int)current + 7;
            if (type == typeof(float)) return (float)current + 3.5f;
            if (type == typeof(char)) return (char)((char)current + 1);
            if (type.IsEnum) return Enum.GetValues(type).Cast<object>().FirstOrDefault(v => !Equals(v, current));
            if (type == typeof(Background)) return Background.FromTexture2D(_texture);
            if (type == typeof(IBinding)) return new StubBinding();
            if (type == typeof(Clickable)) return new Clickable((Action)null);
            if (typeof(Delegate).IsAssignableFrom(type)) return DelegateOfType(type);
            return null;
        }

        private static object DelegateOfType(Type type)
        {
            var method = typeof(PooledElementSurfaceResetTests)
                .GetMethod(nameof(NoOp), BindingFlags.NonPublic | BindingFlags.Static)
                .MakeGenericMethod(type.GetGenericArguments()[0]);
            return Delegate.CreateDelegate(type, method);
        }
    }
}

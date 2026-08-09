using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Unity.Properties;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    // A reset helper is a hand-written mirror of the widget's writable surface, and an unpinned mirror
    // drifts silently — the same reason the utility-to-longhand table is derived from the stylesheets
    // rather than restated. So the surface is reflected rather than listed: every public settable
    // instance property the widget's whole base chain declares is moved away from what a fresh instance
    // reads, the helper runs, and the element must read back exactly what that fresh instance does.
    //
    // Comparing against a fresh instance rather than against the value written is what makes the wrong
    // default a failure too. It also means a property the dirty pass cannot move is a case that proves
    // nothing, so those are declared and compared both ways: one that starts moving fails here rather
    // than quietly leaving a hole, and so does a new one that stops.
    internal sealed class PooledElementSurfaceResetTests
    {
        private static readonly Dictionary<string, string> Immovable = new()
        {
            ["TextField.cursorIndex"] = "no write here moves it off what a fresh instance reads",
            ["TextField.selectIndex"] = "no write here moves it off what a fresh instance reads",
        };

        // Moved by the dirty pass and then left out of the comparison, each for a reason that holds
        // somewhere else. Nothing may be added here without one.
        private static readonly Dictionary<string, string> NotCompared = new()
        {
            ["Button.clickable"] = "every instance builds its own manipulator, so equality with a fresh one is not a question",
            ["Button.generateVisualContent"] = "TextElement installs its own painter and offers no way to read it back",
            ["Label.generateVisualContent"] = "TextElement installs its own painter and offers no way to read it back",
            ["Slider.showInputField"] = "a slider carrying its input field is refused by the pool instead — SliderPoolAdmissionTests",
            ["Toggle.text"] = "reads empty once the toggle has ever had one, where a fresh instance reads null; neither an empty nor a null write gets back to null",
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
        public void Given_a_pooled_widget_moved_off_its_fresh_state_When_its_reset_helper_runs_Then_it_reads_back_what_a_fresh_instance_does(
            string widget, int floor)
        {
            // Arrange
            var element = Construct(widget);
            var surface = Surface(element.GetType()).ToList();
            var problems = new List<string>();
            var moved = Dirty(element, widget, surface, problems);

            // Act
            Reset(widget, element);

            // Assert
            var fresh = Construct(widget);
            problems.AddRange(surface
                .Where(p => moved.Contains(p.Name) && !NotCompared.ContainsKey(Key(widget, p))
                            && !Equals(Read(p, element), Read(p, fresh)))
                .Select(p => $"{Key(widget, p)} is {Read(p, element)}, fresh is {Read(p, fresh)}"));
            Assert.That(
                (string.Join(" | ", problems), surface.Count >= floor),
                Is.EqualTo((string.Empty, true)));
        }

        private static string Key(string widget, PropertyInfo property) => widget + "." + property.Name;

        // Returns the properties the dirty pass actually moved away from a fresh instance. One it could
        // not move would make its case below vacuous, so it is either declared immovable or a problem.
        private HashSet<string> Dirty(
            VisualElement element, string widget, List<PropertyInfo> surface, List<string> problems)
        {
            var pristine = Construct(widget);
            var written = new Dictionary<string, object>();
            foreach (var property in surface)
            {
                // Chosen off a pristine instance and reused below. Deriving it a second time from an
                // already-dirtied element walks every bool straight back to its default, which then reads
                // as a reset that worked.
                var value = DirtyValue(property, Read(property, pristine));
                if (value == null) problems.Add($"{Key(widget, property)} has no dirty value for {property.PropertyType.Name}");
                else written[property.Name] = value;
            }

            // Written twice because Type.GetProperties does not promise an order, and one property's write
            // is refused until another has been made: the scroller visibility does not store until
            // multiline is on. A single pass would land those only when the metadata happened to favour it.
            Write(element, surface, written);
            Write(element, surface, written);

            var moved = surface
                .Where(p => written.ContainsKey(p.Name) && !Equals(Read(p, element), Read(p, pristine)))
                .Select(p => p.Name)
                .ToHashSet();
            problems.AddRange(surface
                .Where(p => written.ContainsKey(p.Name) && !moved.Contains(p.Name) && !Immovable.ContainsKey(Key(widget, p)))
                .Select(p => $"{Key(widget, p)} did not move off its fresh value, so its case proves nothing"));
            problems.AddRange(Immovable.Keys
                .Where(name => name.StartsWith(widget + ".", StringComparison.Ordinal)
                               && moved.Contains(name[(widget.Length + 1)..]))
                .Select(name => $"{name} now moves and must be scrubbed rather than declared immovable"));
            return moved;
        }

        private static void Write(VisualElement element, List<PropertyInfo> surface, Dictionary<string, object> written)
        {
            foreach (var property in surface)
            {
                if (!written.TryGetValue(property.Name, out var value)) continue;
                try { property.SetValue(element, value); }
                catch (Exception) { /* a refusal shows up as a property that did not move */ }
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
            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
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
            if (type == typeof(object)) return new object();
            if (type == typeof(Type)) return typeof(string);
            if (type == typeof(PropertyPath)) return new PropertyPath("velvet-dirty");
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

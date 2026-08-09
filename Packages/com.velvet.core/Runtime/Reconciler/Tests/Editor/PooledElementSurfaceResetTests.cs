using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Properties;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    // A reset helper is a hand-written mirror of the widget's writable surface, and an unpinned mirror
    // drifts silently — the same reason the utility-to-longhand table is derived from the stylesheets
    // rather than restated. So the surface is reflected rather than listed: every public settable
    // instance property the widget's whole base chain declares, plus every one behind a get-only handle
    // onto an interface, is moved away from what a fresh instance reads, the helper runs, and the element
    // must read back exactly what that fresh instance does.
    //
    // Comparing against a fresh instance rather than against the value written is what makes the wrong
    // default a failure too. It also means a property the dirty pass cannot move is a case that proves
    // nothing, so those are declared and compared both ways: one that starts moving fails here rather
    // than quietly leaving a hole, and so does a new one that stops.
    //
    // What stays out of reach: anything structural. Sub-elements created or destroyed, class lists,
    // children, callbacks and manipulators are all invisible to a property comparison, and
    // FiberElementPoolReset's limitations block is where that is stated.
    internal sealed class PooledElementSurfaceResetTests
    {
        // All three read through focusable, which the dirty pass leaves false on a Button — its constructed
        // value is true, and the base-chain walk reaches Focusable last. A Label's counterparts do move, and
        // the scrub they exercise is the same line, so what is lost here is a second exercise of it.
        private static readonly Dictionary<string, string> Immovable = new()
        {
            ["Button.selection.isSelectable"] = "reads through a focusable the dirty pass leaves false",
            ["Button.selection.cursorIndex"] = "reads through a focusable the dirty pass leaves false",
            ["Button.selection.selectIndex"] = "reads through a focusable the dirty pass leaves false",
        };

        // Moved by the dirty pass and then left out of the comparison, each for a reason that holds
        // somewhere else. Nothing may be added here without one, and an entry that stops being needed
        // fails below rather than sitting on forever.
        private static readonly Dictionary<string, string> NotCompared = new()
        {
            ["Button.clickable"] = "every instance builds its own manipulator, so equality with a fresh one is not a question",
            ["Slider.showInputField"] = "a slider carrying its input field is refused by the pool instead — SliderPoolAdmissionTests",
            ["Toggle.text"] = "reads empty once the toggle has ever had one, where a fresh instance reads null; neither an empty nor a null write gets back to null",
        };

        // Get-only handles the walk does not descend into. Each is somebody else's subject.
        private static readonly Dictionary<string, string> NotDescended = new()
        {
            ["style"] = "the inline scrub is enumerated in FiberElementPoolReset.ResetInlineStyle and pinned by PooledElementStyleGhostTests",
            ["schedule"] = "a handle onto items only their owner holds, which FiberElementPoolReset's limitations state is not reachable",
            ["experimental"] = "reaches the same animation and style surfaces from a second direction",
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

        // A property of the element, or of something a get-only handle on the element leads to.
        private sealed class Slot
        {
            public string Name;
            public Type Type;
            public Func<VisualElement, object> Read;
            public Action<VisualElement, object> Write;
        }

        // The floors are the measured slot count of each widget, and they are tight on purpose. Twice now a
        // narrowing went unnoticed because the number below had room in it: once when the walk stopped at
        // VisualElement, and once when these were left at the counts from before it descended into handles.
        // A surface that grows still passes here and is then compared like any other slot.
        [TestCase("Button", 40)]
        [TestCase("Label", 38)]
        [TestCase("Toggle", 28)]
        [TestCase("Slider", 33)]
        [TestCase("TextField", 64)]
        public void Given_a_pooled_widget_moved_off_its_fresh_state_When_its_reset_helper_runs_Then_it_reads_back_what_a_fresh_instance_does(
            string widget, int floor)
        {
            // Arrange
            var element = Construct(widget);
            var surface = Surface(element);
            var problems = new List<string>();
            var moved = Dirty(element, widget, surface, problems);

            // Act
            Reset(widget, element);

            // Assert
            var fresh = Construct(widget);
            foreach (var slot in surface.Where(s => moved.Contains(s.Name)))
            {
                var same = Equals(Read(slot, element), Read(slot, fresh));
                var declared = NotCompared.ContainsKey(Key(widget, slot));
                if (!same && !declared) problems.Add($"{Key(widget, slot)} is {Read(slot, element)}, fresh is {Read(slot, fresh)}");
                if (same && declared) problems.Add($"{Key(widget, slot)} now matches a fresh instance and no longer needs its exclusion");
            }
            Assert.That(
                (string.Join(" | ", problems), surface.Count >= floor),
                Is.EqualTo((string.Empty, true)));
        }

        private static string Key(string widget, Slot slot) => widget + "." + slot.Name;

        // Returns the slots the dirty pass actually moved away from a fresh instance. One it could not move
        // would make its case above vacuous, so it is either declared immovable or a problem.
        private HashSet<string> Dirty(VisualElement element, string widget, List<Slot> surface, List<string> problems)
        {
            var pristine = Construct(widget);
            var written = new Dictionary<string, object>();
            foreach (var slot in surface)
            {
                // Chosen off a pristine instance and reused below. Deriving it a second time from an
                // already-dirtied element walks every bool straight back to its default, which then reads
                // as a reset that worked.
                var value = DirtyValue(slot, Read(slot, pristine));
                if (value == null) problems.Add($"{Key(widget, slot)} has no dirty value for {slot.Type.Name}");
                else written[slot.Name] = value;
            }

            // Written twice because reflection does not promise an order, and one property's write is
            // refused until another has been made: the scroller visibility does not store until multiline is
            // on. A single pass would land those only when the metadata happened to favour it.
            Write(element, surface, written);
            Write(element, surface, written);

            var moved = surface
                .Where(s => written.ContainsKey(s.Name) && !Equals(Read(s, element), Read(s, pristine)))
                .Select(s => s.Name)
                .ToHashSet();
            problems.AddRange(surface
                .Where(s => written.ContainsKey(s.Name) && !moved.Contains(s.Name) && !Immovable.ContainsKey(Key(widget, s)))
                .Select(s => $"{Key(widget, s)} did not move off its fresh value, so its case proves nothing"));
            problems.AddRange(Immovable.Keys
                .Where(name => name.StartsWith(widget + ".", StringComparison.Ordinal)
                               && moved.Contains(name[(widget.Length + 1)..]))
                .Select(name => $"{name} now moves and must be scrubbed rather than declared immovable"));
            return moved;
        }

        private static void Write(VisualElement element, List<Slot> surface, Dictionary<string, object> written)
        {
            foreach (var slot in surface)
            {
                if (!written.TryGetValue(slot.Name, out var value)) continue;
                try { slot.Write(element, value); }
                catch (Exception) { /* a refusal shows up as a slot that did not move */ }
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

        private static List<Slot> Surface(VisualElement element)
        {
            var slots = new List<Slot>();
            var seen = new HashSet<string>();
            foreach (var property in Declared(element.GetType()))
            {
                if (Settable(property) && seen.Add(property.Name))
                {
                    var captured = property;
                    slots.Add(new Slot
                    {
                        Name = captured.Name,
                        Type = captured.PropertyType,
                        Read = e => captured.GetValue(e),
                        Write = (e, v) => captured.SetValue(e, v),
                    });
                    continue;
                }
                if (property.SetMethod != null || !property.PropertyType.IsInterface) continue;
                if (NotDescended.ContainsKey(property.Name) || !seen.Add(property.Name)) continue;
                slots.AddRange(Behind(property));
            }
            return slots;
        }

        // The settable properties of whatever a get-only handle leads to. TextField reaches its placeholder,
        // its caret and its selection colours only this way, and a walk over the element's own properties
        // sees none of them.
        private static IEnumerable<Slot> Behind(PropertyInfo handle)
        {
            foreach (var inner in handle.PropertyType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!Settable(inner)) continue;
                var capturedHandle = handle;
                var capturedInner = inner;
                yield return new Slot
                {
                    Name = capturedHandle.Name + "." + capturedInner.Name,
                    Type = capturedInner.PropertyType,
                    Read = e => capturedInner.GetValue(capturedHandle.GetValue(e)),
                    Write = (e, v) => capturedInner.SetValue(capturedHandle.GetValue(e), v),
                };
            }
        }

        private static IEnumerable<PropertyInfo> Declared(Type type)
        {
            for (var current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                foreach (var property in current.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (property.GetIndexParameters().Length == 0 && property.GetMethod is { IsPublic: true })
                    {
                        yield return property;
                    }
                }
            }
        }

        private static bool Settable(PropertyInfo property) =>
            property.GetIndexParameters().Length == 0
            && property.GetMethod is { IsPublic: true }
            && property.SetMethod is { IsPublic: true };

        private static object Read(Slot slot, VisualElement element)
        {
            try { return slot.Read(element); }
            catch (Exception e) { return "threw " + (e.InnerException?.GetType().Name ?? e.GetType().Name); }
        }

        private object DirtyValue(Slot slot, object current)
        {
            var type = slot.Type;
            if (type == typeof(bool)) return !(bool)current;
            if (type == typeof(string)) return "velvet-dirty";
            if (type == typeof(int)) return (int)current + 7;
            if (type == typeof(float)) return (float)current + 3.5f;
            if (type == typeof(char)) return (char)((char)current + 1);
            if (type == typeof(object)) return new object();
            if (type == typeof(Type)) return typeof(string);
            if (type == typeof(Color)) return new Color(1f, 0f, 1f, 1f);
            if (type == typeof(Vector3)) return (Vector3)current + new Vector3(3f, 5f, 7f);
            if (type == typeof(Quaternion)) return Quaternion.Euler(11f, 13f, 17f);
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

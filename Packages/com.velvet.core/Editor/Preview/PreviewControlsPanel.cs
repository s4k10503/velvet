using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Editor.Preview
{
    /// <summary>
    /// The "controls" addon for the preview window: reflects a story's args type into a column of
    /// typed editor knobs (Toggle / IntegerField / FloatField / TextField / EnumField / ColorField), holding one
    /// live args instance. Editing a knob writes back into that instance and raises <see cref="ArgsChanged"/> so
    /// the window re-mounts the story with the edited args.
    /// </summary>
    internal sealed class PreviewControlsPanel : VisualElement
    {
        private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public;

        private readonly Label _heading;
        private readonly VisualElement _rows;

        // The live args instance the knobs mutate, or null for a parameterless story.
        private object _args;

        /// <summary>Raised with the live args instance whenever a control changes; the window re-mounts with it.</summary>
        public event Action<object> ArgsChanged;

        /// <summary>The live args instance currently driven by the controls (null for a parameterless story).</summary>
        public object Args => _args;

        public PreviewControlsPanel()
        {
            style.flexGrow = 1f;
            style.minHeight = 80f;

            _heading = new Label("Controls")
            {
                style =
                {
                    unityFontStyleAndWeight = FontStyle.Bold,
                    paddingLeft = 8f, paddingTop = 4f, paddingBottom = 4f,
                },
            };
            Add(_heading);

            var scroll = new ScrollView { style = { flexGrow = 1f } };
            _rows = new VisualElement { style = { paddingLeft = 8f, paddingRight = 8f, paddingBottom = 8f } };
            scroll.Add(_rows);
            Add(scroll);
        }

        /// <summary>
        /// Rebuilds the controls for <paramref name="story"/>, constructing a fresh default args instance so each
        /// selected story starts from its declared defaults. A parameterless / null story shows an empty state.
        /// </summary>
        public void SetStory(VelvetPreviewStory story)
        {
            _rows.Clear();
            _args = null;

            if (story?.ArgsType == null)
            {
                _heading.text = "Controls";
                _rows.Add(new Label("No controls for this story.") { style = { color = MutedColor() } });
                return;
            }

            _heading.text = "Controls — " + story.ArgsType.Name;

            // Default-constructing the args runs the type's ctor / field initializers, which can throw; surface
            // that as a note row instead of letting it escape the selectionChanged callback and break selection.
            try
            {
                _args = story.CreateDefaultArgs();
            }
            catch (Exception ex)
            {
                _rows.Add(new Label($"Could not create args ({story.ArgsType.Name}): {ex.Message}")
                {
                    style = { color = MutedColor(), whiteSpace = WhiteSpace.Normal },
                });
                return;
            }

            foreach (var field in story.ArgsType.GetFields(MemberFlags))
            {
                if (field.IsInitOnly || field.IsLiteral) continue;
                AddRow(field.Name, field.FieldType, () => field.GetValue(_args), v => field.SetValue(_args, v));
            }

            foreach (var property in story.ArgsType.GetProperties(MemberFlags))
            {
                if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0) continue;
                AddRow(property.Name, property.PropertyType, () => property.GetValue(_args), v => property.SetValue(_args, v));
            }
        }

        private void AddRow(string label, Type type, Func<object> get, Action<object> set)
        {
            var field = BuildField(label, type, get(), value =>
            {
                set(value);
                ArgsChanged?.Invoke(_args);
            });

            if (field == null)
            {
                _rows.Add(new Label($"{label}  ({type.Name}: unsupported type)") { style = { color = MutedColor() } });
                return;
            }

            _rows.Add(field);
        }

        // Maps a member type to its editor control, wiring the change callback. Returns null for an unsupported
        // type so the caller can show a read-only note instead of crashing.
        private static VisualElement BuildField(string label, Type type, object current, Action<object> onChange)
        {
            if (type == typeof(bool))
            {
                var f = new Toggle(label) { value = (bool)current };
                f.RegisterValueChangedCallback(e => onChange(e.newValue));
                return f;
            }

            if (type == typeof(int))
            {
                var f = new IntegerField(label) { value = (int)current };
                f.RegisterValueChangedCallback(e => onChange(e.newValue));
                return f;
            }

            if (type == typeof(float))
            {
                var f = new FloatField(label) { value = (float)current };
                f.RegisterValueChangedCallback(e => onChange(e.newValue));
                return f;
            }

            if (type == typeof(string))
            {
                var f = new TextField(label) { value = (string)current ?? string.Empty };
                f.RegisterValueChangedCallback(e => onChange(e.newValue));
                return f;
            }

            if (type.IsEnum)
            {
                var f = new EnumField(label, (Enum)current);
                f.RegisterValueChangedCallback(e => onChange(e.newValue));
                return f;
            }

            if (type == typeof(Color))
            {
                var f = new ColorField(label) { value = (Color)current };
                f.RegisterValueChangedCallback(e => onChange(e.newValue));
                return f;
            }

            return null;
        }

        private static Color MutedColor() => new(0.6f, 0.6f, 0.6f, 1f);
    }
}

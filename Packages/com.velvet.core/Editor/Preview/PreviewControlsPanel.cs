using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Editor.Preview
{
    /// <summary>
    /// The "controls" addon for the preview window: reflects a story's args type into a column of typed editor
    /// knobs, holding one live args instance. Editing a knob writes back into that instance and raises
    /// <see cref="ArgsChanged"/> so the window re-renders the story with the edited args.
    /// </summary>
    internal sealed class PreviewControlsPanel : VisualElement
    {
        private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public;

        // Storybook's inferred enum control is a radio group up to this many options and a select above it.
        private const int MaxRadioOptions = 5;

        private const string DateFormat = "yyyy-MM-dd HH:mm:ss";

        private readonly Label _heading;
        private readonly VisualElement _rows;

        // The live args instance the knobs mutate, or null for a parameterless story.
        private object _args;

        /// <summary>Raised with the live args instance whenever a control changes; the window re-renders with it.</summary>
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
                _rows.Add(Note("No controls for this story."));
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
                _rows.Add(Note($"Could not create args ({story.ArgsType.Name}): {ex.Message}"));
                return;
            }

            AddMemberRows(_rows, _args, story.ArgsType, () => ArgsChanged?.Invoke(_args));
        }

        // A nested owner is written back through changed(), so an edit inside a struct reaches the args instance
        // holding it.
        private static void AddMemberRows(VisualElement container, object owner, Type type, Action changed)
        {
            foreach (var field in type.GetFields(MemberFlags))
            {
                if (field.IsInitOnly || field.IsLiteral) continue;
                AddRow(container, field.Name, field.FieldType, field, field.GetValue(owner), value =>
                {
                    field.SetValue(owner, value);
                    changed();
                });
            }

            foreach (var property in type.GetProperties(MemberFlags))
            {
                if (!IsEditable(property)) continue;
                AddRow(container, property.Name, property.PropertyType, property, property.GetValue(owner), value =>
                {
                    property.SetValue(owner, value);
                    changed();
                });
            }
        }

        private static bool IsEditable(PropertyInfo property) =>
            property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0;

        private static void AddRow(
            VisualElement container, string label, Type type, MemberInfo member, object current, Action<object> onChange)
        {
            var range = member?.GetCustomAttribute<RangeAttribute>();
            container.Add(BuildField(label, type, range, current, onChange)
                          ?? Note($"{label}  ({type.Name}: unsupported type)"));
        }

        // Returns null for a type no control edits, so the caller shows a read-only note instead.
        private static VisualElement BuildField(
            string label, Type type, RangeAttribute range, object current, Action<object> onChange)
        {
            if (range != null && (type == typeof(int) || type == typeof(float)))
            {
                return BuildRange(label, type, range, current, onChange);
            }

            if (type == typeof(bool)) return Bind(new Toggle(label), (bool)current, onChange);
            if (type == typeof(int)) return Bind(new IntegerField(label), (int)current, onChange);
            if (type == typeof(long)) return Bind(new LongField(label), (long)current, onChange);
            if (type == typeof(float)) return Bind(new FloatField(label), (float)current, onChange);
            if (type == typeof(double)) return Bind(new DoubleField(label), (double)current, onChange);
            if (type == typeof(string)) return Bind(new TextField(label), (string)current ?? string.Empty, onChange);
            if (type == typeof(Color)) return Bind(new ColorField(label), (Color)current, onChange);
            if (type == typeof(DateTime)) return BuildDate(label, (DateTime)current, onChange);
            if (type.IsEnum) return BuildEnum(label, type, (Enum)current, onChange);
            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                var picker = new ObjectField(label) { objectType = type, allowSceneObjects = false };
                return Bind(picker, (UnityEngine.Object)current, onChange);
            }

            return BuildObject(label, type, current, onChange);
        }

        private static VisualElement Bind<T>(BaseField<T> field, T current, Action<object> onChange)
        {
            field.SetValueWithoutNotify(current);
            field.RegisterValueChangedCallback(e => onChange(e.newValue));
            return field;
        }

        private static VisualElement BuildRange(
            string label, Type type, RangeAttribute range, object current, Action<object> onChange) =>
            type == typeof(int)
                ? Bind(new SliderInt(label, (int)range.min, (int)range.max) { showInputField = true }, (int)current, onChange)
                : Bind(new Slider(label, range.min, range.max) { showInputField = true }, (float)current, onChange);

        // Text that does not parse as a date leaves the args value where it was.
        private static VisualElement BuildDate(string label, DateTime current, Action<object> onChange)
        {
            var field = new TextField(label);
            field.SetValueWithoutNotify(current.ToString(DateFormat, CultureInfo.InvariantCulture));
            field.RegisterValueChangedCallback(e =>
            {
                if (DateTime.TryParse(e.newValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                {
                    onChange(parsed);
                }
            });
            return field;
        }

        private static VisualElement BuildEnum(string label, Type type, Enum current, Action<object> onChange)
        {
            if (type.IsDefined(typeof(FlagsAttribute)))
            {
                return Bind(new EnumFlagsField(label, current), current, onChange);
            }

            var values = Enum.GetValues(type);
            if (values.Length > MaxRadioOptions) return Bind(new EnumField(label, current), current, onChange);

            var radio = new RadioButtonGroup(label, new List<string>(Enum.GetNames(type)));
            radio.SetValueWithoutNotify(Array.IndexOf(values, current));
            radio.RegisterValueChangedCallback(e => onChange(values.GetValue(e.newValue)));
            return radio;
        }

        // Storybook's object control, which also edits arrays: a foldout of the value's own controls.
        private static VisualElement BuildObject(string label, Type type, object current, Action<object> onChange)
        {
            var elementType = ElementTypeOf(type);
            if (elementType == null && !HasEditableMembers(type)) return null;

            var foldout = new Foldout { text = label };
            FillObject(foldout, type, elementType, current, onChange);
            return foldout;
        }

        private static void FillObject(
            Foldout foldout, Type type, Type elementType, object current, Action<object> onChange)
        {
            foldout.Clear();
            if (current == null)
            {
                AddCreateButton(foldout, type, elementType, onChange);
                return;
            }

            if (elementType == null)
            {
                AddMemberRows(foldout, current, type, () => onChange(current));
                return;
            }

            AddElementRows(foldout, type, elementType, (IList)current, onChange);
        }

        private static void AddCreateButton(Foldout foldout, Type type, Type elementType, Action<object> onChange)
        {
            if (!type.IsArray && type.GetConstructor(Type.EmptyTypes) == null)
            {
                foldout.Add(Note("null"));
                return;
            }

            foldout.Add(new Button(() =>
            {
                var created = type.IsArray ? Array.CreateInstance(elementType, 0) : Activator.CreateInstance(type);
                onChange(created);
                FillObject(foldout, type, elementType, created, onChange);
            }) { text = "Set object" });
        }

        private static void AddElementRows(
            Foldout foldout, Type type, Type elementType, IList list, Action<object> onChange)
        {
            var length = new IntegerField("Length");
            length.SetValueWithoutNotify(list.Count);
            length.RegisterValueChangedCallback(e =>
            {
                var resized = Resize(list, type, elementType, Math.Max(0, e.newValue));
                onChange(resized);
                FillObject(foldout, type, elementType, resized, onChange);
            });
            foldout.Add(length);

            for (var i = 0; i < list.Count; i++)
            {
                var index = i;
                AddRow(foldout, $"[{i}]", elementType, null, list[i], value =>
                {
                    list[index] = value;
                    onChange(list);
                });
            }
        }

        private static IList Resize(IList list, Type type, Type elementType, int length)
        {
            if (type.IsArray)
            {
                var array = Array.CreateInstance(elementType, length);
                Array.Copy((Array)list, array, Math.Min(length, list.Count));
                return array;
            }

            while (list.Count > length) list.RemoveAt(list.Count - 1);
            while (list.Count < length) list.Add(elementType.IsValueType ? Activator.CreateInstance(elementType) : null);
            return list;
        }

        private static Type ElementTypeOf(Type type)
        {
            if (type.IsArray) return type.GetArrayRank() == 1 ? type.GetElementType() : null;
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)
                ? type.GetGenericArguments()[0]
                : null;
        }

        private static bool HasEditableMembers(Type type)
        {
            foreach (var field in type.GetFields(MemberFlags))
            {
                if (!field.IsInitOnly && !field.IsLiteral) return true;
            }

            return Array.Exists(type.GetProperties(MemberFlags), IsEditable);
        }

        private static Label Note(string text) =>
            new(text) { style = { color = new Color(0.6f, 0.6f, 0.6f, 1f), whiteSpace = WhiteSpace.Normal } };
    }
}

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
    /// knobs. Editing a knob replaces the args instance with an edited copy and raises <see cref="ArgsChanged"/>
    /// with it, so the window re-renders the story with the edited args.
    /// </summary>
    internal sealed class PreviewControlsPanel : VisualElement
    {
        private const BindingFlags MemberFlags = BindingFlags.Instance | BindingFlags.Public;

        // Storybook's inferred enum control is a radio group up to this many options and a select above it.
        private const int MaxRadioOptions = 5;

        private const string DateFormat = "yyyy-MM-dd HH:mm:ss";

        private static readonly MethodInfo s_memberwiseClone =
            typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

        private readonly Label _heading;
        private readonly VisualElement _rows;

        // The current args instance, replaced by every edit, or null for a parameterless story.
        private object _args;

        /// <summary>Raised with the new args instance whenever a control changes; the window re-renders with it.</summary>
        public event Action<object> ArgsChanged;

        /// <summary>The current args instance (null for a parameterless story).</summary>
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

            var root = new Slot(story.ArgsType, () => _args, ReplaceArgs, null);
            AddMemberRows(_rows, root, new Chain(_args, null));
        }

        private void ReplaceArgs(object args)
        {
            _args = args;
            ArgsChanged?.Invoke(args);
        }

        // An edit writes a copy of every container on the path from the args down to the edited member, so the
        // story gets a new args instance and new containers along that path, as Storybook replaces args rather
        // than mutating them. No container the story was handed before is changed.
        private static void AddMemberRows(VisualElement container, Slot owner, Chain ancestors)
        {
            foreach (var field in owner.Type.GetFields(MemberFlags))
            {
                if (field.IsInitOnly || field.IsLiteral) continue;
                var read = (Func<object>)(() => field.GetValue(owner.Read()));
                AddRow(container, field.Name, field, new Slot(field.FieldType, read, value =>
                {
                    var copy = ShallowCopy(owner.Read());
                    field.SetValue(copy, value);
                    owner.Write(copy);
                }, ancestors));
            }

            foreach (var property in owner.Type.GetProperties(MemberFlags))
            {
                if (!IsEditable(property)) continue;
                var read = (Func<object>)(() => property.GetValue(owner.Read()));
                AddRow(container, property.Name, property, new Slot(property.PropertyType, read, value =>
                {
                    var copy = ShallowCopy(owner.Read());
                    property.SetValue(copy, value);
                    owner.Write(copy);
                }, ancestors));
            }
        }

        // A List<T> copied member-wise would share its backing array with the original, so it is rebuilt instead.
        private static object ShallowCopy(object value) =>
            value is IList && !value.GetType().IsArray
                ? Activator.CreateInstance(value.GetType(), value)
                : s_memberwiseClone.Invoke(value, null);

        private static bool IsEditable(PropertyInfo property) =>
            property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0;

        private static void AddRow(VisualElement container, string label, MemberInfo member, Slot slot)
        {
            var range = member?.GetCustomAttribute<RangeAttribute>();
            container.Add(BuildField(label, slot, range) ?? Note($"{label}  ({slot.Type.Name}: unsupported type)"));
        }

        // Returns null for a type no control edits, so the caller shows a read-only note instead.
        private static VisualElement BuildField(string label, Slot slot, RangeAttribute range)
        {
            var type = slot.Type;
            var current = slot.Read();
            var onChange = slot.Write;
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

            return BuildObject(label, slot);
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

        // Storybook's object control, which also edits arrays: a foldout of the value's own controls, built when
        // it is first expanded so a graph that refers back to itself builds none of its controls until opened.
        private static VisualElement BuildObject(string label, Slot slot)
        {
            var elementType = ElementTypeOf(slot.Type);
            if (elementType == null && !HasEditableMembers(slot.Type)) return null;

            var foldout = new Foldout { text = label, value = false };
            foldout.RegisterValueChangedCallback(_ =>
            {
                if (foldout.contentContainer.childCount == 0) FillObject(foldout, slot, elementType);
            });
            return foldout;
        }

        private static void FillObject(Foldout foldout, Slot slot, Type elementType)
        {
            foldout.Clear();
            var current = slot.Read();
            if (current == null)
            {
                AddCreateButton(foldout, slot, elementType);
                return;
            }

            if (Chain.Holds(slot.Ancestors, current))
            {
                foldout.Add(Note("cycle"));
                return;
            }

            var ancestors = new Chain(current, slot.Ancestors);
            if (elementType == null)
            {
                AddMemberRows(foldout, slot, ancestors);
                return;
            }

            AddElementRows(foldout, slot, elementType, ancestors);
        }

        private static void AddCreateButton(Foldout foldout, Slot slot, Type elementType)
        {
            var type = slot.Type;
            if (!type.IsArray && (type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null))
            {
                foldout.Add(Note("null"));
                return;
            }

            foldout.Add(new Button(() =>
            {
                slot.Write(type.IsArray ? Array.CreateInstance(elementType, 0) : Activator.CreateInstance(type));
                FillObject(foldout, slot, elementType);
            }) { text = "Set object" });
        }

        private static void AddElementRows(Foldout foldout, Slot slot, Type elementType, Chain ancestors)
        {
            var count = ((IList)slot.Read()).Count;
            var length = new IntegerField("Length");
            length.SetValueWithoutNotify(count);
            length.RegisterValueChangedCallback(e =>
            {
                slot.Write(Resized((IList)slot.Read(), slot.Type, elementType, Math.Max(0, e.newValue)));
                FillObject(foldout, slot, elementType);
            });
            foldout.Add(length);

            for (var i = 0; i < count; i++)
            {
                var index = i;
                AddRow(foldout, $"[{i}]", null, new Slot(elementType, () => ((IList)slot.Read())[index], value =>
                {
                    var copy = (IList)ShallowCopy(slot.Read());
                    copy[index] = value;
                    slot.Write(copy);
                }, ancestors));
            }
        }

        private static IList Resized(IList list, Type type, Type elementType, int length)
        {
            if (type.IsArray)
            {
                var array = Array.CreateInstance(elementType, length);
                Array.Copy((Array)list, array, Math.Min(length, list.Count));
                return array;
            }

            var copy = (IList)ShallowCopy(list);
            while (copy.Count > length) copy.RemoveAt(copy.Count - 1);
            while (copy.Count < length) copy.Add(elementType.IsValueType ? Activator.CreateInstance(elementType) : null);
            return copy;
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

        // One editable location in the args graph: how to read its current value, and how to hand its owner a
        // replacement for it.
        private sealed record Slot(Type Type, Func<object> Read, Action<object> Write, Chain Ancestors);

        // The objects above a location, so a graph that refers back to an ancestor is noted rather than walked.
        private sealed record Chain(object Value, Chain Parent)
        {
            public static bool Holds(Chain chain, object value)
            {
                for (var link = chain; link != null; link = link.Parent)
                {
                    if (ReferenceEquals(link.Value, value)) return true;
                }

                return false;
            }
        }

        private static Label Note(string text) =>
            new(text) { style = { color = new Color(0.6f, 0.6f, 0.6f, 1f), whiteSpace = WhiteSpace.Normal } };
    }
}

#nullable enable
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // A declared maxLength is not written onto the field. While one stands, a key or a paste may insert
    // only what fits at the caret. TextFieldMaxLengthInputTests pins both.
    // Ordering: the guard is trickle-down on the input, so it runs after a key handler on the field and
    // before the engine's bubble insert. A paste that does not fit is shortened on the clipboard and put
    // back on this element's bubble pass, which runs after the engine reads it.
    internal static class FiberTextFieldLength
    {
        private static readonly ConditionalWeakTable<TextField, Limit> s_byField = new();
        private static readonly ConditionalWeakTable<TextElement, Limit> s_byInput = new();
        private static readonly EventCallback<KeyDownEvent> s_onKeyDown = OnKeyDown;
        private static readonly EventCallback<ExecuteCommandEvent> s_onCommand = OnCommand;
        private static readonly EventCallback<KeyDownEvent> s_restoreKey = RestoreKey;
        private static readonly EventCallback<ExecuteCommandEvent> s_restoreCommand = RestoreCommand;

        private sealed class Limit
        {
            public int Value;
            public bool Stashed;
            public string? Clipboard;
            public TextField Field = null!;
        }

        public static void Sync(TextField field, int? limit)
        {
            if (limit is not { } value || value < 0)
            {
                Forget(field);
                return;
            }

            if (s_byField.TryGetValue(field, out var existing))
            {
                existing.Value = value;
                return;
            }

            var input = (TextElement)field.textEdition;
            var box = new Limit { Value = value, Field = field };
            s_byField.Add(field, box);
            s_byInput.Add(input, box);
            input.RegisterCallback(s_onKeyDown, TrickleDown.TrickleDown);
            input.RegisterCallback(s_onCommand, TrickleDown.TrickleDown);
            input.RegisterCallback(s_restoreKey);
            input.RegisterCallback(s_restoreCommand);
        }

        public static void Forget(TextField field)
        {
            if (!s_byField.TryGetValue(field, out var box))
            {
                return;
            }

            var input = (TextElement)field.textEdition;
            input.UnregisterCallback(s_onKeyDown, TrickleDown.TrickleDown);
            input.UnregisterCallback(s_onCommand, TrickleDown.TrickleDown);
            input.UnregisterCallback(s_restoreKey);
            input.UnregisterCallback(s_restoreCommand);
            Restore(box);
            s_byField.Remove(field);
            s_byInput.Remove(input);
        }

        private static void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.currentTarget is not TextElement input || !s_byInput.TryGetValue(input, out var box))
            {
                return;
            }

            if (evt.isPropagationStopped || box.Field.isReadOnly)
            {
                return;
            }

            if (IsPaste(evt))
            {
                PreparePaste(box, evt);
                return;
            }

            if (InsertionLength(evt, box.Field.multiline) is { } incoming && Room(box) < incoming)
            {
                evt.StopPropagation();
            }
        }

        private static void OnCommand(ExecuteCommandEvent evt)
        {
            if (evt.commandName != "Paste"
                || evt.currentTarget is not TextElement input
                || !s_byInput.TryGetValue(input, out var box)
                || evt.isPropagationStopped
                || box.Field.isReadOnly)
            {
                return;
            }

            PreparePaste(box, evt);
        }

        private static void PreparePaste(Limit box, EventBase evt)
        {
            var raw = GUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(raw))
            {
                return;
            }

            var incoming = box.Field.multiline ? raw : ReplaceNewlinesWithSpaces(raw);
            var room = Room(box);
            if (incoming.Length <= room)
            {
                return;
            }

            // A cut that lands on a high surrogate would insert that char alone. Back up so the pair stays out.
            if (room > 0 && char.IsHighSurrogate(incoming[room - 1]))
            {
                room--;
            }

            if (room <= 0)
            {
                evt.StopPropagation();
                return;
            }

            box.Stashed = true;
            box.Clipboard = raw;
            GUIUtility.systemCopyBuffer = incoming.Substring(0, room);
        }

        private static void RestoreKey(KeyDownEvent evt) => RestoreTarget(evt.currentTarget);

        private static void RestoreCommand(ExecuteCommandEvent evt) => RestoreTarget(evt.currentTarget);

        private static void RestoreTarget(object target)
        {
            if (target is TextElement input)
            {
                RestoreInput(input);
            }
        }

        private static void RestoreInput(TextElement input)
        {
            if (s_byInput.TryGetValue(input, out var box))
            {
                Restore(box);
            }
        }

        private static void Restore(Limit box)
        {
            if (!box.Stashed)
            {
                return;
            }

            GUIUtility.systemCopyBuffer = box.Clipboard ?? string.Empty;
            box.Stashed = false;
            box.Clipboard = null;
        }

        private static int Room(Limit box)
        {
            var field = box.Field;
            var text = field.text ?? string.Empty;
            var start = Clamp(field.cursorIndex, text.Length);
            var end = Clamp(field.selectIndex, text.Length);
            if (start > end)
            {
                (start, end) = (end, start);
            }

            return box.Value - (text.Length - (end - start));
        }

        private static int Clamp(int index, int length)
        {
            if (index < 0)
            {
                return 0;
            }

            return index > length ? length : index;
        }

        // A surrogate occupies two chars, so one that does not fit is refused together with its pair.
        private static int? InsertionLength(KeyDownEvent evt, bool multiline)
        {
            if (IsShortcut(evt) || LeavesTheText(evt, multiline))
            {
                return null;
            }

            if (evt.keyCode == KeyCode.Tab)
            {
                return multiline && !evt.shiftKey ? 1 : null;
            }

            var c = evt.character;
            if (c >= ' ' || (multiline && !evt.altKey && (c == '\n' || c == '\r')))
            {
                return char.IsSurrogate(c) ? 2 : 1;
            }

            return null;
        }

        private static bool IsShortcut(KeyDownEvent evt)
        {
            var c = evt.character;
            if (evt.actionKey && (!evt.altKey || c == '\0'))
            {
                return true;
            }

            if (IsFunctionKey(evt.keyCode) || (evt.altKey && c == '\0'))
            {
                return true;
            }

            return c == '\t' && evt.keyCode == KeyCode.None && evt.modifiers == EventModifiers.None;
        }

        private static bool IsFunctionKey(KeyCode key)
            => (key >= KeyCode.F1 && key <= KeyCode.F15) || (key >= KeyCode.F16 && key <= KeyCode.F24);

        private static bool LeavesTheText(KeyDownEvent evt, bool multiline)
        {
            if (evt.keyCode == KeyCode.Escape)
            {
                return true;
            }

            if (!multiline && (evt.keyCode == KeyCode.KeypadEnter || evt.keyCode == KeyCode.Return))
            {
                return true;
            }

            var c = evt.character;
            if (!multiline && (c == '\n' || c == '\r'))
            {
                return true;
            }

            return multiline && c == '\n' && evt.shiftKey;
        }

        private static bool IsPaste(KeyDownEvent evt)
        {
            var mac = SystemInfo.operatingSystemFamily == OperatingSystemFamily.MacOSX;
            if (evt.keyCode == KeyCode.V)
            {
                return evt.modifiers == (mac ? EventModifiers.Command : EventModifiers.Control);
            }

            return !mac
                   && evt.keyCode == KeyCode.Insert
                   && evt.modifiers == (EventModifiers.Shift | EventModifiers.FunctionKey);
        }

        private static string ReplaceNewlinesWithSpaces(string value)
        {
            value = value.Replace("\r\n", " ");
            value = value.Replace('\n', ' ');
            value = value.Replace('\r', ' ');
            return value;
        }
    }
}

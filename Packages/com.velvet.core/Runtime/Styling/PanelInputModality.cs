#nullable enable
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // React Aria's useFocusVisible input modality, kept per panel as React Aria keeps it per window: after a
    // pointer press, no focus in the panel shows a ring, a programmatic Focus() included, until a key press
    // or a navigation move.
    internal static class PanelInputModality
    {
        private sealed class Reading
        {
            public bool Pointer;
        }

        private static readonly ConditionalWeakTable<IPanel, Reading> s_readings = new();

        internal static void Track(IPanel? panel)
        {
            if (panel == null || s_readings.TryGetValue(panel, out _))
            {
                return;
            }
            var reading = new Reading();
            s_readings.Add(panel, reading);
            var root = panel.visualTree;
            root.RegisterCallback<PointerDownEvent>(_ => reading.Pointer = true, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationMoveEvent>(_ => reading.Pointer = false, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (IsModalityKey(evt))
                {
                    reading.Pointer = false;
                }
            }, TrickleDown.TrickleDown);
        }

        internal static bool LastInputWasPointer(IPanel? panel)
        {
            if (panel == null)
            {
                return false;
            }
            s_readings.TryGetValue(panel, out var reading);
            return reading is { Pointer: true };
        }

        // React Aria's isValidKey, less its non-Mac Alt rule: a Shift, Ctrl or Command key pressed alone, or a
        // Ctrl or Command chord, is not keyboard use.
        private static bool IsModalityKey(KeyDownEvent evt)
            => !evt.ctrlKey && !evt.commandKey && evt.keyCode is not (KeyCode.LeftShift or KeyCode.RightShift
                or KeyCode.LeftControl or KeyCode.RightControl or KeyCode.LeftCommand or KeyCode.RightCommand);
    }
}

#nullable enable
using System;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // React Aria's useFocusVisible input modality: one process-wide reading, which every panel Velvet renders
    // into writes, as every window React Aria tracks writes its one currentModality. After a pointer press,
    // release or move, no focus shows a ring, a programmatic Focus() included, until a key press or release or a
    // navigation move. A press, a key or a navigation move is also announced to Changed, which a focused
    // element's ring follows as useFocusVisibleListener's does; useFocusVisible announces neither a pointer move
    // nor a pointer release.
    internal static class InputModality
    {
        private static readonly ConditionalWeakTable<IPanel, object> s_tracked = new();

        private static readonly bool s_isMac =
            Application.platform is RuntimePlatform.OSXEditor or RuntimePlatform.OSXPlayer;

        private static bool s_pointer;

        // The event that changed the reading: a pointer press, or the key or navigation event.
        internal static event Action<bool, EventBase>? Changed;

        internal static void Track(IPanel? panel)
        {
            if (panel == null || s_tracked.TryGetValue(panel, out _))
            {
                return;
            }
            // MUTANT_SURVIVES(equivalent, line removed): a panel tracked twice writes the same reading twice and
            // announces each change twice, and every consumer ignores a signal that repeats its last one.
            s_tracked.Add(panel, panel);
            var root = panel.visualTree;
            root.RegisterCallback<PointerDownEvent>(evt => Announce(true, evt), TrickleDown.TrickleDown);
            root.RegisterCallback<PointerUpEvent>(_ => s_pointer = true, TrickleDown.TrickleDown);
            root.RegisterCallback<PointerMoveEvent>(_ => s_pointer = true, TrickleDown.TrickleDown);
            root.RegisterCallback<NavigationMoveEvent>(evt => Announce(false, evt), TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(AnnounceKey, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyUpEvent>(AnnounceKey, TrickleDown.TrickleDown);
        }

        // An element outside any panel takes no focus, so it reads no modality.
        internal static bool LastInputWasPointer(IPanel? panel) => panel != null && s_pointer;

        private static void Announce(bool pointer, EventBase cause)
        {
            s_pointer = pointer;
            Changed?.Invoke(pointer, cause);
        }

        private static void AnnounceKey<T>(T evt) where T : EventBase, IKeyboardEvent
        {
            if (IsModalityKey(evt))
            {
                Announce(false, evt);
            }
        }

        // React Aria's isValidKey: a Shift, Ctrl or Command key pressed alone, a Ctrl or Command chord, or an
        // Alt chord off a Mac is not keyboard use.
        // MUTANT_SURVIVES(unreachable, clause removed): dropping `&& !s_isMac` differs only where s_isMac is true,
        // on a Mac, and the suites run on Linux.
        private static bool IsModalityKey(IKeyboardEvent evt)
            => !evt.ctrlKey && !evt.commandKey && !(evt.altKey && !s_isMac)
                && evt.keyCode is not (KeyCode.LeftShift or KeyCode.RightShift or KeyCode.LeftControl
                    or KeyCode.RightControl or KeyCode.LeftCommand or KeyCode.RightCommand);
    }
}

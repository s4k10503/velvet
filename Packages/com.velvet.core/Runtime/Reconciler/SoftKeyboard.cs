using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // The soft keyboard a text field holds and the status it reports, read through two delegates rather than
    // in place. An editor opens no soft keyboard and a keyboard's status is native, so this is where a test
    // stands one in, by reflection from the test side; the defaults are the engine's own reads.
    internal static class SoftKeyboard
    {
        private static Func<TextField, TouchScreenKeyboard?> s_of = field => field.textEdition.touchScreenKeyboard;

        private static Func<TouchScreenKeyboard, TouchScreenKeyboard.Status> s_statusOf = keyboard => keyboard.status;

        internal static TouchScreenKeyboard? Of(TextField field) => s_of(field);

        internal static TouchScreenKeyboard.Status StatusOf(TouchScreenKeyboard keyboard) => s_statusOf(keyboard);
    }
}

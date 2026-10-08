using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Velvet
{
    /// <summary>
    /// Whether the application is on screen, as TanStack's focus manager reads <c>document.visibilityState</c>:
    /// a window that has lost focus but is still shown is visible, and a minimized or hidden one is not.
    /// </summary>
    /// <remarks>
    /// Unity exposes only focus (<c>Application.isFocused</c>), so each platform asks its window system.
    /// On a mobile platform a backgrounded application is the one that has lost focus. On Windows the
    /// process's main window is asked whether it is minimized. On macOS the application is hidden when
    /// <c>NSApplication</c> says so or when none of its windows is both visible and not miniaturized. Linux and
    /// WebGL have no reading here and always count as visible.
    /// </remarks>
    internal static class ApplicationVisibility
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);

        private static IntPtr s_mainWindow;

        private static bool WindowsMainWindowIsMinimized()
        {
            try
            {
                if (s_mainWindow == IntPtr.Zero)
                {
                    s_mainWindow = Process.GetCurrentProcess().MainWindowHandle;
                }

                return s_mainWindow != IntPtr.Zero && IsIconic(s_mainWindow);
            }
            catch (Exception missing) when (missing is DllNotFoundException or EntryPointNotFoundException)
            {
                return false;
            }
        }
#endif

#if UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
        private const string ObjC = "/usr/lib/libobjc.dylib";

        [DllImport(ObjC)]
        private static extern IntPtr objc_getClass(string name);

        [DllImport(ObjC)]
        private static extern IntPtr sel_registerName(string name);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        private static extern IntPtr SendForObject(IntPtr receiver, IntPtr selector);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        private static extern byte SendForBool(IntPtr receiver, IntPtr selector);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        private static extern UIntPtr SendForCount(IntPtr receiver, IntPtr selector);

        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        private static extern IntPtr SendForObjectAt(IntPtr receiver, IntPtr selector, UIntPtr index);

        private static bool MacApplicationIsHidden()
        {
            try
            {
                var app = SendForObject(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));
                if (app == IntPtr.Zero)
                {
                    return false;
                }

                if (SendForBool(app, sel_registerName("isHidden")) != 0)
                {
                    return true;
                }

                var windows = SendForObject(app, sel_registerName("windows"));
                var count = (ulong)SendForCount(windows, sel_registerName("count"));
                if (count == 0)
                {
                    return false;
                }

                var objectAt = sel_registerName("objectAtIndex:");
                var isVisible = sel_registerName("isVisible");
                var isMiniaturized = sel_registerName("isMiniaturized");
                var anyShown = false;
                for (ulong index = 0; index < count; index++)
                {
                    var window = SendForObjectAt(windows, objectAt, new UIntPtr(index));
                    anyShown |= SendForBool(window, isVisible) != 0 && SendForBool(window, isMiniaturized) == 0;
                }

                return !anyShown;
            }
            catch (Exception missing) when (missing is DllNotFoundException or EntryPointNotFoundException)
            {
                return false;
            }
        }
#endif

        internal static bool IsVisible()
        {
            if (Application.isMobilePlatform)
            {
                return Application.isFocused;
            }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            return !WindowsMainWindowIsMinimized();
#elif UNITY_STANDALONE_OSX || UNITY_EDITOR_OSX
            return !MacApplicationIsHidden();
#else
            return true;
#endif
        }
    }
}

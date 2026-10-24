using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Velvet
{
    /// <summary>
    /// Whether the application is on screen, as TanStack's focus manager reads <c>document.visibilityState</c>:
    /// a window that has lost focus but is still shown is visible, and a minimized or hidden one is not.
    /// </summary>
    /// <remarks>
    /// Unity exposes only focus (<c>Application.isFocused</c>), so each desktop platform asks its window
    /// system. On a mobile platform a backgrounded application is the one that has lost focus. On Windows the
    /// main thread's top-level window is asked whether it is minimized. On macOS the application is hidden when
    /// <c>NSApplication</c> says so or when it has windows and none is both visible and not miniaturized; one
    /// with no windows reads as visible. Linux and WebGL have no reading and always count as visible, as does
    /// any platform whose native read throws: the first failure logs one warning and latches, and every later
    /// read answers visible without trying again, so a diagnostic read never faults a mutation.
    /// </remarks>
    internal static class ApplicationVisibility
    {
        // Not readonly: ApplicationVisibilityTests substitutes the read.
        private static Func<bool> s_readHidden = ReadHidden;
        private static bool s_unavailable;

        internal static bool IsVisible()
        {
            if (Application.isMobilePlatform)
            {
                return Application.isFocused;
            }

            if (s_unavailable)
            {
                return true;
            }

            try
            {
                return !s_readHidden();
            }
            catch (Exception failure)
            {
                s_unavailable = true;
                Debug.LogWarning(
                    "Velvet cannot read whether the application window is hidden, so a retry or a paused "
                    + "mutation counts it as visible until the scripts reload. "
                    + $"{failure.GetType().Name}: {failure.Message}");
                return true;
            }
        }

        // Inside the editor each arm is keyed on the machine the editor runs on, never on the build target,
        // so the native library an editor calls is its own whichever target is active.
        private static bool ReadHidden()
        {
#if UNITY_EDITOR_WIN || (!UNITY_EDITOR && UNITY_STANDALONE_WIN)
            return MainWindowIsMinimized();
#elif UNITY_EDITOR_OSX || (!UNITY_EDITOR && UNITY_STANDALONE_OSX)
            return ApplicationIsHidden();
#else
            return false;
#endif
        }

        // The decisions each desktop read makes sit here, outside the #if arms below, so that they are built and
        // run on every platform; an arm holds only the native calls it hands them.
        internal interface IWindowSource
        {
            bool ApplicationIsHidden();

            ulong WindowCount();

            bool WindowIsShown(ulong index);
        }

        // An application with no windows reads as visible.
        internal static bool IsHiddenFrom<TSource>(ref TSource source) where TSource : struct, IWindowSource
        {
            if (source.ApplicationIsHidden())
            {
                return true;
            }

            var count = source.WindowCount();
            var anyShown = count == 0;
            for (ulong index = 0; index < count; index++)
            {
                anyShown |= source.WindowIsShown(index);
            }

            return !anyShown;
        }

        internal static bool IsMinimized(ref IntPtr mainWindow, Action findMainWindow, Func<IntPtr, bool> isIconic)
        {
            if (mainWindow == IntPtr.Zero)
            {
                findMainWindow();
                if (mainWindow == IntPtr.Zero)
                {
                    // No window to ask, such as under -batchmode: every later read answers visible.
                    throw new InvalidOperationException("The main thread has no top-level window.");
                }
            }

            return isIconic(mainWindow);
        }

        // The enumeration's callback: false stops it, on the first visible window with no owner.
        internal static bool KeepLooking(
            IntPtr window, Func<IntPtr, bool> isVisible, Func<IntPtr, IntPtr> ownerOf, ref IntPtr mainWindow)
        {
            if (!isVisible(window) || ownerOf(window) != IntPtr.Zero)
            {
                return true;
            }

            mainWindow = window;
            return false;
        }

        internal static bool IsUnset(IntPtr handle) => handle == IntPtr.Zero;

        internal static IntPtr Required(IntPtr handle, string failure)
        {
            if (IsUnset(handle))
            {
                throw new InvalidOperationException(failure);
            }

            return handle;
        }

        internal static bool Flag(byte value) => value != 0;

        internal static bool IsShown(byte visible, byte miniaturized) => visible != 0 && miniaturized == 0;

#if UNITY_EDITOR_WIN || (!UNITY_EDITOR && UNITY_STANDALONE_WIN)
        private delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);

        private const uint OwnerWindow = 4;

        private static readonly EnumWindowProc s_pickWindow = PickWindow;
        private static readonly Action s_findMainWindow = FindMainWindow;
        private static readonly Func<IntPtr, bool> s_isIconic = static window => IsIconic(window);
        private static readonly Func<IntPtr, bool> s_isVisible = static window => IsWindowVisible(window);
        private static readonly Func<IntPtr, IntPtr> s_ownerOf = static window => GetWindow(window, OwnerWindow);
        private static IntPtr s_mainWindow;

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool EnumThreadWindows(uint threadId, EnumWindowProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr window, uint command);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);

        // A minimized window is not the active one, so it is found among the main thread's top-level
        // windows: the first visible one with no owner. Called from the main thread, whose id it reads.
        private static bool MainWindowIsMinimized() => IsMinimized(ref s_mainWindow, s_findMainWindow, s_isIconic);

        private static void FindMainWindow()
        {
            // MUTANT_SURVIVES(unreachable, line removed): the #if opening the Windows arm is false in the Linux editor CI's suites run in.
            EnumThreadWindows(GetCurrentThreadId(), s_pickWindow, IntPtr.Zero);
        }

        [AOT.MonoPInvokeCallback(typeof(EnumWindowProc))]
        private static bool PickWindow(IntPtr window, IntPtr parameter) =>
            KeepLooking(window, s_isVisible, s_ownerOf, ref s_mainWindow);
#endif

#if UNITY_EDITOR_OSX || (!UNITY_EDITOR && UNITY_STANDALONE_OSX)
        private const string ObjC = "/usr/lib/libobjc.dylib";

        private static IntPtr s_application;
        private static IntPtr s_isHidden;
        private static IntPtr s_windows;
        private static IntPtr s_count;
        private static IntPtr s_objectAt;
        private static IntPtr s_isVisible;
        private static IntPtr s_isMiniaturized;

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

        private struct NativeWindows : IWindowSource
        {
            private IntPtr _windows;

            public bool ApplicationIsHidden() => Flag(SendForBool(s_application, s_isHidden));

            public ulong WindowCount()
            {
                // MUTANT_SURVIVES(unreachable, line removed): the #if opening the macOS arm is false in the Linux editor CI's suites run in.
                _windows = SendForObject(s_application, s_windows);
                return (ulong)SendForCount(_windows, s_count);
            }

            public bool WindowIsShown(ulong index)
            {
                var window = SendForObjectAt(_windows, s_objectAt, new UIntPtr(index));
                return IsShown(SendForBool(window, s_isVisible), SendForBool(window, s_isMiniaturized));
            }
        }

        private static bool ApplicationIsHidden()
        {
            if (IsUnset(s_application))
            {
                // MUTANT_SURVIVES(unreachable, line removed): the #if opening the macOS arm is false in the Linux editor CI's suites run in.
                s_isHidden = sel_registerName("isHidden");
                // MUTANT_SURVIVES(unreachable, line removed): the #if opening the macOS arm is false in the Linux editor CI's suites run in.
                s_windows = sel_registerName("windows");
                // MUTANT_SURVIVES(unreachable, line removed): the #if opening the macOS arm is false in the Linux editor CI's suites run in.
                s_count = sel_registerName("count");
                // MUTANT_SURVIVES(unreachable, line removed): the #if opening the macOS arm is false in the Linux editor CI's suites run in.
                s_objectAt = sel_registerName("objectAtIndex:");
                // MUTANT_SURVIVES(unreachable, line removed): the #if opening the macOS arm is false in the Linux editor CI's suites run in.
                s_isVisible = sel_registerName("isVisible");
                // MUTANT_SURVIVES(unreachable, line removed): the #if opening the macOS arm is false in the Linux editor CI's suites run in.
                s_isMiniaturized = sel_registerName("isMiniaturized");
                s_application = Required(
                    SendForObject(objc_getClass("NSApplication"), sel_registerName("sharedApplication")),
                    "NSApplication has no shared instance.");
            }

            var windows = new NativeWindows();
            return IsHiddenFrom(ref windows);
        }
#endif
    }
}

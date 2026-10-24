using System;
using System.Collections.Generic;
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
            var main = new NativeMainWindow();
            var list = new NoWindowList();
            return IsHiddenOn(WindowReading.MainWindow, ref main, ref list);
#elif UNITY_EDITOR_OSX || (!UNITY_EDITOR && UNITY_STANDALONE_OSX)
            var main = new NoMainWindow();
            var list = new NativeWindowList();
            return IsHiddenOn(WindowReading.WindowList, ref main, ref list);
#else
            var main = new NoMainWindow();
            var list = new NoWindowList();
            return IsHiddenOn(WindowReading.None, ref main, ref list);
#endif
        }

        // The decisions each desktop read makes sit here, outside the #if arms below, so that they are built and
        // run on every platform; an arm holds only the native calls it hands them.
        internal enum WindowReading
        {
            None,
            MainWindow,
            WindowList,
        }

        // Windows: the main thread's top-level windows, of which the first visible one with no owner is asked
        // whether it is minimized.
        internal interface IMainWindowSource
        {
            IntPtr MainWindow { get; set; }

            void CollectWindows(List<IntPtr> into);

            bool IsVisible(IntPtr window);

            IntPtr OwnerOf(IntPtr window);

            bool IsIconic(IntPtr window);
        }

        // macOS: the application, then its windows.
        internal interface IWindowListSource
        {
            IntPtr Application { get; set; }

            void RegisterSelectors();

            IntPtr LookUpApplication();

            byte ApplicationHidden();

            ulong WindowCount();

            byte WindowVisible(ulong index);

            byte WindowMiniaturized(ulong index);
        }

        private struct NoMainWindow : IMainWindowSource
        {
            public IntPtr MainWindow { get; set; }

            public void CollectWindows(List<IntPtr> into)
            {
            }

            public bool IsVisible(IntPtr window) => default;

            public IntPtr OwnerOf(IntPtr window) => IntPtr.Zero;

            public bool IsIconic(IntPtr window) => default;
        }

        private struct NoWindowList : IWindowListSource
        {
            public IntPtr Application { get; set; }

            public void RegisterSelectors()
            {
            }

            public IntPtr LookUpApplication() => IntPtr.Zero;

            public byte ApplicationHidden() => 0;

            public ulong WindowCount() => 0;

            public byte WindowVisible(ulong index) => 0;

            public byte WindowMiniaturized(ulong index) => 0;
        }

        internal static bool IsHiddenOn<TMain, TList>(WindowReading reading, ref TMain main, ref TList list)
            where TMain : struct, IMainWindowSource
            where TList : struct, IWindowListSource
        {
            switch (reading)
            {
                case WindowReading.MainWindow:
                    return IsMinimized(ref main);
                case WindowReading.WindowList:
                    return IsHidden(ref list);
                default:
                    return false;
            }
        }

        // The window is searched for once and kept. Called from the main thread, whose windows the source lists.
        internal static bool IsMinimized<TMain>(ref TMain main) where TMain : struct, IMainWindowSource
        {
            if (IsUnset(main.MainWindow))
            {
                var windows = new List<IntPtr>();
                main.CollectWindows(windows);
                main.MainWindow = FirstMainWindow(ref main, windows);
                if (IsUnset(main.MainWindow))
                {
                    // No window to ask, such as under -batchmode: every later read answers visible.
                    throw new InvalidOperationException("The main thread has no top-level window.");
                }
            }

            return main.IsIconic(main.MainWindow);
        }

        // A minimized window is not the active one, so the main window is the first visible one with no owner.
        internal static IntPtr FirstMainWindow<TMain>(ref TMain main, List<IntPtr> windows)
            where TMain : struct, IMainWindowSource
        {
            for (var index = 0; index < windows.Count; index++)
            {
                if (main.IsVisible(windows[index]) && IsUnset(main.OwnerOf(windows[index])))
                {
                    return windows[index];
                }
            }

            return IntPtr.Zero;
        }

        // The application is hidden when it says so or when it has windows and none is both visible and not
        // miniaturized; one with no windows reads as visible.
        internal static bool IsHidden<TList>(ref TList list) where TList : struct, IWindowListSource
        {
            if (IsUnset(list.Application))
            {
                list.RegisterSelectors();
                list.Application = Required(list.LookUpApplication(), "NSApplication has no shared instance.");
            }

            if (Flag(list.ApplicationHidden()))
            {
                return true;
            }

            var count = list.WindowCount();
            var anyShown = count == 0;
            for (ulong index = 0; index < count; index++)
            {
                anyShown |= IsShown(list.WindowVisible(index), list.WindowMiniaturized(index));
            }

            return !anyShown;
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

        private static readonly EnumWindowProc s_collect = Collect;
        private static List<IntPtr> s_collecting;
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

        [AOT.MonoPInvokeCallback(typeof(EnumWindowProc))]
        private static bool Collect(IntPtr window, IntPtr parameter)
        {
            // MUTANT_SURVIVES(unreachable, line removed): the #if opening the Windows arm is false in the Linux editor CI's suites run in.
            s_collecting.Add(window);
            // MUTANT_SURVIVES(unreachable, literal): the #if opening the Windows arm is false in the Linux editor CI's suites run in.
            return true;
        }

        private struct NativeMainWindow : IMainWindowSource
        {
            public IntPtr MainWindow
            {
                get => s_mainWindow;
                set => s_mainWindow = value;
            }

            public void CollectWindows(List<IntPtr> into)
            {
                // MUTANT_SURVIVES(unreachable, line removed): the #if opening the Windows arm is false in the Linux editor CI's suites run in.
                s_collecting = into;
                // MUTANT_SURVIVES(unreachable, line removed): the #if opening the Windows arm is false in the Linux editor CI's suites run in.
                EnumThreadWindows(GetCurrentThreadId(), s_collect, IntPtr.Zero);
            }

            public bool IsVisible(IntPtr window) => IsWindowVisible(window);

            public IntPtr OwnerOf(IntPtr window) => GetWindow(window, OwnerWindow);

            public bool IsIconic(IntPtr window) => ApplicationVisibility.IsIconic(window);
        }
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

        private struct NativeWindowList : IWindowListSource
        {
            private IntPtr _windows;

            public IntPtr Application
            {
                get => s_application;
                set => s_application = value;
            }

            public void RegisterSelectors()
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
            }

            public IntPtr LookUpApplication() =>
                SendForObject(objc_getClass("NSApplication"), sel_registerName("sharedApplication"));

            public byte ApplicationHidden() => SendForBool(s_application, s_isHidden);

            public ulong WindowCount()
            {
                // MUTANT_SURVIVES(unreachable, line removed): the #if opening the macOS arm is false in the Linux editor CI's suites run in.
                _windows = SendForObject(s_application, s_windows);
                return (ulong)SendForCount(_windows, s_count);
            }

            public byte WindowVisible(ulong index) =>
                SendForBool(SendForObjectAt(_windows, s_objectAt, new UIntPtr(index)), s_isVisible);

            public byte WindowMiniaturized(ulong index) =>
                SendForBool(SendForObjectAt(_windows, s_objectAt, new UIntPtr(index)), s_isMiniaturized);
        }
#endif
    }
}

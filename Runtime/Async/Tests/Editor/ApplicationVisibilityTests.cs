using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what <c>ApplicationVisibility</c> answers from the platform's native read: that read's own
    /// answer while it succeeds, and visible, with one warning and no second attempt, once it throws. The
    /// read is replaced through its private field, since CI runs neither the Windows nor the macOS one; the
    /// decisions those reads make are platform-neutral members that the cases below drive with stand-ins for
    /// the native calls.
    /// </summary>
    [TestFixture]
    internal sealed class ApplicationVisibilityTests
    {
        private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;
        private const string WarningText = "cannot read whether the application window is hidden";

        private static readonly FieldInfo s_readHidden =
            typeof(ApplicationVisibility).GetField("s_readHidden", Hidden)!;

        private static readonly FieldInfo s_unavailable =
            typeof(ApplicationVisibility).GetField("s_unavailable", Hidden)!;

        private readonly List<(LogType Type, string Condition)> _entries = new();
        private object _originalRead;
        private object _originalUnavailable;
        private int _reads;

        [SetUp]
        public void SetUp()
        {
            _originalRead = s_readHidden.GetValue(null);
            _originalUnavailable = s_unavailable.GetValue(null);
            s_unavailable.SetValue(null, false);
            _reads = 0;
            _entries.Clear();
            Application.logMessageReceived += Record;
        }

        [TearDown]
        public void TearDown()
        {
            Application.logMessageReceived -= Record;
            s_readHidden.SetValue(null, _originalRead);
            s_unavailable.SetValue(null, _originalUnavailable);
        }

        private void Record(string condition, string stackTrace, LogType type) => _entries.Add((type, condition));

        private void ReadWith(Func<bool> read) => s_readHidden.SetValue(null, read);

        // What a P/Invoke raises for a missing library, a missing entry point and a signature it cannot
        // marshal, and what the Windows read throws itself when it finds no window.
        private static IEnumerable<Exception> NativeFailures()
        {
            yield return new DllNotFoundException("user32.dll");
            yield return new EntryPointNotFoundException("objc_msgSend");
            yield return new System.Runtime.InteropServices.MarshalDirectiveException("return type");
            yield return new InvalidOperationException("The main thread has no top-level window.");
        }

        [TestCaseSource(nameof(NativeFailures))]
        public void Given_ANativeReadThatThrows_When_VisibilityIsRead_Then_ItReadsVisible(Exception failure)
        {
            // Arrange
            ReadWith(() =>
            {
                _reads++;
                throw failure;
            });

            // Act
            var visible = ApplicationVisibility.IsVisible();

            // Assert
            Assert.That((visible, _reads), Is.EqualTo((true, 1)), "A failed read was attempted once and answered visible");
        }

        [Test]
        public void Given_ANativeReadThatThrew_When_VisibilityIsReadAgain_Then_TheNativeReadIsNotRetried()
        {
            // Arrange
            ReadWith(() =>
            {
                _reads++;
                throw new DllNotFoundException("user32.dll");
            });
            ApplicationVisibility.IsVisible();

            // Act
            var later = new[] { ApplicationVisibility.IsVisible(), ApplicationVisibility.IsVisible() };

            // Assert
            Assert.That(string.Join(",", later) + " after " + _reads, Is.EqualTo("True,True after 1"));
        }

        [Test]
        public void Given_ANativeReadThatThrows_When_VisibilityIsReadThreeTimes_Then_OneWarningIsLogged()
        {
            // Arrange
            ReadWith(() => throw new EntryPointNotFoundException("objc_msgSend"));

            // Act
            ApplicationVisibility.IsVisible();
            ApplicationVisibility.IsVisible();
            ApplicationVisibility.IsVisible();

            // Assert
            Assert.That(
                _entries.Count(entry => entry.Type == LogType.Warning && entry.Condition.Contains(WarningText)),
                Is.EqualTo(1));
        }

        // Linux alone: the Windows and macOS editors read real windows, whose state the runner does not set.
        [Test]
        [UnityPlatform(RuntimePlatform.LinuxEditor)]
        public void Given_AnEditorWithNoNativeRead_When_VisibilityIsRead_Then_ItReadsVisible()
        {
            // Arrange
            // Nothing to arrange: the platform's own read stays in place.

            // Act
            var visible = ApplicationVisibility.IsVisible();

            // Assert
            Assert.That(visible, Is.True);
        }

        [Test]
        public void Given_ANativeReadReadingHidden_When_VisibilityIsReadTwice_Then_BothReadHiddenFromTheNativeRead()
        {
            // Arrange
            ReadWith(() => ++_reads > 0);

            // Act
            var reads = new[] { ApplicationVisibility.IsVisible(), ApplicationVisibility.IsVisible() };

            // Assert
            Assert.That(string.Join(",", reads) + " after " + _reads, Is.EqualTo("False,False after 2"));
        }

        private struct FakeWindowList : ApplicationVisibility.IWindowListSource
        {
            internal bool Hidden;
            internal bool[] Shown;
            internal IntPtr Looked;
            internal int CountReads;
            internal int Registrations;

            public IntPtr Application { get; set; }

            public void RegisterSelectors() => Registrations++;

            public IntPtr LookUpApplication() => Looked;

            public byte ApplicationHidden() => (byte)(Hidden ? 1 : 0);

            public ulong WindowCount()
            {
                CountReads++;
                return (ulong)Shown.Length;
            }

            // Past the last window these throw, so a loop that reads one too many fails the case.
            public byte WindowVisible(ulong index) => (byte)(Shown[(int)index] ? 1 : 0);

            public byte WindowMiniaturized(ulong index) => 0;
        }

        private static FakeWindowList ListOf(bool applicationIsHidden, params bool[] shown) =>
            new() { Hidden = applicationIsHidden, Shown = shown, Looked = s_window, Application = s_window };

        private static bool HiddenFrom(FakeWindowList list) => ApplicationVisibility.IsHidden(ref list);

        [Test]
        public void Given_ApplicationReportsItselfHidden_When_HiddenIsDecided_Then_ItIsHidden()
        {
            // Act
            var hidden = HiddenFrom(ListOf(true, true));

            // Assert
            Assert.That(hidden, Is.True, "A hidden application is hidden whatever its windows say");
        }

        [Test]
        public void Given_ApplicationReportsItselfHidden_When_HiddenIsDecided_Then_ItsWindowsAreNotCounted()
        {
            // Arrange
            var list = ListOf(true, true);

            // Act
            ApplicationVisibility.IsHidden(ref list);

            // Assert
            Assert.That(list.CountReads, Is.EqualTo(0), "The windows are asked about only once the application is not hidden");
        }

        [Test]
        public void Given_NoWindows_When_HiddenIsDecided_Then_ItIsNotHidden()
        {
            // Act
            var hidden = HiddenFrom(ListOf(false));

            // Assert
            Assert.That(hidden, Is.False, "An application with no windows reads as visible");
        }

        [Test]
        public void Given_WindowsNoneOfWhichIsShown_When_HiddenIsDecided_Then_ItIsHidden()
        {
            // Act
            var hidden = HiddenFrom(ListOf(false, false, false));

            // Assert
            Assert.That(hidden, Is.True, "Windows that are all hidden or miniaturized hide the application");
        }

        [Test]
        public void Given_OneShownWindowAmongOthers_When_HiddenIsDecided_Then_ItIsNotHidden()
        {
            // Act
            var hidden = HiddenFrom(ListOf(false, false, true, false));

            // Assert
            Assert.That(hidden, Is.False, "One shown window is enough, wherever it sits");
        }

        [Test]
        public void Given_NoApplicationYet_When_HiddenIsDecided_Then_TheSelectorsAreRegisteredOnce()
        {
            // Arrange
            var list = ListOf(false, true);
            list.Application = IntPtr.Zero;

            // Act
            ApplicationVisibility.IsHidden(ref list);
            ApplicationVisibility.IsHidden(ref list);

            // Assert
            Assert.That(list.Registrations, Is.EqualTo(1), "The application is looked up once and kept");
        }

        [Test]
        public void Given_NoApplicationYet_When_TheLookUpFindsOne_Then_ItIsKept()
        {
            // Arrange
            var list = ListOf(false, true);
            list.Application = IntPtr.Zero;

            // Act
            ApplicationVisibility.IsHidden(ref list);

            // Assert
            Assert.That(list.Application, Is.EqualTo(s_window));
        }

        [Test]
        public void Given_NoApplicationYet_When_TheLookUpFindsNone_Then_ItThrows()
        {
            // Arrange
            var list = ListOf(false, true);
            list.Application = IntPtr.Zero;
            list.Looked = IntPtr.Zero;

            // Act
            TestDelegate read = () => ApplicationVisibility.IsHidden(ref list);

            // Assert
            Assert.That(read, Throws.InvalidOperationException, "Without NSApplication there is nothing to ask");
        }

        private static readonly IntPtr s_window = new(7);

        private struct FakeMainWindow : ApplicationVisibility.IMainWindowSource
        {
            internal IntPtr[] Windows;
            internal HashSet<IntPtr> Visible;
            internal Dictionary<IntPtr, IntPtr> Owners;
            internal HashSet<IntPtr> Iconic;
            internal int Collections;
            internal List<IntPtr> OwnerLookups;

            public IntPtr MainWindow { get; set; }

            public void CollectWindows(List<IntPtr> into)
            {
                Collections++;
                into.AddRange(Windows);
            }

            public bool IsVisible(IntPtr window) => Visible.Contains(window);

            public IntPtr OwnerOf(IntPtr window)
            {
                OwnerLookups.Add(window);
                return Owners.TryGetValue(window, out var owner) ? owner : IntPtr.Zero;
            }

            public bool IsIconic(IntPtr window) => Iconic.Contains(window);
        }

        private static readonly IntPtr s_first = new(1);
        private static readonly IntPtr s_second = new(2);
        private static readonly IntPtr s_third = new(3);

        // Three windows: the first invisible, the second visible but owned, the third visible and free.
        private static FakeMainWindow ThreeWindows() => new()
        {
            Windows = new[] { s_first, s_second, s_third },
            Visible = new HashSet<IntPtr> { s_second, s_third },
            Owners = new Dictionary<IntPtr, IntPtr> { [s_second] = s_first },
            Iconic = new HashSet<IntPtr>(),
            OwnerLookups = new List<IntPtr>(),
        };

        [Test]
        public void Given_InvisibleAndOwnedWindowsAheadOfAFreeOne_When_TheMainWindowIsPicked_Then_ItIsTheFreeOne()
        {
            // Arrange
            var main = ThreeWindows();

            // Act
            var picked = ApplicationVisibility.FirstMainWindow(ref main, new List<IntPtr>(main.Windows));

            // Assert
            Assert.That(picked, Is.EqualTo(s_third), "The first visible window with no owner");
        }

        [Test]
        public void Given_NoVisibleUnownedWindow_When_TheMainWindowIsPicked_Then_ItIsNone()
        {
            // Arrange
            var main = ThreeWindows();
            main.Visible = new HashSet<IntPtr>();

            // Act
            var picked = ApplicationVisibility.FirstMainWindow(ref main, new List<IntPtr>(main.Windows));

            // Assert
            Assert.That(picked, Is.EqualTo(IntPtr.Zero));
        }

        [Test]
        public void Given_AnInvisibleWindow_When_TheMainWindowIsPicked_Then_ItsOwnerIsNotLookedUp()
        {
            // Arrange
            var main = ThreeWindows();

            // Act
            ApplicationVisibility.FirstMainWindow(ref main, new List<IntPtr>(main.Windows));

            // Assert
            Assert.That(main.OwnerLookups, Is.EqualTo(new[] { s_second, s_third }), "The owner is read only of a visible window");
        }

        [Test]
        public void Given_NoMainWindowYet_When_MinimizedIsRead_Then_ItsIconicStateAnswers()
        {
            // Arrange
            var main = ThreeWindows();
            main.Iconic.Add(s_third);

            // Act
            var minimized = ApplicationVisibility.IsMinimized(ref main);

            // Assert
            Assert.That(minimized, Is.True, "The window the search picked is the one asked");
        }

        [Test]
        public void Given_AMainWindowAlreadyFound_When_MinimizedIsRead_Then_NoSearchRuns()
        {
            // Arrange
            var main = ThreeWindows();
            main.MainWindow = s_third;

            // Act
            ApplicationVisibility.IsMinimized(ref main);

            // Assert
            Assert.That(main.Collections, Is.EqualTo(0), "A window found once is not searched for again");
        }

        [Test]
        public void Given_AMainWindowAlreadyFound_When_MinimizedIsRead_Then_ItsIconicStateAnswers()
        {
            // Arrange
            var main = ThreeWindows();
            main.MainWindow = s_third;
            main.Iconic.Add(s_third);

            // Act
            var minimized = ApplicationVisibility.IsMinimized(ref main);

            // Assert
            Assert.That(minimized, Is.True);
        }

        [Test]
        public void Given_NoMainWindowYet_When_MinimizedIsRead_Then_TheWindowIsKept()
        {
            // Arrange
            var main = ThreeWindows();

            // Act
            ApplicationVisibility.IsMinimized(ref main);

            // Assert
            Assert.That(main.MainWindow, Is.EqualTo(s_third));
        }

        [Test]
        public void Given_NoMainWindowYet_When_TheSearchFindsNone_Then_ItThrows()
        {
            // Arrange
            var main = ThreeWindows();
            main.Windows = Array.Empty<IntPtr>();

            // Act
            TestDelegate read = () => ApplicationVisibility.IsMinimized(ref main);

            // Assert
            Assert.That(read, Throws.InvalidOperationException, "A thread with no window has nothing to ask");
        }

        private struct NoWindowsAtAll : ApplicationVisibility.IMainWindowSource, ApplicationVisibility.IWindowListSource
        {
            public IntPtr MainWindow { get; set; }

            public IntPtr Application { get; set; }

            public void CollectWindows(List<IntPtr> into) => throw new InvalidOperationException("read");

            public bool IsVisible(IntPtr window) => throw new InvalidOperationException("read");

            public IntPtr OwnerOf(IntPtr window) => throw new InvalidOperationException("read");

            public bool IsIconic(IntPtr window) => throw new InvalidOperationException("read");

            public void RegisterSelectors() => throw new InvalidOperationException("read");

            public IntPtr LookUpApplication() => throw new InvalidOperationException("read");

            public byte ApplicationHidden() => throw new InvalidOperationException("read");

            public ulong WindowCount() => throw new InvalidOperationException("read");

            public byte WindowVisible(ulong index) => throw new InvalidOperationException("read");

            public byte WindowMiniaturized(ulong index) => throw new InvalidOperationException("read");
        }

        [Test]
        public void Given_NoReading_When_HiddenIsDecided_Then_ItIsNotHiddenAndNothingIsRead()
        {
            // Arrange
            var main = new NoWindowsAtAll();
            var list = new NoWindowsAtAll();

            // Act
            var hidden = ApplicationVisibility.IsHiddenOn(ApplicationVisibility.WindowReading.None, ref main, ref list);

            // Assert
            Assert.That(hidden, Is.False, "A platform with no reading answers visible without asking");
        }

        [Test]
        public void Given_TheMainWindowReading_When_HiddenIsDecided_Then_TheMainWindowIsAsked()
        {
            // Arrange
            var main = ThreeWindows();
            main.Iconic.Add(s_third);
            var list = new NoWindowsAtAll();

            // Act
            var hidden = ApplicationVisibility.IsHiddenOn(ApplicationVisibility.WindowReading.MainWindow, ref main, ref list);

            // Assert
            Assert.That(hidden, Is.True, "A minimized main window is hidden, and the window list is not read");
        }

        [Test]
        public void Given_TheWindowListReading_When_HiddenIsDecided_Then_TheListIsAsked()
        {
            // Arrange
            var main = new NoWindowsAtAll();
            var list = ListOf(true);

            // Act
            var hidden = ApplicationVisibility.IsHiddenOn(ApplicationVisibility.WindowReading.WindowList, ref main, ref list);

            // Assert
            Assert.That(hidden, Is.True, "A hidden application is hidden, and the main window is not read");
        }

        [TestCase(0, true)]
        [TestCase(1, false)]
        public void Given_AHandle_When_ItIsAskedWhetherUnset_Then_OnlyZeroIs(int value, bool expected)
        {
            // Act
            var unset = ApplicationVisibility.IsUnset(new IntPtr(value));

            // Assert
            Assert.That(unset, Is.EqualTo(expected));
        }

        [Test]
        public void Given_AZeroHandle_When_ItIsRequired_Then_ItThrowsWithTheFailure()
        {
            // Act
            TestDelegate require = () => ApplicationVisibility.Required(IntPtr.Zero, "no instance");

            // Assert
            Assert.That(require, Throws.InvalidOperationException.With.Message.EqualTo("no instance"));
        }

        [Test]
        public void Given_ANonZeroHandle_When_ItIsRequired_Then_ItIsReturned()
        {
            // Act
            var handle = ApplicationVisibility.Required(s_window, "no instance");

            // Assert
            Assert.That(handle, Is.EqualTo(s_window));
        }

        [TestCase((byte)0, false)]
        [TestCase((byte)1, true)]
        [TestCase((byte)255, true)]
        public void Given_ANativeBoolean_When_ItIsRead_Then_AnyNonZeroByteIsTrue(byte value, bool expected)
        {
            // Act
            var flag = ApplicationVisibility.Flag(value);

            // Assert
            Assert.That(flag, Is.EqualTo(expected));
        }

        [TestCase((byte)1, (byte)0, true)]
        [TestCase((byte)0, (byte)0, false)]
        [TestCase((byte)1, (byte)1, false)]
        public void Given_AWindowsVisibleAndMiniaturizedBytes_When_ItIsAskedWhetherShown_Then_ItIsVisibleAndNotMiniaturized(
            byte visible, byte miniaturized, bool expected)
        {
            // Act
            var shown = ApplicationVisibility.IsShown(visible, miniaturized);

            // Assert
            Assert.That(shown, Is.EqualTo(expected));
        }
    }
}

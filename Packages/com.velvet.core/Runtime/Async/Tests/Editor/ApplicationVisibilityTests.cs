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

        private struct FakeWindows : ApplicationVisibility.IWindowSource
        {
            internal bool Hidden;
            internal bool[] Shown;
            internal int CountReads;

            public bool ApplicationIsHidden() => Hidden;

            public ulong WindowCount()
            {
                CountReads++;
                return (ulong)Shown.Length;
            }

            // Past the last window this throws, so a loop that reads one too many fails the case.
            public bool WindowIsShown(ulong index) => Shown[(int)index];
        }

        private static bool HiddenFrom(bool applicationIsHidden, params bool[] shown)
        {
            var windows = new FakeWindows { Hidden = applicationIsHidden, Shown = shown };
            return ApplicationVisibility.IsHiddenFrom(ref windows);
        }

        [Test]
        public void Given_ApplicationReportsItselfHidden_When_HiddenIsDecided_Then_ItIsHidden()
        {
            // Act
            var hidden = HiddenFrom(true, true);

            // Assert
            Assert.That(hidden, Is.True, "A hidden application is hidden whatever its windows say");
        }

        [Test]
        public void Given_ApplicationReportsItselfHidden_When_HiddenIsDecided_Then_ItsWindowsAreNotCounted()
        {
            // Arrange
            var windows = new FakeWindows { Hidden = true, Shown = new[] { true } };

            // Act
            ApplicationVisibility.IsHiddenFrom(ref windows);

            // Assert
            Assert.That(windows.CountReads, Is.EqualTo(0), "The windows are asked about only once the application is not hidden");
        }

        [Test]
        public void Given_NoWindows_When_HiddenIsDecided_Then_ItIsNotHidden()
        {
            // Act
            var hidden = HiddenFrom(false);

            // Assert
            Assert.That(hidden, Is.False, "An application with no windows reads as visible");
        }

        [Test]
        public void Given_WindowsNoneOfWhichIsShown_When_HiddenIsDecided_Then_ItIsHidden()
        {
            // Act
            var hidden = HiddenFrom(false, false, false);

            // Assert
            Assert.That(hidden, Is.True, "Windows that are all hidden or miniaturized hide the application");
        }

        [Test]
        public void Given_OneShownWindowAmongOthers_When_HiddenIsDecided_Then_ItIsNotHidden()
        {
            // Act
            var hidden = HiddenFrom(false, false, true, false);

            // Assert
            Assert.That(hidden, Is.False, "One shown window is enough, wherever it sits");
        }

        private static readonly IntPtr s_window = new(7);

        [Test]
        public void Given_AMainWindowAlreadyFound_When_MinimizedIsRead_Then_NoSearchRuns()
        {
            // Arrange
            var main = s_window;
            var searches = 0;

            // Act
            ApplicationVisibility.IsMinimized(ref main, () => searches++, _ => false);

            // Assert
            Assert.That(searches, Is.EqualTo(0), "A window found once is not searched for again");
        }

        [Test]
        public void Given_AMainWindowAlreadyFound_When_MinimizedIsRead_Then_ItsIconicStateAnswers()
        {
            // Arrange
            var main = s_window;

            // Act
            var minimized = ApplicationVisibility.IsMinimized(ref main, () => { }, window => window == s_window);

            // Assert
            Assert.That(minimized, Is.True, "The answer is the found window's own state");
        }

        [Test]
        public void Given_NoMainWindowYet_When_TheSearchFindsOne_Then_ItsIconicStateAnswers()
        {
            // Arrange
            var main = IntPtr.Zero;

            // Act
            var minimized = ApplicationVisibility.IsMinimized(ref main, () => main = s_window, window => window == s_window);

            // Assert
            Assert.That(minimized, Is.True, "The window the search left is the one asked");
        }

        [Test]
        public void Given_NoMainWindowYet_When_TheSearchFindsNone_Then_ItThrows()
        {
            // Arrange
            var main = IntPtr.Zero;

            // Act
            TestDelegate read = () => ApplicationVisibility.IsMinimized(ref main, () => { }, _ => false);

            // Assert
            Assert.That(read, Throws.InvalidOperationException, "A thread with no window has nothing to ask");
        }

        private static bool KeepLooking(bool visible, IntPtr owner, ref IntPtr main) =>
            ApplicationVisibility.KeepLooking(s_window, _ => visible, _ => owner, ref main);

        [Test]
        public void Given_AVisibleUnownedWindow_When_TheEnumerationAsks_Then_ItStops()
        {
            // Arrange
            var main = IntPtr.Zero;

            // Act
            var keepLooking = KeepLooking(true, IntPtr.Zero, ref main);

            // Assert
            Assert.That(keepLooking, Is.False, "The first visible window with no owner ends the search");
        }

        [Test]
        public void Given_AVisibleUnownedWindow_When_TheEnumerationAsks_Then_ItIsTheMainWindow()
        {
            // Arrange
            var main = IntPtr.Zero;

            // Act
            KeepLooking(true, IntPtr.Zero, ref main);

            // Assert
            Assert.That(main, Is.EqualTo(s_window));
        }

        [Test]
        public void Given_AnInvisibleWindow_When_TheEnumerationAsks_Then_ItGoesOn()
        {
            // Arrange
            var main = IntPtr.Zero;

            // Act
            var keepLooking = KeepLooking(false, IntPtr.Zero, ref main);

            // Assert
            Assert.That(keepLooking, Is.True);
        }

        [Test]
        public void Given_AnInvisibleWindow_When_TheEnumerationAsks_Then_ItIsNotTheMainWindow()
        {
            // Arrange
            var main = IntPtr.Zero;

            // Act
            KeepLooking(false, IntPtr.Zero, ref main);

            // Assert
            Assert.That(main, Is.EqualTo(IntPtr.Zero));
        }

        [Test]
        public void Given_AnInvisibleWindow_When_TheEnumerationAsks_Then_ItsOwnerIsNotLookedUp()
        {
            // Arrange
            var main = IntPtr.Zero;
            var lookups = 0;

            // Act
            ApplicationVisibility.KeepLooking(s_window, _ => false, _ =>
            {
                lookups++;
                return IntPtr.Zero;
            }, ref main);

            // Assert
            Assert.That(lookups, Is.EqualTo(0), "The owner is read only of a visible window");
        }

        [Test]
        public void Given_AnOwnedVisibleWindow_When_TheEnumerationAsks_Then_ItGoesOn()
        {
            // Arrange
            var main = IntPtr.Zero;

            // Act
            var keepLooking = KeepLooking(true, new IntPtr(9), ref main);

            // Assert
            Assert.That(keepLooking, Is.True, "A window with an owner is not the main window");
        }

        [Test]
        public void Given_AnOwnedVisibleWindow_When_TheEnumerationAsks_Then_ItIsNotTheMainWindow()
        {
            // Arrange
            var main = IntPtr.Zero;

            // Act
            KeepLooking(true, new IntPtr(9), ref main);

            // Assert
            Assert.That(main, Is.EqualTo(IntPtr.Zero));
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

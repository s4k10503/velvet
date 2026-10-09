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
    /// read is replaced through its private field, since CI runs neither the Windows nor the macOS one.
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
    }
}

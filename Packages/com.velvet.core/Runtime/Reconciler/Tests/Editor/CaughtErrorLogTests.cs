using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a tree mounted without an <c>OnCaughtError</c> handler logs when an error boundary
    /// catches: one exception entry that reads as the caught exception itself, whose stack trace goes on to
    /// name the boundary and the component stack.
    /// </summary>
    [TestFixture]
    internal sealed class CaughtErrorLogTests
    {
        private const string ThrownMessage = "caught boom";

        private readonly List<(LogType Type, string Condition, string StackTrace)> _entries = new();
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            _entries.Clear();
            Application.logMessageReceived += Record;
        }

        [TearDown]
        public void TearDown() => Application.logMessageReceived -= Record;

        private void Record(string condition, string stackTrace, LogType type)
            => _entries.Add((type, condition, stackTrace));

        [Test]
        public void Given_ABoundaryCatchingAChildsRender_When_MountedWithoutOptions_Then_TheCaughtExceptionIsLoggedAsItself()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, $"InvalidOperationException: {ThrownMessage}");

            // Act
            using var mounted = V.Mount(_root, V.Component(BoundaryRender, key: "boundary"));

            // Assert — the rendered text separates a catch from the uncaught path, which logs the same entry.
            Assert.That(
                string.Join(" / ", _entries.Select(entry => $"{entry.Type}: {entry.Condition}"))
                + " | " + _root.FindFirstLabel()?.text,
                Is.EqualTo($"Exception: InvalidOperationException: {ThrownMessage} | fallback"));
        }

        [Test]
        public void Given_ABoundaryCatchingAChildsRender_When_MountedWithoutOptions_Then_TheLogNamesTheBoundaryAndTheComponentStack()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, $"InvalidOperationException: {ThrownMessage}");

            // Act
            using var mounted = V.Mount(_root, V.Component(BoundaryRender, key: "boundary"));

            // Assert — from the report's first word through the component stack's first line, read the same
            // whether or not the listener's copy of that line kept its "at" prefix.
            var logged = string.Join("\n", _entries.Select(entry => entry.StackTrace)).Replace("\n    at ", "\n");
            var start = logged.IndexOf("Caught by the ", StringComparison.Ordinal);
            var firstLineEnd = start < 0 ? -1 : logged.IndexOf('\n', start);
            var secondLineEnd = firstLineEnd < 0 ? -1 : logged.IndexOf('\n', firstLineEnd + 1);
            Assert.That(
                secondLineEnd < 0 ? logged : logged.Substring(start, secondLineEnd - start),
                Is.EqualTo(
                    "Caught by the CaughtErrorLogTests.BoundaryRender error boundary, which rendered its fallback" +
                    " in place of its children. Component stack:\nCaughtErrorLogTests.ThrowingChildRender"));
        }

        [Component(IsErrorBoundary = true)]
        private static VNode BoundaryRender()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Component(ThrowingChildRender, key: "child");
        }

        [Component]
        private static VNode ThrowingChildRender() => throw new InvalidOperationException(ThrownMessage);
    }
}

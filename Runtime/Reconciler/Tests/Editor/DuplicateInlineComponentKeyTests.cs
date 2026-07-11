using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the duplicate sibling-key guard for inline component nodes. Two same-identity siblings
    /// sharing one explicit key resolve to the SAME registry fiber — the leaf-level keyed diff warns
    /// on duplicates, but the component path silently expanded the one fiber once per sibling: its
    /// DOM output was emitted twice while the fiber's slot bookkeeping tracked only the last
    /// position (and both copies shared one hook state), so a later re-render patched one copy and
    /// stranded the other. The repeat must warn and be skipped; two independent instances require
    /// unique keys, exactly as the reachable V.List keySelector documentation implies.
    /// </summary>
    [TestFixture]
    internal sealed class DuplicateInlineComponentKeyTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
        }

        [Component]
        private static VNode FooRow()
        {
            return V.Label(text: "foo");
        }

        [Component]
        private static VNode DupHost()
        {
            return V.Div(name: "dup-host", children: new VNode[]
            {
                V.Component(FooRow, key: "x"),
                V.Component(FooRow, key: "x"),
            });
        }

        [Test]
        public void Given_TwoSiblingComponentsWithTheSameKey_When_Mounted_Then_OnlyOneInstanceCommits()
        {
            // Arrange — the duplicate is reported (LogAssert also fails if no warning fires).
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Duplicate component key"));

            // Act
            using var mounted = V.Mount(_root, V.Component(DupHost, key: "host"));

            // Assert — one fiber, one committed copy; the repeat did not double-emit its DOM.
            Assert.AreEqual(1, _root.Q<VisualElement>("dup-host").childCount);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Records the positional (unkeyed) reconciliation semantics for STATEFUL siblings: when same-type
    /// children carry no key, their hook state is bound to array POSITION, not identity. Inserting a sibling at
    /// the head shifts every subsequent position by one, so each fiber's state stays at its old index and
    /// therefore lands on a different conceptual child — the classic "state bleeds by one" bug that motivates
    /// using keys. <see cref="ReconcilerIndexedTests"/> only pins host-element type-match patch vs. type-mismatch
    /// replace per index; it never exercises stateful-component hook state moving with position. This fixture
    /// fills that gap.
    ///
    /// These are parity tests written against the canonical declarative-UI behaviour, so they are expected GREEN: Velvet's
    /// ChildReconciler indexed path should bind hook state to the slot index. This case is
    /// also an unverified divergence candidate — if Velvet shifts state differently (e.g. discards or
    /// mis-assigns the trailing slot), the assertion turns RED and exposes the behavioural difference.
    /// GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class PositionalStateBleedParityTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_insertHead = default;
        }

        private static StateUpdater<bool> s_insertHead;

        // A stateful child with NO key, used purely positionally. Its own count is rendered as the button's
        // text and a click increments it. Several of these sit side by side so an insertion at the head can
        // shift their array positions.
        [Component]
        private static VNode CountingChild()
        {
            var (count, setCount) = Hooks.UseState(0);
            return V.Button(text: count.ToString(), onClick: () => setCount.Invoke(c => c + 1));
        }

        // Parent renders the counting children UNKEYED. Before insertion it renders [X, Y]; a click on
        // "insert-head" flips state so it renders [New, X, Y] — all same type, all keyless — which is the
        // positional-insertion scenario.
        [Component]
        private static VNode HeadInsertingParent()
        {
            var (inserted, setInserted) = Hooks.UseState(false);
            s_insertHead = setInserted;

            var children = new List<VNode>
            {
                V.Button(name: "insert-head", onClick: () => setInserted.Invoke(_ => true)),
            };
            if (inserted)
            {
                children.Add(V.Component(CountingChild));
            }
            children.Add(V.Component(CountingChild));
            children.Add(V.Component(CountingChild));

            return V.Div(name: "parent", children: children.ToArray());
        }

        // Returns the counting children's button texts in document (tree) order, excluding the control button.
        private static string[] CountingTexts(VisualElement root) =>
            root.Query<Button>()
                .ToList()
                .Where(b => b.name != "insert-head")
                .Select(b => b.text)
                .ToArray();

        [Test]
        public void Given_UnkeyedStatefulSiblings_When_ElementInsertedAtHead_Then_StateShiftsByPosition()
        {
            // Arrange — two unkeyed stateful siblings advanced to counts [1, 2] by position.
            using var mounted = V.Mount(_root, V.Component(HeadInsertingParent, key: "parent"));
            var counters = _root.Query<Button>().ToList().Where(b => b.name != "insert-head").ToList();
            counters[0].SimulateClick();                 // X -> 1
            counters[1].SimulateClick();                 // Y -> 1
            counters[1].SimulateClick();                 // Y -> 2
            Assume.That(CountingTexts(_root), Is.EqualTo(new[] { "1", "2" }),
                "Precondition: the two unkeyed siblings hold counts 1 and 2 by position");

            // Act — a new sibling is inserted at the head, shifting every following position by one.
            _root.Q<Button>("insert-head").SimulateClick();

            // Assert — hook state stayed bound to position: slot 0 keeps 1, slot 1 keeps 2, and the new trailing
            // slot is fresh at 0. The old siblings' state has bled forward by one conceptual child.
            Assert.That(CountingTexts(_root), Is.EqualTo(new[] { "1", "2", "0" }));
        }
    }
}

using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which old element each new keyed element takes on the walk a Fragment sibling sends a
    /// container through, as React's reconcileChildrenArray decides it.
    /// <list type="bullet">
    /// <item>Two siblings sharing a key and rendered again in the same order each keep their own element.</item>
    /// <item>The second of them dropped, the first keeps its element and the second's goes.</item>
    /// <item>Once a sibling before them no longer matches, the first takes the last old element carrying the
    /// key and the second mounts afresh.</item>
    /// <item>Mounting them logs a warning naming the key.</item>
    /// <item>An element whose type changes under its key leaves only the new element.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class GeneralPathLeafMatchTests
    {
        private readonly record struct PhaseState(int Phase);

        private sealed class PhaseStore : Store<PhaseState>
        {
            public PhaseStore() : base(new PhaseState(0)) { }
            public void Set(int phase) => SetState(_ => new PhaseState(phase));
            protected override void ResetCore() => SetState(_ => new PhaseState(0));
        }

        private static PhaseStore s_store;
        private static Func<int, VNode[]> s_children;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
            s_children = null;
        }

        // The Fragment ahead of the children a case gives is what sends the list through the walk.
        [Component]
        private static VNode List()
        {
            var phase = Hooks.UseStore(s_store, s => s.Phase);
            var children = s_children(phase);
            var all = new VNode[children.Length + 1];
            all[0] = V.Fragment(new VNode[] { V.Label(key: "f", text: "F") });
            children.CopyTo(all, 1);
            return V.Div(name: "list", children: all);
        }

        private MountedTree Mount(PhaseStore store, Func<int, VNode[]> children)
        {
            s_store = store;
            s_children = children;
            return V.Mount(_root, V.Component(List, key: "list"));
        }

        private static void Render(MountedTree mounted, PhaseStore store, int phase)
        {
            store.Set(phase);
            mounted.Root.Reconciler.Context.BatchScheduler.DrainImmediateForTest();
        }

        [Test]
        public void Given_TwoSiblingsSharingAKey_When_RenderedAgainInTheSameOrder_Then_EachKeepsItsElement()
        {
            // Arrange
            using var store = new PhaseStore();
            using var mounted = Mount(store, phase => new VNode[]
            {
                V.Label(key: "x", text: "A" + phase), V.Label(key: "x", text: "B" + phase),
            });
            var list = _root.Q<VisualElement>("list");
            var (first, second) = (list.ElementAt(1), list.ElementAt(2));

            // Act
            Render(mounted, store, 1);

            // Assert
            Assert.That(
                (ReferenceEquals(list.ElementAt(1), first), ReferenceEquals(list.ElementAt(2), second)),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_TwoSiblingsSharingAKey_When_TheSecondIsDropped_Then_TheFirstKeepsItsElementAlone()
        {
            // Arrange
            using var store = new PhaseStore();
            using var mounted = Mount(store, phase => phase == 0
                ? new VNode[] { V.Label(key: "x", text: "A"), V.Label(key: "x", text: "B") }
                : new VNode[] { V.Label(key: "x", text: "A") });
            var list = _root.Q<VisualElement>("list");
            var first = list.ElementAt(1);

            // Act
            Render(mounted, store, 1);

            // Assert — the fragment's label and the first repeat, which is still the element it mounted as.
            Assert.That((list.childCount, ReferenceEquals(list.ElementAt(1), first)), Is.EqualTo((2, true)));
        }

        // GREEN_ON_BASE(characterization): the base resolved every repeat to the last old element for its key.
        // This case's order leaves the first repeat there too. Let an entry off the linear pass keep the next
        // on it (`linear || oldIndex == ordinal`) and the second repeat takes the first's element: this reddens.
        [Test]
        public void Given_TwoSiblingsSharingAKeyAfterAnother_When_ThatOneIsDropped_Then_TheFirstTakesTheLastElementForTheKey()
        {
            // Arrange
            using var store = new PhaseStore();
            using var mounted = Mount(store, phase => phase == 0
                ? new VNode[] { V.Label(key: "y", text: "Y"), V.Label(key: "x", text: "A"), V.Label(key: "x", text: "B") }
                : new VNode[] { V.Label(key: "x", text: "A"), V.Label(key: "x", text: "B") });
            var list = _root.Q<VisualElement>("list");
            var (first, second) = (list.ElementAt(2), list.ElementAt(3));

            // Act
            Render(mounted, store, 1);

            // Assert — the first repeat is on the second's element, and the second on neither.
            Assert.That(
                (list.childCount, ReferenceEquals(list.ElementAt(1), second),
                    ReferenceEquals(list.ElementAt(2), first) || ReferenceEquals(list.ElementAt(2), second)),
                Is.EqualTo((3, true, false)));
        }

        [Test]
        public void Given_TwoSiblingsSharingAKey_When_Mounted_Then_TheRepeatIsReported()
        {
            // Arrange
            using var store = new PhaseStore();
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Duplicate key detected among new siblings: \"x\""));

            // Act
            using var mounted = Mount(store, _ => new VNode[] { V.Label(key: "x"), V.Label(key: "x") });

            // Assert — both render; the warning is held by LogAssert at the end of the case.
            Assert.That(_root.Q<VisualElement>("list").childCount, Is.EqualTo(3));
        }

        // GREEN_ON_BASE(characterization): the base removed an old element its key's new element replaced.
        // Keep every old element a new one took (drop `if (newElements[j].isExisting)`) and the label stays
        // beside the button.
        [Test]
        public void Given_AKeyedElement_When_ItsTypeChanges_Then_OnlyTheNewElementRemains()
        {
            // Arrange
            using var store = new PhaseStore();
            using var mounted = Mount(store, phase => new VNode[]
            {
                phase == 0 ? V.Label(key: "k", text: "L") : V.Button(key: "k", text: "B"),
            });
            var list = _root.Q<VisualElement>("list");

            // Act
            Render(mounted, store, 1);

            // Assert
            Assert.That((list.childCount, list.ElementAt(1) is Button), Is.EqualTo((2, true)));
        }
    }
}

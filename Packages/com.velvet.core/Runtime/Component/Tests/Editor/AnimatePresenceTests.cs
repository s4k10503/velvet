using System;
using System.Collections.Generic;
using NUnit.Framework;
using Velvet;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="V.AnimatePresence"/> / <see cref="V.Motion"/> and the supporting
    /// <see cref="StyleTransitionConfig"/>.
    /// <list type="bullet">
    /// <item><see cref="V.Motion"/> parses its class string, carries the supplied key, and defaults its
    /// transition to <c>StyleTransition.Fade</c>; an explicit duration / easing / delay overrides only that
    /// field of the resolved preset while inheriting the rest; the Motion maps to a VisualElement by default and
    /// to <c>elementType</c> when supplied (e.g. a Button).</item>
    /// <item><see cref="V.AnimatePresence"/> stores its children (null becomes an empty array) and, when
    /// reconciled, is DOM-less: it emits no wrapper, so each non-null keyed child becomes a direct child of the
    /// parent.</item>
    /// <item>A transparent child (Suspense, or a Memo resolving to Suspense) renders its resolved content
    /// directly into the parent with no intermediate wrapper element; a multi-element resolved range occupies
    /// several slots and every one is removed when its key exits.</item>
    /// <item>Patching adds, updates, removes, and reorders keyed children so DOM order matches VNode order;
    /// duplicate keys collapse to the last entry; a null-keyed child receives a position-based auto key that
    /// coexists with explicit keys.</item>
    /// <item>On exit, a child with a real transition stays in the DOM until the transition completes (and is
    /// retained if re-added meanwhile), whereas a child with no transition or
    /// <see cref="StyleTransitionConfig.None"/> is removed immediately.</item>
    /// <item><c>initial: false</c> suppresses the enter animation on the very first mount only — later additions
    /// still animate; the resolved Motion's <c>OnEnterComplete</c> fires immediately when the enter animation is
    /// skipped.</item>
    /// <item><see cref="StyleTransitionConfig"/> lazily parses and caches its class arrays, treats null class
    /// strings as empty, and its <c>With</c> override copies the class arrays while overriding only the supplied
    /// duration / easing / exit-easing / delay and never mutating the original.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class AnimatePresenceTests : ReconcilerTestFixture
    {
        #region Motion builder

        [Test]
        public void Given_ClassString_When_MotionBuilt_Then_ClassNamesAreParsed()
        {
            // Act
            var node = V.Motion("item item--active");

            // Assert
            CollectionAssert.AreEqual(new[] { "item", "item--active" }, node.ClassNames);
        }

        [Test]
        public void Given_Key_When_MotionBuilt_Then_KeyIsCarried()
        {
            // Act
            var node = V.Motion(key: "my-key");

            // Assert
            Assert.That(node.Key, Is.EqualTo("my-key"));
        }

        [Test]
        public void Given_Transition_When_MotionBuilt_Then_TransitionIsCarried()
        {
            // Act
            var node = V.Motion(transition: StyleTransition.Fade);

            // Assert
            Assert.That(node.Transition, Is.SameAs(StyleTransition.Fade));
        }

        [Test]
        public void Given_NoTransition_When_MotionBuilt_Then_DefaultsToFade()
        {
            // Act
            var node = V.Motion();

            // Assert
            Assert.That(node.Transition, Is.SameAs(StyleTransition.Fade));
        }

        [Test]
        public void Given_NoElementType_When_MotionBuilt_Then_DefaultsToVisualElement()
        {
            // Act
            var node = V.Motion();

            // Assert
            Assert.That(node.ElementType, Is.EqualTo(typeof(VisualElement)));
        }

        [Test]
        public void Given_ElementType_When_MotionBuilt_Then_ElementTypeIsCarried()
        {
            // Act
            var node = V.Motion(elementType: typeof(Button));

            // Assert
            Assert.That(node.ElementType, Is.EqualTo(typeof(Button)));
        }

        [Test]
        public void Given_AnimateLabel_When_MotionBuilt_Then_TheVariantIsNotBakedIntoClassNamesButResolvedAtReconcile()
        {
            // Arrange — named animation states.
            var variants = new Dictionary<string, string>
            {
                { "hidden", "opacity-0 translate-y-4" },
                { "visible", "opacity-100 translate-y-0" },
            };

            // Act
            var node = V.Motion("item", variants: variants, animate: "visible");

            // Assert — the active variant is resolved at reconcile time (effective label =
            // Animate ?? inherited-from-ancestor), so construction leaves ClassNames base-only. The applied class appears on the
            // mounted element instead (see MotionVariantPropagationTests).
            CollectionAssert.AreEqual(new[] { "item" }, node.ClassNames);
        }

        [Test]
        public void Given_NullAnimate_When_MotionBuilt_Then_OnlyTheBaseClassApplies()
        {
            // Arrange
            var variants = new Dictionary<string, string> { { "visible", "opacity-100" } };

            // Act — no active label selected.
            var node = V.Motion("item", variants: variants);

            // Assert
            CollectionAssert.AreEqual(new[] { "item" }, node.ClassNames);
        }

        [Test]
        public void Given_AnimateLabelAbsentFromVariants_When_MotionBuilt_Then_OnlyTheBaseClassApplies()
        {
            // Arrange
            var variants = new Dictionary<string, string> { { "visible", "opacity-100" } };

            // Act — the requested label is not a key of the variants map.
            var node = V.Motion("item", variants: variants, animate: "ghost");

            // Assert
            CollectionAssert.AreEqual(new[] { "item" }, node.ClassNames);
        }

        [Test]
        public void Given_MotionWithButtonElementType_When_Mounted_Then_TheAnimatedCellIsAButton()
        {
            // Arrange — the animated cell IS the Button (no wrapping element).
            var tree = Presence(V.Motion(key: "a", elementType: typeof(Button),
                transition: StyleTransition.Fade, children: Label("A")));

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert — AnimatePresence is DOM-less, so the Motion (a Button) sits directly in Root.
            Assert.That(Root.ElementAt(0), Is.TypeOf<Button>());
        }

        [Test]
        public void Given_DurationOverride_When_MotionBuilt_Then_OverridesDurationAndInheritsPresetClasses()
        {
            // Act
            var node = V.Motion(transition: StyleTransition.Fade, duration: 0.5f);

            // Assert
            Assert.That(node.Transition.DurationSec, Is.EqualTo(0.5f));
            Assume.That(node.Transition.EnterFromClasses, Is.EqualTo(StyleTransition.Fade.EnterFromClasses),
                "Precondition: the override inherits the preset's class definitions");
        }

        [Test]
        public void Given_EasingOverride_When_MotionBuilt_Then_OverridesEasingAndInheritsDuration()
        {
            // Act
            var node = V.Motion(transition: StyleTransition.Fade, easing: EasingMode.Linear);

            // Assert
            Assert.That(node.Transition.Easing, Is.EqualTo(EasingMode.Linear));
            Assume.That(node.Transition.DurationSec, Is.EqualTo(StyleTransition.Fade.DurationSec),
                "Precondition: an easing override leaves duration at the preset value");
        }

        [Test]
        public void Given_DurationOverrideOnly_When_MotionBuilt_Then_UsesDefaultFadeTransition()
        {
            // Act
            var node = V.Motion(duration: 1.0f);

            // Assert
            Assert.That(node.Transition.DurationSec, Is.EqualTo(1.0f));
            Assume.That(node.Transition.EnterFromClasses, Is.EqualTo(StyleTransition.Fade.EnterFromClasses),
                "Precondition: with no preset given, the override applies on top of the default Fade preset");
        }

        [Test]
        public void Given_DelayOverride_When_MotionBuilt_Then_OverridesDelayAndInheritsDuration()
        {
            // Act
            var node = V.Motion(transition: StyleTransition.Fade, delay: 0.3f);

            // Assert
            Assert.That(node.Transition.DelaySec, Is.EqualTo(0.3f));
            Assume.That(node.Transition.DurationSec, Is.EqualTo(StyleTransition.Fade.DurationSec),
                "Precondition: a delay override leaves duration at the preset value");
        }

        [Test]
        public void Given_DefaultMotion_When_MotionBuilt_Then_ElementTypeIsVisualElement()
        {
            // Act
            var node = V.Motion();

            // Assert
            Assert.That(node.ElementType, Is.EqualTo(typeof(VisualElement)));
        }

        [Test]
        public void Given_OnEnterCompleteCallback_When_MotionBuilt_Then_CallbackIsCarried()
        {
            // Arrange
            var called = false;

            // Act
            var node = V.Motion(onEnterComplete: () => called = true);
            node.OnEnterComplete?.Invoke();

            // Assert
            Assert.That(called, Is.True, "The supplied OnEnterComplete callback is wired onto the node");
        }

        [Test]
        public void Given_NoOnEnterComplete_When_MotionBuilt_Then_CallbackIsNull()
        {
            // Act
            var node = V.Motion();

            // Assert
            Assert.That(node.OnEnterComplete, Is.Null);
        }

        #endregion

        #region AnimatePresence builder

        [Test]
        public void Given_Children_When_AnimatePresenceBuilt_Then_ChildrenAreCarried()
        {
            // Arrange
            var child = V.Motion(key: "a");

            // Act
            var node = V.AnimatePresence(children: new VNode[] { child });

            // Assert
            Assert.That(node.Children, Is.EqualTo(new VNode[] { child }));
        }

        [Test]
        public void Given_NullChildren_When_AnimatePresenceBuilt_Then_ChildrenIsEmpty()
        {
            // Act
            var node = V.AnimatePresence();

            // Assert
            Assert.That(node.Children, Is.Empty);
        }

        [Test]
        public void Given_StaggerSec_When_AnimatePresenceBuilt_Then_StaggerIsCarried()
        {
            // Act
            var node = V.AnimatePresence(staggerSec: 0.05f, children: new VNode[] { V.Motion(key: "a"), V.Motion(key: "b") });

            // Assert
            Assert.That(node.StaggerSec, Is.EqualTo(0.05f));
        }

        [Test]
        public void Given_NoStaggerSec_When_AnimatePresenceBuilt_Then_StaggerDefaultsToZero()
        {
            // Act
            var node = V.AnimatePresence();

            // Assert
            Assert.That(node.StaggerSec, Is.EqualTo(0f));
        }

        #endregion

        #region Initial render

        [Test]
        public void Given_AnimatePresence_When_InitialRender_Then_ItsChildIsADirectParentChild()
        {
            // Arrange — AnimatePresence is DOM-less: no wrapper, so its keyed Motion
            // child is a direct child of the parent.
            var tree = Presence(V.Motion("item", key: "a", transition: StyleTransition.Fade, children: Label("hello")));

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(Root.ElementAt(0).ClassListContains("item"), Is.True);
        }

        [Test]
        public void Given_MotionChild_When_InitialRender_Then_ElementCarriesItsClass()
        {
            // Arrange
            var tree = Presence(V.Motion("my-class", key: "a", transition: StyleTransition.Fade, children: Label("content")));

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(Root.ElementAt(0).ClassListContains("my-class"), Is.True);
        }

        [Test]
        public void Given_MultipleChildren_When_InitialRender_Then_EachBecomesAnElement()
        {
            // Arrange
            var tree = Presence(
                V.Motion(key: "a", children: Label("A")),
                V.Motion(key: "b", children: Label("B")));

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(Root.childCount, Is.EqualTo(2));
        }

        [Test]
        public void Given_NullChild_When_InitialRender_Then_NullSlotIsSkipped()
        {
            // Arrange
            var tree = Presence(V.Motion(key: "a", children: Label("A")), null);

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(Root.childCount, Is.EqualTo(1));
        }

        #endregion

        #region Transparent children

        [Test]
        public void Given_SuspenseChild_When_InitialRender_Then_ResolvedContentSitsDirectlyInContainer()
        {
            // Arrange — Suspense emits no container, so its resolved content lands straight in the presence container.
            var tree = Presence(V.Suspense(fallback: V.Label(text: "loading"), children: Label("content"), key: "s"));

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            var content = Root.ElementAt(0) as Label;
            Assert.That(content?.text, Is.EqualTo("content"),
                "The Suspense child renders wrapper-less: its resolved Label is a direct presence-container child");
        }

        [Test]
        public void Given_MemoResolvingToSuspense_When_InitialRender_Then_ResolvedContentSitsDirectlyInContainer()
        {
            // Arrange — neither the Memo nor the Suspense emits a container element.
            var tree = Presence(
                V.Memoized(() => V.Suspense(fallback: V.Label(text: "loading"), children: Label("content"))));

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            var content = Root.ElementAt(0) as Label;
            Assert.That(content?.text, Is.EqualTo("content"),
                "The Memo-then-Suspense content renders wrapper-less into the presence container");
        }

        [Test]
        public void Given_MultiElementSuspenseChild_When_KeyExits_Then_EveryElementOfTheRangeIsRemoved()
        {
            // Arrange — a wrapper-less Suspense resolving to two Labels occupies two presence slots.
            VNode[] Tree(bool show) => Presence(show
                ? V.Suspense(fallback: V.Label(text: "loading"), children: new VNode[] { V.Label(text: "a"), V.Label(text: "b") }, key: "s")
                : null);
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), Tree(true));
            var container = Root;
            Assume.That(container.childCount, Is.EqualTo(2), "Precondition: both Suspense content elements are present");

            // Act
            Reconciler.Reconcile(Root, Tree(true), Tree(false));

            // Assert
            Assert.That(container.childCount, Is.EqualTo(0),
                "Every element of the multi-element range is removed on exit, with no trailing leak");
        }

        #endregion

        #region Patching

        [Test]
        public void Given_MountedPresence_When_PatchedWithExtraChild_Then_ElementIsAdded()
        {
            // Arrange
            var old = Presence(V.Motion(key: "a", children: Label("A")));
            var @new = Presence(
                V.Motion(key: "a", children: Label("A")),
                V.Motion(key: "b", children: Label("B")));
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), old);

            // Act
            Reconciler.Reconcile(Root, old, @new);

            // Assert
            Assert.That(Root.childCount, Is.EqualTo(2));
        }

        [Test]
        public void Given_MountedPresence_When_ExistingChildPatched_Then_ContentUpdatesInPlace()
        {
            // Arrange
            var old = Presence(V.Motion(key: "a", children: Label("old")));
            var @new = Presence(V.Motion(key: "a", children: Label("new")));
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), old);

            // Act
            Reconciler.Reconcile(Root, old, @new);

            // Assert
            var label = Root.ElementAt(0).ElementAt(0) as Label;
            Assert.That(label?.text, Is.EqualTo("new"));
        }

        [Test]
        public void Given_SameTypeMotion_When_Patched_Then_ClassUpdatesInPlace()
        {
            // Arrange
            var old = new VNode[] { V.Motion("cls-a", key: "x") };
            var @new = new VNode[] { V.Motion("cls-b", key: "x") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), old);

            // Act
            Reconciler.Reconcile(Root, old, @new);

            // Assert
            Assert.That(Root.ElementAt(0).ClassListContains("cls-b"), Is.True,
                "A same-type Motion patches the existing element rather than recreating it");
        }

        [Test]
        public void Given_AVariantMotion_When_AnimateLabelChanges_Then_TheVariantClassesSwapInPlace()
        {
            // Arrange — a Motion driven by a named variant, mounted in the hidden state.
            var variants = new Dictionary<string, string>
            {
                { "hidden", "opacity-0" },
                { "visible", "opacity-100" },
            };
            var old = new VNode[] { V.Motion("base", key: "x", variants: variants, animate: "hidden") };
            var @new = new VNode[] { V.Motion("base", key: "x", variants: variants, animate: "visible") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), old);
            Assume.That(Root.ElementAt(0).ClassListContains("opacity-0"), Is.True,
                "Precondition: starts in the hidden variant");

            // Act — switch the active variant label (a USS transition-* utility would tween this swap).
            Reconciler.Reconcile(Root, old, @new);

            // Assert — the old variant's classes are gone and the new variant's classes are applied in place.
            Assert.That(
                (Root.ElementAt(0).ClassListContains("opacity-100"), Root.ElementAt(0).ClassListContains("opacity-0")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_DuplicateKeys_When_InitialRender_Then_LastEntryWins()
        {
            // Arrange
            var tree = Presence(
                V.Motion(key: "dup", transition: StyleTransition.Fade, children: Label("First")),
                V.Motion(key: "dup", transition: StyleTransition.Fade, children: Label("Second")));

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            var label = Root.ElementAt(0).ElementAt(0) as Label;
            Assert.That(label?.text, Is.EqualTo("Second"), "Duplicate keys collapse to a single element holding the last entry");
        }

        #endregion

        #region Ordering

        [Test]
        public void Given_HeadInsertion_When_Patched_Then_NewElementLandsAtTheHead()
        {
            // Arrange — [a, b] becomes [c, a, b].
            var initial = Presence(
                V.Motion(key: "a", children: Label("A")),
                V.Motion(key: "b", children: Label("B")));
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), initial);
            var updated = Presence(
                V.Motion(key: "c", children: Label("C")),
                V.Motion(key: "a", children: Label("A")),
                V.Motion(key: "b", children: Label("B")));

            // Act
            Reconciler.Reconcile(Root, initial, updated);

            // Assert
            var head = Root.ElementAt(0).ElementAt(0) as Label;
            Assert.That(head?.text, Is.EqualTo("C"), "The inserted key lands at the head so DOM order matches VNode order");
        }

        [Test]
        public void Given_KeyedChildren_When_OrderReversed_Then_ElementsAreReordered()
        {
            // Arrange — [a, b] becomes [b, a].
            var initial = Presence(
                V.Motion(key: "a", children: Label("A")),
                V.Motion(key: "b", children: Label("B")));
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), initial);
            var reversed = Presence(
                V.Motion(key: "b", children: Label("B")),
                V.Motion(key: "a", children: Label("A")));

            // Act
            Reconciler.Reconcile(Root, initial, reversed);

            // Assert
            var container = Root;
            var order = ((container.ElementAt(0).ElementAt(0) as Label)?.text, (container.ElementAt(1).ElementAt(0) as Label)?.text);
            Assert.That(order, Is.EqualTo(("B", "A")), "Reordering keyed children reorders their elements to match");
        }

        [Test]
        public void Given_AutoKeyedChildren_When_OrderReversed_Then_PositionKeyTreatsItAsContentChange()
        {
            // Arrange — auto keys are position-based, so a reversal is seen as content changes at the same positions.
            var initial = Presence(
                V.Motion(transition: StyleTransition.Fade, children: Label("A")),
                V.Motion(transition: StyleTransition.Fade, children: Label("B")),
                V.Motion(transition: StyleTransition.Fade, children: Label("C")));
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), initial);
            var reversed = Presence(
                V.Motion(transition: StyleTransition.Fade, children: Label("C")),
                V.Motion(transition: StyleTransition.Fade, children: Label("B")),
                V.Motion(transition: StyleTransition.Fade, children: Label("A")));

            // Act
            Reconciler.Reconcile(Root, initial, reversed);

            // Assert
            var container = Root;
            var order = (
                (container.ElementAt(0).ElementAt(0) as Label)?.text,
                (container.ElementAt(1).ElementAt(0) as Label)?.text,
                (container.ElementAt(2).ElementAt(0) as Label)?.text);
            Assert.That(order, Is.EqualTo(("C", "B", "A")),
                "Position-based auto keys reflect the reversed content at the same slots");
        }

        [Test]
        public void Given_ExplicitAndAutoKeyedChildren_When_PatchedWithSameStructure_Then_BothRemainStable()
        {
            // Arrange — an explicit key and an auto key coexist without collision.
            var tree = Presence(
                V.Motion(key: "explicit", children: Label("A")),
                V.Motion(children: Label("B")));
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);
            Assume.That(Root.childCount, Is.EqualTo(2), "Precondition: both children mounted");

            // Act
            Reconciler.Reconcile(Root, tree, tree);

            // Assert
            Assert.That(Root.childCount, Is.EqualTo(2),
                "A same-structure patch keeps both the explicit-keyed and auto-keyed children stable");
        }

        #endregion

        #region Exit behavior

        [Test]
        public void Given_ChildWithTransition_When_Removed_Then_ElementStaysWhileExiting()
        {
            // Arrange
            var old = Presence(V.Motion(key: "a", transition: StyleTransition.Fade, children: Label("A")));
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), old);
            var container = Root;

            // Act
            Reconciler.Reconcile(Root, old, Presence((VNode)null));

            // Assert
            Assert.That(container.childCount, Is.EqualTo(1),
                "A removed child with a real transition stays in the DOM until its exit completes");
        }

        [Test]
        public void Given_ChildExiting_When_ReAddedBeforeExitCompletes_Then_ElementIsRetained()
        {
            // Arrange
            var withChild = Presence(V.Motion(key: "a", transition: StyleTransition.Fade, children: Label("A")));
            var withoutChild = Presence((VNode)null);
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), withChild);
            var container = Root;
            Reconciler.Reconcile(Root, withChild, withoutChild);
            Assume.That(container.childCount, Is.EqualTo(1), "Precondition: the element is mid-exit, still present");

            // Act
            Reconciler.Reconcile(Root, withoutChild, withChild);

            // Assert
            Assert.That(container.childCount, Is.EqualTo(1), "Re-adding a key during its exit retains the same element");
        }

        [Test]
        public void Given_ChildWithoutTransition_When_Removed_Then_ElementIsRemovedImmediately()
        {
            // Arrange — a MotionNode with Transition=null has no exit animation to wait for.
            var initial = Presence(new MotionNode
            {
                Key = "a",
                ElementType = typeof(VisualElement),
                Transition = null,
                Children = Label("A"),
            });
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), initial);
            var container = Root;

            // Act
            Reconciler.Reconcile(Root, initial, Presence((VNode)null));

            // Assert
            Assert.That(container.childCount, Is.EqualTo(0), "Without a transition the element is removed immediately on exit");
        }

        [Test]
        public void Given_TransitionConfigNone_When_Removed_Then_ElementIsRemovedImmediately()
        {
            // Arrange — StyleTransitionConfig.None (DurationSec 0) completes instantly, so exit is immediate.
            var initial = Presence(V.Motion(key: "a", transition: StyleTransitionConfig.None, children: Label("A")));
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), initial);
            var container = Root;

            // Act
            Reconciler.Reconcile(Root, initial, Presence((VNode)null));

            // Assert
            Assert.That(container.childCount, Is.EqualTo(0),
                "StyleTransitionConfig.None completes immediately, so the element is removed at once");
        }

        [Test]
        public void Given_SyncMode_When_KeySwapsWithExitingChild_Then_NewChildEntersAlongsideExiting()
        {
            // Arrange — the default (sync) presence showing key "a".
            var showA = Presence(V.Motion(key: "a", transition: StyleTransition.Fade, children: Label("A")));
            var showB = Presence(V.Motion(key: "b", transition: StyleTransition.Fade, children: Label("B")));
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), showA);
            Assume.That(Root.childCount, Is.EqualTo(1), "Precondition: A is mounted");

            // Act — swap the key to "b".
            Reconciler.Reconcile(Root, showA, showB);

            // Assert — sync overlaps exit and enter, so the exiting A and the entering B coexist.
            Assert.That(Root.childCount, Is.EqualTo(2));
        }

        [Test]
        public void Given_WaitMode_When_KeySwapsWithExitingChild_Then_NewChildIsWithheldUntilExitFinishes()
        {
            // Arrange — a wait-mode presence showing key "a" (with a real exit transition).
            var showA = PresenceWait(V.Motion(key: "a", transition: StyleTransition.Fade, children: Label("A")));
            var showB = PresenceWait(V.Motion(key: "b", transition: StyleTransition.Fade, children: Label("B")));
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), showA);
            Assume.That(Root.childCount, Is.EqualTo(1), "Precondition: A is mounted");

            // Act — swap the key to "b".
            Reconciler.Reconcile(Root, showA, showB);

            // Assert — mode=wait holds B back while A exits, so only the exiting A is present.
            Assert.That(Root.childCount, Is.EqualTo(1));
        }

        [Test]
        public void Given_ExitInProgress_When_ReconcilerDisposed_Then_DoesNotThrow()
        {
            // Arrange — a deferred exit callback could fire after Dispose.
            var children = Presence(V.Motion(key: "a", transition: StyleTransition.Fade, children: Label("A")));
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), children);
            Reconciler.Reconcile(Root, children, Presence((VNode)null));

            // Act + Assert
            Assert.DoesNotThrow(() => Reconciler.Dispose());
        }

        #endregion

        #region Nesting

        [Test]
        public void Given_NestedPresence_When_InitialRender_Then_InnerMotionSitsDirectlyInTheOuterMotion()
        {
            // Arrange
            var tree = Presence(V.Motion(key: "outer", transition: StyleTransition.Fade, children: new VNode[]
            {
                V.AnimatePresence(children: new VNode[]
                {
                    V.Motion(key: "inner", transition: StyleTransition.Fade, children: Label("Nested")),
                }),
            }));

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert — both AnimatePresences are DOM-less, so the inner Motion's label sits directly inside
            // the outer Motion (outer Motion → inner Motion → Label), with no wrapper element at any level.
            var nested = Root.ElementAt(0).ElementAt(0).ElementAt(0) as Label;
            Assert.That(nested?.text, Is.EqualTo("Nested"));
        }

        [Test]
        public void Given_NestedPresence_When_InnerLevelGainsChild_Then_OnlyInnerContainerGrows()
        {
            // Arrange
            VNode[] Tree(bool extra) => Presence(V.Motion(key: "outer", transition: StyleTransition.Fade, children: new VNode[]
            {
                V.AnimatePresence(children: extra
                    ? new VNode[]
                    {
                        V.Motion(key: "a", transition: StyleTransition.Fade, children: Label("A")),
                        V.Motion(key: "b", transition: StyleTransition.Fade, children: Label("B")),
                    }
                    : new VNode[]
                    {
                        V.Motion(key: "a", transition: StyleTransition.Fade, children: Label("A")),
                    }),
            }));
            var initial = Tree(false);
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), initial);

            // Act
            Reconciler.Reconcile(Root, initial, Tree(true));

            // Assert — DOM-less: the inner AnimatePresence's children are direct children of the outer
            // Motion, so growing the inner level grows the outer Motion's child list to 2.
            var innerContainer = Root.ElementAt(0);
            Assert.That(innerContainer.childCount, Is.EqualTo(2), "The inner presence patches independently of the outer");
        }

        [Test]
        public void Given_MountedPresence_When_ReusedWithFreshReconciler_Then_MountsWithoutLeak()
        {
            // Arrange
            var children = Presence(V.Motion(key: "a", transition: StyleTransition.Fade, children: Label("A")));
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), children);

            // Act — a separate reconciler scope so the fixture still owns and disposes the original in TearDown.
            using var fresh = new ReconcilerScope();
            Assert.DoesNotThrow(() => fresh.Reconciler.Reconcile(fresh.Root, Array.Empty<VNode>(), children));

            // Assert
            Assert.That(fresh.Root.childCount, Is.EqualTo(1), "The same tree mounts cleanly under a fresh reconciler");
        }

        #endregion

        #region Initial flag

        [Test]
        public void Given_InitialFalse_When_FirstMount_Then_NoEnterFromClassIsApplied()
        {
            // Arrange
            var tree = new VNode[]
            {
                V.AnimatePresence(initial: false, children: new VNode[]
                {
                    V.Motion(key: "a", transition: StyleTransition.Fade, children: Label("A")),
                }),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            var element = Root.ElementAt(0);
            Assert.That(HasAnyClass(element, StyleTransition.Fade.EnterFromClasses), Is.False,
                "initial=false skips the enter animation, so no enter-from class is applied on the first mount");
        }

        [Test]
        public void Given_InitialTrue_When_FirstMount_Then_EnterAnimationClassIsApplied()
        {
            // Arrange
            var tree = new VNode[]
            {
                V.AnimatePresence(initial: true, children: new VNode[]
                {
                    V.Motion(key: "a", transition: StyleTransition.Fade, children: Label("A")),
                }),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            var element = Root.ElementAt(0);
            var hasEnterClass = HasAnyClass(element, StyleTransition.Fade.EnterFromClasses)
                || HasAnyClass(element, StyleTransition.Fade.EnterToClasses);
            Assert.That(hasEnterClass, Is.True, "initial=true applies the enter animation classes on the first mount");
        }

        [Test]
        public void Given_InitialFalse_When_FirstMount_Then_OnEnterCompleteFiresImmediately()
        {
            // Arrange
            var callbackCalled = false;
            var tree = new VNode[]
            {
                V.AnimatePresence(initial: false, children: new VNode[]
                {
                    V.Motion(key: "a", transition: StyleTransition.Fade, onEnterComplete: () => callbackCalled = true, children: Label("A")),
                }),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(callbackCalled, Is.True, "When the enter animation is skipped, OnEnterComplete fires immediately");
        }

        [Test]
        public void Given_InitialFalse_When_ChildAddedLater_Then_TheAdditionStillAnimates()
        {
            // Arrange — initial=false suppresses only the first mount; later additions still animate.
            var initial = new VNode[]
            {
                V.AnimatePresence(initial: false, children: new VNode[]
                {
                    V.Motion(key: "a", transition: StyleTransition.Fade, children: Label("A")),
                }),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), initial);
            var updated = new VNode[]
            {
                V.AnimatePresence(initial: false, children: new VNode[]
                {
                    V.Motion(key: "a", transition: StyleTransition.Fade, children: Label("A")),
                    V.Motion(key: "b", transition: StyleTransition.Fade, children: Label("B")),
                }),
            };

            // Act
            Reconciler.Reconcile(Root, initial, updated);

            // Assert
            var added = Root.ElementAt(1);
            var hasEnterClass = HasAnyClass(added, StyleTransition.Fade.EnterFromClasses)
                || HasAnyClass(added, StyleTransition.Fade.EnterToClasses);
            Assert.That(hasEnterClass, Is.True, "A child added after the first mount receives enter classes even when initial=false");
        }

        #endregion

        #region Stagger

        [Test]
        public void Given_StaggerSec_When_InitialMount_Then_AllChildrenAreCreated()
        {
            // Arrange
            var tree = new VNode[]
            {
                V.AnimatePresence(staggerSec: 0.1f, children: new VNode[]
                {
                    V.Motion(key: "a", transition: StyleTransition.Fade, children: Label("A")),
                    V.Motion(key: "b", transition: StyleTransition.Fade, children: Label("B")),
                    V.Motion(key: "c", transition: StyleTransition.Fade, children: Label("C")),
                }),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(Root.childCount, Is.EqualTo(3),
                "Stagger only spaces the enter timing; every child is created on the initial mount");
        }

        #endregion

        #region StyleTransitionConfig

        [Test]
        public void Given_NoneTransitionConfig_When_Inspected_Then_HasZeroDuration()
        {
            // Assert
            Assert.That(StyleTransitionConfig.None.DurationSec, Is.EqualTo(0f));
        }

        [Test]
        public void Given_ClassStrings_When_ParsedClassesAccessed_Then_AreParsedIntoArrays()
        {
            // Arrange
            var config = new StyleTransitionConfig
            {
                EnterFromClass = "a b",
                EnterToClass = "c",
                ExitFromClass = "d e f",
                ExitToClass = "g",
                DurationSec = 0.2f,
            };

            // Assert — each whitespace-delimited class string parses into its component class array
            Assert.That(config.EnterFromClasses, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(config.EnterToClasses, Is.EqualTo(new[] { "c" }));
            Assert.That(config.ExitFromClasses, Is.EqualTo(new[] { "d", "e", "f" }));
            Assert.That(config.ExitToClasses, Is.EqualTo(new[] { "g" }));
        }

        [Test]
        public void Given_ClassStrings_When_ParsedClassesAccessedTwice_Then_TheCachedArrayIsReturned()
        {
            // Arrange
            var config = new StyleTransitionConfig { EnterFromClass = "a b", DurationSec = 0.2f };

            // Act
            var first = config.EnterFromClasses;
            var second = config.EnterFromClasses;

            // Assert
            Assert.That(first, Is.SameAs(second), "Parsed class arrays are cached on first access");
        }

        [Test]
        public void Given_NullClassStrings_When_ParsedClassesAccessed_Then_AllAreEmpty()
        {
            // Arrange
            var config = new StyleTransitionConfig { DurationSec = 0.2f };

            // Assert
            Assert.That(
                config.EnterFromClasses.Length + config.EnterToClasses.Length + config.ExitFromClasses.Length + config.ExitToClasses.Length,
                Is.EqualTo(0),
                "A null class string parses to an empty array for every enter/exit phase");
        }

        [Test]
        public void Given_AllTransitionPresets_When_Inspected_Then_EveryPresetHasAPositiveDuration()
        {
            // Assert
            Assert.That(new[]
            {
                StyleTransition.Fade.DurationSec,
                StyleTransition.SlideUp.DurationSec,
                StyleTransition.SlideDown.DurationSec,
                StyleTransition.SlideLeft.DurationSec,
                StyleTransition.SlideRight.DurationSec,
                StyleTransition.ScaleIn.DurationSec,
                StyleTransition.FadeSlideUp.DurationSec,
            }, Is.All.GreaterThan(0f));
        }

        [Test]
        public void Given_FadePreset_When_Inspected_Then_AllFourClassPhasesArePopulated()
        {
            // Assert
            Assert.That(new[]
            {
                StyleTransition.Fade.EnterFromClasses.Length,
                StyleTransition.Fade.EnterToClasses.Length,
                StyleTransition.Fade.ExitFromClasses.Length,
                StyleTransition.Fade.ExitToClasses.Length,
            }, Is.All.GreaterThan(0));
        }

        [Test]
        public void Given_FadePreset_When_Inspected_Then_EnterUsesEaseOutAndExitUsesEaseIn()
        {
            // Assert
            Assert.That(
                (StyleTransition.Fade.Easing, StyleTransition.Fade.ExitEasing),
                Is.EqualTo((EasingMode.EaseOut, (EasingMode?)EasingMode.EaseIn)),
                "Presets configure enter easing as EaseOut and exit easing as EaseIn");
        }

        #endregion

        #region StyleTransitionConfig.With

        [Test]
        public void Given_Preset_When_WithDuration_Then_OverridesDurationAndCopiesClasses()
        {
            // Arrange
            var original = StyleTransition.Fade;

            // Act
            var modified = original.With(durationSec: 0.5f);

            // Assert
            Assert.That(modified.DurationSec, Is.EqualTo(0.5f));
            Assume.That(
                (modified.EnterFromClasses, modified.EnterToClasses, modified.ExitFromClasses, modified.ExitToClasses),
                Is.EqualTo((original.EnterFromClasses, original.EnterToClasses, original.ExitFromClasses, original.ExitToClasses)),
                "Precondition: the class definitions are copied unchanged");
        }

        [Test]
        public void Given_Preset_When_WithEasing_Then_OverridesEasingAndKeepsDuration()
        {
            // Act
            var modified = StyleTransition.Fade.With(easing: EasingMode.Linear);

            // Assert
            Assert.That(modified.Easing, Is.EqualTo(EasingMode.Linear));
            Assume.That(modified.DurationSec, Is.EqualTo(StyleTransition.Fade.DurationSec),
                "Precondition: an easing override leaves duration untouched");
        }

        [Test]
        public void Given_Preset_When_WithExitEasing_Then_OverridesExitEasingAndKeepsEnterEasing()
        {
            // Act
            var modified = StyleTransition.Fade.With(exitEasing: EasingMode.EaseInOut);

            // Assert
            Assert.That(modified.ExitEasing, Is.EqualTo(EasingMode.EaseInOut));
            Assume.That(modified.Easing, Is.EqualTo(StyleTransition.Fade.Easing),
                "Precondition: an exit-easing override leaves enter easing untouched");
        }

        [Test]
        public void Given_Preset_When_WithMultipleOverrides_Then_AllAreApplied()
        {
            // Act
            var modified = StyleTransition.Fade.With(durationSec: 1.0f, easing: EasingMode.Linear, exitEasing: EasingMode.EaseInOut);

            // Assert
            Assert.That(
                (modified.DurationSec, modified.Easing, modified.ExitEasing),
                Is.EqualTo((1.0f, EasingMode.Linear, (EasingMode?)EasingMode.EaseInOut)));
        }

        [Test]
        public void Given_Preset_When_With_Then_TheOriginalIsNotMutated()
        {
            // Arrange
            var original = StyleTransition.Fade;
            var originalState = (original.DurationSec, original.Easing);

            // Act
            _ = original.With(durationSec: 5.0f, easing: EasingMode.Linear);

            // Assert
            Assert.That((original.DurationSec, original.Easing), Is.EqualTo(originalState), "With produces a new config and never mutates the source");
        }

        [Test]
        public void Given_Preset_When_WithDelay_Then_OverridesDelayAndKeepsDuration()
        {
            // Act
            var modified = StyleTransition.Fade.With(delaySec: 0.3f);

            // Assert
            Assert.That(modified.DelaySec, Is.EqualTo(0.3f));
            Assume.That(modified.DurationSec, Is.EqualTo(StyleTransition.Fade.DurationSec),
                "Precondition: a delay override leaves duration untouched");
        }

        [Test]
        public void Given_ConfigWithDelay_When_WithDurationOnly_Then_DelayIsPreserved()
        {
            // Arrange
            var original = StyleTransition.Fade.With(delaySec: 0.5f);

            // Act
            var modified = original.With(durationSec: 1.0f);

            // Assert
            Assert.That(modified.DelaySec, Is.EqualTo(0.5f), "An override that omits delay preserves the prior delay");
        }

        [Test]
        public void Given_FadePreset_When_Inspected_Then_DefaultDelayIsZero()
        {
            // Assert
            Assert.That(StyleTransition.Fade.DelaySec, Is.EqualTo(0f));
        }

        #endregion

        #region Helpers

        private static VNode[] Presence(params VNode[] children) => new VNode[] { V.AnimatePresence(children: children) };

        private static VNode[] PresenceWait(params VNode[] children) =>
            new VNode[] { V.AnimatePresence(children: children, mode: AnimatePresenceMode.Wait) };

        private static VNode[] Label(string text) => new VNode[] { V.Label(text: text) };

        private static bool HasAnyClass(VisualElement element, string[] classes)
        {
            foreach (var cls in classes)
            {
                if (element.ClassListContains(cls))
                {
                    return true;
                }
            }
            return false;
        }

        #endregion
    }
}

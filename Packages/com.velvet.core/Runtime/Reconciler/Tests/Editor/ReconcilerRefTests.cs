using System;
using NUnit.Framework;
using Velvet.TestUtilities;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the callback-ref and creation-callback contract of element reconciliation.
    /// <list type="bullet">
    /// <item>A callback ref runs on element creation, receiving the live element. On a patch it is
    /// identity-gated (React's contract): the same callback delegate leaves the installed ref
    /// untouched, while a changed identity cycles it — the old cleanup fires, then the new callback
    /// runs as setup against the same reused instance. (The per-render lambdas these tests pass are
    /// fresh identities, so their patches cycle.)</item>
    /// <item>A callback ref may return a cleanup action; the cleanup fires when the element is removed.
    /// A null cleanup return is a no-op on removal.</item>
    /// <item>A typed <c>Ref&lt;T&gt;</c> exposes the element through <c>Current</c>; its
    /// <c>SetElement</c> delegate is identity-stable for the Ref's lifetime (so patches leave it
    /// installed), and its cleanup resets <c>Current</c> to null on removal.</item>
    /// <item>Keyed reconciliation that reuses an element keeps the same instance bound to its ref
    /// across a reorder.</item>
    /// <item>The creation callback (<c>OnCreated</c>) runs only when the element is created — never on
    /// a patch — and runs again when a type change forces recreation.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ReconcilerRefTests : ReconcilerTestFixture
    {
        [Test]
        public void Given_RefCallback_When_ElementCreated_Then_ReceivesTheCreatedElement()
        {
            // Arrange
            VisualElement captured = null;
            var tree = new VNode[]
            {
                V.Button(text: "click me", refCallback: el => { captured = el; return null; }),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(captured, Is.SameAs(Root.ElementAt(0)),
                "The callback receives the freshly created element");
        }

        [Test]
        public void Given_RefCallback_When_ElementPatched_Then_ReceivesTheSameReusedElement()
        {
            // Arrange
            VisualElement capturedOnCreate = null;
            VisualElement capturedOnPatch = null;
            var tree1 = new VNode[]
            {
                V.Label(text: "old", refCallback: el => { capturedOnCreate = el; return null; }),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);

            // Act
            var tree2 = new VNode[]
            {
                V.Label(text: "new", refCallback: el => { capturedOnPatch = el; return null; }),
            };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(capturedOnPatch, Is.SameAs(capturedOnCreate),
                "A patch reuses the element and re-invokes the callback with that same instance");
        }

        [Test]
        public void Given_NoRefCallback_When_ElementCreated_Then_ReconcileDoesNotThrow()
        {
            // Arrange
            var tree = new VNode[] { V.Label(text: "no ref") };

            // Act + Assert
            Assert.DoesNotThrow(() => Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree));
        }

        [Test]
        public void Given_TypedRef_When_ElementCreated_Then_CurrentHoldsTheTypedElement()
        {
            // Arrange
            var buttonRef = new Ref<Button>();
            var tree = new VNode[]
            {
                V.Button(text: "typed", refCallback: buttonRef.SetElement),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(buttonRef.Current, Is.SameAs(Root.ElementAt(0)),
                "Ref<T>.Current exposes the created element");
        }

        [Test]
        public void Given_KeyedRefs_When_SiblingsReordered_Then_EachRefKeepsItsReusedElement()
        {
            // Arrange
            var refA = new Ref<Label>();
            var refB = new Ref<Label>();
            var tree1 = new VNode[]
            {
                V.Label(text: "A", key: "a", refCallback: refA.SetElement),
                V.Label(text: "B", key: "b", refCallback: refB.SetElement),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            var elementA = refA.Current;
            var elementB = refB.Current;
            Assume.That(elementA, Is.Not.Null, "Precondition: ref A captured an element on mount");
            Assume.That(elementB, Is.Not.Null, "Precondition: ref B captured an element on mount");

            // Act
            var tree2 = new VNode[]
            {
                V.Label(text: "B-updated", key: "b", refCallback: refB.SetElement),
                V.Label(text: "A-updated", key: "a", refCallback: refA.SetElement),
            };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That((refA.Current, refB.Current), Is.EqualTo((elementA, elementB)),
                "Keyed reorder reuses each element, so each ref keeps its original instance");
        }

        [Test]
        public void Given_RefCallbackWithCleanup_When_ElementRemoved_Then_CleanupFires()
        {
            // Arrange
            var setupCount = 0;
            var cleanupCount = 0;
            var tree1 = new VNode[]
            {
                V.Label(text: "with-cleanup", refCallback: _ =>
                {
                    setupCount++;
                    return () => cleanupCount++;
                }),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            Assume.That(setupCount, Is.EqualTo(1), "Precondition: setup ran once on mount");
            Assume.That(cleanupCount, Is.EqualTo(0), "Precondition: cleanup has not fired yet");

            // Act
            Reconciler.Reconcile(Root, tree1, Array.Empty<VNode>());

            // Assert
            Assert.That(cleanupCount, Is.EqualTo(1), "Cleanup fires when the element is removed");
        }

        [Test]
        public void Given_RefCallbackSwappedOnPatch_When_Patched_Then_OldCleanupFires()
        {
            // Arrange
            var oldCleanupCount = 0;
            var newSetupCount = 0;
            var tree1 = new VNode[]
            {
                V.Label(text: "patch-target", refCallback: _ => () => oldCleanupCount++),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);

            // Act
            var tree2 = new VNode[]
            {
                V.Label(text: "patch-target", refCallback: _ => { newSetupCount++; return null; }),
            };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(oldCleanupCount, Is.EqualTo(1),
                "Swapping the callback on the same element fires the old cleanup first");
        }

        [Test]
        public void Given_RefCallbackSwappedOnPatch_When_Patched_Then_NewCallbackRunsAsSetup()
        {
            // Arrange
            var newSetupCount = 0;
            var tree1 = new VNode[]
            {
                V.Label(text: "patch-target", refCallback: _ => () => { }),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);

            // Act
            var tree2 = new VNode[]
            {
                V.Label(text: "patch-target", refCallback: _ => { newSetupCount++; return null; }),
            };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(newSetupCount, Is.EqualTo(1), "After the old cleanup, the new callback runs as setup");
        }

        [Test]
        public void Given_TypedRef_When_PatchedRepeatedly_Then_CurrentStaysTheLiveInstance()
        {
            // Arrange
            var labelRef = new Ref<Label>();
            var tree1 = new VNode[] { V.Label(text: "v1", refCallback: labelRef.SetElement) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            var firstElement = labelRef.Current;
            Assume.That(firstElement, Is.Not.Null, "Precondition: the ref captured an element on mount");

            // Act
            var tree2 = new VNode[] { V.Label(text: "v2", refCallback: labelRef.SetElement) };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(labelRef.Current, Is.SameAs(firstElement),
                "Across a patch the cleanup-then-resetup never leaves Current transiently null");
        }

        [Test]
        public void Given_RefCallbackReturningNullCleanup_When_ElementRemoved_Then_RemovalIsANoOp()
        {
            // Arrange
            var tree = new VNode[] { V.Label(text: "no-cleanup", refCallback: _ => null) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Act + Assert
            Assert.DoesNotThrow(() => Reconciler.Reconcile(Root, tree, Array.Empty<VNode>()),
                "A null cleanup return value is allowed and is a no-op on removal");
        }

        [Test]
        public void Given_TypedRef_When_ElementRemoved_Then_CurrentResetsToNull()
        {
            // Arrange
            var labelRef = new Ref<Label>();
            var tree = new VNode[] { V.Label(text: "auto-clear", refCallback: labelRef.SetElement) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);
            Assume.That(labelRef.Current, Is.Not.Null, "Precondition: Current holds the element after mount");

            // Act
            Reconciler.Reconcile(Root, tree, Array.Empty<VNode>());

            // Assert
            Assert.That(labelRef.Current, Is.Null,
                "Ref<T>.SetElement's cleanup resets Current to null on removal");
        }

        [Test]
        public void Given_OnCreated_When_ElementPatched_Then_NotInvokedAgain()
        {
            // Arrange
            var createCount = 0;
            var tree1 = new VNode[] { MakeNode(typeof(VisualElement), _ => createCount++) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            Assume.That(createCount, Is.EqualTo(1), "Precondition: OnCreated ran once on creation");

            // Act
            var tree2 = new VNode[] { MakeNode(typeof(VisualElement), _ => createCount++) };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(createCount, Is.EqualTo(1), "OnCreated does not fire on a patch");
        }

        [Test]
        public void Given_OnCreated_When_TypeChangeForcesRecreation_Then_InvokedAgain()
        {
            // Arrange
            var createCount = 0;
            var tree1 = new VNode[] { MakeNode(typeof(Button), _ => createCount++) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree1);
            Assume.That(createCount, Is.EqualTo(1), "Precondition: OnCreated ran once on creation");

            // Act
            var tree2 = new VNode[] { MakeNode(typeof(Label), _ => createCount++) };
            Reconciler.Reconcile(Root, tree1, tree2);

            // Assert
            Assert.That(createCount, Is.EqualTo(2), "A type change recreates the element and re-fires OnCreated");
        }

        private static ElementNode MakeNode(Type elementType, Action<VisualElement> onCreated)
            => new()
            {
                ElementType = elementType,
                ClassNames = Array.Empty<string>(),
                Children = Array.Empty<VNode>(),
                Events = Array.Empty<FiberEventBinding>(),
                OnCreated = onCreated,
            };
    }
}

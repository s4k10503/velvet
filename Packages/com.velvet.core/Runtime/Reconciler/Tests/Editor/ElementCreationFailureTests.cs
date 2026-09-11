using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what an element whose creation fails partway leaves behind: the elements built for it before
    /// the failure are released rather than left to the ref drain, so a ref one of them carries is never set
    /// up on an element no tree holds.
    /// <list type="bullet">
    /// <item>A child an element built before a later child's constructor refused gets no ref.</item>
    /// <item>A leaf the general walk built before a later leaf's constructor refused gets no ref, though the
    /// walk had not placed it anywhere yet.</item>
    /// <item>A walk left by a suspend its own Suspense spans did not catch releases the leaves it built the
    /// same way, and the element whose creation that suspend ended is left to the boundary, which reveals
    /// its children once the resource resolves.</item>
    /// </list>
    /// The virtualized list reaches the same creation paths for a row; <c>VirtualListTests</c> holds what a
    /// half-built row leaves.
    /// </summary>
    [TestFixture]
    internal sealed class ElementCreationFailureTests : ReconcilerTestFixture
    {
        private sealed class ConstructionRefusingElement : VisualElement
        {
            public ConstructionRefusingElement()
            {
                throw new InvalidOperationException("constructor refused");
            }
        }

        private int _refSetUps;
        private int _refCleanUps;

        public override void SetUp()
        {
            base.SetUp();
            _refSetUps = 0;
            _refCleanUps = 0;
        }

        private Action CountRef(VisualElement _)
        {
            _refSetUps++;
            return () => _refCleanUps++;
        }

        // Reconciles tree onto an empty root, which throws, and returns what reached the caller beside what
        // the ref counted: the pass's own boundary has run its ref drain by the time the call returns.
        private string OutcomeOfFailedMount(VNode[] tree)
        {
            var thrown = "nothing";
            try
            {
                Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);
            }
            catch (Exception exception)
            {
                thrown = exception.GetBaseException().Message;
            }

            return "[" + thrown + "] thrown, ref " + _refSetUps + " set up, " + _refCleanUps + " cleaned up";
        }

        [Test]
        public void Given_AnElementWhoseSecondChildRefusesConstruction_When_ItIsCreated_Then_ItsFirstChildGetsNoRef()
        {
            // Arrange — the children take the indexed diff, which places the first child inside the element
            // before the second is built.
            var tree = new VNode[]
            {
                V.Div(children: new VNode[]
                {
                    V.Label(refCallback: CountRef),
                    V.Custom<ConstructionRefusingElement>(),
                }),
            };

            // Act
            var outcome = OutcomeOfFailedMount(tree);

            // Assert — what reached the caller rides along: an element that builds completely is placed, and
            // the ref of a child it holds is set up rightly.
            Assert.That(outcome, Is.EqualTo("[constructor refused] thrown, ref 0 set up, 0 cleaned up"));
        }

        [Test]
        public void Given_ALeafTheGeneralWalkBuiltBeforeASiblingRefusedConstruction_When_TheWalkFails_Then_ThatLeafGetsNoRef()
        {
            // Arrange — a null among the leaves sends them through the general walk
            // (GeneralPathReconciler.NeedsExpansion), which places none of them until every one is built.
            var tree = new VNode[]
            {
                V.Label(refCallback: CountRef),
                null,
                V.Custom<ConstructionRefusingElement>(),
            };

            // Act
            var outcome = OutcomeOfFailedMount(tree);

            // Assert — both terms for the reason the case above gives.
            Assert.That(outcome, Is.EqualTo("[constructor refused] thrown, ref 0 set up, 0 cleaned up"));
        }

        private static VelvetTaskCompletionSource<string> s_resource;
        private static Func<VisualElement, Action> s_leafRef;

        [Component]
        private static VNode ResourceReaderRender()
        {
            var text = Hooks.Use(_ => s_resource.Task);
            return V.Label(text: text);
        }

        // The component among the element's children is what sends them through the general walk, and its
        // suspend is what ends the element's creation.
        [Component]
        private static VNode SuspendingCreationHostRender()
            => V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[]
                {
                    V.Div(children: new VNode[]
                    {
                        V.Label(text: "leaf", refCallback: s_leafRef),
                        V.Component(ResourceReaderRender, key: "reader"),
                    }),
                });

        [Test]
        public void Given_AnElementWhoseChildSuspendsDuringItsCreation_When_TheBoundaryFallsBack_Then_AChildBuiltBeforeItGetsNoRef()
        {
            // Arrange
            s_resource = new VelvetTaskCompletionSource<string>();
            s_leafRef = CountRef;
            var root = new VisualElement();

            // Act
            using var mounted = V.Mount(root, V.Component(SuspendingCreationHostRender, key: "host"));

            // Assert — the fallback rides along: a creation that never suspended sets the leaf's ref up rightly.
            Assert.That(
                (root.FindLabelByText("loading") != null ? "fell back" : "never fell back")
                    + ", ref " + _refSetUps + " set up",
                Is.EqualTo("fell back, ref 0 set up"));
        }

        // GREEN_ON_BASE(characterization): the base leaves what a suspended creation built alone.
        // Released the way a failed creation's is, it left the Suspense boundary on its fallback after the
        // resource resolved, and this is what fails when that release takes suspends too.
        [Test]
        public void Given_AnElementWhoseChildSuspendsDuringItsCreation_When_TheResourceResolves_Then_TheBoundaryRevealsItsChildren()
        {
            // Arrange
            s_resource = new VelvetTaskCompletionSource<string>();
            s_leafRef = CountRef;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(SuspendingCreationHostRender, key: "host"));
            var fellBack = root.FindLabelByText("loading") != null;

            // Act
            s_resource.TrySetResult("ready");
            mounted.FlushStateForTest();

            // Assert — the fallback term separates a creation that suspended from one that never did.
            Assert.That(
                (fellBack ? "fell back" : "never fell back") + ", then "
                    + (root.FindLabelByText("ready") != null ? "revealed" : "not revealed"),
                Is.EqualTo("fell back, then revealed"));
        }
    }
}

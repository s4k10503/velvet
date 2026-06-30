using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Two DIFFERENT function components at the same tree position must remount instead of
    /// patching. Velvet matches ComponentNodes by C# type in <see cref="ReconcileKeying.CanPatch"/>, but
    /// every <c>[Component]</c> function compiles to the same CLR type (<c>ComponentNode</c>), so the
    /// patch-compatibility decision must additionally compare component IDENTITY
    /// (<c>ComponentNode.ResolvedIdentity</c>, i.e. the <c>Body.Method</c>). Without that, A's element could
    /// be patched as B at the same slot rather than A being unmounted and B mounted fresh.
    /// </summary>
    /// <remarks>
    /// The production expansion path matches components by identity in <c>ComponentRegistry.GetOrCreateInline</c>,
    /// so today this <c>CanPatch</c> branch is latent / defense-in-depth. The first two tests guard the
    /// predicate directly; the behavioral test asserts the expected outcome (fresh state on swap).
    /// </remarks>
    [TestFixture]
    internal sealed class ComponentIdentityPatchParityTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetBeta();
        }

        [Test]
        public void Given_TwoDifferentComponents_When_CanPatch_Then_NotPatchCompatible()
        {
            // Arrange — two different [Component] functions both produce a ComponentNode of the same CLR type.
            var alpha = V.Component(AlphaRender, key: "slot");
            var beta = V.Component(BetaRender, key: "slot");
            Assume.That(alpha.GetType(), Is.EqualTo(beta.GetType()),
                "Precondition: distinct components share the same CLR type, so type alone cannot tell them apart");
            Assume.That(alpha.ResolvedIdentity, Is.Not.EqualTo(beta.ResolvedIdentity),
                "Precondition: distinct components have distinct identities");

            // Act / Assert — different identity must not patch (it remounts).
            Assert.That(ReconcileKeying.CanPatch(alpha, beta), Is.False);
        }

        [Test]
        public void Given_SameComponent_When_CanPatch_Then_PatchCompatible()
        {
            // Arrange — same [Component] function across two renders.
            var first = V.Component(AlphaRender, key: "slot");
            var second = V.Component(AlphaRender, key: "slot");

            // Act / Assert — same identity still patches (no regression for the common path).
            Assert.That(ReconcileKeying.CanPatch(first, second), Is.True);
        }

        [Test]
        public void Given_MountedComponent_When_DifferentComponentReconciledAtSameKey_Then_BetaMountsFresh()
        {
            // The production expansion path matches components by identity in ComponentRegistry, so a real
            // reconcile resolves a component swap there rather than through CanPatch (which is why the CanPatch
            // branch is latent). This test pins the OUTCOME that path already delivers: a different
            // component at the same keyed position renders fresh with its own state, never Alpha's render body.
            var reconciler = new Reconciler();
            var alphaTree = new VNode[] { V.Component(AlphaRender, key: "slot") };
            reconciler.Reconcile(_root, Array.Empty<VNode>(), alphaTree);
            Assume.That(_root.childCount, Is.EqualTo(1), "Precondition: Alpha mounted one element");
            Assume.That(((Label)_root.ElementAt(0)).text, Is.EqualTo("alpha-initial"),
                "Precondition: Alpha rendered its own seed state");

            // Act — swap to a DIFFERENT component at the SAME keyed position.
            var betaTree = new VNode[] { V.Component(BetaRender, key: "slot") };
            reconciler.Reconcile(_root, alphaTree, betaTree);

            // Assert — Beta runs its own render with its own fresh state; Alpha's body is not reused.
            Assert.That(s_betaRenderCount, Is.GreaterThanOrEqualTo(1), "Beta must have rendered");
            Assert.That(((Label)_root.ElementAt(0)).text, Is.EqualTo("beta-initial"),
                "The mounted element must reflect Beta's own fresh state, not Alpha's");

            reconciler.Dispose();
        }

        #region Alpha component

        [Component]
        private static VNode AlphaRender()
        {
            var (text, _) = Hooks.UseState("alpha-initial");
            return V.Label(text: text);
        }

        #endregion

        #region Beta component

        private static int s_betaRenderCount;

        private static void ResetBeta() => s_betaRenderCount = 0;

        [Component]
        private static VNode BetaRender()
        {
            s_betaRenderCount++;
            var (text, _) = Hooks.UseState("beta-initial");
            return V.Label(text: text);
        }

        #endregion
    }
}

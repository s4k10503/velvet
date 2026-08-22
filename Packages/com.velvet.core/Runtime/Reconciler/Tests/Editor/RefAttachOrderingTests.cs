using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies when a callback ref attaches relative to the detach of the ref a replaced element
    /// carried. The general reconcile path creates the arriving element before it removes the
    /// departing one, so a ref that attaches at creation time would run its setup ahead of the
    /// departing element's cleanup — and the register-in-setup / unregister-in-cleanup idiom would
    /// then have the departing cleanup drop the arriving registration.
    /// <list type="bullet">
    /// <item>The departing element's cleanup runs before the arriving element's setup.</item>
    /// <item>A portal id registered by that idiom resolves to the arriving element afterwards.</item>
    /// </list>
    /// Both are driven by a leaf whose explicit key changes, which is what sends the pair down the
    /// create-then-remove branch of the general path rather than the in-place patch.
    /// </summary>
    [TestFixture]
    internal sealed class RefAttachOrderingTests : ReconcilerTestFixture
    {
        private const string ModalRootId = "ref-attach-ordering-modal-root";

        private static readonly List<string> s_log = new();

        [Component]
        private static VNode Screen(string which)
            => V.Div(key: which, name: which, refCallback: Tracked(which));

        [Component]
        private static VNode PortalHostScreen(string which)
            => V.Div(key: which, name: which, refCallback: RegisterModalRoot);

        private static Func<VisualElement, Action> Tracked(string which)
            => _ =>
            {
                s_log.Add("attach:" + which);
                return () => s_log.Add("detach:" + which);
            };

        private static Action RegisterModalRoot(VisualElement element)
        {
            FiberPortalRegistry.Register(ModalRootId, element);
            return () => FiberPortalRegistry.Unregister(ModalRootId);
        }

        public override void SetUp()
        {
            base.SetUp();
            s_log.Clear();
        }

        public override void TearDown()
        {
            base.TearDown();
            FiberPortalRegistry.Unregister(ModalRootId);
        }

        [Test]
        public void Given_AKeyedLeafReplacedInOnePass_When_TheReplacementCarriesARef_Then_TheDepartingCleanupRunsFirst()
        {
            // Arrange
            var departing = new VNode[] { V.Component(Screen, "departing", key: "screen") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), departing);
            s_log.Clear();

            // Act
            Reconciler.Reconcile(Root, departing,
                new VNode[] { V.Component(Screen, "arriving", key: "screen") });

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("detach:departing,attach:arriving"));
        }

        [Test]
        public void Given_TheRegisterInRefIdiom_When_AKeyedLeafIsReplaced_Then_TheIdResolvesToTheArrivingElement()
        {
            // Arrange
            var departing = new VNode[] { V.Component(PortalHostScreen, "departing", key: "screen") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), departing);

            // Act
            Reconciler.Reconcile(Root, departing,
                new VNode[] { V.Component(PortalHostScreen, "arriving", key: "screen") });

            // Assert
            Assert.That(FiberPortalRegistry.Get(ModalRootId), Is.SameAs(Root!.ElementAt(0)));
        }
    }
}

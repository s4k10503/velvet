using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what a layoutId Motion leaves in its context's bookkeeping: a Motion torn down before its first layout
    /// leaves no wait behind, nor does one whose box is reseated, whose old box then settles nothing, and an id whose box moves to another parent is no longer indexed under the first, so
    /// returning that parent to the pool leaves the id's box where it now is.
    /// </summary>
    [TestFixture]
    internal sealed class LayoutIdBookkeepingTests
    {
        private static readonly MethodInfo s_setEntry = typeof(MotionLayoutIdDriver).GetMethod("SetEntry",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        private MountedTree _mounted;

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
        }

        private static VNode Card() => V.Motion(name: "card", className: "w-[40px] h-[40px]", layoutId: "card", enterOnMount: false);

        private static LayoutIdBox BoxUnder(VisualElement parent)
            => new(parent, new Rect(0f, 0f, 40f, 40f), Vector2.zero, new Vector2(40f, 40f), Vector2.one, null);

        // GREEN_ON_BASE(characterization): the base cancels a torn-down layoutId Motion's wait.
        [Test]
        public void Given_ALayoutIdMotionNotLaidOutYet_When_ItIsTornDown_Then_NoWaitIsLeftForIt()
        {
            // Arrange
            var root = new VisualElement();
            _mounted = V.Mount(root, Card());
            var card = root.Q<VisualElement>("card");
            var ctx = _mounted.Root.Reconciler.Context;
            var waited = ctx.LayoutIdPendingSettles.ContainsKey(card);

            // Act
            _mounted.Root.Reconciler.Reconcile(root, new VNode[] { Card() }, new VNode[] { V.Div() });

            // Assert — null where no wait was ever made, which false never equals.
            Assert.That(waited ? ctx.LayoutIdPendingSettles.ContainsKey(card) : (bool?)null, Is.EqualTo(false));
        }

        // GREEN_ON_BASE(characterization): the base cancels a layoutId Motion's wait when its box is reseated.
        [Test]
        public void Given_ALayoutIdMotionWaitingForItsLayout_When_ItsBoxIsReseated_Then_NoWaitIsLeftForIt()
        {
            // Arrange
            var root = new VisualElement();
            _mounted = V.Mount(root, Card());
            var card = root.Q<VisualElement>("card");
            var ctx = _mounted.Root.Reconciler.Context;
            var waited = ctx.LayoutIdPendingSettles.ContainsKey(card);

            // Act
            MotionLayoutIdDriver.BeforeReseat(card, ctx);

            // Assert — null where no wait was ever made, which false never equals.
            Assert.That(waited ? ctx.LayoutIdPendingSettles.ContainsKey(card) : (bool?)null, Is.EqualTo(false));
        }

        // GREEN_ON_BASE(characterization): the base unregisters a cancelled wait from the box it was registered on.
        [Test]
        public void Given_AReseatedLayoutIdMotionWaitingOnItsNewBox_When_ItsOldBoxChangesGeometry_Then_TheNewWaitStands()
        {
            // Arrange
            var root = new VisualElement();
            _mounted = V.Mount(root, Card());
            var card = root.Q<VisualElement>("card");
            var ctx = _mounted.Root.Reconciler.Context;
            var waitedOnItself = ctx.LayoutIdPendingSettles.TryGetValue(card, out var first) && ReferenceEquals(first.Target, card);
            MotionLayoutIdDriver.BeforeReseat(card, ctx);
            var newBox = new VisualElement();
            var next = new LayoutIdPendingSettle(null, new Rect(float.NaN, float.NaN, float.NaN, float.NaN), ctx.LayoutIdTimings[card])
            {
                Target = newBox,
            };
            next.Callback = _ => { };
            newBox.RegisterCallback(next.Callback);
            ctx.LayoutIdPendingSettles[card] = next;

            // Act
            using (var changed = GeometryChangedEvent.GetPooled(Rect.zero, new Rect(0f, 0f, 40f, 40f)))
            {
                card.SimulateEvent(changed);
            }

            // Assert — null where the first wait was not registered on the card itself, which true never equals.
            Assert.That(waitedOnItself ? ctx.LayoutIdPendingSettles.TryGetValue(card, out var standing) && ReferenceEquals(standing, next) : (bool?)null,
                Is.True);
        }

        // GREEN_ON_BASE(characterization): the base unindexes an id from the parent its box left.
        [Test]
        public void Given_AnIdWhoseBoxMovedToAnotherParent_When_TheFirstParentIsForgotten_Then_TheBoxStaysUnderTheSecond()
        {
            // Arrange
            _mounted = V.Mount(new VisualElement(), V.Div());
            var ctx = _mounted.Root.Reconciler.Context;
            var (first, second) = (new VisualElement(), new VisualElement());
            s_setEntry.Invoke(null, new object[] { ctx, "card", null, (LayoutIdBox?)BoxUnder(first) });
            s_setEntry.Invoke(null, new object[] { ctx, "card", null, (LayoutIdBox?)BoxUnder(second) });

            // Act
            MotionLayoutIdDriver.ForgetParent(first, ctx);

            // Assert
            Assert.That(ctx.LayoutIdRegistry["card"].Box?.Parent, Is.SameAs(second));
        }
    }
}

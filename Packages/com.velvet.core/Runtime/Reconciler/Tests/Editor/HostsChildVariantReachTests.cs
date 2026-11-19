using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how far above a reconcile target <see cref="ReconcilerContext.HostsChildVariant"/> looks for the
    /// element whose child container the target is: an owner four ancestors up counts, one five up does not.
    /// </summary>
    [TestFixture]
    internal sealed class HostsChildVariantReachTests
    {
        private sealed class DeepContentOwner : VisualElement
        {
            internal VisualElement Content;

            public override VisualElement contentContainer => Content ?? this;
        }

        // Builds owner > (ownerDistance - 1 plain elements) > target, with owner's content container being target,
        // and carrying a child-variant manipulator.
        private static bool OwnerAtDistanceHostsTarget(int ownerDistance)
        {
            var ctx = new ReconcilerContext();
            var target = new VisualElement();
            var top = target;
            for (var i = 1; i < ownerDistance; i++)
            {
                var next = new VisualElement();
                next.hierarchy.Add(top);
                top = next;
            }
            var owner = new DeepContentOwner { Content = target };
            owner.hierarchy.Add(top);
            ctx.ChildVariantManipulators[owner] = new StyleChildVariantManipulator(ctx, new string[0]);
            return ctx.HostsChildVariant(target);
        }

        [Test]
        public void Given_AnOwnerFourAncestorsAboveTheTarget_When_AskedWhetherTheTargetHostsAChildVariant_Then_ItDoes()
        {
            // Act
            var hosts = OwnerAtDistanceHostsTarget(4);

            // Assert
            Assert.That(hosts, Is.True);
        }

        [Test]
        public void Given_AnOwnerFiveAncestorsAboveTheTarget_When_AskedWhetherTheTargetHostsAChildVariant_Then_ItDoesNot()
        {
            // Act
            var hosts = OwnerAtDistanceHostsTarget(5);

            // Assert
            Assert.That(hosts, Is.False);
        }
    }
}

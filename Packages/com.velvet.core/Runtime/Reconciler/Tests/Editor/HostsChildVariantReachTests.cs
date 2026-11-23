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

        // GREEN_ON_BASE(characterization): the base looks four ancestors above a reconcile target for the element whose child container it is.
        // Change `level < 4` to `level < 3` in HostsChildVariant and the owner four up is not reached.
        [Test]
        public void Given_AnOwnerFourAncestorsAboveTheTarget_When_AskedWhetherTheTargetHostsAChildVariant_Then_ItDoes()
        {
            // Act
            var hosts = OwnerAtDistanceHostsTarget(4);

            // Assert
            Assert.That(hosts, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base does not look five ancestors above a reconcile target for the element whose child container it is.
        // Change `level < 4` to `level < 5` in HostsChildVariant and the owner five up is reached.
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

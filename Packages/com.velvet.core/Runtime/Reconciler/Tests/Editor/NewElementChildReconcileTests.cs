using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which created elements have their children reconciled into them: an element that declares
    /// children does, and one that declares none (a Label, a Button with a text, an empty Div) does not.
    /// </summary>
    [TestFixture]
    internal sealed class NewElementChildReconcileTests
    {
        // Forwards to the host the factory was built with, counting the child reconciles it is asked for.
        private sealed class CountingHost : IReconcilerHost
        {
            private readonly IReconcilerHost _inner;

            public CountingHost(IReconcilerHost inner) => _inner = inner;

            public int ChildReconciles { get; private set; }

            public void ReleasePortalRangeForRetarget(VisualElement placeholder)
                => _inner.ReleasePortalRangeForRetarget(placeholder);

            public void RemoveRows(VisualElement target, int slotStart, int count)
                => _inner.RemoveRows(target, slotStart, count);

            public void NotifyContextValueChange(ContextProviderNode newProvider)
                => _inner.NotifyContextValueChange(newProvider);

            public void ReconcileChildren(VisualElement parent, VNode?[] oldChildren, VNode?[] newChildren, int slotStart = 0)
            {
                ChildReconciles++;
                _inner.ReconcileChildren(parent, oldChildren, newChildren, slotStart);
            }
        }

        [Test]
        public void Given_ElementsWithAndWithoutChildren_When_TheyAreCreated_Then_OnlyTheOneWithChildrenHasThemReconciled()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var factory = typeof(Reconciler)
                .GetField("_factory", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(scope.Reconciler)!;
            var hostField = typeof(FiberNodeFactory).GetField("_host", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var host = new CountingHost((IReconcilerHost)hostField.GetValue(factory)!);
            hostField.SetValue(factory, host);
            var rows = new VNode?[]
            {
                V.Label(text: "a"),
                V.Button(text: "b"),
                V.Div(name: "empty"),
                V.Div(name: "full", children: new VNode?[] { V.Label(name: "inner") }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), rows);

            // Assert
            Assert.That((host.ChildReconciles, scope.Root.Q<Label>("inner") != null), Is.EqualTo((1, true)));
        }
    }
}

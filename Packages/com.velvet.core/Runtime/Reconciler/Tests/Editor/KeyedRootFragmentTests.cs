// annotations only: incremental nullable hygiene. See the leading comment in Velvet core Hooks.cs for details.
#nullable enable annotations
using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a keyed <c>V.Fragment</c> a component returns as its whole output keeps its key, as in
    /// React, which unwraps only an unkeyed top-level Fragment: changing that key remounts what the Fragment
    /// holds, whether the change arrives through the component's own re-render or its parent's.
    /// </summary>
    [TestFixture]
    internal sealed class KeyedRootFragmentTests
    {
        private static StateUpdater<string> s_setOwnKey;
        private static StateUpdater<string> s_setParentKey;
        private static int s_childSetups;
        private MountedTree? _mounted;

        [SetUp]
        public void SetUp()
        {
            s_childSetups = 0;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
        }

        private readonly record struct KeyProps(string Key);

        [Component]
        private static VNode Child()
        {
            Hooks.UseLayoutEffect(() => { s_childSetups++; return (Action)(() => { }); }, Array.Empty<object>());
            return V.Label(name: "child");
        }

        [Component(Compiler = false)]
        private static VNode OwnKeyed()
        {
            var (key, setKey) = Hooks.UseState("a");
            s_setOwnKey = setKey;
            return V.Fragment(new VNode[] { V.Component(Child) }, key: key);
        }

        [Component(Compiler = false)]
        private static VNode PropKeyed(KeyProps props)
            => V.Fragment(new VNode[] { V.Component(Child) }, key: props.Key);

        [Component(Compiler = false)]
        private static VNode ParentKeyed()
        {
            var (key, setKey) = Hooks.UseState("a");
            s_setParentKey = setKey;
            return V.Div(name: "host", children: new VNode[] { V.Component(PropKeyed, new KeyProps(key)) });
        }

        [Test]
        public void Given_AComponentReturningAKeyedFragment_When_ItsOwnRerenderChangesTheKey_Then_TheChildRemounts()
        {
            // Arrange
            _mounted = V.Mount(new VisualElement(), V.Component(OwnKeyed, key: "root"));
            _mounted.FlushEffectsForTest();

            // Act
            s_setOwnKey.Invoke("b");
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_childSetups, Is.EqualTo(2));
        }

        [Test]
        public void Given_AComponentReturningAKeyedFragment_When_ItsParentChangesTheKey_Then_TheChildRemounts()
        {
            // Arrange
            _mounted = V.Mount(new VisualElement(), V.Component(ParentKeyed, key: "root"));
            _mounted.FlushEffectsForTest();

            // Act
            s_setParentKey.Invoke("b");
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_childSetups, Is.EqualTo(2));
        }
    }
}

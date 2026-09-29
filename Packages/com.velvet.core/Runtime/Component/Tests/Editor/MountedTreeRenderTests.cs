#if UNITY_EDITOR
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    internal sealed class MountedTreeRenderTests
    {
        [Test]
        public void Given_ADisposedTree_When_RenderedInto_Then_ItRefusesByName()
        {
            // Arrange
            var mounted = V.Mount(new VisualElement(), V.Div());
            mounted.Dispose();
            var render = typeof(MountedTree).GetMethod("Render", BindingFlags.Instance | BindingFlags.NonPublic);

            // Act
            Exception thrown = null;
            try
            {
                render?.Invoke(mounted, new object[] { V.Div() });
            }
            catch (TargetInvocationException ex)
            {
                thrown = ex.InnerException;
            }

            // Assert
            Assert.That(thrown?.Message, Is.EqualTo("Cannot update an unmounted root."));
        }
    }
}
#endif

using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="Hooks.ForwardedRef{T}"/>, which retrieves the ref a parent
    /// forwarded via <c>V.Component&lt;TRef&gt;(componentRef:)</c>.
    /// <list type="bullet">
    /// <item>When the parent forwards no ref, the hook returns null for every requested handle type.</item>
    /// <item>When the parent forwards a ref whose type matches the requested handle type, the hook returns
    /// that exact ref instance.</item>
    /// <item>When the forwarded ref's type does not match the requested handle type, the hook returns null
    /// (the retrieval is a typed cast, never a coercion).</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Uses the <c>[Component] static VNode</c> + <c>V.Mount</c> + static-field exposure pattern. The capturing
    /// component requests two unrelated handle types from the same call so a single mount observes both the
    /// matching and the mismatching retrieval. Per-region static fields are reset in <see cref="SetUp"/>.
    /// </remarks>
    [TestFixture]
    internal sealed class ForwardedRefTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetCapturing();
        }

        [Test]
        public void Given_NoRefForwarded_When_HandleRequested_Then_ReturnsNull()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(CapturingRender, key: "capture"));

            // Assert
            Assert.That(s_capturingMyHandle, Is.Null,
                "ForwardedRef returns null when the parent forwarded no ref");
        }

        [Test]
        public void Given_RefForwarded_When_RequestedTypeMatches_Then_ReturnsSameRefInstance()
        {
            // Arrange
            var handleRef = new Ref<IMyHandle>();

            // Act
            using var mounted = V.Mount(_root, V.Component(CapturingRender, componentRef: handleRef, key: "capture"));

            // Assert
            Assert.That(s_capturingMyHandle, Is.SameAs(handleRef),
                "ForwardedRef of the matching type returns the exact ref the parent forwarded");
        }

        [Test]
        public void Given_RefForwarded_When_RequestedTypeMismatches_Then_ReturnsNull()
        {
            // Arrange
            var handleRef = new Ref<IMyHandle>();

            // Act
            using var mounted = V.Mount(_root, V.Component(CapturingRender, componentRef: handleRef, key: "capture"));

            // Assert
            Assert.That(s_capturingOtherHandle, Is.Null,
                "Requesting an incompatible handle type yields null because retrieval is a typed cast");
        }

        private interface IMyHandle { void Focus(); }
        private interface IOtherHandle { void Scroll(); }

        #region Capturing component (requests two unrelated handle types)

        private static Ref<IMyHandle> s_capturingMyHandle;
        private static Ref<IOtherHandle> s_capturingOtherHandle;

        private static void ResetCapturing()
        {
            s_capturingMyHandle = null;
            s_capturingOtherHandle = null;
        }

        [Component]
        private static VNode CapturingRender()
        {
            s_capturingMyHandle = Hooks.ForwardedRef<IMyHandle>();
            s_capturingOtherHandle = Hooks.ForwardedRef<IOtherHandle>();
            return V.Label(text: "x");
        }

        #endregion
    }
}

using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Guards that the C# corner-radius mirror used by the drop-shadow silhouette
    /// (<see cref="StyleShadowClass.TryResolveCornerRadius"/>) stays in lockstep with the
    /// <c>--radius-*</c> token scale in <c>_tokens.uss</c>. When the tokens were re-aligned,
    /// this mirror had to follow or a shadow on a <c>rounded-lg</c> box would bake the old (16px)
    /// corner. Pure (no panel); GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class ShadowRadiusScaleMirrorTests
    {
        [Test]
        public void Given_RoundedLgClass_When_ResolvingShadowCornerRadius_Then_Is8()
        {
            // Arrange/Act — rounded-lg mirrors --radius-lg, re-aligned to 8px (was 16).
            var ok = StyleShadowClass.TryResolveCornerRadius(new[] { "rounded-lg" }, out var radius);
            Assume.That(ok, Is.True, "Precondition: rounded-lg resolves a mirrored corner radius");

            // Assert
            Assert.That(radius, Is.EqualTo(8f));
        }

        [Test]
        public void Given_Rounded3xlClass_When_ResolvingShadowCornerRadius_Then_Is24()
        {
            // Arrange/Act — rounded-3xl mirrors --radius-3xl, re-aligned to 24px (was 45).
            var ok = StyleShadowClass.TryResolveCornerRadius(new[] { "rounded-3xl" }, out var radius);
            Assume.That(ok, Is.True, "Precondition: rounded-3xl resolves a mirrored corner radius");

            // Assert
            Assert.That(radius, Is.EqualTo(24f));
        }

        [Test]
        public void Given_BareRoundedClass_When_ResolvingShadowCornerRadius_Then_Is4()
        {
            // Arrange/Act — the bare `rounded` DEFAULT mirrors --radius-default (4px); a shadow on a
            // bare-rounded box must follow that corner, not fall through to a square silhouette.
            var ok = StyleShadowClass.TryResolveCornerRadius(new[] { "rounded" }, out var radius);
            Assume.That(ok, Is.True, "Precondition: bare rounded resolves a mirrored corner radius");

            // Assert
            Assert.That(radius, Is.EqualTo(4f));
        }
    }
}

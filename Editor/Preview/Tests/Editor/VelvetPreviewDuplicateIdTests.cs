using System;
using System.Reflection;
using NUnit.Framework;

namespace Velvet.Tests
{
    // The colliding pair lives here rather than beside the discovery tests, whose fixture discovers its whole
    // assembly and would be refused.
    internal sealed class VelvetPreviewDuplicateIdTests
    {
        [VelvetPreview(Name = "Twin", Group = "DuplicateIdFixture")]
        private static VNode TwinA() => V.Div();

        [VelvetPreview(Name = "Twin", Group = "DuplicateIdFixture")]
        private static VNode TwinB() => V.Div();

        [Test]
        public void Given_TwoStoriesShareAnId_When_Discovering_Then_DiscoveryIsRefusedNamingBoth()
        {
            // Arrange
            var discover = typeof(VelvetPreviewRegistry).GetMethod(
                "DiscoverStoriesIn", BindingFlags.Static | BindingFlags.NonPublic);
            Assume.That(discover, Is.Not.Null, "VelvetPreviewRegistry.DiscoverStoriesIn must exist");
            var assemblies = new[] { typeof(VelvetPreviewDuplicateIdTests).Assembly };

            // Act
            string refusal = "";
            try
            {
                discover.Invoke(null, new object[] { assemblies });
            }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
            {
                refusal = ex.InnerException.Message;
            }

            // Assert
            Assert.That(refusal, Does.Contain(nameof(TwinA)).And.Contain(nameof(TwinB)));
        }
    }
}

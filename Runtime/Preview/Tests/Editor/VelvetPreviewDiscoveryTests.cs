using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Velvet.Tests
{
    /// <summary>
    /// Discovery contract for <see cref="VelvetPreviewRegistry"/>: a well-formed <c>[VelvetPreview]</c> method is
    /// found and described by its attribute; malformed ones (parameterized, generic) are excluded so a broken
    /// story cannot crash a preview tool mid-list; a duplicate Group/Name id is dropped; and the public
    /// <c>DiscoverStories()</c> excludes stories declared in test assemblies so fixtures never leak into the
    /// preview window or capture set.
    /// </summary>
    /// <remarks>
    /// The production <c>DiscoverStories()</c> deliberately skips test-runner assemblies, so these fixture
    /// stories are invisible to it. To still exercise discovery on this fixture's own assembly, the tests call
    /// the <c>internal</c> <c>DiscoverStoriesIn(IEnumerable&lt;Assembly&gt;)</c> via reflection (the repo
    /// convention for verifying internals from a test that lacks InternalsVisibleTo).
    /// </remarks>
    internal sealed class VelvetPreviewDiscoveryTests
    {
        private const string Group = "DiscoveryFixture";

        [VelvetPreview(Name = "Valid Story", Group = Group)]
        private static VNode ValidStory() => V.Div("box", V.Label(text: "hello"));

        // Parameterized: not a valid story signature; discovery must skip it (warns, does not throw).
        [VelvetPreview(Name = "Invalid Story", Group = Group)]
        private static VNode InvalidStory(int unused) => V.Div();

        // Generic: invoking it via reflection would throw a raw InvalidOperationException (not the unwrapped
        // TargetInvocationException), so discovery must exclude it up front.
        [VelvetPreview(Name = "Generic Story", Group = Group)]
        private static VNode GenericStory<T>() => V.Div();

        // Two methods sharing one Group/Name → one duplicate id; discovery keeps one and drops the other.
        [VelvetPreview(Name = "Twin", Group = Group)]
        private static VNode TwinA() => V.Div();

        [VelvetPreview(Name = "Twin", Group = Group)]
        private static VNode TwinB() => V.Div();

        // A DiscoverStoriesIn call scans the WHOLE test assembly, so it warns once per malformed story across all
        // preview fixtures: this fixture's parameterized + generic + duplicate (3) and the args fixture's
        // abstract-args story (1) = 4. Register all four before discovery so none counts as an unexpected log
        // (which would fail the test); per-test scoped, unlike the process-global ignoreFailingMessages flag.
        private static void ExpectDiscoveryWarnings()
        {
            for (var i = 0; i < 4; i++) LogAssert.Expect(LogType.Warning, new Regex("VelvetPreview"));
        }

        private static List<VelvetPreviewStory> DiscoverThisAssembly()
        {
            var method = typeof(VelvetPreviewRegistry).GetMethod(
                "DiscoverStoriesIn", BindingFlags.Static | BindingFlags.NonPublic);
            Assume.That(method, Is.Not.Null, "VelvetPreviewRegistry.DiscoverStoriesIn must exist");
            var assemblies = new[] { typeof(VelvetPreviewDiscoveryTests).Assembly };
            return (List<VelvetPreviewStory>)method.Invoke(null, new object[] { assemblies });
        }

        [Test]
        public void Given_AValidStoryMethod_When_Discovering_Then_ItAppearsWithItsAttributeName()
        {
            // Arrange
            ExpectDiscoveryWarnings();
            var stories = DiscoverThisAssembly();

            // Act
            var found = stories.SingleOrDefault(s => s.Group == Group && s.Name == "Valid Story");

            // Assert
            Assert.That(found, Is.Not.Null);
        }

        [Test]
        public void Given_AParameterizedStoryMethod_When_Discovering_Then_ItIsExcluded()
        {
            // Arrange
            ExpectDiscoveryWarnings();

            // Act
            var stories = DiscoverThisAssembly();

            // Assert
            Assert.That(stories.Any(s => s.Group == Group && s.Name == "Invalid Story"), Is.False);
        }

        [Test]
        public void Given_AGenericStoryMethod_When_Discovering_Then_ItIsExcluded()
        {
            // Arrange
            ExpectDiscoveryWarnings();

            // Act
            var stories = DiscoverThisAssembly();

            // Assert
            Assert.That(stories.Any(s => s.Group == Group && s.Name == "Generic Story"), Is.False);
        }

        [Test]
        public void Given_TwoStoriesShareAnId_When_Discovering_Then_OnlyOneIsRetained()
        {
            // Arrange
            ExpectDiscoveryWarnings();

            // Act
            var stories = DiscoverThisAssembly();

            // Assert
            Assert.That(stories.Count(s => s.Group == Group && s.Name == "Twin"), Is.EqualTo(1));
        }

        [Test]
        public void Given_AStoryInATestAssembly_When_PublicDiscoverStories_Then_ItIsNotReturned()
        {
            // Arrange — this fixture lives in a test-runner assembly, which the public scan excludes.
            var stories = VelvetPreviewRegistry.DiscoverStories();

            // Act
            var leaked = stories.Any(s => s.Group == Group);

            // Assert
            Assert.That(leaked, Is.False);
        }
    }
}

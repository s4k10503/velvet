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
    /// <para>
    /// Also specifies the args-story contract: a <c>[VelvetPreview]</c> method taking a single args object is
    /// discovered with a non-null <see cref="VelvetPreviewStory.ArgsType"/>, and <c>Build(args)</c> threads the
    /// edited args into the rendered tree (the live-controls round trip) — colocated here because both concerns
    /// exercise the same <c>DiscoverStoriesIn</c> reflection path over this one assembly, and the malformed
    /// stories below and the abstract-args story are counted together by <see cref="ExpectDiscoveryWarnings"/>.
    /// </para>
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
        private const string ArgsGroup = "ArgsFixture";

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

        internal sealed class LabelArgs
        {
            public string Text = "default";
        }

        // An abstract args type cannot be default-constructed even though it declares a PUBLIC parameterless
        // constructor — so the IsAbstract guard, not the ctor-presence check, is what must exclude it. Without
        // that guard the story would be accepted and only blow up later in Activator.CreateInstance.
        internal abstract class AbstractArgs
        {
            public AbstractArgs() { }
        }

        [VelvetPreview(Name = "Label", Group = ArgsGroup)]
        private static VNode LabelStory(LabelArgs args) => V.Label(text: args.Text);

        [VelvetPreview(Name = "Abstract", Group = ArgsGroup)]
        private static VNode AbstractArgsStory(AbstractArgs args) => V.Label(text: "x");

        // A DiscoverStoriesIn call scans the WHOLE test assembly, so it warns once per malformed story across all
        // preview fixtures: this fixture's parameterized + generic + duplicate (3) and the abstract-args story
        // (1) = 4. Register all four before discovery so none counts as an unexpected log (which would fail the
        // test); per-test scoped, unlike the process-global ignoreFailingMessages flag.
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

        private static VelvetPreviewStory ArgsStory()
        {
            foreach (var s in DiscoverThisAssembly())
            {
                if (s.Group == ArgsGroup && s.Name == "Label") return s;
            }

            return null;
        }

        // Finds the first ElementNode carrying text in a built tree (the story's single label).
        private static string LabelTextOf(VNode node)
        {
            switch (node)
            {
                case ElementNode element when element.Props?.Text != null:
                    return element.Props.Text;
                case BaseElementNode element:
                    foreach (var child in element.Children)
                    {
                        var found = LabelTextOf(child);
                        if (found != null) return found;
                    }

                    return null;
                default:
                    return null;
            }
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

        // --- Args-story contract ---

        [Test]
        public void Given_AnArgsStoryMethod_When_Discovered_Then_ArgsTypeIsNonNull()
        {
            // Arrange
            ExpectDiscoveryWarnings();
            var story = ArgsStory();
            Assume.That(story, Is.Not.Null, "the args-story must be discovered");

            // Act
            var argsType = story.ArgsType;

            // Assert
            Assert.That(argsType, Is.EqualTo(typeof(LabelArgs)));
        }

        [Test]
        public void Given_AnArgsStory_When_BuiltWithMutatedArgs_Then_TheNewValueReachesTheTree()
        {
            // Arrange
            ExpectDiscoveryWarnings();
            var story = ArgsStory();
            Assume.That(story, Is.Not.Null, "the args-story must be discovered");
            var args = new LabelArgs { Text = "X" };

            // Act
            var tree = story.Build(args);

            // Assert
            Assert.That(LabelTextOf(tree), Is.EqualTo("X"));
        }

        [Test]
        public void Given_AnAbstractArgsStory_When_Discovering_Then_ItIsExcluded()
        {
            // Arrange
            ExpectDiscoveryWarnings();

            // Act
            var stories = DiscoverThisAssembly();

            // Assert — an abstract (non-constructible) args type makes the story invalid, so it is not listed.
            Assert.That(stories.Exists(s => s.Group == ArgsGroup && s.Name == "Abstract"), Is.False);
        }
    }
}

using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Velvet.Tests
{
    /// <summary>
    /// Args-story contract: a <c>[VelvetPreview]</c> method taking a single args object is discovered with a
    /// non-null <see cref="VelvetPreviewStory.ArgsType"/>, and <c>Build(args)</c> threads the edited args into
    /// the rendered tree (the live-controls round trip).
    /// </summary>
    /// <remarks>
    /// Discovery is exercised on this fixture's own assembly via the <c>internal</c>
    /// <c>DiscoverStoriesIn(IEnumerable&lt;Assembly&gt;)</c> reached by reflection, since the public
    /// <c>DiscoverStories()</c> excludes test assemblies.
    /// </remarks>
    internal sealed class VelvetPreviewArgsTests
    {
        private const string Group = "ArgsFixture";

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

        [VelvetPreview(Name = "Label", Group = Group)]
        private static VNode LabelStory(LabelArgs args) => V.Label(text: args.Text);

        [VelvetPreview(Name = "Abstract", Group = Group)]
        private static VNode AbstractArgsStory(AbstractArgs args) => V.Label(text: "x");

        // Discovering this assembly walks the sibling discovery fixture (malformed: parameterized, generic,
        // duplicate id → 3 warnings) AND this fixture's own abstract-args story (1 warning). Allow those expected
        // warnings so they do not register as unexpected logs and fail the test.
        private static void ExpectDiscoveryWarnings()
        {
            for (var i = 0; i < 4; i++) LogAssert.Expect(LogType.Warning, new Regex("VelvetPreview"));
        }

        private static List<VelvetPreviewStory> DiscoverThisAssembly()
        {
            var discover = typeof(VelvetPreviewRegistry).GetMethod(
                "DiscoverStoriesIn", BindingFlags.Static | BindingFlags.NonPublic);
            Assume.That(discover, Is.Not.Null, "VelvetPreviewRegistry.DiscoverStoriesIn must exist");
            return (List<VelvetPreviewStory>)discover.Invoke(
                null, new object[] { new[] { typeof(VelvetPreviewArgsTests).Assembly } });
        }

        private static VelvetPreviewStory ArgsStory()
        {
            foreach (var s in DiscoverThisAssembly())
            {
                if (s.Group == Group && s.Name == "Label") return s;
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
            Assert.That(stories.Exists(s => s.Group == Group && s.Name == "Abstract"), Is.False);
        }
    }
}

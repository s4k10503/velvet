using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Velvet.Tests
{
    internal sealed class VelvetPreviewDiscoveryTests
    {
        private const string Group = "DiscoveryFixture";
        private const string ArgsGroup = "ArgsFixture";

        [VelvetPreview(Name = "Valid Story", Group = Group)]
        private static VNode ValidStory() => V.Div("box", V.Label(text: "hello"));

        [VelvetPreview(Name = "Invalid Story", Group = Group)]
        private static VNode InvalidStory(int unused) => V.Div();

        [VelvetPreview(Name = "Generic Story", Group = Group)]
        private static VNode GenericStory<T>() => V.Div();

        [VelvetPreview(Name = "Twin", Group = Group)]
        private static VNode TwinA() => V.Div();

        [VelvetPreview(Name = "Twin", Group = Group)]
        private static VNode TwinB() => V.Div();

        internal sealed class LabelArgs
        {
            public string Text = "default";
        }

        // The public constructor is what leaves IsAbstract, rather than the ctor-presence check, as the
        // only term that can exclude this.
        internal abstract class AbstractArgs
        {
            public AbstractArgs() { }
        }

        [VelvetPreview(Name = "Label", Group = ArgsGroup)]
        private static VNode LabelStory(LabelArgs args) => V.Label(text: args.Text);

        [VelvetPreview(Name = "Abstract", Group = ArgsGroup)]
        private static VNode AbstractArgsStory(AbstractArgs args) => V.Label(text: "x");

        // Discovery scans the whole test assembly, so its expected warnings are registered centrally.
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
            // Arrange
            var stories = VelvetPreviewRegistry.DiscoverStories();

            // Act
            var leaked = stories.Any(s => s.Group == Group);

            // Assert
            Assert.That(leaked, Is.False);
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

            // Assert
            Assert.That(stories.Exists(s => s.Group == ArgsGroup && s.Name == "Abstract"), Is.False);
        }
    }
}

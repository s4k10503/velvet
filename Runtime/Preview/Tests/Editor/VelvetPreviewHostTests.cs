using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    internal sealed class VelvetPreviewHostTests : PanelTestBase
    {
        private const string Marker = "preview-host-marker";

        private static VNode MarkerStory() => V.Div(name: Marker, className: "box");
        private static VNode NullStory() => null;
        private static VNode ThrowingStory() => throw new InvalidOperationException("boom");

        internal sealed class Args
        {
            public string Text = "a";
            public bool Throw;
            public bool Null;
        }

        private static VNode ArgsStory(Args args)
        {
            if (args.Throw) throw new InvalidOperationException("boom");
            return args.Null ? null : V.Component(StatefulProbe.Render, args.Text);
        }

        protected override Rect WindowSize => new Rect(0, 0, 400, 300);

        [TearDown]
        public override void TearDown()
        {
            VelvetStyleHints.PreviewStyleSheet = null;
            base.TearDown();
        }

        private static VelvetPreviewStory Story(string methodName)
        {
            var method = typeof(VelvetPreviewHostTests).GetMethod(
                methodName, BindingFlags.Static | BindingFlags.NonPublic);
            Assume.That(method, Is.Not.Null, $"fixture method '{methodName}' must exist");
            var attribute = new VelvetPreviewAttribute { Name = methodName, Group = "HostFixture" };
            var ctor = typeof(VelvetPreviewStory).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(MethodInfo), typeof(VelvetPreviewAttribute) }, null);
            Assume.That(ctor, Is.Not.Null, "VelvetPreviewStory's internal constructor must exist");
            return (VelvetPreviewStory)ctor.Invoke(new object[] { method, attribute });
        }

        [Test]
        public void Given_AStory_When_HostMounts_Then_TheStoryElementIsUnderTheTarget()
        {
            // Arrange
            using var host = new VelvetPreviewHost(_window.rootVisualElement);

            // Act
            host.Mount(Story(nameof(MarkerStory)));

            // Assert
            Assert.That(_window.rootVisualElement.Q<VisualElement>(Marker), Is.Not.Null);
        }

        [Test]
        public void Given_AMountedStory_When_HostDisposed_Then_TheStoryElementIsRemoved()
        {
            // Arrange
            var host = new VelvetPreviewHost(_window.rootVisualElement);
            host.Mount(Story(nameof(MarkerStory)));
            Assume.That(_window.rootVisualElement.Q<VisualElement>(Marker), Is.Not.Null, "the story mounted first");

            // Act
            host.Dispose();

            // Assert
            Assert.That(_window.rootVisualElement.Q<VisualElement>(Marker), Is.Null);
        }

        [Test]
        public void Given_AHintedStyleSheet_When_AStoryMountsNull_Then_TheSheetIsNotLeftOnTheTarget()
        {
            // Arrange
            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            VelvetStyleHints.PreviewStyleSheet = sheet;
            using var host = new VelvetPreviewHost(_window.rootVisualElement);

            // Act
            host.Mount(Story(nameof(NullStory)));

            // Assert
            Assert.That(_window.rootVisualElement.styleSheets.Contains(sheet), Is.False);
        }

        [Test]
        public void Given_AMountedArgsStory_When_ArgsUpdated_Then_AComponentInItKeepsItsState()
        {
            // Arrange
            using var host = new VelvetPreviewHost(_window.rootVisualElement);
            host.Mount(Story(nameof(ArgsStory)), new Args { Text = "a" });
            StatefulProbe.Initializations = 0;

            // Act
            host.UpdateArgs(new Args { Text = "b" });

            // Assert
            Assert.That(StatefulProbe.Initializations, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base rebuilds the whole tree from the new args.
        // The branch re-renders the mounted one instead and has to reach the same text.
        [Test]
        public void Given_AMountedArgsStory_When_ArgsUpdated_Then_TheTreeShowsTheNewArgs()
        {
            // Arrange
            using var host = new VelvetPreviewHost(_window.rootVisualElement);
            host.Mount(Story(nameof(ArgsStory)), new Args { Text = "a" });

            // Act
            host.UpdateArgs(new Args { Text = "b" });

            // Assert
            Assert.That(_window.rootVisualElement.Q<Label>(StatefulProbe.Name)?.text, Is.EqualTo("b"));
        }

        // GREEN_ON_BASE(characterization): the base unmounts before it builds, leaving nothing mounted.
        // The branch keeps the tree through the build and has to unmount it when the build throws.
        [Test]
        public void Given_AMountedArgsStory_When_AnUpdateThrows_Then_TheStoryIsUnmounted()
        {
            // Arrange
            using var host = new VelvetPreviewHost(_window.rootVisualElement);
            host.Mount(Story(nameof(ArgsStory)), new Args { Text = "a" });

            // Act
            host.UpdateArgs(new Args { Throw = true });

            // Assert
            Assert.That(
                (host.Story == null, _window.rootVisualElement.Q<Label>(StatefulProbe.Name) == null),
                Is.EqualTo((true, true)));
        }

        // GREEN_ON_BASE(characterization): the base names a null tree in its error the same way.
        [Test]
        public void Given_AMountedArgsStory_When_AnUpdateBuildsNull_Then_TheErrorSaysSo()
        {
            // Arrange
            using var host = new VelvetPreviewHost(_window.rootVisualElement);
            host.Mount(Story(nameof(ArgsStory)), new Args { Text = "a" });

            // Act
            host.UpdateArgs(new Args { Null = true });

            // Assert
            Assert.That(host.MountError?.Message, Is.EqualTo("Story returned a null VNode."));
        }

        [Test]
        public void Given_AStoryThatThrows_When_HostMounts_Then_StoryIsLeftNull()
        {
            // Arrange
            using var host = new VelvetPreviewHost(_window.rootVisualElement);

            // Act
            host.Mount(Story(nameof(ThrowingStory)));

            // Assert
            Assert.That(host.Story, Is.Null);
        }
    }

    internal static class StatefulProbe
    {
        public const string Name = "stateful-probe";

        public static int Initializations;

        [Component]
        public static VNode Render(string text)
        {
            Hooks.UseState(() => ++Initializations);
            return V.Label(name: Name, text: text);
        }
    }
}

using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;
using Velvet.Editor.Preview;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    // The example stories Velvet.Editor declares are what the window lists here.
    internal sealed class VelvetPreviewWindowStoryListTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private VelvetPreviewWindow _window;

        [SetUp]
        public void SetUp()
        {
            TestGraphics.IgnoreIfHeadless("an EditorWindow panel");
            EditorPrefs.DeleteKey("Velvet.Preview.LastStoryId");
            _window = EditorWindow.GetWindow<VelvetPreviewWindow>();
            _window.Show();
        }

        [TearDown]
        public void TearDown()
        {
            if (_window == null) return;
            _window.Close();
            _window = null;
        }

        // GREEN_ON_BASE(characterization): the base lists the discovered stories the same way.
        [Test]
        public void Given_TheWindowOpen_When_ItListsStories_Then_TheExampleStoriesAreAmongThem()
        {
            // Act
            var stories = (List<VelvetPreviewStory>)typeof(VelvetPreviewWindow).GetField("_stories", Private)
                ?.GetValue(_window);

            // Assert
            Assert.That(stories?.Exists(s => s.Id == "Examples/Card"), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base asks for a selection the same way when it has stories.
        [Test]
        public void Given_StoriesAndNoSelection_When_TheStatusIsShown_Then_ItAsksForASelection()
        {
            // Arrange
            var select = typeof(VelvetPreviewWindow).GetMethod("Select", Private);
            Assume.That(select, Is.Not.Null, "VelvetPreviewWindow.Select must exist");

            // Act
            select.Invoke(_window, new object[] { null });

            // Assert
            var status = (Label)typeof(VelvetPreviewWindow).GetField("_statusLabel", Private)?.GetValue(_window);
            Assert.That(status?.text, Is.EqualTo("Select a story."));
        }
    }
}

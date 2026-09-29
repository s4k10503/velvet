using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.Editor.Preview;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    // The Controls addon and the host wired together as the window wires them, over components that take the
    // args, or a list inside them, as their props.
    internal sealed class PreviewControlsRerenderTests
    {
        internal sealed class CardArgs
        {
            public string Title = "a";
            public List<string> Items = new() { "x" };
        }

        private static VNode CardStory(CardArgs args) =>
            V.Div("", V.Component(TitleCard.Render, args), V.Component(ItemsCard.Render, args.Items));

        private VisualElement _target;
        private VelvetPreviewHost _host;
        private VisualElement _panel;

        [SetUp]
        public void SetUp()
        {
            var method = typeof(PreviewControlsRerenderTests).GetMethod(
                nameof(CardStory), BindingFlags.Static | BindingFlags.NonPublic);
            var ctor = typeof(VelvetPreviewStory).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(MethodInfo), typeof(VelvetPreviewAttribute) }, null);
            Assume.That(ctor, Is.Not.Null, "VelvetPreviewStory's internal constructor must exist");
            var story = (VelvetPreviewStory)ctor.Invoke(
                new object[] { method, new VelvetPreviewAttribute { Name = "Card", Group = "RerenderFixture" } });

            var panelType = typeof(VelvetPreviewWindow).Assembly.GetType("Velvet.Editor.Preview.PreviewControlsPanel");
            Assume.That(panelType, Is.Not.Null, "PreviewControlsPanel must exist");
            _panel = (VisualElement)Activator.CreateInstance(panelType);
            panelType.GetMethod("SetStory").Invoke(_panel, new object[] { story });

            _target = new VisualElement();
            _host = new VelvetPreviewHost(_target);
            _host.Mount(story, panelType.GetProperty("Args").GetValue(_panel));
            panelType.GetEvent("ArgsChanged").AddEventHandler(_panel, (Action<object>)(args => _host.UpdateArgs(args)));
        }

        [TearDown]
        public void TearDown() => _host.Dispose();

        private static TField Field<TField, TValue>(VisualElement scope, string label)
            where TField : BaseField<TValue> =>
            scope?.Query<TField>().Where(f => f.label == label).First();

        // GREEN_ON_BASE(characterization): the base mounts the story afresh on every edit, so its view is new.
        // The branch re-renders the mounted tree and has to hand the component a new args instance to show it.
        [Test]
        public void Given_AComponentTakingTheArgs_When_AKnobIsEdited_Then_ItShowsTheEdit()
        {
            // Act
            Field<TextField, string>(_panel, nameof(CardArgs.Title))?.SimulateChange("b");

            // Assert
            Assert.That(_target.Q<Label>(TitleCard.Name)?.text, Is.EqualTo("b"));
        }

        [Test]
        public void Given_AComponentTakingAListFromTheArgs_When_AnElementIsEdited_Then_ItShowsTheEdit()
        {
            // Arrange
            var items = _panel.Query<Foldout>().Where(f => f.text == nameof(CardArgs.Items)).First();
            items?.SimulateChange(true);

            // Act
            Field<TextField, string>(items, "[0]")?.SimulateChange("y");

            // Assert
            Assert.That(_target.Q<Label>(ItemsCard.Name)?.text, Is.EqualTo("y"));
        }
    }

    internal static class TitleCard
    {
        public const string Name = "title-card";

        [Component]
        public static VNode Render(PreviewControlsRerenderTests.CardArgs args) => V.Label(name: Name, text: args.Title);
    }

    internal static class ItemsCard
    {
        public const string Name = "items-card";

        [Component(Memoize = true)]
        public static VNode Render(List<string> items) => V.Label(name: Name, text: string.Join(",", items));
    }
}

using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which panel's per-frame check carries a face stash: the panel of the element it watches, joined when
    /// the watch starts and left when the stash is abandoned.
    /// </summary>
    internal sealed class SilhouetteFaceStashPanelCheckTests : PanelTestBase
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        private VisualElement Attached()
        {
            var element = new VisualElement();
            _window.rootVisualElement.Add(element);
            return element;
        }

        private static void Watch(SilhouetteFaceStash stash, VisualElement element)
            => typeof(SilhouetteFaceStash).GetMethod("Watch", Private).Invoke(stash, new object[] { element });

        private static object CheckedBy(SilhouetteFaceStash stash)
            => typeof(SilhouetteFaceStash).GetField("_checkedBy", Private).GetValue(stash);

        // The stashes the panel's check carries, empty where the panel has no check.
        private static IList CarriedOn(IPanel panel)
        {
            var table = typeof(SilhouetteFaceStash).GetField("s_checks", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);
            var args = new object[] { panel, null };
            table.GetType().GetMethod("TryGetValue").Invoke(table, args);
            return args[1] == null
                ? new ArrayList()
                : (IList)args[1].GetType().GetField("Stashes").GetValue(args[1]);
        }

        // GREEN_ON_BASE(characterization): the base has a face stash checked by the panel its element is on.
        [Test]
        public void Given_AnElementOnAPanel_When_AStashWatchesIt_Then_TheStashIsCheckedByThatPanel()
        {
            // Arrange
            var element = Attached();
            var stash = new SilhouetteFaceStash();

            // Act
            Watch(stash, element);

            // Assert
            Assert.That(CheckedBy(stash), Is.SameAs(element.panel));
        }

        // GREEN_ON_BASE(characterization): the base drops an abandoned face stash from its panel's check.
        [Test]
        public void Given_AStashWatchingAnElementOnAPanel_When_TheStashIsAbandoned_Then_ThePanelsCheckNoLongerCarriesIt()
        {
            // Arrange
            var element = Attached();
            var stash = new SilhouetteFaceStash();
            Watch(stash, element);

            // Act
            stash.Abandon(element);

            // Assert
            Assert.That(CarriedOn(element.panel), Has.No.Member(stash));
        }
    }
}

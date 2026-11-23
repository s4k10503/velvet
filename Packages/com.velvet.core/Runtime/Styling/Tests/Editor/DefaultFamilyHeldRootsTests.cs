using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what releasing the last hold on an element does to the list <see cref="DefaultFamilyRoots"/> keeps
    /// its held elements in: the release leaves every other entry where it was, so what it costs does not grow with
    /// the elements held, and an entry it leaves behind is neither decided nor written again. GWT, one assert per
    /// case.
    /// </summary>
    [TestFixture]
    internal sealed class DefaultFamilyHeldRootsTests
    {
        private static FontAsset s_sans;

        private FontAsset _sans;
        private readonly System.Collections.Generic.List<System.IDisposable> _holds = new();

        [SetUp]
        public void SetUp()
        {
            VelvetFonts.Clear();
            _sans = s_sans != null ? s_sans : (s_sans = FontAsset.CreateFontAsset(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")));
            VelvetFonts.Register(new VelvetFontFamily("sans",
                new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, upright = _sans }));
            VelvetFonts.DefaultFamily = "sans";
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var hold in _holds) hold.Dispose();
            _holds.Clear();
            VelvetFonts.Clear();
        }

        private static object[] HeldList()
        {
            var held = (IList)typeof(DefaultFamilyRoots)
                .GetField("s_held", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
            var snapshot = new object[held?.Count ?? 0];
            held?.CopyTo(snapshot, 0);
            return snapshot;
        }

        // The entries present both before and after whose place in the list changed, or -1 where the list is not
        // there to read.
        private static int EntriesMoved(object[] before, object[] after)
        {
            if (before.Length == 0) return -1;
            var moved = 0;
            for (var i = 0; i < before.Length; i++)
            {
                var now = System.Array.IndexOf(after, before[i]);
                if (now >= 0 && now != i) moved++;
            }
            return moved;
        }

        private static FontAsset InlineFontOf(VisualElement element)
            => element.style.unityFontDefinition.keyword == StyleKeyword.Null
                ? null
                : element.style.unityFontDefinition.value.fontAsset;

        [Test]
        public void Given_FiveHeldElements_When_TheFirstIsReleased_Then_NoOtherEntryChangesPlaceInTheHeldList()
        {
            // Arrange
            var elements = new VisualElement[5];
            var holds = _holds;
            // SetUp's registration pruned the list, which leaves the next prune above what five more holds reach.
            for (var i = 0; i < 5; i++) holds.Add(DefaultFamilyRoots.Hold(elements[i] = new VisualElement()));
            var before = HeldList();

            // Act
            var first = holds[0];
            holds.RemoveAt(0);
            first.Dispose();
            var moved = EntriesMoved(before, HeldList());

            // Assert
            Assert.That(moved, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base takes a released element's entry out of the list at the release, so none is left to decide.
        [Test]
        public void Given_AReleasedElement_When_TheFontsChange_Then_ItsInlineFontIsNotWrittenAgain()
        {
            // Arrange
            var released = new VisualElement();
            using var holdingOther = DefaultFamilyRoots.Hold(new VisualElement());
            DefaultFamilyRoots.Hold(released).Dispose();

            // Act
            VelvetFonts.Register(new VelvetFontFamily("sans",
                new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, upright = _sans }));

            // Assert
            Assert.That(InlineFontOf(released), Is.Null);
        }
    }
}

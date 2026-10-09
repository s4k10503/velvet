using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// <see cref="StyleOverrides.BackgroundImage"/> against a gradient utility, which writes the same slot:
    /// the override shows whichever was written last, as React's <c>style</c> wins over <c>className</c>, and
    /// once it goes away the slot shows the gradient's current bake.
    /// </summary>
    [TestFixture]
    internal sealed class BackgroundImagePrecedenceTests : PanelTestBase
    {
        private const string Gradient = "w-[100px] h-[40px] bg-gradient-to-r to-blue-500";

        private static StateUpdater<int> s_setStep;
        private static Func<int, (string ClassName, StyleOverrides Styles)> s_nodeFor;
        private static Func<VisualElement, Action> s_ref;
        private Texture2D _poster;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            _poster = new Texture2D(4, 4);
            s_setStep = default;
            s_nodeFor = _ => (Gradient, null);
            s_ref = null;
        }

        [TearDown]
        public override void TearDown()
        {
            base.TearDown();
            UnityEngine.Object.DestroyImmediate(_poster);
        }

        [Component]
        private static VNode RenderCard()
        {
            var (step, setStep) = Hooks.UseState(0);
            s_setStep = setStep;
            var (className, styles) = s_nodeFor(step);
            return V.Div(className: className, name: "card", styles: styles, refCallback: s_ref);
        }

        private VisualElement Card => _window.rootVisualElement.Q<VisualElement>("card");

        private void Mount(Func<int, (string, StyleOverrides)> nodeFor)
        {
            s_nodeFor = nodeFor;
            _mounted = V.Mount(_window.rootVisualElement, V.Component(RenderCard));
        }

        private void Step(int n)
        {
            s_setStep.Invoke(n);
            _mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private StyleOverrides Poster => new() { BackgroundImage = new StyleBackground(_poster) };

        private Texture2D GradientBake => _mounted.Root.Reconciler.Context.GradientBackgrounds[Card].Texture;

        [Test]
        public void Given_AnImageOverrideAndAGradient_When_Mounted_Then_TheGradientSizesNothing()
        {
            // Arrange / Act
            Mount(_ => (Gradient, Poster));

            // Assert — a Tailwind gradient sets only the image, so under another image it leaves the size alone.
            Assert.That(Card.style.backgroundSize.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AnImageOverrideOverAGradient_When_TheOverrideIsRemoved_Then_TheGradientStretchesAgain()
        {
            // Arrange — read under the override, so a gradient that never let go of the size cannot pass.
            Mount(step => (Gradient, step == 0 ? Poster : null));
            var covered = Card.style.backgroundSize.keyword;

            // Act
            Step(1);

            // Assert
            var size = Card.style.backgroundSize;
            Assert.That((covered, size.keyword, size.value.x.value), Is.EqualTo((StyleKeyword.Null, StyleKeyword.Undefined, 100f)));
        }

        [Test]
        public void Given_AnImageOverrideOverAGradient_When_APanIsDeclared_Then_NothingPans()
        {
            // Arrange / Act
            Mount(_ => (Gradient + " animate-gradient", Poster));

            // Assert
            Assert.That(_mounted.Root.Reconciler.Context.AnimationBindings.ContainsKey(Card), Is.False);
        }

        // GREEN_ON_BASE(characterization): the base re-bakes only over an inline image that is its own bake.
        // This pins that the ranked image record keeps a refCallback's image from being re-baked over.
        [Test]
        public void Given_ARefCallbackImageOverAGradient_When_TheGradientIsResized_Then_TheRefCallbackImageStands()
        {
            // Arrange
            var own = new Texture2D(2, 2);
            s_ref = element =>
            {
                element.style.backgroundImage = new StyleBackground(own);
                return null;
            };
            try
            {
                Mount(step => (step == 0 ? Gradient : "w-[200px] h-[40px] bg-gradient-to-r to-blue-500", null));
                ForcePanelUpdate(Card.panel);

                // Act
                Step(1);
                ForcePanelUpdate(Card.panel);

                // Assert
                Assert.That(Card.style.backgroundImage.value.texture, Is.SameAs(own));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(own);
            }
        }

        [Test]
        public void Given_AnImageOverrideAndAGradient_When_Mounted_Then_TheOverrideShows()
        {
            // Arrange / Act — the gradient is applied after the override on mount.
            Mount(_ => (Gradient, Poster));

            // Assert
            Assert.That(Card.style.backgroundImage.value.texture, Is.SameAs(_poster));
        }

        // A bg-[addr:…] image resolves through the resolver's Addressables cache; seeding it stands in for a
        // loaded asset, which EditMode has none of.
        private static System.Collections.Generic.Dictionary<string, Texture2D> AddressCache()
            => (System.Collections.Generic.Dictionary<string, Texture2D>)typeof(StyleBackgroundImageResolver)
                .GetField("_cache", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .GetValue(null);

        [Test]
        public void Given_AnImageOverride_When_AnAddressImageUtilityIsAdded_Then_TheOverrideStillShows()
        {
            // Arrange
            var addressed = new Texture2D(2, 2);
            AddressCache()["precedence-test"] = addressed;
            try
            {
                Mount(step => (step == 0 ? "w-[100px]" : "w-[100px] bg-[addr:precedence-test]", Poster));

                // Act
                Step(1);

                // Assert
                Assert.That(Card.style.backgroundImage.value.texture, Is.SameAs(_poster));
            }
            finally
            {
                AddressCache().Remove("precedence-test");
                UnityEngine.Object.DestroyImmediate(addressed);
            }
        }

        [Test]
        public void Given_AnImageOverride_When_AnImportantAddressImageUtilityIsMounted_Then_TheUtilityShows()
        {
            // Arrange
            var addressed = new Texture2D(2, 2);
            AddressCache()["precedence-test"] = addressed;
            try
            {
                // Act — the override is written after the utility on mount.
                Mount(_ => ("!bg-[addr:precedence-test]", Poster));

                // Assert
                Assert.That(Card.style.backgroundImage.value.texture, Is.SameAs(addressed));
            }
            finally
            {
                AddressCache().Remove("precedence-test");
                UnityEngine.Object.DestroyImmediate(addressed);
            }
        }

        [Test]
        public void Given_AnImportantAddressImageOverAnImageOverride_When_TheUtilityIsRemoved_Then_TheOverrideShows()
        {
            // Arrange — read while the important utility holds the slot, so an override that never yielded to it
            // cannot pass.
            var addressed = new Texture2D(2, 2);
            AddressCache()["precedence-test"] = addressed;
            try
            {
                Mount(step => (step == 0 ? "w-[100px] !bg-[addr:precedence-test]" : "w-[100px]", Poster));
                var important = Card.style.backgroundImage.value.texture;

                // Act
                Step(1);

                // Assert
                Assert.That((important == addressed, Card.style.backgroundImage.value.texture == _poster),
                    Is.EqualTo((true, true)));
            }
            finally
            {
                AddressCache().Remove("precedence-test");
                UnityEngine.Object.DestroyImmediate(addressed);
            }
        }

        [Test]
        public void Given_AnImageOverride_When_TheGradientIsResizedUnderItAndTheOverrideRemoved_Then_TheResizedBakeShows()
        {
            // Arrange — a box twice as wide bakes the gradient for a new aspect, through the geometry the panel
            // reports, while the override covers it.
            Mount(step => step switch
            {
                0 => ("w-[100px] h-[40px] bg-gradient-to-r to-blue-500", Poster),
                1 => ("w-[200px] h-[40px] bg-gradient-to-r to-blue-500", Poster),
                _ => ("w-[200px] h-[40px] bg-gradient-to-r to-blue-500", null),
            });
            ForcePanelUpdate(Card.panel);
            var first = GradientBake;
            Step(1);
            ForcePanelUpdate(Card.panel);
            var resized = GradientBake;

            // Act
            Step(2);

            // Assert — the bake moved under the override, and the slot shows where it moved to.
            var shown = Card.style.backgroundImage.value.texture;
            Assert.That((resized != first, shown != null && shown == resized), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AnImageOverride_When_TheAddressImageApiWritesAnImage_Then_TheOverrideStillShows()
        {
            // Arrange
            var written = new Texture2D(2, 2);
            Mount(_ => ("w-[100px]", Poster));
            try
            {
                // Act
                StyleBackgroundImageResolver.Apply(Card, written);

                // Assert
                Assert.That(Card.style.backgroundImage.value.texture, Is.SameAs(_poster));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(written);
            }
        }

        [Test]
        public void Given_AnImageOverrideOverAGradient_When_TheGradientIsRebakedAndTheOverrideRemoved_Then_TheNewBakeShows()
        {
            // Arrange — a stop change re-bakes the gradient under the override.
            Mount(step => step switch
            {
                0 => ("from-red-500 " + Gradient, Poster),
                1 => ("from-green-500 " + Gradient, Poster),
                _ => ("from-green-500 " + Gradient, null),
            });
            Step(1);
            var covered = Card.style.backgroundImage.value.texture;

            // Act
            Step(2);

            // Assert — the bake is read off the binding, so a stale texture cannot pass for the current one.
            var shown = Card.style.backgroundImage.value.texture;
            Assert.That((covered == _poster, shown != null && shown == GradientBake),
                Is.EqualTo((true, true)));
        }
    }
}

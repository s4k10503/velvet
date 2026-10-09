using System;
using System.Globalization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// A percentage slice inset, as <c>border-image-slice</c> reads one: a percentage of the background
    /// image's size on that axis — its height for the top and bottom, its width for the left and right —
    /// resolved against the image the element shows and again whenever that image changes. The images are
    /// 40×80 and 80×40, so a reading taken against the wrong axis or the old image differs from the right one.
    /// </summary>
    [TestFixture]
    internal sealed class NineSlicePercentTests
    {
        private Texture2D _tall;
        private Texture2D _wide;

        [SetUp]
        public void SetUp()
        {
            _tall = new Texture2D(40, 80);
            _wide = new Texture2D(80, 40);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_tall);
            UnityEngine.Object.DestroyImmediate(_wide);
        }

        private StyleOverrides Showing(Texture2D image) => new() { BackgroundImage = new StyleBackground(image) };

        [TestCase("slice-[25%]", "20,10,20,10")]
        [TestCase("slice-[25%_10]", "20,10,20,10")]
        [TestCase("slice-[10_50%]", "10,20,10,20")]
        [TestCase("slice-[25%_50%_50%_25%]", "20,20,40,10")]
        [TestCase("slice-t-[50%]", "40,null,null,null")]
        [TestCase("slice-x-[50%]", "null,20,null,20")]
        [TestCase("slice-[12.5%]", "10,5,10,5")]
        [TestCase("slice-[0%_10]", "0,10,0,10")]
        [TestCase("slice-[150%]", "80,40,80,40")]
        [TestCase("slice-[1e30%]", "80,40,80,40")]
        public void Given_APercentSliceOverAnImage_When_Mounted_Then_EachEdgeIsThatShareOfItsAxis(
            string className, string expected)
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            reconciler.Reconcile(root, Array.Empty<VNode>(),
                new VNode[] { V.Div(className: className, styles: Showing(_tall)) });

            // Assert — top, right, bottom, left.
            Assert.That(Insets(root.ElementAt(0)), Is.EqualTo(expected));
        }

        [Test]
        public void Given_APercentSliceOverAnImage_When_TheImageChanges_Then_TheInsetsFollowTheNewImage()
        {
            // Arrange — read against the first image, so a slice that never resolved cannot pass.
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var oldTree = new VNode[] { V.Div(className: "slice-[25%]", styles: Showing(_tall)) };
            var newTree = new VNode[] { V.Div(className: "slice-[25%]", styles: Showing(_wide)) };
            reconciler.Reconcile(root, Array.Empty<VNode>(), oldTree);
            var before = Insets(root.ElementAt(0));

            // Act
            reconciler.Reconcile(root, oldTree, newTree);

            // Assert
            Assert.That((before, Insets(root.ElementAt(0))), Is.EqualTo(("20,10,20,10", "10,20,10,20")));
        }

        [Test]
        public void Given_APercentSliceOverASprite_When_Mounted_Then_EachEdgeIsThatShareOfTheSpritesRect()
        {
            // Arrange — the sprite is the tall texture's top half, 40×40, so its rect rather than its texture
            // is what the percentage is of.
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var sprite = Sprite.Create(_tall, new Rect(0f, 40f, 40f, 40f), Vector2.zero);
            try
            {
                // Act
                reconciler.Reconcile(root, Array.Empty<VNode>(), new VNode[]
                {
                    V.Div(className: "slice-[25%]",
                        styles: new StyleOverrides { BackgroundImage = new StyleBackground(sprite) }),
                });

                // Assert
                Assert.That(Insets(root.ElementAt(0)), Is.EqualTo("10,10,10,10"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sprite);
            }
        }

        [Test]
        public void Given_APercentSliceOverAVectorImage_When_Mounted_Then_EachEdgeIsThatShareOfItsSize()
        {
            // Arrange — a filled 40×80 rectangle, whose saved size is what the percentage is of; the expected
            // insets are read off that size, which is required to be non-zero so a percentage of nothing cannot
            // pass.
            using var reconciler = new Reconciler();
            var root = new VisualElement();
            var image = ScriptableObject.CreateInstance<VectorImage>();
            try
            {
                using (var painter = new Painter2D())
                {
                    painter.BeginPath();
                    painter.MoveTo(Vector2.zero);
                    painter.LineTo(new Vector2(40f, 0f));
                    painter.LineTo(new Vector2(40f, 80f));
                    painter.LineTo(new Vector2(0f, 80f));
                    painter.ClosePath();
                    painter.Fill();
                    painter.SaveToVectorImage(image);
                }
                var vertical = Mathf.RoundToInt(image.height / 4f);
                var horizontal = Mathf.RoundToInt(image.width / 4f);

                // Act
                reconciler.Reconcile(root, Array.Empty<VNode>(), new VNode[]
                {
                    V.Div(className: "slice-[25%]",
                        styles: new StyleOverrides { BackgroundImage = new StyleBackground(image) }),
                });

                // Assert
                Assert.That((vertical > 0 && horizontal > 0, Insets(root.ElementAt(0))),
                    Is.EqualTo((true, $"{vertical},{horizontal},{vertical},{horizontal}")));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(image);
            }
        }

        [Test]
        public void Given_APercentSliceOverNoImage_When_Mounted_Then_EveryEdgeIsZero()
        {
            // Arrange
            using var reconciler = new Reconciler();
            var root = new VisualElement();

            // Act
            reconciler.Reconcile(root, Array.Empty<VNode>(), new VNode[] { V.Div(className: "slice-[25%]") });

            // Assert
            Assert.That(Insets(root.ElementAt(0)), Is.EqualTo("0,0,0,0"));
        }

        private static string Insets(VisualElement element)
        {
            var style = element.style;
            return string.Join(",", Read(style.unitySliceTop), Read(style.unitySliceRight),
                Read(style.unitySliceBottom), Read(style.unitySliceLeft));
        }

        private static string Read(StyleInt inset)
            => inset.keyword == StyleKeyword.Null ? "null" : inset.value.ToString(CultureInfo.InvariantCulture);
    }
}

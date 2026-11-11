using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the sizes a line box holds as measured: the one it recorded, until another text is shown or the box is
    /// reset. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class TextLineBoxMeasureRecordTests
    {
        private static readonly MethodInfo s_record =
            typeof(TextLineBox).GetMethod("Record", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static readonly MethodInfo s_isMeasuredSize =
            typeof(TextLineBox).GetMethod("IsMeasuredSize", BindingFlags.Instance | BindingFlags.NonPublic)!;

        private static TextLineBox NewBox() => new(new Label(), centred: false);

        private static void Record(TextLineBox box, float width, float height)
            => s_record.Invoke(box, new object[] { width, height });

        private static bool IsMeasured(TextLineBox box, float width, float height)
            => (bool)s_isMeasuredSize.Invoke(box, new object[] { new Rect(0f, 0f, width, height) });

        // GREEN_ON_BASE(characterization): the base holds a recorded size as measured.
        [Test]
        public void Given_ASizeRecorded_When_TheBoxIsAskedAboutThatSize_Then_ItIsMeasured()
        {
            // Arrange
            var box = NewBox();
            Record(box, 120f, 18f);

            // Act
            var measured = IsMeasured(box, 120f, 18f);

            // Assert
            Assert.That(measured, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base holds no size as measured before it records one.
        [Test]
        public void Given_NoSizeRecorded_When_TheBoxIsAskedAboutASize_Then_ItIsNotMeasured()
        {
            // Act
            var measured = IsMeasured(NewBox(), 120f, 18f);

            // Assert
            Assert.That(measured, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base forgets its recorded sizes when another text is shown.
        [Test]
        public void Given_ASizeRecorded_When_AnotherTextIsShown_Then_ItIsNoLongerMeasured()
        {
            // Arrange
            var box = NewBox();
            Record(box, 120f, 18f);
            var recorded = IsMeasured(box, 120f, 18f);

            // Act
            box.Shown(null!, "another", tagged: false, ownMarkup: false);

            // Assert — null where the size was never held, which false cannot equal.
            Assert.That(recorded ? IsMeasured(box, 120f, 18f) : (bool?)null, Is.EqualTo(false));
        }

        // GREEN_ON_BASE(characterization): the base forgets its recorded sizes when the box is reset for the pool.
        [Test]
        public void Given_ASizeRecorded_When_TheBoxIsReset_Then_ItIsNoLongerMeasured()
        {
            // Arrange
            var box = NewBox();
            Record(box, 120f, 18f);
            var recorded = IsMeasured(box, 120f, 18f);

            // Act
            box.Reset();

            // Assert — null where the size was never held, which false cannot equal.
            Assert.That(recorded ? IsMeasured(box, 120f, 18f) : (bool?)null, Is.EqualTo(false));
        }
    }
}

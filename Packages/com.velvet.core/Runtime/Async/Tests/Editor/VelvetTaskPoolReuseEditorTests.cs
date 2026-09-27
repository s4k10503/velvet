using System;
using System.Reflection;
using NUnit.Framework;

#if UNITY_EDITOR
using static Velvet.TestUtilities.VelvetTaskFrameDriverTestExtensions;
#endif

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class VelvetTaskPoolReuseEditorTests
    {
        static readonly FieldInfo VoidTaskSourceField =
            typeof(VelvetTask).GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic)!;

        static readonly FieldInfo IntTaskSourceField =
            typeof(VelvetTask<int>).GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic)!;

#if UNITY_EDITOR
        [TearDown]
        public void TearDown() => DrainEditorUpdateForTest();
#endif

        static async VelvetTask<int> YieldThenReturn()
        {
            await VelvetTask.Yield();
            return 1;
        }

        // GREEN_ON_BASE(characterization): the base already pools a consumed source on the main thread.
        // Moving the pool behind a thread check must not stop that.
        [Test]
        public void Given_AConsumedFaultedTask_When_TheMainThreadFaultsAnother_Then_ItReusesTheSource()
        {
            // Arrange
            var first = VelvetTask.FromException(new InvalidOperationException("first"));
            var firstSource = VoidTaskSourceField.GetValue(first);
            try
            {
                first.GetAwaiter().GetResult();
            }
            catch (InvalidOperationException)
            {
            }

            // Act
            var second = VelvetTask.FromException(new InvalidOperationException("second"));

            // Assert
            Assert.That(VoidTaskSourceField.GetValue(second), Is.SameAs(firstSource));
        }

        // GREEN_ON_BASE(characterization): the base already pools a consumed value-carrying source.
        // Moving the pool behind a thread check must not stop that on the main thread.
        [Test]
        public void Given_AConsumedFaultedValueTask_When_TheMainThreadFaultsAnother_Then_ItReusesTheSource()
        {
            // Arrange
            var first = VelvetTask.FromException<int>(new InvalidOperationException("first"));
            var firstSource = IntTaskSourceField.GetValue(first);
            try
            {
                first.GetAwaiter().GetResult();
            }
            catch (InvalidOperationException)
            {
            }

            // Act
            var second = VelvetTask.FromException<int>(new InvalidOperationException("second"));

            // Assert
            Assert.That(IntTaskSourceField.GetValue(second), Is.SameAs(firstSource));
        }

        // GREEN_ON_BASE(characterization): the base already pools a consumed state-machine runner.
        // Moving the pool behind a thread check must not stop that on the main thread.
        [Test]
        public void Given_AConsumedSuspendedAsyncMethod_When_TheMainThreadSuspendsItAgain_Then_ItReusesTheRunner()
        {
            // Arrange
            var first = YieldThenReturn();
            var firstRunner = IntTaskSourceField.GetValue(first);
#if UNITY_EDITOR
            DrainEditorUpdateForTest();
#endif
            first.GetAwaiter().GetResult();

            // Act
            var second = YieldThenReturn();

            // Assert
            Assert.That(IntTaskSourceField.GetValue(second), Is.SameAs(firstRunner));
        }
    }
}

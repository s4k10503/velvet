using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Velvet.Tests
{
    internal sealed class VelvetTaskToCoroutinePlayModeTests
    {
        [UnityTest]
        public IEnumerator Given_PendingVelvetTask_When_DrivenByToCoroutine_Then_CompletesEnumerator()
        {
            // Arrange
            var source = new VelvetTaskCompletionSource();
            var enumerator = source.Task.ToCoroutine();

            // Act
            Assume.That(enumerator.MoveNext(), Is.True);
            source.SetResult();
            while (enumerator.MoveNext())
            {
            }

            // Assert
            Assert.That(enumerator.MoveNext(), Is.False);
            yield return null;
        }
    }
}

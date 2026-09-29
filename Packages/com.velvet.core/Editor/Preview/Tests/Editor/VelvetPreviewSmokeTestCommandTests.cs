using System;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Velvet.Editor.Preview;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    internal sealed class VelvetPreviewSmokeTestCommandTests
    {
        private static VNode Passes() => V.Div();

        private static VNode Fails() => V.Div();

        // Built rather than run, so no story of this assembly mounts through the setups other fixtures here declare.
        private static VelvetPreviewSmokeResult Result(string method, string failure) =>
            (VelvetPreviewSmokeResult)Activator.CreateInstance(
                typeof(VelvetPreviewSmokeResult), BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object[]
                {
                    PreviewStoryTestFactory.Build(
                        typeof(VelvetPreviewSmokeTestCommandTests).GetMethod(
                            method, BindingFlags.Static | BindingFlags.NonPublic),
                        new VelvetPreviewAttribute { Name = method, Group = "SmokeCommandFixture" }),
                    failure,
                },
                null);

        [Test]
        public void Given_OnePassingAndOneFailingStory_When_Reported_Then_TheFailureIsLoggedAndCounted()
        {
            // Arrange
            var results = new[] { Result(nameof(Passes), null), Result(nameof(Fails), "story boom") };
            LogAssert.Expect(LogType.Error, new Regex("'SmokeCommandFixture/Fails' failed: story boom"));

            // Act
            var failed = VelvetPreviewSmokeTestCommand.Report(results);

            // Assert
            Assert.That(failed, Is.EqualTo(1));
        }
    }
}

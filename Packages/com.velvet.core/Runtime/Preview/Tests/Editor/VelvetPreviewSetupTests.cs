using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    internal sealed class VelvetPreviewSetupTests
    {
        private static int s_setupRuns;
        private static int s_teardownRuns;

        internal sealed class Args { public string Text = "a"; }

        [VelvetPreview(Name = "SetupArgs", Group = "SetupFixture")]
        private static VNode ArgsStory(Args args) => V.Label(text: args.Text);

        [VelvetPreviewSetup]
        private static IDisposable Setup()
        {
            s_setupRuns++;
            return new Teardown();
        }

        [SetUp]
        public void ResetCounters()
        {
            s_setupRuns = 0;
            s_teardownRuns = 0;
        }

        private static VelvetPreviewStory ArgsStoryHandle()
        {
            var method = typeof(VelvetPreviewSetupTests).GetMethod(
                nameof(ArgsStory), BindingFlags.Static | BindingFlags.NonPublic);
            var attribute = new VelvetPreviewAttribute { Name = "SetupArgs", Group = "SetupFixture" };
            var ctor = typeof(VelvetPreviewStory).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(MethodInfo), typeof(VelvetPreviewAttribute) }, null);
            return (VelvetPreviewStory)ctor.Invoke(new object[] { method, attribute });
        }

        [Test]
        public void Given_AnAssemblyWithSetup_When_RunSetupFor_Then_TheSetupRuns()
        {
            // Arrange
            var assembly = typeof(VelvetPreviewSetupTests).Assembly;

            // Act
            using var environment = VelvetPreviewRegistry.RunSetupFor(assembly);

            // Assert
            Assert.That(s_setupRuns, Is.EqualTo(1));
        }

        [Test]
        public void Given_AnOpenedEnvironment_When_Disposed_Then_TheTeardownRuns()
        {
            // Arrange
            var environment = VelvetPreviewRegistry.RunSetupFor(typeof(VelvetPreviewSetupTests).Assembly);
            Assume.That(environment, Is.Not.Null, "the fixture's assembly declares a setup");

            // Act
            environment.Dispose();

            // Assert
            Assert.That(s_teardownRuns, Is.EqualTo(1));
        }

        [Test]
        public void Given_AMountedArgsStory_When_ArgsUpdated_Then_TheEnvironmentIsNotReRun()
        {
            // Arrange
            TestGraphics.IgnoreIfHeadless("an EditorWindow panel");
            var window = ScriptableObject.CreateInstance<SetupHostWindow>();
            window.Show();
            try
            {
                using var host = new VelvetPreviewHost(window.rootVisualElement);
                host.Mount(ArgsStoryHandle(), new Args { Text = "a" });
                Assume.That(s_setupRuns, Is.EqualTo(1), "Precondition: the mount ran the environment once");

                // Act
                host.UpdateArgs(new Args { Text = "b" });

                // Assert
                Assert.That(s_setupRuns, Is.EqualTo(1));
            }
            finally
            {
                window.Close();
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        private sealed class Teardown : IDisposable
        {
            public void Dispose() => s_teardownRuns++;
        }

        private sealed class SetupHostWindow : EditorWindow { }
    }
}

using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Environment contract for <see cref="VelvetPreviewRegistry.RunSetupFor"/>: the assembly's
    /// <c>[VelvetPreviewSetup]</c> runs when previewing starts and its teardown runs symmetrically on dispose, so
    /// previewing leaves no global state behind. Also covers that a controls-driven args update re-renders the
    /// tree WITHOUT re-running the environment (no per-keystroke font/store/CTS rebuild).
    /// </summary>
    internal sealed class VelvetPreviewSetupTests
    {
        // Process-static so the setup method (which must be static) can record that it ran / tore down.
        private static int s_setupRuns;
        private static int s_teardownRuns;

        // An args-story declared in THIS assembly so a host mount runs the setup above; UpdateArgs must not.
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
            // Arrange — a real panel and an args-story mounted once (the setup runs exactly once).
            TestGraphics.IgnoreIfHeadless("an EditorWindow panel");
            var window = ScriptableObject.CreateInstance<SetupHostWindow>();
            window.Show();
            try
            {
                using var host = new VelvetPreviewHost(window.rootVisualElement);
                host.Mount(ArgsStoryHandle(), new Args { Text = "a" });
                Assume.That(s_setupRuns, Is.EqualTo(1), "Precondition: the mount ran the environment once");

                // Act — a controls edit updates the args (re-renders the tree only).
                host.UpdateArgs(new Args { Text = "b" });

                // Assert — the environment was NOT torn down and rebuilt (fonts/store/CTS stay open per keystroke).
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

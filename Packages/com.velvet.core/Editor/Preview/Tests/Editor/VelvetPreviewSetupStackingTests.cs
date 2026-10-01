using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    // RunSetupFor over this assembly runs the three setups below; nothing else in it declares one.
    internal sealed class VelvetPreviewSetupStackingTests
    {
        private static readonly List<string> s_log = new();
        private static bool s_secondTeardownThrows;
        private static bool s_publishSheets;
        private static StyleSheet s_firstSheet;
        private static StyleSheet s_secondSheet;

        // Declared ahead of SetupA so that declaration order and name order disagree.
        [VelvetPreviewSetup]
        private static IDisposable SetupB()
        {
            s_log.Add("setup B");
            if (s_publishSheets) VelvetStyleHints.PreviewStyleSheet = s_secondSheet;
            return new SecondTeardown();
        }

        [VelvetPreviewSetup]
        private static Action SetupA()
        {
            s_log.Add("setup A");
            if (s_publishSheets) VelvetStyleHints.PreviewStyleSheet = s_firstSheet;
            return () => s_log.Add("teardown A");
        }

        private static VNode Story() => V.Div();

        // A nested type's full name extends its outer type's, so type-then-name order runs it after SetupB.
        private static class Later
        {
            [VelvetPreviewSetup]
            private static void Setup() => s_log.Add("setup N");
        }

        [SetUp]
        public void SetUp()
        {
            s_log.Clear();
            s_secondTeardownThrows = false;
            s_publishSheets = true;
            VelvetStyleHints.PreviewStyleSheet = null;
            s_firstSheet = ScriptableObject.CreateInstance<StyleSheet>();
            s_secondSheet = ScriptableObject.CreateInstance<StyleSheet>();
        }

        [TearDown]
        public void TearDown()
        {
            // Reset here as well as in SetUp, so no later fixture in this assembly mounts through a teardown that
            // throws or a setup that publishes this fixture's sheets.
            s_secondTeardownThrows = false;
            s_publishSheets = false;
            VelvetStyleHints.PreviewStyleSheet = null;
            UnityEngine.Object.DestroyImmediate(s_firstSheet);
            UnityEngine.Object.DestroyImmediate(s_secondSheet);
        }

        private static Assembly ThisAssembly => typeof(VelvetPreviewSetupStackingTests).Assembly;

        private static VelvetPreviewStory StoryHandle()
        {
            var method = typeof(VelvetPreviewSetupStackingTests).GetMethod(
                nameof(Story), BindingFlags.Static | BindingFlags.NonPublic);
            var attribute = new VelvetPreviewAttribute { Name = "Stacked", Group = "SetupStackingFixture" };
            var ctor = typeof(VelvetPreviewStory).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                new[] { typeof(MethodInfo), typeof(VelvetPreviewAttribute) }, null);
            Assume.That(ctor, Is.Not.Null, "VelvetPreviewStory's internal constructor must exist");
            return (VelvetPreviewStory)ctor.Invoke(new object[] { method, attribute });
        }

        [Test]
        public void Given_TwoSetupsInOneAssembly_When_RunSetupFor_Then_BothRunInNameOrder()
        {
            // Act
            var environment = VelvetPreviewRegistry.RunSetupFor(ThisAssembly);
            var ran = string.Join(", ", s_log);
            environment?.Dispose();

            // Assert
            Assert.That(ran, Is.EqualTo("setup A, setup B, setup N"));
        }

        [Test]
        public void Given_TwoOpenedSetups_When_Disposed_Then_TheirTeardownsRunInReverseOrder()
        {
            // Arrange
            var environment = VelvetPreviewRegistry.RunSetupFor(ThisAssembly);
            s_log.Clear();

            // Act
            environment?.Dispose();

            // Assert
            Assert.That(string.Join(", ", s_log), Is.EqualTo("teardown B, teardown A"));
        }

        [Test]
        public void Given_ADisposedEnvironment_When_DisposedAgain_Then_NoTeardownRunsTwice()
        {
            // Arrange
            var environment = VelvetPreviewRegistry.RunSetupFor(ThisAssembly);
            environment?.Dispose();
            s_log.Clear();

            // Act
            environment?.Dispose();

            // Assert
            Assert.That(string.Join(", ", s_log), Is.Empty);
        }

        [Test]
        public void Given_ALaterTeardownThatThrows_When_Disposed_Then_TheEarlierTeardownStillRuns()
        {
            // Arrange
            s_secondTeardownThrows = true;
            var environment = VelvetPreviewRegistry.RunSetupFor(ThisAssembly);
            LogAssert.Expect(LogType.Exception, new Regex("second teardown"));

            // Act
            try
            {
                environment?.Dispose();
            }
            catch (InvalidOperationException)
            {
                // Swallowed so a teardown that lets the throw escape still reaches the assertion.
            }

            // Assert
            Assert.That(
                (s_log.Contains("teardown B"), s_log.Contains("teardown A")),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_TwoSetupsEachPublishingASheet_When_RunSetupForIsCalledDirectly_Then_ItsEnvironmentCarriesBothInSetupOrder()
        {
            // Act
            var environment = VelvetPreviewRegistry.RunSetupFor(ThisAssembly);
            environment?.Dispose();

            // Assert
            Assert.That(
                environment?.StyleSheets,
                Is.EqualTo(new[] { s_firstSheet, s_secondSheet }));
        }

        [Test]
        public void Given_TwoSetupsEachPublishingASheet_When_AStoryMounts_Then_BothSheetsAreOnTheTarget()
        {
            // Arrange
            var target = new VisualElement();
            using var host = new VelvetPreviewHost(target);

            // Act
            host.Mount(StoryHandle());

            // Assert
            Assert.That(
                (target.styleSheets.Contains(s_firstSheet), target.styleSheets.Contains(s_secondSheet)),
                Is.EqualTo((true, true)));
        }

        // GREEN_ON_BASE(characterization): the base removes the one sheet it layers on disposal.
        // The branch layers two and has to remove both.
        [Test]
        public void Given_TwoLayeredSheets_When_HostDisposed_Then_NeitherIsLeftOnTheTarget()
        {
            // Arrange
            var target = new VisualElement();
            var host = new VelvetPreviewHost(target);
            host.Mount(StoryHandle());

            // Act
            host.Dispose();

            // Assert
            Assert.That(
                (target.styleSheets.Contains(s_firstSheet), target.styleSheets.Contains(s_secondSheet)),
                Is.EqualTo((false, false)));
        }

        // GREEN_ON_BASE(characterization): the base forgets a sheet once it has removed it.
        // The branch's list has to as well, or disposing takes a sheet the target was given later.
        [Test]
        public void Given_ASheetReaddedToTheTargetAfterARemount_When_HostDisposed_Then_TheSheetStays()
        {
            // Arrange
            var target = new VisualElement();
            using var host = new VelvetPreviewHost(target);
            host.Mount(StoryHandle());
            s_publishSheets = false;
            host.Mount(StoryHandle());
            target.styleSheets.Add(s_firstSheet);

            // Act
            host.Dispose();

            // Assert
            Assert.That(target.styleSheets.Contains(s_firstSheet), Is.True);
        }

        private sealed class SecondTeardown : IDisposable
        {
            public void Dispose()
            {
                s_log.Add("teardown B");
                if (s_secondTeardownThrows) throw new InvalidOperationException("second teardown");
            }
        }
    }
}

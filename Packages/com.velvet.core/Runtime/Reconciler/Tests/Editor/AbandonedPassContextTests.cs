using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a component that rendered inline ahead of a suspend nothing catches reads when it renders alone
    /// before the retry: the pass that was given up takes back the node it was handed, so that render finds the
    /// Provider above it in the tree its parent last committed.
    /// </summary>
    [TestFixture]
    internal sealed class AbandonedPassContextTests
    {
        private static readonly ComponentContext<string> s_context = ComponentContext<string>.Create("default");
        private static StateUpdater<bool> s_setShowReader;
        private static StateUpdater<int> s_setOwn;
        private static VelvetTaskCompletionSource<int> s_source;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_setShowReader = default;
            s_setOwn = default;
            s_source = new VelvetTaskCompletionSource<int>();
        }

        // GREEN_ON_BASE(characterization): the base already restores the node; deleting `fiber.SourceNode = SourceNode;` from FiberInputs.WriteTo reddens it.
        [Test]
        public void Given_AComponentRenderedAheadOfASuspendNothingCaught_When_ItRendersAloneBeforeTheResolve_Then_ItReadsTheProvidersValue()
        {
            // Arrange
            LogAssert.Expect(LogType.Warning, new Regex("no Suspense boundary"));
            using var mounted = V.Mount(_root, V.Component(OwnerRender, key: "owner"));
            s_setShowReader.Invoke(true);
            mounted.FlushStateForTest();

            // Act — the consumer's own update, with the resource still pending
            s_setOwn.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.DisplayedLabelTexts("|"), Is.EqualTo("provided:1"));
        }

        [Component(Compiler = false)]
        private static VNode OwnerRender()
        {
            var (showReader, setShowReader) = Hooks.UseState(false);
            s_setShowReader = setShowReader;
            return V.Provider(s_context, "provided", new VNode[]
            {
                V.Component(ConsumerRender, key: "consumer"),
                showReader ? V.Component(ReaderRender, key: "reader") : null,
            });
        }

        [Component(Compiler = false)]
        private static VNode ConsumerRender()
        {
            var (own, setOwn) = Hooks.UseState(0);
            s_setOwn = setOwn;
            return V.Label(text: Hooks.UseContext(s_context) + ":" + own);
        }

        [Component(Compiler = false)]
        private static VNode ReaderRender()
        {
            var value = Hooks.Use<int>(_ => s_source.Task, 1);
            return V.Label(text: "reader:" + value);
        }
    }
}

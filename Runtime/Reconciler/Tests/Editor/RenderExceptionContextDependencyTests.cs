using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins context-dependency retention across a failing render. The dependency list was cleared
    /// in place at the top of every render attempt and rebuilt by the UseContext calls that attempt
    /// reached — so a render that threw partway left the committed list empty or partial, and the
    /// Provider-change walk (which skips fibers without a recorded dependency) never re-rendered the
    /// consumer again: it was stuck on a stale context value forever, with no error. A memoized
    /// consumer has no other re-render path, so the reads of each attempt must be staged and swapped
    /// in only when the attempt settles, like the hook-slot machinery already does.
    /// </summary>
    [TestFixture]
    internal sealed class RenderExceptionContextDependencyTests
    {
        private static readonly ComponentContext<int> NumberContext = ComponentContext<int>.Create(0);
        private static readonly ComponentContext<string> LetterContext = ComponentContext<string>.Create("-");

        private readonly record struct ProviderState(int Number, string Letter);

        private sealed class ProviderStore : Store<ProviderState>
        {
            public ProviderStore() : base(new ProviderState(1, "a")) { }
            public void SetNumber(int n) => SetState(s => s with { Number = n });
            public void SetLetter(string l) => SetState(s => s with { Letter = l });
            protected override void ResetCore() => SetState(_ => new ProviderState(1, "a"));
        }

        private static ProviderStore s_store;
        private static bool s_throwOnce;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
            s_throwOnce = false;
        }

        // Memoized: with stable (absent) props, a parent-driven render bails, so a later re-render
        // can arrive only through the Provider-change walk — the exact gate under test. The one-shot
        // throw fires after the first UseContext but before the second, leaving the second context's
        // dependency to the staging discipline.
        [Component(Memoize = true)]
        private static VNode TwoContextConsumer()
        {
            var number = Hooks.UseContext(NumberContext);
            if (s_throwOnce)
            {
                s_throwOnce = false;
                throw new InvalidOperationException("boom");
            }
            var letter = Hooks.UseContext(LetterContext);
            return V.Label(name: "ctx-out", text: number + "-" + letter);
        }

        [Component]
        private static VNode ProviderHost()
        {
            var state = Hooks.UseStore(s_store, s => s);
            return V.Provider(NumberContext, state.Number, new VNode[]
            {
                V.Provider(LetterContext, state.Letter, new VNode[]
                {
                    V.Component(TwoContextConsumer, key: "consumer"),
                }),
            });
        }

        [Test]
        public void Given_ARenderThrewBeforeItsSecondContextRead_When_ThatContextChanges_Then_TheConsumerStillRerenders()
        {
            // Arrange — a successful mount records both context reads; a later render throws between
            // the first and the second read (no error boundary above).
            using var store = new ProviderStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(ProviderHost, key: "host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            Assume.That(_root.Q<Label>("ctx-out").text, Is.EqualTo("1-a"), "Precondition: both contexts render");
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("boom"));
            s_throwOnce = true;
            store.SetNumber(2);
            scheduler.DrainImmediateForTest();

            // Act — only the SECOND context's value changes afterwards.
            store.SetLetter("b");
            scheduler.DrainImmediateForTest();

            // Assert — the consumer still re-rendered for it (the failed render did not drop the
            // committed dependency), showing both current values.
            Assert.AreEqual("2-b", _root.Q<Label>("ctx-out").text);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Which of the members read here separates a token whose source was cancelled and then disposed
    /// from one whose source was only cancelled.
    /// <see cref="RouteTestStubs.ReadTokenState"/> answers <c>"released"</c> off that member alone, and
    /// the routing cases that tell a cancelled loader from one handed a released source rest on it. On a
    /// runtime where that member stopped separating the two, those cases would pass on a released
    /// source, and this is what fails there instead.
    /// </summary>
    [TestFixture]
    internal sealed class CancellationTokenReleaseTests
    {
        // GREEN_ON_BASE(characterization): the runtime already answers the way this case records.
        // What the branch adds is the routing cases that lean on the one member separating the two.
        [Test]
        public void Given_ACancelledSource_When_ItIsDisposedToo_Then_ItsWaitHandleIsTheOneProbedMemberAnsweringDifferently()
        {
            // Arrange
            var released = Probe(release: true);
            var cancelledOnly = Probe(release: false);

            // Act
            var answers = new List<string>();
            foreach (var member in released.Keys)
            {
                answers.Add(released[member] == cancelledOnly[member]
                    ? $"{member}=same"
                    : $"{member}={released[member]}/{cancelledOnly[member]}");
            }

            // Assert
            Assert.That(
                string.Join(" ", answers),
                Is.EqualTo(
                    "CanBeCanceled=same "
                    + "CreateLinkedTokenSource=same "
                    + "IsCancellationRequested=same "
                    + "Register=same "
                    + "RegisterCallbackRan=same "
                    + "RegisterWithState=same "
                    + "ThrowIfCancellationRequested=same "
                    + "WaitHandle=ObjectDisposedException/ok"),
                "A read of the wait handle is the only one of these that can tell a released source from "
                + "a live cancelled one, so it is the only one a case may discriminate on");
        }

        private static SortedDictionary<string, string> Probe(bool release)
        {
            var source = new CancellationTokenSource();
            var token = source.Token;
            source.Cancel();
            if (release)
            {
                source.Dispose();
            }

            var callbackRan = false;
            var register = Answer(() =>
            {
                token.Register(() => callbackRan = true);
                return "ok";
            });

            return new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["IsCancellationRequested"] = Answer(() => token.IsCancellationRequested.ToString()),
                ["CanBeCanceled"] = Answer(() => token.CanBeCanceled.ToString()),
                ["ThrowIfCancellationRequested"] = Answer(() =>
                {
                    token.ThrowIfCancellationRequested();
                    return "no-throw";
                }),
                ["Register"] = register,
                ["RegisterCallbackRan"] = callbackRan.ToString(),
                ["RegisterWithState"] = Answer(() =>
                {
                    token.Register(_ => { }, new object());
                    return "ok";
                }),
                ["CreateLinkedTokenSource"] = Answer(() =>
                {
                    using var linked = CancellationTokenSource.CreateLinkedTokenSource(token);
                    return "ok";
                }),
                ["WaitHandle"] = Answer(() =>
                {
                    _ = token.WaitHandle;
                    return "ok";
                }),
            };
        }

        private static string Answer(Func<string> read)
        {
            try
            {
                return read();
            }
            catch (Exception ex)
            {
                return ex.GetType().Name;
            }
        }
    }
}

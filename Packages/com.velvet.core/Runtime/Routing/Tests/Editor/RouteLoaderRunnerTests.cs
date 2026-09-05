using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Velvet;
using Velvet.TestUtilities;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class RouteLoaderRunnerTests
    {
        #region No loaders

        [Test]
        public void Given_MatchesWithoutLoaders_When_RunLoaders_Then_AllCompleted()
        {
            // Arrange
            var runner = new RouteLoaderRunner();

            // Act
            var allCompleted = runner.RunLoadersSync(MakeMatch("/"), CancellationToken.None).AllCompleted;

            // Assert
            Assert.That(allCompleted, Is.True);
        }

        [Test]
        public void Given_MatchesWithoutLoaders_When_RunLoaders_Then_ResultsAreEmpty()
        {
            // Arrange
            var runner = new RouteLoaderRunner();

            // Act
            var results = runner.RunLoadersSync(MakeMatch("/"), CancellationToken.None).Results;

            // Assert
            Assert.That(results, Is.Empty);
        }

        #endregion

        #region Await mode

        [Test]
        public void Given_CompletedAwaitLoader_When_RunLoaders_Then_AllCompleted()
        {
            // Arrange
            var runner = new RouteLoaderRunner();
            var matches = MakeMatch("test", loader: (ctx, ct) => VelvetTask.FromResult<object>("loaded-data"));

            // Act
            var allCompleted = runner.RunLoadersSync(matches, CancellationToken.None).AllCompleted;

            // Assert
            Assert.That(allCompleted, Is.True);
        }

        [Test]
        public void Given_CompletedAwaitLoader_When_RunLoaders_Then_ResultIsKeyedByRouteId()
        {
            // Arrange
            var runner = new RouteLoaderRunner();
            var matches = MakeMatch("test", loader: (ctx, ct) => VelvetTask.FromResult<object>("loaded-data"));

            // Act
            var results = runner.RunLoadersSync(matches, CancellationToken.None).Results;

            // Assert
            Assert.That(results["test"], Is.EqualTo("loaded-data"));
        }

        [Test]
        public void Given_AnUnresolvedAwaitLoader_When_RunLoaders_Then_TheRoundStaysOpen()
        {
            // Arrange
            var runner = new RouteLoaderRunner();
            var unresolved = new VelvetTaskCompletionSource<object>();
            var matches = MakeMatch("test", loader: (ctx, ct) => unresolved.Task);

            // Act
            var round = runner.RunLoadersAsync(matches, CancellationToken.None);

            // Assert
            Assert.That(round.Status, Is.EqualTo(VelvetTaskStatus.Pending),
                "An Await loader that has not resolved is one the round is still waiting on");
        }

        [UnityTest]
        public IEnumerator Given_AnUnresolvedAwaitLoader_When_ItsTaskResolves_Then_TheResultIsKeyedByRouteId()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var runner = new RouteLoaderRunner();
            var unresolved = new VelvetTaskCompletionSource<object>();
            var matches = MakeMatch("test", loader: (ctx, ct) => unresolved.Task);
            var round = runner.RunLoadersAsync(matches, CancellationToken.None);

            // Act
            unresolved.TrySetResult("late-data");
            var settled = await round;

            // Assert
            Assert.That(settled.Results.GetValueOrDefault("test"), Is.EqualTo("late-data"));
        });

        [UnityTest]
        public IEnumerator Given_AnUnresolvedAwaitLoader_When_ItsTaskFails_Then_TheRoundRecordsTheLoadersOwnError()
            => VelvetTask.ToCoroutine(async () =>
        {
            // The framework used to author the exception itself for a loader that had not finished, so the
            // route's error was one no application code raised.
            // Arrange
            var runner = new RouteLoaderRunner();
            var unresolved = new VelvetTaskCompletionSource<object>();
            var matches = MakeMatch("fail", loader: (ctx, ct) => unresolved.Task);
            var round = runner.RunLoadersAsync(matches, CancellationToken.None);

            // Act
            unresolved.TrySetException(new InvalidOperationException("late-failure"));
            var settled = await round;

            // Assert
            Assert.That(settled.Errors.GetValueOrDefault("fail")?.Message, Is.EqualTo("late-failure"));
        });

        #endregion

        #region Suspend mode

        [Test]
        public void Given_SuspendLoader_When_RunLoaders_Then_DoesNotReportAllCompleted()
        {
            // Arrange
            var runner = new RouteLoaderRunner();
            var tcs = new VelvetTaskCompletionSource<object>();
            var matches = MakeMatch("test", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend);

            // Act
            var allCompleted = runner.RunLoadersSync(matches, CancellationToken.None).AllCompleted;

            // Assert
            Assert.That(allCompleted, Is.False);
        }

        [UnityTest]
        public IEnumerator Given_SuspendLoader_When_TaskResolves_Then_FiresOnCompletedWithPathAndResult()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var runner = new RouteLoaderRunner();
            var tcs = new VelvetTaskCompletionSource<object>();
            string completedPath = null;
            object completedResult = null;
            runner.OnSuspendLoaderCompleted += (path, result) =>
            {
                completedPath = path;
                completedResult = result;
            };
            var matches = MakeMatch("test", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend);
            runner.Promote(runner.RunLoadersSync(matches, CancellationToken.None));
            Assume.That(completedPath, Is.Null, "Precondition: not completed before the task resolves");

            // Act
            tcs.TrySetResult("deferred-data");
            await VelvetTask.Yield();

            // Assert
            Assert.That((completedPath, completedResult), Is.EqualTo(("test", (object)"deferred-data")));
        });

        [UnityTest]
        public IEnumerator Given_SuspendLoader_When_TaskResolves_Then_ActiveTaskCountReturnsToZero()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var runner = new RouteLoaderRunner();
            var tcs = new VelvetTaskCompletionSource<object>();
            var matches = MakeMatch("test", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend);
            runner.RunLoadersSync(matches, CancellationToken.None);

            // Act
            tcs.TrySetResult("deferred-data");
            await VelvetTask.Yield();

            // Assert
            Assert.That(runner.ActiveSuspendTaskCount, Is.EqualTo(0), "The finally block decrements the live-task counter");
        });

        [UnityTest]
        public IEnumerator Given_SuspendLoader_When_TaskFails_Then_FiresOnFailedWithPathAndException()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var runner = new RouteLoaderRunner();
            var tcs = new VelvetTaskCompletionSource<object>();
            string failedPath = null;
            Exception failedException = null;
            runner.OnSuspendLoaderFailed += (path, ex) =>
            {
                failedPath = path;
                failedException = ex;
            };
            var matches = MakeMatch("fail", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend);
            runner.Promote(runner.RunLoadersSync(matches, CancellationToken.None));
            Assume.That(failedPath, Is.Null, "Precondition: not failed before the task throws");

            // Act
            tcs.TrySetException(new InvalidOperationException("deferred-failure"));
            await VelvetTask.Yield();

            // Assert
            Assert.That((failedPath, failedException?.Message), Is.EqualTo(("fail", "deferred-failure")));
        });

        [UnityTest]
        public IEnumerator Given_SuspendLoader_When_TaskFails_Then_RecordsErrorKeyedByRouteId()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var runner = new RouteLoaderRunner();
            var tcs = new VelvetTaskCompletionSource<object>();
            var matches = MakeMatch("fail", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend);
            var round = runner.RunLoadersSync(matches, CancellationToken.None);

            // Act
            tcs.TrySetException(new InvalidOperationException("deferred-failure"));
            await VelvetTask.Yield();

            // Assert
            Assert.That(round.Errors["fail"].Message, Does.Contain("deferred-failure"));
        });

        [UnityTest]
        public IEnumerator Given_SuspendLoader_When_TaskFails_Then_ActiveTaskCountReturnsToZero()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange
            var runner = new RouteLoaderRunner();
            var tcs = new VelvetTaskCompletionSource<object>();
            var matches = MakeMatch("fail", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend);
            runner.RunLoadersSync(matches, CancellationToken.None);

            // Act
            tcs.TrySetException(new InvalidOperationException("deferred-failure"));
            await VelvetTask.Yield();

            // Assert
            Assert.That(runner.ActiveSuspendTaskCount, Is.EqualTo(0), "The finally block decrements the counter even on failure");
        });

        [UnityTest]
        public IEnumerator Given_LiveSuspendLoader_When_RetiredAndLoaderHonorsToken_Then_ActiveTaskCountReturnsToZero()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Arrange — the loader honors the token: it cancels its task when the CancellationToken fires.
            var runner = new RouteLoaderRunner();
            var tcs = new VelvetTaskCompletionSource<object>();
            var matches = MakeMatch("honor-ct",
                loader: (ctx, ct) =>
                {
                    ct.Register(() => tcs.TrySetCanceled(ct));
                    return tcs.Task;
                },
                loaderMode: LoaderMode.Suspend);
            runner.Promote(runner.RunLoadersSync(matches, CancellationToken.None));
            var liveBefore = runner.ActiveSuspendTaskCount;

            // Act
            runner.Dispose();
            await VelvetTask.Yield();

            // Assert
            Assert.That($"live={liveBefore} after={runner.ActiveSuspendTaskCount}", Is.EqualTo("live=1 after=0"),
                "A token-honoring loader unwinds and the counter returns to zero");
        });

        [Test]
        public void Given_APromotedRoundAndANewerOne_When_TheRunnerIsDisposed_Then_BothTokensAreCancelled()
        {
            // Disposal has two rounds to end and only one of them is the current one: a round goes live at
            // its caller's commit and stays there while the next attempt starts a round of its own.
            // Arrange
            CancellationToken live = default;
            CancellationToken newer = default;
            var runner = new RouteLoaderRunner();
            runner.Promote(runner.RunLoadersSync(
                MakeMatch("live", loader: (ctx, ct) =>
                {
                    live = ct;
                    return VelvetTask.FromResult<object>("live-data");
                }),
                CancellationToken.None));
            runner.RunLoadersSync(
                MakeMatch("newer", loader: (ctx, ct) =>
                {
                    newer = ct;
                    return VelvetTask.FromResult<object>("newer-data");
                }),
                CancellationToken.None);

            // Act
            runner.Dispose();

            // Assert
            Assert.That($"live={live.IsCancellationRequested} newer={newer.IsCancellationRequested}",
                Is.EqualTo("live=True newer=True"));
        }

        [Test]
        public void Given_TheLiveRound_When_ItIsPromotedAgain_Then_ItsTokenSurvives()
        {
            // Promoting ends the round that held the live place, and the round handed in is the one that
            // takes it — so a promotion whose argument is already live has to end nothing.
            // Arrange
            CancellationToken captured = default;
            var runner = new RouteLoaderRunner();
            var live = runner.RunLoadersSync(
                MakeMatch("live", loader: (ctx, ct) =>
                {
                    captured = ct;
                    return VelvetTask.FromResult<object>("live-data");
                }),
                CancellationToken.None);
            runner.Promote(live);

            // Act
            runner.Promote(live);

            // Assert
            Assert.That(captured.IsCancellationRequested, Is.False,
                "The round holding the live place is not the round a promotion ends");
        }

        [UnityTest]
        public IEnumerator Given_APromotedSuspendLoaderSucceedingOnCancellation_When_ItsRoundIsRetired_Then_NoCompletionIsFired()
            => VelvetTask.ToCoroutine(async () =>
        {
            // A loader may answer its token by resolving a fallback rather than throwing, and that
            // continuation runs inside the retiring Cancel call itself. The round is promoted first, so a
            // zero firing count is not simply what an unpromoted round reports; the live-task count is folded
            // in because zero would otherwise also be satisfied by a loader that never ran at all.
            // Arrange
            var runner = new RouteLoaderRunner();
            var tcs = new VelvetTaskCompletionSource<object>();
            var fired = 0;
            runner.OnSuspendLoaderCompleted += (_, __) => fired++;
            var matches = MakeMatch("succeed-on-ct",
                loader: (ctx, ct) =>
                {
                    ct.Register(() => tcs.TrySetResult("fallback"));
                    return tcs.Task;
                },
                loaderMode: LoaderMode.Suspend);
            runner.Promote(runner.RunLoadersSync(matches, CancellationToken.None));
            var liveBefore = runner.ActiveSuspendTaskCount;

            // Act
            runner.Dispose();
            await VelvetTask.Yield();

            // Assert
            Assert.That($"live={liveBefore} fired={fired}", Is.EqualTo("live=1 fired=0"),
                "The round being torn down must not be read as the current one by its own late success");
        });

        [UnityTest]
        public IEnumerator Given_ASupersededRound_When_OneSuspendLoaderSucceedsAndAnotherFails_Then_BothOutcomesAreRecorded()
            => VelvetTask.ToCoroutine(async () =>
        {
            // Pending is counted off on both paths whatever the round's currency, so a round that records only
            // one of the two reports itself settled while holding neither a result nor an error for the route
            // the other path owns.
            // Arrange
            var runner = new RouteLoaderRunner();
            var succeeding = new VelvetTaskCompletionSource<object>();
            var failing = new VelvetTaskCompletionSource<object>();
            var matches = new List<RouteMatch>();
            matches.AddRange(MakeMatch("succeeds", loader: (ctx, ct) => succeeding.Task,
                loaderMode: LoaderMode.Suspend));
            matches.AddRange(MakeMatch("fails", loader: (ctx, ct) => failing.Task,
                loaderMode: LoaderMode.Suspend));
            var round = runner.RunLoadersSync(matches, CancellationToken.None);
            runner.RunLoadersSync(MakeMatch("plain"), CancellationToken.None);

            // Act
            succeeding.TrySetResult("late-data");
            failing.TrySetException(new InvalidOperationException("late-failure"));
            await VelvetTask.Yield();

            // Assert
            Assert.That(
                $"results={string.Join(",", round.Results.Keys)} errors={string.Join(",", round.Errors.Keys)}",
                Is.EqualTo("results=succeeds errors=fails"),
                "A superseded round records what its loaders did on both paths alike");
        });

        #endregion

        #region Announcement failures

        [UnityTest]
        public IEnumerator Given_ASuspendLoaderThatSucceeded_When_TheCompletionSubscriberThrows_Then_TheRoundStillSettles()
            => VelvetTask.ToCoroutine(async () =>
        {
            // A round holding nothing for the route would settle too, so the result is folded in.
            // Arrange
            var runner = new RouteLoaderRunner();
            var tcs = new VelvetTaskCompletionSource<object>();
            runner.OnSuspendLoaderCompleted += (_, __) => throw new InvalidOperationException("handler-threw");
            var matches = MakeMatch("test", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend);
            var round = runner.RunLoadersSync(matches, CancellationToken.None);
            runner.Promote(round);
            ContainedFailureLog.Expect<InvalidOperationException>(nameof(RouteLoaderRunner), "handler-threw");

            // Act
            tcs.TrySetResult("deferred-data");
            await VelvetTask.Yield();

            // Assert
            Assert.That($"settled={round.Settled} results=[{string.Join("|", round.Results.Keys)}]",
                Is.EqualTo("settled=True results=[test]"),
                "A subscriber's failure must not count this round's pending loader off a second time");
        });

        [UnityTest]
        public IEnumerator Given_ASuspendLoaderThatSucceeded_When_TheCompletionSubscriberThrows_Then_NoLoadErrorIsRecorded()
            => VelvetTask.ToCoroutine(async () =>
        {
            // An empty error map is also what a round that recorded nothing at all holds, so the result is
            // folded in.
            // Arrange
            var runner = new RouteLoaderRunner();
            var tcs = new VelvetTaskCompletionSource<object>();
            runner.OnSuspendLoaderCompleted += (_, __) => throw new InvalidOperationException("handler-threw");
            var matches = MakeMatch("test", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend);
            var round = runner.RunLoadersSync(matches, CancellationToken.None);
            runner.Promote(round);
            ContainedFailureLog.Expect<InvalidOperationException>(nameof(RouteLoaderRunner), "handler-threw");

            // Act
            tcs.TrySetResult("deferred-data");
            await VelvetTask.Yield();

            // Assert
            Assert.That(
                $"errors=[{string.Join("|", round.Errors.Keys)}] results=[{string.Join("|", round.Results.Keys)}]",
                Is.EqualTo("errors=[] results=[test]"),
                "What failed is the subscriber, so a caller reading this route's loader error would be reading a load failure that did not happen");
        });

        [UnityTest]
        public IEnumerator Given_ASuspendLoaderThatSucceeded_When_TheCompletionSubscriberThrowsACancellation_Then_TheRunnerStillSettlesAndReportsIt()
            => VelvetTask.ToCoroutine(async () =>
        {
            // A cancellation is the spelling the await's other catch clause takes, so a containment written
            // against the general one alone leaves this round short a pending count and its report to
            // whatever observes a forgotten task.
            // Arrange
            var runner = new RouteLoaderRunner();
            var tcs = new VelvetTaskCompletionSource<object>();
            runner.OnSuspendLoaderCompleted += (_, __) => throw new OperationCanceledException("handler-cancelled");
            var matches = MakeMatch("test", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend);
            var round = runner.RunLoadersSync(matches, CancellationToken.None);
            runner.Promote(round);
            ContainedFailureLog.Expect<OperationCanceledException>(nameof(RouteLoaderRunner), "handler-cancelled");
            using var reports = new ContainedReportProbe();

            // Act
            tcs.TrySetResult("deferred-data");
            await VelvetTask.Yield();

            // Assert
            Assert.That($"settled={round.Settled} reports={reports.Count}", Is.EqualTo("settled=True reports=1"),
                "A cancellation raised by a subscriber is that subscriber's failure, not this round's cancellation");
        });

        [UnityTest]
        public IEnumerator Given_ASuspendLoaderThatFailed_When_TheFailureSubscriberThrows_Then_TheRunnerReportsItAndKeepsTheLoaderError()
            => VelvetTask.ToCoroutine(async () =>
        {
            // The sibling announcement. The round's accounting is finished before it runs, so what this pins
            // is that a subscriber's throw is reported by the runner rather than left to whatever observes
            // the forgotten task.
            // Arrange
            var runner = new RouteLoaderRunner();
            var tcs = new VelvetTaskCompletionSource<object>();
            runner.OnSuspendLoaderFailed += (_, __) => throw new InvalidOperationException("handler-threw");
            var matches = MakeMatch("fail", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend);
            var round = runner.RunLoadersSync(matches, CancellationToken.None);
            runner.Promote(round);
            ContainedFailureLog.Expect<InvalidOperationException>(nameof(RouteLoaderRunner), "handler-threw");
            using var reports = new ContainedReportProbe();

            // Act
            tcs.TrySetException(new InvalidOperationException("deferred-failure"));
            await VelvetTask.Yield();

            // Assert
            Assert.That($"reports={reports.Count} errors=[{string.Join("|", round.Errors.Keys)}]",
                Is.EqualTo("reports=1 errors=[fail]"),
                "The loader's own failure is still the route's, and the subscriber's is the runner's to report");
        });

        // A report the runner made carries its tag; one published by whatever observes a task the runner
        // forgot does not, and that is what these cases read the two apart by.
        private sealed class ContainedReportProbe : IDisposable
        {
            private int _count;

            internal ContainedReportProbe() => Application.logMessageReceived += Capture;

            internal int Count => _count;

            private void Capture(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Error && condition.Contains(nameof(RouteLoaderRunner))) _count++;
            }

            public void Dispose() => Application.logMessageReceived -= Capture;
        }

        #endregion

        #region Cancellation

        [Test]
        public void Given_RunningLoader_When_RunLoaders_Then_LoaderTokenCanBeCanceled()
        {
            // Arrange
            CancellationToken capturedToken = default;
            var runner = new RouteLoaderRunner();
            var matches = MakeMatch("test", loader: (ctx, ct) =>
            {
                capturedToken = ct;
                return VelvetTask.FromResult<object>("data");
            });

            // Act
            runner.RunLoadersSync(matches, CancellationToken.None);

            // Assert
            Assert.That(capturedToken.CanBeCanceled, Is.True);
        }

        [Test]
        public void Given_CapturedLoaderToken_When_TheRunnerIsDisposed_Then_TokenIsCancelled()
        {
            // Arrange
            CancellationToken capturedToken = default;
            var runner = new RouteLoaderRunner();
            var matches = MakeMatch("test", loader: (ctx, ct) =>
            {
                capturedToken = ct;
                return VelvetTask.FromResult<object>("data");
            });
            runner.Promote(runner.RunLoadersSync(matches, CancellationToken.None));
            Assume.That(capturedToken.CanBeCanceled, Is.True, "Precondition: the loader received a cancelable token");

            // Act
            runner.Dispose();

            // Assert
            Assert.That(capturedToken.IsCancellationRequested, Is.True);
        }

        // GREEN_ON_BASE(characterization): the base already releases a superseded round's source.
        // This branch moved that source from one runner-wide field to a field per round, and the
        // release has to survive the move.
        [Test]
        public void Given_ALoaderThatStartsAnotherRound_When_TheOuterRoundsNextLoaderGetsItsToken_Then_TheSourceBehindItIsReleased()
        {
            // Retiring a round releases its source as well as cancelling it, and the launch loop goes on
            // handing that round's token to the loaders below the one that retired it. The release is what
            // this case is about; the cancellation on the same token is pinned by a case of its own.
            // Arrange
            var runner = new RouteLoaderRunner();
            CancellationToken nextLoadersToken = default;
            var matches = new List<RouteMatch>();
            matches.AddRange(MakeMatch("nesting", loader: (ctx, ct) =>
            {
                runner.RunLoadersSync(MakeMatch("nested"), CancellationToken.None);
                return VelvetTask.FromResult<object>("nesting-data");
            }));
            matches.AddRange(MakeMatch("next", loader: (ctx, ct) =>
            {
                nextLoadersToken = ct;
                return VelvetTask.FromResult<object>("next-data");
            }));

            // Act
            runner.RunLoadersSync(matches, CancellationToken.None);

            // Assert
            Assert.That(() => nextLoadersToken.WaitHandle, Throws.TypeOf<ObjectDisposedException>(),
                "A retired round's source is released, not left open behind the token its loaders hold");
        }

        [Test]
        public void Given_ACancellationCallbackThatStartsARound_When_ItRetiresTheRoundBeingInstalled_Then_ThatRoundsLoaderLaunchesUnderTheRetiredToken()
        {
            // The outgoing round is cancelled from inside the installation of the round replacing it, which
            // runs whatever a Loader of that round registered on the token it was handed. A round that
            // callback starts retires the round being installed, before its Loaders have launched.
            // Arrange
            var runner = new RouteLoaderRunner();
            bool? secondRoundsLoaderSawCancellation = null;
            runner.RunLoadersSync(
                MakeMatch("registering", loader: (ctx, ct) =>
                {
                    ct.Register(() => runner.RunLoadersSync(MakeMatch("from-callback"), CancellationToken.None));
                    return VelvetTask.FromResult<object>("registering-data");
                }),
                CancellationToken.None);

            // Act
            runner.RunLoadersSync(
                MakeMatch("second", loader: (ctx, ct) =>
                {
                    secondRoundsLoaderSawCancellation = ct.IsCancellationRequested;
                    return VelvetTask.FromResult<object>("second-data");
                }),
                CancellationToken.None);

            // Assert
            Assert.That(secondRoundsLoaderSawCancellation, Is.True,
                "A round retired mid-installation still reaches its Loaders, under the token its own source issued");
        }

        #endregion

        #region Error handling

        [Test]
        public void Given_ThrowingAwaitLoader_When_RunLoaders_Then_DoesNotReportAllCompleted()
        {
            // Arrange
            var runner = new RouteLoaderRunner();
            var matches = MakeMatch("fail", loader: (ctx, ct) => throw new InvalidOperationException("loader failed"));

            // Act
            var allCompleted = runner.RunLoadersSync(matches, CancellationToken.None).AllCompleted;

            // Assert
            Assert.That(allCompleted, Is.False);
        }

        [Test]
        public void Given_ThrowingAwaitLoader_When_RunLoaders_Then_RecordsErrorKeyedByRouteId()
        {
            // Arrange
            var runner = new RouteLoaderRunner();
            var matches = MakeMatch("fail", loader: (ctx, ct) => throw new InvalidOperationException("loader failed"));

            // Act
            var round = runner.RunLoadersSync(matches, CancellationToken.None);

            // Assert
            Assert.That(round.Errors["fail"].Message, Does.Contain("loader failed"));
        }

        #endregion

        #region Round settlement

        [Test]
        public void Given_ARoundWithAnUnfinishedSuspendLoader_When_ALaterRoundHasNone_Then_EachRoundReportsItsOwn()
        {
            // The router asks a round whether it finished at the moment it commits the entry that round's
            // data went into, which can be after another round has started — a loader delegate that
            // navigates starts one before the round it belongs to has even finished launching. A round that
            // answered for whichever round is current would report an unfinished one as finished.
            // Arrange
            var runner = new RouteLoaderRunner();
            var unresolved = new VelvetTaskCompletionSource<object>();
            var firstRound = runner.RunLoadersSync(
                MakeMatch("slow", loader: (ctx, ct) => unresolved.Task, loaderMode: LoaderMode.Suspend),
                CancellationToken.None);

            // Act
            var secondRound = runner.RunLoadersSync(MakeMatch("plain"), CancellationToken.None);

            // Assert
            Assert.That($"first={firstRound.Settled} second={secondRound.Settled}",
                Is.EqualTo("first=False second=True"),
                "A round reports its own outstanding loaders, not those of whichever round is current");
        }

        [Test]
        public void Given_ALoaderThatStartsAnotherRound_When_TheOuterRoundsNextLoaderLaunches_Then_ItGetsTheOuterRoundsToken()
        {
            // The nested round cancels the outer one on its way in, so the token the outer round's remaining
            // loaders belong to is a cancelled one.
            // Arrange
            var runner = new RouteLoaderRunner();
            bool? nextLoaderSawCancellation = null;
            var matches = new List<RouteMatch>();
            matches.AddRange(MakeMatch("nesting", loader: (ctx, ct) =>
            {
                runner.RunLoadersSync(MakeMatch("nested"), CancellationToken.None);
                return VelvetTask.FromResult<object>("nesting-data");
            }));
            matches.AddRange(MakeMatch("next", loader: (ctx, ct) =>
            {
                nextLoaderSawCancellation = ct.IsCancellationRequested;
                return VelvetTask.FromResult<object>("next-data");
            }));

            // Act
            runner.RunLoadersSync(matches, CancellationToken.None);

            // Assert
            Assert.That(nextLoaderSawCancellation, Is.True,
                "A round's later loaders must launch under the token of the round they belong to");
        }

        [Test]
        public void Given_ALoaderThatStartsAnotherRound_When_TheOuterRoundFailsOnBothSidesOfIt_Then_BothErrorsAreTheOuterRounds()
        {
            // The nested round starts from inside the loop that is still launching the outer round's loaders,
            // so an error map belonging to the runner rather than to the round is cleared between the two
            // failures and then written by the second one.
            // Arrange
            var runner = new RouteLoaderRunner();
            var matches = new List<RouteMatch>();
            matches.AddRange(MakeMatch("early", loader: (ctx, ct) => throw new InvalidOperationException("early")));
            matches.AddRange(MakeMatch("nesting", loader: (ctx, ct) =>
            {
                runner.RunLoadersSync(MakeMatch("nested"), CancellationToken.None);
                return VelvetTask.FromResult<object>("nesting-data");
            }));
            matches.AddRange(MakeMatch("late", loader: (ctx, ct) => throw new InvalidOperationException("late")));

            // Act
            var round = runner.RunLoadersSync(matches, CancellationToken.None);

            // Assert
            Assert.That(string.Join(",", round.Errors.Keys.OrderBy(key => key, StringComparer.Ordinal)),
                Is.EqualTo("early,late"),
                "A round keeps every error its own loaders raised, on both sides of a nested round");
        }

        #endregion
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using Velvet;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the loader execution contract of <see cref="RouteLoaderRunner"/>.
    /// <list type="bullet">
    /// <item>Matches without loaders complete immediately with empty results.</item>
    /// <item>An Await loader must be already-completed; its result is collected synchronously keyed by
    /// <see cref="RouteMatch.RouteId"/>, and <c>allCompleted</c> is true.</item>
    /// <item>A Suspend loader returns immediately with <c>allCompleted</c> false and runs in the background,
    /// firing <c>OnSuspendLoaderCompleted</c> on success or <c>OnSuspendLoaderFailed</c> on failure; a failure
    /// is also recorded per-path in <c>Errors</c> symmetrically with the Await path.</item>
    /// <item><see cref="RouteLoaderRunner.ActiveSuspendTaskCount"/> is incremented while a Suspend task is live
    /// and returned to zero in the finally block on success, failure, and honored cancellation alike.</item>
    /// <item>The loader receives a cancelable token that <c>CancelPending</c> cancels, and a loader that
    /// answers that token by succeeding rather than throwing is still treated as a torn-down round.</item>
    /// <item>An Await loader that throws records the error in <c>Errors</c> and reports <c>allCompleted</c> false.</item>
    /// <item>Each round tracks its own outstanding Suspend loaders, so a round asked after a later one has
    /// started still answers for itself.</item>
    /// <item>A round's loaders all launch under that round's own token, so a loader that starts another round
    /// mid-run does not lend the next round's currency to the leftovers of the one it superseded.</item>
    /// </list>
    /// </summary>
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
            var (_, allCompleted) = runner.RunLoadersSync(MakeMatch("/"), CancellationToken.None);

            // Assert
            Assert.That(allCompleted, Is.True);
        }

        [Test]
        public void Given_MatchesWithoutLoaders_When_RunLoaders_Then_ResultsAreEmpty()
        {
            // Arrange
            var runner = new RouteLoaderRunner();

            // Act
            var (results, _) = runner.RunLoadersSync(MakeMatch("/"), CancellationToken.None);

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
            var matches = MakeMatch("test", loader: (ctx, ct) => UniTask.FromResult<object>("loaded-data"));

            // Act
            var (_, allCompleted) = runner.RunLoadersSync(matches, CancellationToken.None);

            // Assert
            Assert.That(allCompleted, Is.True);
        }

        [Test]
        public void Given_CompletedAwaitLoader_When_RunLoaders_Then_ResultIsKeyedByRouteId()
        {
            // Arrange
            var runner = new RouteLoaderRunner();
            var matches = MakeMatch("test", loader: (ctx, ct) => UniTask.FromResult<object>("loaded-data"));

            // Act
            var (results, _) = runner.RunLoadersSync(matches, CancellationToken.None);

            // Assert
            Assert.That(results["test"], Is.EqualTo("loaded-data"));
        }

        #endregion

        #region Suspend mode

        [Test]
        public void Given_SuspendLoader_When_RunLoaders_Then_DoesNotReportAllCompleted()
        {
            // Arrange
            var runner = new RouteLoaderRunner();
            var tcs = new UniTaskCompletionSource<object>();
            var matches = MakeMatch("test", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend);

            // Act
            var (_, allCompleted) = runner.RunLoadersSync(matches, CancellationToken.None);

            // Assert
            Assert.That(allCompleted, Is.False);
        }

        [UnityTest]
        public IEnumerator Given_SuspendLoader_When_TaskResolves_Then_FiresOnCompletedWithPathAndResult()
            => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var runner = new RouteLoaderRunner();
            var tcs = new UniTaskCompletionSource<object>();
            string completedPath = null;
            object completedResult = null;
            runner.OnSuspendLoaderCompleted += (path, result) =>
            {
                completedPath = path;
                completedResult = result;
            };
            var matches = MakeMatch("test", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend);
            runner.RunLoadersSync(matches, CancellationToken.None);
            Assume.That(completedPath, Is.Null, "Precondition: not completed before the task resolves");

            // Act
            tcs.TrySetResult("deferred-data");
            await UniTask.Yield();

            // Assert
            Assert.That((completedPath, completedResult), Is.EqualTo(("test", (object)"deferred-data")));
        });

        [UnityTest]
        public IEnumerator Given_SuspendLoader_When_TaskResolves_Then_ActiveTaskCountReturnsToZero()
            => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var runner = new RouteLoaderRunner();
            var tcs = new UniTaskCompletionSource<object>();
            var matches = MakeMatch("test", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend);
            runner.RunLoadersSync(matches, CancellationToken.None);

            // Act
            tcs.TrySetResult("deferred-data");
            await UniTask.Yield();

            // Assert
            Assert.That(runner.ActiveSuspendTaskCount, Is.EqualTo(0), "The finally block decrements the live-task counter");
        });

        [UnityTest]
        public IEnumerator Given_SuspendLoader_When_TaskFails_Then_FiresOnFailedWithPathAndException()
            => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var runner = new RouteLoaderRunner();
            var tcs = new UniTaskCompletionSource<object>();
            string failedPath = null;
            Exception failedException = null;
            runner.OnSuspendLoaderFailed += (path, ex) =>
            {
                failedPath = path;
                failedException = ex;
            };
            var matches = MakeMatch("fail", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend);
            runner.RunLoadersSync(matches, CancellationToken.None);
            Assume.That(failedPath, Is.Null, "Precondition: not failed before the task throws");

            // Act
            tcs.TrySetException(new InvalidOperationException("deferred-failure"));
            await UniTask.Yield();

            // Assert
            Assert.That((failedPath, failedException?.Message), Is.EqualTo(("fail", "deferred-failure")));
        });

        [UnityTest]
        public IEnumerator Given_SuspendLoader_When_TaskFails_Then_RecordsErrorKeyedByRouteId()
            => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var runner = new RouteLoaderRunner();
            var tcs = new UniTaskCompletionSource<object>();
            var matches = MakeMatch("fail", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend);
            runner.RunLoadersSync(matches, CancellationToken.None);

            // Act
            tcs.TrySetException(new InvalidOperationException("deferred-failure"));
            await UniTask.Yield();

            // Assert
            Assert.That(runner.Errors["fail"].Message, Does.Contain("deferred-failure"));
        });

        [UnityTest]
        public IEnumerator Given_SuspendLoader_When_TaskFails_Then_ActiveTaskCountReturnsToZero()
            => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var runner = new RouteLoaderRunner();
            var tcs = new UniTaskCompletionSource<object>();
            var matches = MakeMatch("fail", loader: (ctx, ct) => tcs.Task, loaderMode: LoaderMode.Suspend);
            runner.RunLoadersSync(matches, CancellationToken.None);

            // Act
            tcs.TrySetException(new InvalidOperationException("deferred-failure"));
            await UniTask.Yield();

            // Assert
            Assert.That(runner.ActiveSuspendTaskCount, Is.EqualTo(0), "The finally block decrements the counter even on failure");
        });

        [UnityTest]
        public IEnumerator Given_LiveSuspendLoader_When_CancelPendingAndLoaderHonorsToken_Then_ActiveTaskCountReturnsToZero()
            => UniTask.ToCoroutine(async () =>
        {
            // Arrange — the loader honors the token: it cancels its task when the CancellationToken fires.
            var runner = new RouteLoaderRunner();
            var tcs = new UniTaskCompletionSource<object>();
            var matches = MakeMatch("honor-ct",
                loader: (ctx, ct) =>
                {
                    ct.Register(() => tcs.TrySetCanceled(ct));
                    return tcs.Task;
                },
                loaderMode: LoaderMode.Suspend);
            runner.RunLoadersSync(matches, CancellationToken.None);
            Assume.That(runner.ActiveSuspendTaskCount, Is.EqualTo(1), "Precondition: the task is live after RunLoadersSync");

            // Act
            runner.CancelPending();
            await UniTask.Yield();

            // Assert
            Assert.That(runner.ActiveSuspendTaskCount, Is.EqualTo(0), "A token-honoring loader unwinds and the counter returns to zero");
        });

        [UnityTest]
        public IEnumerator Given_SuspendLoaderSucceedsOnCancellation_When_CancelPendingRuns_Then_NoCompletionIsFired()
            => UniTask.ToCoroutine(async () =>
        {
            // A loader may answer its token by resolving a fallback rather than throwing, and that
            // continuation runs inside CancelPending's own Cancel call. The live-task count is folded into
            // the assertion because a zero firing count would otherwise also be satisfied by a loader that
            // never ran at all.
            // Arrange
            var runner = new RouteLoaderRunner();
            var tcs = new UniTaskCompletionSource<object>();
            var fired = 0;
            runner.OnSuspendLoaderCompleted += (_, __) => fired++;
            var matches = MakeMatch("succeed-on-ct",
                loader: (ctx, ct) =>
                {
                    ct.Register(() => tcs.TrySetResult("fallback"));
                    return tcs.Task;
                },
                loaderMode: LoaderMode.Suspend);
            runner.RunLoadersSync(matches, CancellationToken.None);
            var liveBefore = runner.ActiveSuspendTaskCount;

            // Act
            runner.CancelPending();
            await UniTask.Yield();

            // Assert
            Assert.That($"live={liveBefore} fired={fired}", Is.EqualTo("live=1 fired=0"),
                "The round being torn down must not be read as the current one by its own late success");
        });

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
                return UniTask.FromResult<object>("data");
            });

            // Act
            runner.RunLoadersSync(matches, CancellationToken.None);

            // Assert
            Assert.That(capturedToken.CanBeCanceled, Is.True);
        }

        [Test]
        public void Given_CapturedLoaderToken_When_CancelPending_Then_TokenIsCancelled()
        {
            // Arrange
            CancellationToken capturedToken = default;
            var runner = new RouteLoaderRunner();
            var matches = MakeMatch("test", loader: (ctx, ct) =>
            {
                capturedToken = ct;
                return UniTask.FromResult<object>("data");
            });
            runner.RunLoadersSync(matches, CancellationToken.None);
            Assume.That(capturedToken.CanBeCanceled, Is.True, "Precondition: the loader received a cancelable token");

            // Act
            runner.CancelPending();

            // Assert
            Assert.That(capturedToken.IsCancellationRequested, Is.True);
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
            var (_, allCompleted) = runner.RunLoadersSync(matches, CancellationToken.None);

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
            runner.RunLoadersSync(matches, CancellationToken.None);

            // Assert
            Assert.That(runner.Errors["fail"].Message, Does.Contain("loader failed"));
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
            var unresolved = new UniTaskCompletionSource<object>();
            runner.RunLoadersSync(
                MakeMatch("slow", loader: (ctx, ct) => unresolved.Task, loaderMode: LoaderMode.Suspend),
                CancellationToken.None);
            var firstRound = runner.CurrentRound;

            // Act
            runner.RunLoadersSync(MakeMatch("plain"), CancellationToken.None);

            // Assert
            Assert.That($"first={firstRound.Settled} second={runner.CurrentRound.Settled}",
                Is.EqualTo("first=False second=True"),
                "A round reports its own outstanding loaders, not those of whichever round is current");
        }

        [Test]
        public void Given_ALoaderThatStartsAnotherRound_When_TheOuterRoundsNextLoaderLaunches_Then_ItGetsTheOuterRoundsToken()
        {
            // The nested round cancels the outer one on its way in, so the token the outer round's remaining
            // loaders belong to is a cancelled one. Handing them the nested round's live token instead makes
            // their completions read as current to every check keyed on the round's source.
            // Arrange
            var runner = new RouteLoaderRunner();
            bool? nextLoaderSawCancellation = null;
            var matches = new List<RouteMatch>();
            matches.AddRange(MakeMatch("nesting", loader: (ctx, ct) =>
            {
                runner.RunLoadersSync(MakeMatch("nested"), CancellationToken.None);
                return UniTask.FromResult<object>("nesting-data");
            }));
            matches.AddRange(MakeMatch("next", loader: (ctx, ct) =>
            {
                nextLoaderSawCancellation = ct.IsCancellationRequested;
                return UniTask.FromResult<object>("next-data");
            }));

            // Act
            runner.RunLoadersSync(matches, CancellationToken.None);

            // Assert
            Assert.That(nextLoaderSawCancellation, Is.True,
                "A round's later loaders must launch under the token of the round they belong to");
        }

        #endregion
    }
}

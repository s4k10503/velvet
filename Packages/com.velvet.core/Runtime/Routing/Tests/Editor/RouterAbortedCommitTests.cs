using System;
using NUnit.Framework;
using static Velvet.Tests.RouteTestStubs;

namespace Velvet.Tests
{
    /// <summary>
    /// A navigation whose loaders have settled and whose history commit then throws leaves the router's
    /// committed state on the location it was already on. A mode outside the enum is what reaches that
    /// throw: every defined mode has a commit branch.
    /// </summary>
    [TestFixture]
    internal sealed class RouterAbortedCommitTests
    {
        [TearDown]
        public void TearDown()
        {
            Router.Current?.Dispose();
        }

        private static Exception NavigateWithAModeOutsideTheEnum(Router router, string path)
        {
            try
            {
                router.NavigateAsync(path, (NavigationMode)int.MaxValue).GetAwaiter().GetResult();
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return ex;
            }
            return null;
        }

        [Test]
        public void Given_ACommitThatThrows_When_TheExceptionReachesTheCaller_Then_TheCommittedLocationKeepsItsLoaderData()
        {
            // Arrange
            var router = BuildRouter("/home",
                Route("home", loader: (ctx, ct) => VelvetTask.FromResult<object>("home-data")),
                Route("other", loader: (ctx, ct) => VelvetTask.FromResult<object>("other-data")));

            // Act
            var caught = NavigateWithAModeOutsideTheEnum(router, "/other");

            // Assert
            Assert.That(
                $"threw={caught != null} path={router.CurrentLocation?.Path} "
                + $"data={string.Join(",", router.CurrentLoaderData.Values)}",
                Is.EqualTo("threw=True path=/home data=home-data"),
                "An attempt that never commits must leave the loader data of the location that did");
        }

        [Test]
        public void Given_ACommitThatThrows_When_TheExceptionReachesTheCaller_Then_TheCommittedLocationKeepsItsLoaderErrors()
        {
            // A failing loader does not stop the attempt short of its commit, so its failure is recorded
            // before the commit throws.
            // Arrange
            var router = BuildRouter("/home",
                Route("home"),
                Route("other", loader: (ctx, ct) => throw new InvalidOperationException("other-failed")));

            // Act
            var caught = NavigateWithAModeOutsideTheEnum(router, "/other");

            // Assert
            Assert.That(
                $"threw={caught != null} path={router.CurrentLocation?.Path} errors={router.CurrentLoaderErrors.Count}",
                Is.EqualTo("threw=True path=/home errors=0"),
                "An attempt that never commits must leave the loader errors of the location that did");
        }
    }
}

using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that every shader the package ships can be resolved by name from running game code. A shader
    /// nothing in a scene references is not put into a player build, and the paints behind
    /// <c>shadow-*</c>, a skewed <c>bg-gradient-*</c> and <c>brightness-*</c> / <c>saturate-*</c> then draw
    /// nothing there while working in the editor for the whole life of a project.
    /// </summary>
    /// <remarks>
    /// This case only discriminates when the suite is run with <c>-testPlatform StandaloneOSX</c>: in the
    /// editor a package shader resolves whether or not anything put it in a build, which is why the defect
    /// this pins survived a green suite. <c>BundledShaderInclusionTests</c> is what guards the editor-side
    /// mechanism on every run.
    /// </remarks>
    [TestFixture]
    internal sealed class BundledShaderPlayerInclusionTests
    {
        [Test]
        public void Given_TheRunningPlayer_When_EachBundledShaderIsLookedUpByName_Then_EveryOneResolves()
        {
            // Arrange — the list's size is folded in below, because an empty list would otherwise report
            // nothing unresolved and pass having looked nothing up.
            var names = VelvetShaders.Names;

            // Act — a shader that reached the build but compiled for no subshader on this platform resolves
            // and still draws nothing, so support is read here rather than only the reference.
            var unusable = names.Where(name =>
            {
                var shader = Shader.Find(name);
                return shader == null || !shader.isSupported;
            });

            // Assert
            Assert.That((names.Length > 0, string.Join(", ", unusable)), Is.EqualTo((true, string.Empty)));
        }
    }
}

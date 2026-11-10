using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the colour matrices the backdrop shader is handed for a function at a partial amount against the Filter
    /// Effects specification's, and that a pass hands the shader the colour functions it runs after its taps.
    /// </summary>
    [TestFixture]
    internal sealed class BackdropColorStepTests
    {
        // GREEN_ON_BASE(characterization): the base builds the Filter Effects saturate matrix.
        [Test]
        public void Given_SaturateAtHalf_When_ItsMatrixIsBuilt_Then_TheGreenRowsRedWeightIsTheSpecifications()
        {
            // Arrange — the specification's green row opens 0.213 - 0.213 * amount.
            var step = new BackdropStep { Function = BackdropFunction.Saturate, A = 0.5f };

            // Act
            var (matrix, _) = BackdropColorMatrix.Of(step);

            // Assert
            Assert.That(matrix.m10, Is.EqualTo(0.213f - (0.213f * 0.5f)).Within(1e-5f));
        }

        // GREEN_ON_BASE(characterization): the base builds the Filter Effects sepia matrix.
        [Test]
        public void Given_SepiaAtHalf_When_ItsMatrixIsBuilt_Then_TheGreenRowsRedWeightIsTheSpecifications()
        {
            // Arrange — the specification's green row opens 0.349 - 0.349 * (1 - amount).
            var step = new BackdropStep { Function = BackdropFunction.Sepia, A = 0.5f };

            // Act
            var (matrix, _) = BackdropColorMatrix.Of(step);

            // Assert
            Assert.That(matrix.m10, Is.EqualTo(0.349f - (0.349f * 0.5f)).Within(1e-5f));
        }

        // GREEN_ON_BASE(characterization): the base hands the shader the colour steps that follow a pass's taps.
        [Test]
        public void Given_APassWithAColourFunctionAfterItsTaps_When_SetOnTheMaterial_Then_TheShaderIsToldToRunIt()
        {
            // Arrange
            var material = new Material(Shader.Find(VelvetShaders.BackdropFilter));
            var pass = new BackdropPass { Source = new BackdropSource(Texture2D.whiteTexture, false) };
            pass.After.Add(new BackdropStep { Function = BackdropFunction.Opacity, A = 0.5f });

            try
            {
                // Act
                pass.SetOn(material);

                // Assert
                Assert.That(material.GetFloat(BackdropShader.AfterCount), Is.EqualTo(1f));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }
    }
}

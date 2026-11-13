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

        // GREEN_ON_BASE(characterization): the base builds the Filter Effects saturate matrix's red row.
        [Test]
        public void Given_SaturateAtHalf_When_ItsMatrixIsBuilt_Then_TheRedRowsRedWeightIsTheSpecifications()
        {
            // Arrange — the specification's red row opens 0.213 + 0.787 * amount.
            var step = new BackdropStep { Function = BackdropFunction.Saturate, A = 0.5f };

            // Act
            var (matrix, _) = BackdropColorMatrix.Of(step);

            // Assert
            Assert.That(matrix.m00, Is.EqualTo(0.213f + (0.787f * 0.5f)).Within(1e-5f));
        }

        // GREEN_ON_BASE(characterization): the base builds the Filter Effects saturate matrix's blue row.
        [Test]
        public void Given_SaturateAtHalf_When_ItsMatrixIsBuilt_Then_TheBlueRowsRedWeightIsTheSpecifications()
        {
            // Arrange — the specification's blue row opens 0.213 - 0.213 * amount.
            var step = new BackdropStep { Function = BackdropFunction.Saturate, A = 0.5f };

            // Act
            var (matrix, _) = BackdropColorMatrix.Of(step);

            // Assert
            Assert.That(matrix.m20, Is.EqualTo(0.213f - (0.213f * 0.5f)).Within(1e-5f));
        }

        // GREEN_ON_BASE(characterization): the base writes the Filter Effects saturate matrix's blue row into it.
        [Test]
        public void Given_SaturateAtHalf_When_ItsMatrixIsBuilt_Then_TheBlueRowsBlueWeightIsTheSpecifications()
        {
            // Arrange — the specification's blue row closes 0.072 + 0.928 * amount, where the identity holds 1.
            var step = new BackdropStep { Function = BackdropFunction.Saturate, A = 0.5f };

            // Act
            var (matrix, _) = BackdropColorMatrix.Of(step);

            // Assert
            Assert.That(matrix.m22, Is.EqualTo(0.072f + (0.928f * 0.5f)).Within(1e-5f));
        }

        // GREEN_ON_BASE(characterization): the base builds the Filter Effects grayscale matrix.
        [Test]
        public void Given_GrayscaleAtQuarter_When_ItsMatrixIsBuilt_Then_TheGreenRowsRedWeightIsTheSpecifications()
        {
            // Arrange — the specification's green row opens 0.2126 - 0.2126 * (1 - amount).
            var step = new BackdropStep { Function = BackdropFunction.Grayscale, A = 0.25f };

            // Act
            var (matrix, _) = BackdropColorMatrix.Of(step);

            // Assert
            Assert.That(matrix.m10, Is.EqualTo(0.2126f - (0.2126f * 0.75f)).Within(1e-5f));
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

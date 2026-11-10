using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the blend factors of the material each Particles blend draws under, where the shader's own defaults
    /// differ from them, and which materials are read by their shader's name.
    /// </summary>
    [TestFixture]
    internal sealed class ParticleBlendMaterialsTests
    {
        private Shader _shader;
        private Material _material;

        [TearDown]
        public void TearDown()
        {
            if (_material != null) Object.DestroyImmediate(_material);
            if (_shader != null) Object.DestroyImmediate(_shader);
        }

        // NaN where the shader cannot be found, which no factor equals.
        private static float FactorOf(ParticleBlend blend, string property)
        {
            var material = ParticleBlendMaterials.For(blend);
            return material != null ? material.GetFloat(property) : float.NaN;
        }

        // GREEN_ON_BASE(characterization): the base already makes and reads these materials as asserted; no case asserted it before.
        [Test]
        public void Given_TheMultiplyBlend_When_ItsMaterialIsMade_Then_ItFadesTheColourTowardWhite()
        {
            // Arrange & Act
            var multiply = FactorOf(ParticleBlend.Multiply, "_Multiply");

            // Assert
            Assert.That(multiply, Is.EqualTo(1f));
        }

        // GREEN_ON_BASE(characterization): the base already makes and reads these materials as asserted; no case asserted it before.
        [Test]
        public void Given_TheMultiplyBlend_When_ItsMaterialIsMade_Then_ItsSourceAlphaFactorIsZero()
        {
            // Arrange & Act
            var factor = FactorOf(ParticleBlend.Multiply, "_SrcBlendAlpha");

            // Assert
            Assert.That(factor, Is.EqualTo((float)BlendMode.Zero));
        }

        // GREEN_ON_BASE(characterization): the base already makes and reads these materials as asserted; no case asserted it before.
        [Test]
        public void Given_TheMultiplyBlend_When_ItsMaterialIsMade_Then_ItsDestinationAlphaFactorIsOne()
        {
            // Arrange & Act
            var factor = FactorOf(ParticleBlend.Multiply, "_DstBlendAlpha");

            // Assert
            Assert.That(factor, Is.EqualTo((float)BlendMode.One));
        }

        // GREEN_ON_BASE(characterization): the base already makes and reads these materials as asserted; no case asserted it before.
        [Test]
        public void Given_ThePremultipliedBlend_When_ItsMaterialIsMade_Then_ItsSourceColourFactorIsOne()
        {
            // Arrange & Act
            var factor = FactorOf(ParticleBlend.Premultiplied, "_SrcBlend");

            // Assert
            Assert.That(factor, Is.EqualTo((float)BlendMode.One));
        }

        // GREEN_ON_BASE(characterization): the base already makes and reads these materials as asserted; no case asserted it before.
        [Test]
        public void Given_AMaterialWhoseShaderDeclaresASourceFactorAlone_When_ItsBlendIsRead_Then_ItIsReadByNameAndDrawsAlphaBlended()
        {
            // Arrange — a source factor of DstColor, the one the Multiply blend carries.
            _shader = ShaderUtil.CreateShaderAsset(
                "Shader \"Hidden/VelvetTests/SourceFactorOnly\" { Properties { _SrcBlend (\"Source blend\", Float) = 2 } SubShader { Pass { } } }");
            _material = new Material(_shader);

            // Act
            var blend = ParticleBlendMaterials.Of(_material);

            // Assert
            Assert.That(blend, Is.EqualTo(ParticleBlend.Alpha));
        }
    }
}

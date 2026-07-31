using UnityEngine;

namespace Velvet
{
    // Shader.Find resolves every one of Velvet's shaders in the editor and returns null in a built player: a
    // shader that no material, scene or Resources asset references is not put into the build at all, and a
    // package shader reached only by name from C# is exactly that case. Measured in a macOS standalone, not
    // inferred — Play Mode resolves all four.
    //
    // The shaders therefore live under Runtime/Resources/Velvet/, where each file's Resources-relative path is
    // its declared Shader name: the one string that puts a shader into the build is the one that looks it up,
    // so no second list can drift from the first. Graphics Settings' Always Included Shaders was the
    // alternative and cannot be shipped — that list is a project setting the consumer owns, not something a
    // package carries.
    //
    // Keeping Shader.Find would work now that the Resources folder puts the shaders in the build — measured in
    // the same player — but nothing in the editor could guard it, because Shader.Find succeeds there whether or
    // not the shader is in a build. Resources.Load is the one call an EditMode test can make and have mean the
    // same thing in a player, which is what BundledShaderResourceTests asserts.
    //
    // Everything under that folder is in every consumer's build whether or not anything uses it;
    // Documentation~/player-builds.md owns that cost.
    internal static class VelvetShaders
    {
        internal static Shader? Find(string shaderName) => Resources.Load<Shader>(shaderName);
    }
}

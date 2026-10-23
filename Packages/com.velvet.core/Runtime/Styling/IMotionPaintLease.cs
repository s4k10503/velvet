namespace Velvet
{
    /// <summary>
    /// One Motion play's hold on an element's paint layers (skew, shadow, gradient, z), which the reconciler's
    /// class pass writes outside a play. While a play holds them the pass records what its classes name instead
    /// of applying it, and the play draws the layers at its progress; releasing hands the layers back to the pass.
    /// </summary>
    internal interface IMotionPaintLease
    {
        /// <summary>Draws the layers at <paramref name="progress"/>, 0 at the pose the play starts from and 1 at the one it ends on.</summary>
        void Apply(float progress);

        /// <summary>Gives the layers back once no play holds them; a lease already released does nothing.</summary>
        void Release();
    }
}

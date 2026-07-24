namespace Velvet
{
    /// <summary>
    /// The numeric font-weight scale (<c>font-thin</c> … <c>font-black</c>). The underlying
    /// integer value is the CSS weight, so a class such as <c>font-semibold</c> resolves to
    /// <c>(int)VelvetFontWeight.SemiBold == 600</c>.
    /// <para/>
    /// UI Toolkit's <c>-unity-font-style</c> can only express the binary <c>normal</c>/<c>bold</c>
    /// axis, so a weight only renders faithfully when a weight-specific Font Asset is registered for it
    /// in a <see cref="VelvetFontFamily"/>. When no such asset exists Velvet folds the weight to the
    /// nearest binary value (<c>&gt;= 600</c> → bold, otherwise normal); see <see cref="VelvetFonts"/>.
    /// </summary>
    public enum VelvetFontWeight
    {
        Thin = 100,
        ExtraLight = 200,
        Light = 300,
        Normal = 400,
        Medium = 500,
        SemiBold = 600,
        Bold = 700,
        ExtraBold = 800,
        Black = 900,
    }
}

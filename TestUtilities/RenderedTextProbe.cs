#nullable enable
using System.Reflection;
using UnityEngine.UIElements;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// The string a <see cref="TextElement"/> shows, which differs from its <c>text</c> once something hands the
    /// engine a rendered text of its own (<c>experimental.SetRenderedText</c>). UI Toolkit exposes no getter, so
    /// it is read from the field the engine keeps it in.
    /// </summary>
    public static class RenderedTextProbe
    {
        private static readonly FieldInfo s_field = typeof(TextElement)
            .GetField("m_RenderedText", BindingFlags.Instance | BindingFlags.NonPublic)!;

        public static string Of(VisualElement element) => (string?)s_field.GetValue((TextElement)element) ?? string.Empty;
    }
}

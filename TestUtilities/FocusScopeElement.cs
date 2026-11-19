#nullable enable

namespace Velvet.TestUtilities
{
    /// <summary>
    /// A container element whose subtree is a focus scope, set through its <c>FocusScope</c> element prop. The
    /// fixtures written against an element scope build theirs here, since <c>V.FocusScope</c> renders no element
    /// of its own.
    /// </summary>
    public static class FocusScopeElement
    {
        public static ElementNode Div(
            string? className = null,
            string? key = null,
            string? name = null,
            bool contain = false,
            bool restoreFocus = false,
            bool autoFocus = false,
            bool singleTabStop = false,
            VNode?[]? children = null,
            FocusScopeOrientation orientation = FocusScopeOrientation.Both,
            bool wrap = false,
            FiberElementProps? props = null)
        {
            var bag = props ?? new FiberElementProps();
            bag.FocusScope = new FocusScopeSettings(contain, restoreFocus, autoFocus, singleTabStop)
            {
                Orientation = orientation,
                Wrap = wrap,
            };
            return V.Div(className: className, key: key, name: name, props: bag, children: children);
        }
    }
}

using System;
using Cysharp.Threading.Tasks;

namespace Velvet
{
    // Functional component backing the V.Navigate DSL primitive: a declarative redirect that
    // navigates to a target path and renders nothing. Kept out of V.cs because it calls hooks
    // (UseNavigate / UseEffect) and must therefore run as a [Component] body.
    internal static class Navigate
    {
        public sealed record Props(string To, bool Replace);

        [Component]
        public static VNode Render(Props p)
        {
            var navigate = Hooks.UseNavigate(p.Replace);

            // The redirect re-issues whenever the target (or replace mode) changes, not only on mount.
            // Keying the effect on To/Replace re-navigates when a parent re-renders this element with new
            // props, while leaving identical re-renders a no-op.
            Hooks.UseEffect(() =>
            {
                navigate(p.To).Forget();
                return (Action)(() => { });
            }, new object[] { p.To, p.Replace });

            return V.Fragment(Array.Empty<VNode>());
        }
    }
}

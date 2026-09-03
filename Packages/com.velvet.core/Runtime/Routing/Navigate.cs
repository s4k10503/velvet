using System;

namespace Velvet
{
    // Not beside its factory in Component/V.cs, where every other factory lives: this one calls hooks,
    // so its body has to be a [Component] rather than a factory that builds a node.
    internal static class Navigate
    {
        public sealed record Props(string To, bool Replace);

        [Component]
        public static VNode Render(Props p)
        {
            var navigate = Hooks.UseNavigate(p.Replace);

            // The dependency list makes target and history-mode changes issue a new redirect.
            Hooks.UseEffect(() =>
            {
                navigate(p.To).Forget();
                return (Action)(() => { });
            }, new object[] { p.To, p.Replace });

            return V.Fragment(Array.Empty<VNode>());
        }
    }
}

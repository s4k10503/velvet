#nullable enable
using System;

namespace Velvet
{
    // Not beside its factory in Component/V.cs, where every other factory lives: this one calls hooks,
    // so its body has to be a [Component] rather than a factory that builds a node.
    internal static class Navigate
    {
        public sealed record Props(
            string To, bool Replace, object? State = null, RelativeRoutingType Relative = RelativeRoutingType.Route);

        [Component]
        public static VNode Render(Props p)
        {
            var navigate = Hooks.UseNavigate();

            // The dependency list makes target and history-mode changes issue a new redirect.
            Hooks.UseEffect(() =>
            {
                navigate(p.To, new NavigateOptions { Replace = p.Replace, State = p.State, Relative = p.Relative }).Forget();
                return (Action)(() => { });
            }, new object[] { p.To, p.Replace, p.State!, p.Relative });

            return V.Fragment(Array.Empty<VNode>());
        }
    }
}

using System;
using Cysharp.Threading.Tasks;

namespace Velvet
{
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

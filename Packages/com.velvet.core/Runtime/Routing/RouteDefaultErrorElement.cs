#nullable enable
using System;

namespace Velvet
{
    // React Router's DefaultErrorComponent: what the root route renders for a loader error no route in the
    // matched chain declares an errorElement for.
    internal static class RouteDefaultErrorElement
    {
        [Component]
        public static VNode Render()
        {
            var error = Hooks.UseRouteError();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Hooks.UseEffect(() =>
            {
                if (error != null)
                {
                    UnityEngine.Debug.LogException(error);
                }
                return (Action?)null;
            }, new object?[] { error });
#endif

            return V.Div(children: new VNode?[]
            {
                V.Label(text: "Unexpected Application Error!"),
                V.Label(text: error?.Message),
                error?.StackTrace != null ? V.Label(text: error.StackTrace) : null,
            });
        }
    }
}

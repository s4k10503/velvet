#nullable enable
using System;

namespace Velvet
{
    internal static class RouteLink
    {
        public sealed record Props(
            string To,
            string? Text,
            string? ClassName,
            string? Name,
            VNode?[]? Children,
            bool Replace,
            FiberEventBinding[]? Events = null,
            object? State = null,
            RelativeRoutingType Relative = RelativeRoutingType.Route);

        [Component]
        public static VNode Render(Props p)
        {
            var navigate = Hooks.UseNavigate();
            var onClick = Hooks.UseCallback<Action<ClickedEvent>>(
                click =>
                {
                    if (!click.DefaultPrevented)
                    {
                        navigate(p.To, new NavigateOptions { Replace = p.Replace, State = p.State, Relative = p.Relative })
                            .Forget();
                    }
                },
                p.To, navigate, p.Replace, p.State, p.Relative);

            // React Router's Link calls the caller's onClick and navigates only when that left the event's
            // default unprevented, so the navigation is bound after every binding the caller passed, on the
            // click those share.
            var callers = p.Events ?? Array.Empty<FiberEventBinding>();
            var events = VNodePool.RentEventArray(callers.Length + 1);
            Array.Copy(callers, events, callers.Length);
            events[callers.Length] = new ClickedEventBinding { Handler = onClick };

            return V.Button(
                className: p.ClassName,
                text: p.Text,
                name: p.Name,
                children: p.Children,
                events: events);
        }
    }

    internal static class RouteNavLink
    {
        public sealed record Props
        {
            public required string To { get; init; }
            public string? Text { get; init; }
            public string? ClassName { get; init; }
            public string? ActiveClass { get; init; }
            public string? Name { get; init; }
            public VNode?[]? Children { get; init; }
            public bool End { get; init; }
            public bool Replace { get; init; }
            public bool CaseSensitive { get; init; }
            public FiberEventBinding[]? Events { get; init; }
            public object? State { get; init; }
            public RelativeRoutingType Relative { get; init; }
        }

        [Component]
        public static VNode Render(Props p)
        {
            var location = Hooks.UseLocation();
            var router = Hooks.UseContext(RouterContext.Router);
            var baseRouteIndex = Hooks.UseBaseRouteIndex();
            // Resolved first, because the click path takes p.To through this same Router call to navigate.
            var target = router.ResolvePath(p.To, baseRouteIndex, p.Relative) ?? p.To;
            // Standing in for a missing location with the empty string was rejected: it goes through the same
            // normalisation as the target, where an empty path is the root, so a link to "/" came out as the
            // current page of a tree that has no current page.
            var isActive = location != null && IsActive(location.Path, target, p.End, p.CaseSensitive);

            var className = isActive && !string.IsNullOrEmpty(p.ActiveClass)
                ? string.IsNullOrEmpty(p.ClassName) ? p.ActiveClass : p.ClassName + " " + p.ActiveClass
                : p.ClassName;

            // Keep click navigation owned by RouteLink after deriving the effective class here.
            return V.Component(
                RouteLink.Render,
                new RouteLink.Props(p.To, p.Text, className, p.Name, p.Children, p.Replace, p.Events, p.State, p.Relative));
        }

        private static bool IsActive(string? currentPath, string resolvedTo, bool end, bool caseSensitive)
        {
            var current = Normalize(currentPath);
            var target = Normalize(resolvedTo);
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            if (end)
            {
                return string.Equals(current, target, comparison);
            }

            if (string.Equals(current, target, comparison))
            {
                return true;
            }

            return target.Length > 0
                && current.StartsWith(target, comparison)
                && (target == "/" || current.Length == target.Length || current[target.Length] == '/');
        }

        private static string Normalize(string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return "/";
            }
            var hashAt = path.IndexOf('#');
            var stripped = RouteQuery.StripQuery(hashAt == -1 ? path : path.Substring(0, hashAt));
            if (stripped.Length > 1)
            {
                stripped = stripped.TrimEnd('/');
            }
            return stripped.Length == 0 ? "/" : stripped;
        }
    }
}

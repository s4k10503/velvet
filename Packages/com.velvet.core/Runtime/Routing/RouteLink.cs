#nullable enable
using System;
using Cysharp.Threading.Tasks;

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
            bool Replace);

        [Component]
        public static VNode Render(Props p)
        {
            var navigate = Hooks.UseNavigate(p.Replace);
            var onClick = Hooks.UseCallback<Action>(
                () => navigate(p.To).Forget(),
                p.To, navigate);

            return V.Button(
                className: p.ClassName,
                text: p.Text,
                onClick: onClick,
                name: p.Name,
                children: p.Children);
        }
    }

    internal static class RouteNavLink
    {
        public sealed record Props(
            string To,
            string? Text,
            string? ClassName,
            string? ActiveClass,
            string? Name,
            VNode?[]? Children,
            bool End,
            bool Replace,
            bool CaseSensitive);

        [Component]
        public static VNode Render(Props p)
        {
            var navigate = Hooks.UseNavigate(p.Replace);
            var location = Hooks.UseLocation();
            var currentPath = location?.Path ?? string.Empty;
            var isActive = IsActive(currentPath, p.To, p.End, p.CaseSensitive);

            var className = isActive && !string.IsNullOrEmpty(p.ActiveClass)
                ? string.IsNullOrEmpty(p.ClassName) ? p.ActiveClass : p.ClassName + " " + p.ActiveClass
                : p.ClassName;

            var onClick = Hooks.UseCallback<Action>(
                () => navigate(p.To).Forget(),
                p.To, navigate);

            return V.Button(
                className: className,
                text: p.Text,
                onClick: onClick,
                name: p.Name,
                children: p.Children);
        }

        private static bool IsActive(string currentPath, string to, bool end, bool caseSensitive)
        {
            var current = Normalize(currentPath);
            var target = Normalize(to);
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
            var qIndex = path.IndexOf('?');
            var stripped = qIndex < 0 ? path : path.Substring(0, qIndex);
            if (stripped.Length > 1)
            {
                stripped = stripped.TrimEnd('/');
            }
            return stripped.Length == 0 ? "/" : stripped;
        }
    }
}

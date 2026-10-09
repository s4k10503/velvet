#nullable enable
using System;
using System.Collections.Generic;

namespace Velvet
{
    // The values the bundled anim-* preset classes (_animations.uss) declare, spelled as the utility tokens the
    // driven channels read (MotionSpringClassParser), so a StyleTransition preset a mount's clock drives resolves
    // a plan. A mirror of the stylesheet, which MotionPresetClassesTests pins.
    internal static class MotionPresetClasses
    {
        private static readonly string[] s_hidden = { "opacity-0" };
        private static readonly string[] s_shown = { "opacity-100" };

        internal static readonly IReadOnlyDictionary<string, string[]> Tokens =
            new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["anim-fade-enter-from"] = s_hidden,
                ["anim-fade-enter-to"] = s_shown,
                ["anim-fade-exit-from"] = s_shown,
                ["anim-fade-exit-to"] = s_hidden,
                ["anim-slide-up-enter-from"] = Translated(0, 20, s_hidden),
                ["anim-slide-up-enter-to"] = Translated(0, 0, s_shown),
                ["anim-slide-up-exit-from"] = Translated(0, 0, s_shown),
                ["anim-slide-up-exit-to"] = Translated(0, -20, s_hidden),
                ["anim-slide-down-enter-from"] = Translated(0, -20, s_hidden),
                ["anim-slide-down-enter-to"] = Translated(0, 0, s_shown),
                ["anim-slide-down-exit-from"] = Translated(0, 0, s_shown),
                ["anim-slide-down-exit-to"] = Translated(0, 20, s_hidden),
                ["anim-slide-left-enter-from"] = Translated(20, 0, s_hidden),
                ["anim-slide-left-enter-to"] = Translated(0, 0, s_shown),
                ["anim-slide-left-exit-from"] = Translated(0, 0, s_shown),
                ["anim-slide-left-exit-to"] = Translated(-20, 0, s_hidden),
                ["anim-slide-right-enter-from"] = Translated(-20, 0, s_hidden),
                ["anim-slide-right-enter-to"] = Translated(0, 0, s_shown),
                ["anim-slide-right-exit-from"] = Translated(0, 0, s_shown),
                ["anim-slide-right-exit-to"] = Translated(20, 0, s_hidden),
                ["anim-scale-enter-from"] = Scaled("0.85", s_hidden),
                ["anim-scale-enter-to"] = Scaled("1", s_shown),
                ["anim-scale-exit-from"] = Scaled("1", s_shown),
                ["anim-scale-exit-to"] = Scaled("0.85", s_hidden),
                ["anim-fade-slide-up-enter-from"] = Translated(0, 12, s_hidden),
                ["anim-fade-slide-up-enter-to"] = Translated(0, 0, s_shown),
                ["anim-fade-slide-up-exit-from"] = Translated(0, 0, s_shown),
                ["anim-fade-slide-up-exit-to"] = Translated(0, -12, s_hidden),
            };

        // The classes with each preset class in it replaced by its tokens.
        internal static string[] Expand(string[] classes)
        {
            var expanded = new List<string>(classes.Length);
            foreach (var cls in classes)
            {
                if (Tokens.TryGetValue(cls, out var tokens))
                {
                    expanded.AddRange(tokens);
                }
                else
                {
                    expanded.Add(cls);
                }
            }
            return expanded.ToArray();
        }

        private static string[] Translated(int xPx, int yPx, string[] opacity)
            => new[] { $"translate-x-[{xPx}px]", $"translate-y-[{yPx}px]", opacity[0] };

        private static string[] Scaled(string scale, string[] opacity) => new[] { $"scale-[{scale}]", opacity[0] };
    }
}

using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// Predefined transition presets for <c>V.Motion</c>. All presets animate only translate / scale /
    /// opacity, so they never trigger a layout recomputation. Override duration / easing per use via
    /// <see cref="StyleTransitionConfig.With"/>, e.g. <c>StyleTransition.Fade.With(durationSec: 0.5f)</c>.
    /// </summary>
    public static class StyleTransition
    {
        /// <summary>Fade in/out. Suited to modal overlays and similar.</summary>
        public static readonly StyleTransitionConfig Fade = new()
        {
            EnterFromClass = "anim-fade-enter-from",
            EnterToClass = "anim-fade-enter-to",
            ExitFromClass = "anim-fade-exit-from",
            ExitToClass = "anim-fade-exit-to",
            DurationSec = 0.2f,
            Easing = EasingMode.EaseOut,
            ExitEasing = EasingMode.EaseIn,
        };

        /// <summary>Slide-in from bottom to top. Suited to bottom sheets and similar.</summary>
        public static readonly StyleTransitionConfig SlideUp = new()
        {
            EnterFromClass = "anim-slide-up-enter-from",
            EnterToClass = "anim-slide-up-enter-to",
            ExitFromClass = "anim-slide-up-exit-from",
            ExitToClass = "anim-slide-up-exit-to",
            DurationSec = 0.25f,
            Easing = EasingMode.EaseOut,
            ExitEasing = EasingMode.EaseIn,
        };

        /// <summary>Slide-in from top to bottom. Suited to dropdowns and similar.</summary>
        public static readonly StyleTransitionConfig SlideDown = new()
        {
            EnterFromClass = "anim-slide-down-enter-from",
            EnterToClass = "anim-slide-down-enter-to",
            ExitFromClass = "anim-slide-down-exit-from",
            ExitToClass = "anim-slide-down-exit-to",
            DurationSec = 0.25f,
            Easing = EasingMode.EaseOut,
            ExitEasing = EasingMode.EaseIn,
        };

        /// <summary>Slide-in from right to left. Suited to side panels and similar.</summary>
        public static readonly StyleTransitionConfig SlideLeft = new()
        {
            EnterFromClass = "anim-slide-left-enter-from",
            EnterToClass = "anim-slide-left-enter-to",
            ExitFromClass = "anim-slide-left-exit-from",
            ExitToClass = "anim-slide-left-exit-to",
            DurationSec = 0.25f,
            Easing = EasingMode.EaseOut,
            ExitEasing = EasingMode.EaseIn,
        };

        /// <summary>Slide-in from left to right. Suited to drawers and similar.</summary>
        public static readonly StyleTransitionConfig SlideRight = new()
        {
            EnterFromClass = "anim-slide-right-enter-from",
            EnterToClass = "anim-slide-right-enter-to",
            ExitFromClass = "anim-slide-right-exit-from",
            ExitToClass = "anim-slide-right-exit-to",
            DurationSec = 0.25f,
            Easing = EasingMode.EaseOut,
            ExitEasing = EasingMode.EaseIn,
        };

        /// <summary>Scale + fade. Suited to modal content and similar.</summary>
        public static readonly StyleTransitionConfig ScaleIn = new()
        {
            EnterFromClass = "anim-scale-enter-from",
            EnterToClass = "anim-scale-enter-to",
            ExitFromClass = "anim-scale-exit-from",
            ExitToClass = "anim-scale-exit-to",
            DurationSec = 0.2f,
            Easing = EasingMode.EaseOut,
            ExitEasing = EasingMode.EaseIn,
        };

        /// <summary>Subtle slide-up + fade. Suited to list items and similar.</summary>
        public static readonly StyleTransitionConfig FadeSlideUp = new()
        {
            EnterFromClass = "anim-fade-slide-up-enter-from",
            EnterToClass = "anim-fade-slide-up-enter-to",
            ExitFromClass = "anim-fade-slide-up-exit-from",
            ExitToClass = "anim-fade-slide-up-exit-to",
            DurationSec = 0.25f,
            Easing = EasingMode.EaseOut,
            ExitEasing = EasingMode.EaseIn,
        };
    }
}

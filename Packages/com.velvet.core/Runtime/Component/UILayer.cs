#nullable enable
namespace Velvet
{
    /// <summary>
    /// Framework-managed screen-space layer panels a <see cref="V.Portal(UILayer, VNode?[], string?)"/>
    /// can target, sorted around the app's main panel. Screen-space panels always composite over the
    /// 3D scene (the engine's compositor draws overlay panels after cameras) — UI that must sit among
    /// or behind scene geometry is <see cref="V.WorldSpace"/>'s depth-tested territory instead.
    /// </summary>
    public enum UILayer
    {
        /// <summary>Below the app's main panel (still over the 3D scene) — backdrops, ambient chrome.</summary>
        Background,
        /// <summary>Above the app's main panel — floating panels, drag ghosts.</summary>
        Overlay,
        /// <summary>Above everything — toasts, modals, debug chrome.</summary>
        Topmost,
    }
}

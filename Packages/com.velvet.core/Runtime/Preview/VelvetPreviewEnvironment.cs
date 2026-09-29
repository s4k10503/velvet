#if UNITY_EDITOR
#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// The environment <see cref="VelvetPreviewRegistry.RunSetupFor"/> opened for one assembly: the stylesheets its
    /// setups published, and their teardowns.
    /// </summary>
    public sealed class VelvetPreviewEnvironment : IDisposable
    {
        private readonly List<IDisposable> _teardowns;

        internal VelvetPreviewEnvironment(List<IDisposable> teardowns, List<StyleSheet> styleSheets)
        {
            _teardowns = teardowns;
            StyleSheets = styleSheets;
        }

        /// <summary>
        /// The <see cref="VelvetStyleHints.PreviewStyleSheet"/> each setup published, in setup order, so a later
        /// sheet is meant to layer over an earlier one.
        /// </summary>
        public IReadOnlyList<StyleSheet> StyleSheets { get; }

        /// <summary>
        /// Runs the setups' teardowns in reverse setup order. One that throws is logged and the rest still run.
        /// </summary>
        public void Dispose()
        {
            for (var i = _teardowns.Count - 1; i >= 0; i--)
            {
                try
                {
                    _teardowns[i].Dispose();
                }
                catch (Exception ex)
                {
                    Debug.LogException(ex);
                }
            }
        }
    }
}
#endif

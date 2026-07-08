using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Supplies a real editor <see cref="IPanel"/> without <see cref="UnityEditor.EditorWindow.Show"/>,
    /// which requires a graphics device and fails under <c>-batchmode -nographics</c>.
    /// Uses Unity's internal <c>Panel.CreateEditorPanel</c> via reflection — the concrete <c>Panel</c> class is
    /// internal to the UIElements module, so it cannot be named directly from this assembly; only its
    /// public <see cref="IPanel"/> surface is referenced by name here.
    /// </summary>
    public sealed class HeadlessEditorPanelHost : IDisposable
    {
        private readonly ScriptableObject _owner;
        private IPanel _panel;

        public HeadlessEditorPanelHost()
        {
            _owner = ScriptableObject.CreateInstance<ScriptableObject>();
            _panel = CreateEditorPanel(_owner);
            Root = _panel.visualTree;
        }

        public VisualElement Root { get; }

        public IPanel Panel => _panel;

        public void Dispose()
        {
            // The concrete Panel implements IDisposable even though the internal type itself cannot be named
            // here; a runtime pattern match reaches it without a compile-time reference to that type.
            if (_panel is IDisposable disposablePanel)
            {
                disposablePanel.Dispose();
            }
            _panel = null;

            if (_owner != null)
            {
                UnityEngine.Object.DestroyImmediate(_owner);
            }
        }

        private static IPanel CreateEditorPanel(ScriptableObject owner)
        {
            var panelType = typeof(IPanel).Assembly.GetType("UnityEngine.UIElements.Panel");
            if (panelType == null)
            {
                throw new TypeLoadException("UnityEngine.UIElements.Panel");
            }

            var method = panelType.GetMethod(
                "CreateEditorPanel",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (method == null)
            {
                throw new MissingMethodException(panelType.FullName, "CreateEditorPanel");
            }

            return (IPanel)method.Invoke(null, new object[] { owner });
        }
    }
}

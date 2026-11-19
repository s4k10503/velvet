using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    internal sealed class ImportedStyleSheetPlayerAssets : ScriptableObject
    {
        [SerializeField] internal StyleSheet ImportingSheet;
        [SerializeField] internal PanelSettings PanelSettings;
        [SerializeField] internal string ScriptingBackend;
        [SerializeField] internal string StrippingLevel;
        [SerializeField] internal string[] ShaderNames;
        [SerializeField] internal string BundlePath;
    }
}

using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Samples.StarterApp
{
    /// <summary>
    /// Hosts the sample on a <see cref="UIDocument"/> panel. Everything a player needs is here: the
    /// stylesheet, the router and the mount. The mount follows the document when its root is recreated.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class StarterAppHost : MonoBehaviour
    {
        private Router _router;
        private MountedTree _tree;

        private UIDocument _document;
        private VisualElement _mountedRoot;

        private void OnEnable()
        {
            _document = GetComponent<UIDocument>();
            _router = new Router(StarterApp.Routes());
            RefreshMount();
            _router.NavigateAsync(StarterApp.TasksPath).Forget();
        }

        private void Update() => RefreshMount();

        private void RefreshMount()
        {
            var root = _document.isActiveAndEnabled ? _document.rootVisualElement : null;
            if (ReferenceEquals(root, _mountedRoot))
            {
                return;
            }

            _tree?.Dispose();
            _tree = null;
            _mountedRoot = root;
            if (root == null)
            {
                return;
            }

            VelvetStyleUtilities.AttachTo(root);
            _tree = V.Mount(root, V.RouterProvider(_router));
        }

        private void OnDisable()
        {
            _tree?.Dispose();
            _tree = null;
            _mountedRoot = null;
            _router?.Dispose();
            _router = null;
        }
    }
}

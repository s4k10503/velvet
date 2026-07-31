using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Samples.StarterApp
{
    /// <summary>
    /// Hosts the sample on a <see cref="UIDocument"/> panel. Everything a player needs is here: the
    /// stylesheet, the router and the mount, all in <c>OnEnable</c>, all undone in <c>OnDisable</c>.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class StarterAppHost : MonoBehaviour
    {
        private Router _router;
        private MountedTree _tree;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;

            // Attach before mounting. The package's Documentation~/setup.md owns which utility families
            // stop working without this call and which are unaffected.
            VelvetStyleUtilities.AttachTo(root);

            _router = new Router(StarterApp.Routes());
            _tree = V.Mount(root, V.Component(StarterApp.Root, _router));
            _router.NavigateAsync(StarterApp.TasksPath).Forget();
        }

        private void OnDisable()
        {
            _tree?.Dispose();
            _tree = null;
            _router?.Dispose();
            _router = null;
        }
    }
}

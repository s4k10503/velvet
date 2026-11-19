using System;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// `using var scope = new ReconcilerScope();` wraps a Reconciler + VisualElement pair for
    /// tests that prefer inline ownership over a base-class fixture (e.g. multiple Reconcilers in
    /// one test). Dispose drains the Reconciler.
    /// </summary>
    public sealed class ReconcilerScope : IDisposable
    {
        // Internal because Reconciler is an internal runtime type; the test assemblies that read this
        // reach it via TestUtilities' InternalsVisibleTo grants (AssemblyInfo.cs).
        internal Reconciler Reconciler { get; }
        public VisualElement Root { get; }

        public ReconcilerScope()
        {
            Reconciler = new Reconciler();
            Root = new VisualElement();
        }

        public void Dispose() => Reconciler.Dispose();
    }

    /// <summary>
    /// Base class for fixtures that use a single Reconciler + VisualElement pair throughout.
    /// <para>
    /// Subclasses inherit <see cref="Reconciler"/> (internal — reached via TestUtilities' InternalsVisibleTo)
    /// and <see cref="Root"/>. Lifecycle methods are virtual; override and call <c>base</c> when extending.
    /// </para>
    /// </summary>
    public abstract class ReconcilerTestFixture
    {
        internal Reconciler? Reconciler { get; private set; }
        protected VisualElement? Root { get; private set; }

        [SetUp]
        public virtual void SetUp()
        {
            Reconciler = new Reconciler();
            Root = new VisualElement();
        }

        [TearDown]
        public virtual void TearDown()
        {
            Reconciler?.Dispose();
            Reconciler = null;
            Root = null;
        }
    }
}

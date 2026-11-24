using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that each of <see cref="InheritedStyleHash"/>'s two paths stores the hash UI Toolkit's own style pass
    /// stored for an element: the compiled accessor an editor builds, and the reflection an IL2CPP player reads through,
    /// reached here past the editor's dispatch. And that <see cref="InheritedStyleHash.Refresh"/> takes the second where
    /// there is no first, and through the first allocates nothing. Each case replaces the stored hash with one the pass
    /// did not store before it refreshes. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class InheritedStyleHashTests
    {
        private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;

        private static readonly FieldInfo StoredHashField = EngineMember.ElementInheritedStylesHash.ResolveField()!;

        private static readonly MethodInfo? ReflectionPath = typeof(InheritedStyleHash).GetMethod("RefreshByReflection", Hidden);

#if !ENABLE_IL2CPP
        private static readonly FieldInfo? CompiledField = typeof(InheritedStyleHash).GetField("s_compiled", Hidden);

        // The accessor the editor built, put back after a case takes it away.
        private object? _compiled;
#endif

        private HeadlessEditorPanelHost _host;
        private VisualElement _element;
        private int _passStored;

        [SetUp]
        public void SetUp()
        {
#if !ENABLE_IL2CPP
            _compiled = CompiledField?.GetValue(null);
#endif
            _host = new HeadlessEditorPanelHost();
            _element = new VisualElement();
            _element.style.color = new Color(0.7891f, 0.2346f, 0.5679f);
            _element.style.fontSize = 13.5f;
            _host.Root.Add(_element);
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            _passStored = StoredHash(_element);
        }

        [TearDown]
        public void TearDown()
        {
#if !ENABLE_IL2CPP
            CompiledField?.SetValue(null, _compiled);
#endif
            _host?.Dispose();
            _host = null;
        }

        private static int StoredHash(VisualElement element) => (int)StoredHashField.GetValue(element);

        private int ReplaceStoredHash()
        {
            var replaced = ~_passStored;
            StoredHashField.SetValue(_element, replaced);
            return replaced;
        }

        [Test]
        public void Given_AStyledElementWhoseStoredHashWasReplaced_When_TheReflectionPathRefreshesIt_Then_ItStoresTheHashTheStylePassStored()
        {
            // Arrange
            ReplaceStoredHash();

            // Act
            ReflectionPath?.Invoke(null, new object[] { _element });

            // Assert — null where InheritedStyleHash declares no reflection path, which a hash can never equal.
            Assert.That(ReflectionPath != null ? StoredHash(_element) : (int?)null, Is.EqualTo(_passStored));
        }

        [Test]
        public void Given_NoCompiledAccessor_When_AReplacedHashIsRefreshed_Then_RefreshStoresTheHashTheStylePassStored()
        {
            // Arrange — no accessor, as an IL2CPP player has none.
#if !ENABLE_IL2CPP
            CompiledField?.SetValue(null, null);
            var withoutAccessor = CompiledField != null && CompiledField.GetValue(null) == null;
#else
            var withoutAccessor = true;
#endif
            ReplaceStoredHash();

            // Act
            InheritedStyleHash.Refresh(_element);

            // Assert — null where the accessor could not be taken away, which a hash can never equal.
            Assert.That(withoutAccessor ? StoredHash(_element) : (int?)null, Is.EqualTo(_passStored));
        }

#if !ENABLE_IL2CPP
        [Test]
        public void Given_AStyledElementWhoseStoredHashWasReplaced_When_TheCompiledAccessorRefreshesIt_Then_ItStoresTheHashTheStylePassStored()
        {
            // Arrange
            var compiled = (Action<VisualElement>?)CompiledField?.GetValue(null);
            ReplaceStoredHash();

            // Act
            compiled?.Invoke(_element);

            // Assert — null where the editor built no compiled accessor, which a hash can never equal.
            Assert.That(compiled != null ? StoredHash(_element) : (int?)null, Is.EqualTo(_passStored));
        }

        [Test]
        public void Given_AStyledElementWhoseStoredHashWasReplaced_When_ItIsRefreshed_Then_NothingIsAllocated()
        {
            // Arrange — each window follows a replacement, so each refresh reads the hash and writes it; one refresh first
            // builds what the type builds once.
            InheritedStyleHash.Refresh(_element);
            var replaced = 0;
            Action replace = () => replaced = ReplaceStoredHash();
            Action refresh = () => InheritedStyleHash.Refresh(_element);

            // Act
            var blocks = GCAllocationProbe.MedianBlocksDuring(replace, refresh);

            // Assert — none, where the canary's are counted and the last refresh wrote over the hash put in its place.
            var rewrote = StoredHash(_element) != replaced;
            Assert.That(GCAllocationCanary.BlocksForKnownArray() > 0 && rewrote ? blocks : -1, Is.Zero);
        }
#endif
    }
}

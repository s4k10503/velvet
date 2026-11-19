using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// What an on-panel reader of a box-shadow variable does where its variable is unresolved: it reads again after the
    /// styles are applied, takes the layout the write leaves as current, and writes its unset where it cannot wait.
    /// </summary>
    internal sealed class CustomPropertyDeferredUnsetPanelTests : PanelTestBase
    {
        private const string ReaderSheet = "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/BoxShadowReaderRule.uss";
        private const string RuleSheet = "Packages/com.velvet.core/Runtime/Styling/Tests/Editor/CustomPropertyRule.uss";
        private const string Declaration = "[--x:0_0_5px_#ff0000]";
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private VisualElement Mount(string classes, bool style)
        {
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "card", className: classes));
            var card = _window.rootVisualElement.Q<VisualElement>("card");
            if (style)
            {
                EditorPanelTestHelpers.ForcePanelUpdate(card.panel);
            }
            return card;
        }

        private float Ring(VisualElement card)
        {
            var binding = RingOverlay.TryGet(card);
            return binding == null ? 0f : binding.Spec.RingSpread;
        }

        private ReconcilerContext Context => _mounted.Root.Reconciler.Context;

        // The reader of a stylesheet variable, read at a tick that comes before the element's first style pass: the
        // variable is known only once that pass has run, which the reader forces itself.
        private VisualElement ReadBeforeTheFirstStylePass()
        {
            _window.rootVisualElement.styleSheets.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(ReaderSheet));
            var card = Mount("shadow-var-host ring-2 shadow-[var(--x)]", style: false);
            EditorPanelTestHelpers.DriveSchedulerOnce(card.panel);
            return card;
        }

        // GREEN_ON_BASE(characterization): the base already reads the variable again once the styles are applied, and keeps the ring.
        [Test]
        public void Given_AShadowReaderOfAStylesheetVariable_When_ATickComesBeforeTheFirstStylePass_Then_TheRingIsKept()
        {
            // Act
            var card = ReadBeforeTheFirstStylePass();

            // Assert
            Assert.That(Ring(card), Is.EqualTo(2f));
        }

        // GREEN_ON_BASE(characterization): the base already reads the variable again into an empty buffer, so the shadow it resolves is drawn.
        [Test]
        public void Given_AShadowReaderOfAStylesheetVariable_When_ATickComesBeforeTheFirstStylePass_Then_TheShadowIsDrawn()
        {
            // Act
            var card = ReadBeforeTheFirstStylePass();

            // Assert
            Assert.That(Context.ShadowBindings.ContainsKey(card), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base already takes the panel's layout as current after the unset it writes.
        [Test]
        public void Given_APanelLaidOutAsItsLastPassLeftIt_When_AShadowReaderWritesTheUnsetThatDiscardsTheShadow_Then_TheLayoutIsStillCurrent()
        {
            // Arrange
            var card = Mount("ring-2 shadow-[var(--x)]", style: true);
            StyleCustomProperties.Toggle(card, Declaration, true, StyleLayerPriority.Base, 0, Context);
            var control = Ring(card);

            // Act
            StyleCustomProperties.Toggle(card, Declaration, false, StyleLayerPriority.Base, 0, Context);

            // Assert — false where the declaration did not give the ring back, which the expected value is not.
            Assert.That(control == 2f && FiberLayoutReflow.IsCurrent(card.panel), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base already writes the unset where the styles cannot be applied twice in one traversal.
        [Test]
        public void Given_AShadowReaderReadTwiceInsideTheStylePass_When_ItsVariableIsUnresolved_Then_ItWritesItsUnset()
        {
            // Arrange
            _window.rootVisualElement.styleSheets.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(RuleSheet));
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "card", className: "css-var-host"));
            var card = _window.rootVisualElement.Q<VisualElement>("card");
            var read = false;
            card.RegisterCallback<CustomStyleResolvedEvent>(_ =>
            {
                if (read)
                {
                    return;
                }
                read = true;
                StyleCustomProperties.Toggle(card, "shadow-[var(--velvet-undeclared)]", true, StyleLayerPriority.Base, 0, Context);
                typeof(StyleCustomProperties).GetMethod("ReresolveAll", Any)!.Invoke(null, new object[] { card });
            });

            // Act
            EditorPanelTestHelpers.ForcePanelUpdate(card.panel);

            // Assert — false where the style pass never reached the callback, which the expected value is not.
            Assert.That(read && ResolvedFlagOfFirstReader(card), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base already reads a reader that waited inside the style pass at the next tick.
        [Test]
        public void Given_AShadowReaderThatWaitedInsideTheStylePassAndTheVariableContextTheTickPollsIsUnchanged_When_ATickRuns_Then_ItWritesItsUnset()
        {
            // Arrange
            _window.rootVisualElement.styleSheets.Add(UnityEditor.AssetDatabase.LoadAssetAtPath<StyleSheet>(RuleSheet));
            _mounted = V.Mount(_window.rootVisualElement, V.Div(name: "card", className: "css-var-host"));
            var card = _window.rootVisualElement.Q<VisualElement>("card");
            var waited = false;
            card.RegisterCallback<CustomStyleResolvedEvent>(_ =>
            {
                if (waited)
                {
                    return;
                }
                StyleCustomProperties.Toggle(card, "shadow-[var(--velvet-undeclared)]", true, StyleLayerPriority.Base, 0, Context);
                waited = !ResolvedFlagOfFirstReader(card);
                // The poll then finds the context it last read, leaving the scheduled read the only one.
                var table = typeof(StyleCustomProperties).GetField("s_consumers", Any)!.GetValue(null)!;
                var args = new object[] { card, null! };
                table.GetType().GetMethod("TryGetValue")!.Invoke(table, args);
                args[1].GetType().GetField("Context", Any)!.SetValue(args[1], StyleCustomProperties.ContextOf(card));
            });
            EditorPanelTestHelpers.ForcePanelUpdate(card.panel);

            // Act
            EditorPanelTestHelpers.DriveSchedulerOnce(card.panel);

            // Assert — false where the reader did not wait inside the pass, which the expected value is not.
            Assert.That(waited && ResolvedFlagOfFirstReader(card), Is.True);
        }

        private static bool ResolvedFlagOfFirstReader(VisualElement element)
        {
            var table = typeof(StyleCustomProperties).GetField("s_consumers", Any)!.GetValue(null)!;
            var args = new object[] { element, null! };
            table.GetType().GetMethod("TryGetValue")!.Invoke(table, args);
            var entries = (System.Collections.IList)args[1].GetType().GetField("Entries", Any)!.GetValue(args[1])!;
            return (bool)entries[0]!.GetType().GetField("Resolved", Any)!.GetValue(entries[0])!;
        }
    }
}

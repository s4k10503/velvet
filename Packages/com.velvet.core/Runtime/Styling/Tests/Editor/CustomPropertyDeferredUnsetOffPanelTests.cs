using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// An off-panel reader of a box-shadow variable that stops resolving waits for the attach: it is withdrawn and keeps
    /// no resolution of the variable it read before.
    /// </summary>
    internal sealed class CustomPropertyDeferredUnsetOffPanelTests
    {
        private const string Declaration = "[--x:0_0_5px_#ff0000]";
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // The reader's state field after its declaration is taken away, the sentinel where it held none before. The declaration is
        // toggled on its own, so the reader is the same consumer throughout.
        private static object ReaderStateAfterTheDeclarationGoes(string field, object sentinel)
        {
            using var scope = new ReconcilerScope();
            var context = ReconcilerContextProbe.Of(scope);
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[] { V.Div() });
            var element = scope.Root[0];
            StyleCustomProperties.Toggle(element, Declaration, true, StyleLayerPriority.Base, 0, context);
            StyleCustomProperties.Toggle(element, "shadow-[var(--x)]", true, StyleLayerPriority.Base, 0, context);
            var before = ReaderField(element, field);
            StyleCustomProperties.Toggle(element, Declaration, false, StyleLayerPriority.Base, 0, context);
            return before == null ? sentinel : ReaderField(element, field);
        }

        private static object ReaderField(VisualElement element, string field)
        {
            var table = typeof(StyleCustomProperties).GetField("s_consumers", Any)!.GetValue(null)!;
            var args = new object[] { element, null! };
            table.GetType().GetMethod("TryGetValue")!.Invoke(table, args);
            var entries = (System.Collections.IList)args[1].GetType().GetField("Entries", Any)!.GetValue(args[1])!;
            return entries[0]!.GetType().GetField(field, Any)!.GetValue(entries[0])!;
        }

        // GREEN_ON_BASE(characterization): the base already forgets the source a reader of a vanished variable resolved from. Drop
        // `consumer.Source = null;` from DefersUnset and it stays.
        [Test]
        public void Given_AnOffPanelShadowReaderThatResolved_When_ItsVariableIsNoLongerDeclared_Then_ItKeepsNoSource()
        {
            // Act
            var source = ReaderStateAfterTheDeclarationGoes("Source", "never resolved");

            // Assert
            Assert.That(source, Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base already withdraws what a reader of a vanished variable had applied. Drop
        // `Withdraw(target, consumer);` from DefersUnset and it stays.
        [Test]
        public void Given_AnOffPanelShadowReaderThatResolved_When_ItsVariableIsNoLongerDeclared_Then_ItKeepsNothingApplied()
        {
            // Act
            var applied = ReaderStateAfterTheDeclarationGoes("Applied", new string[0]);

            // Assert
            Assert.That(applied, Is.Null);
        }
    }
}

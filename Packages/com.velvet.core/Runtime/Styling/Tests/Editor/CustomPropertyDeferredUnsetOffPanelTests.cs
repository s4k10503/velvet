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
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // The reader's state field after its declaration is taken away, the sentinel where it held none before.
        private static object ReaderStateAfterTheDeclarationGoes(string field, object sentinel)
        {
            using var scope = new ReconcilerScope();
            var first = new VNode[] { V.Div(className: "[--x:0_0_5px_#ff0000] shadow-[var(--x)]") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), first);
            var element = scope.Root[0];
            var before = ReaderField(element, field);
            scope.Reconciler.Reconcile(scope.Root, first, new VNode[] { V.Div(className: "shadow-[var(--x)]") });
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

        // GREEN_ON_BASE(characterization): the base already forgets the source a reader of a vanished variable resolved from.
        [Test]
        public void Given_AnOffPanelShadowReaderThatResolved_When_ItsVariableIsNoLongerDeclared_Then_ItKeepsNoSource()
        {
            // Act
            var source = ReaderStateAfterTheDeclarationGoes("Source", "never resolved");

            // Assert
            Assert.That(source, Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base already withdraws what a reader of a vanished variable had applied.
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

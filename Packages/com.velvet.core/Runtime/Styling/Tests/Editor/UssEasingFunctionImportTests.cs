#nullable enable
using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.Editor;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins why UssCustomPropertyText quotes cubic-bezier(): UI Toolkit's own importer, given the raw sheet, leaves the
    /// custom property with no value, which a panel then fails to resolve.
    /// </summary>
    internal sealed class UssEasingFunctionImportTests
    {
        private const string Name = "--ease-snappy";

        // How many values the stock importer stored for the property, or -1 where it stored no such property.
        private static int StoredValueCount(string uss)
        {
            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            try
            {
                var impl = Activator.CreateInstance(StockStyleSheetImport.Impl!, true)!;
                try
                {
                    StockStyleSheetImport.Import!.Invoke(impl, new object[] { sheet, uss });
                }
                catch (TargetInvocationException)
                {
                    // The importer reports through its context, which this instance has none of; what it stored is read.
                }
                return ValueCount(sheet, Name);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sheet);
            }
        }

        private static int ValueCount(StyleSheet sheet, string name)
        {
            var rules = EngineMember.StyleSheetRules.ResolveProperty()?.GetValue(sheet) as Array;
            foreach (var rule in rules ?? Array.Empty<object>())
            {
                var properties = EngineMember.StyleRuleProperties.ResolveProperty()?.GetValue(rule) as Array;
                foreach (var property in properties ?? Array.Empty<object>())
                {
                    if (EngineMember.StylePropertyName.ResolveProperty()?.GetValue(property) as string == name)
                    {
                        return (EngineMember.StylePropertyValues.ResolveProperty()?.GetValue(property) as Array)?.Length ?? -1;
                    }
                }
            }
            return -1;
        }

        // GREEN_ON_BASE(characterization): the stock importer's handling of a raw cubic-bezier() custom property is an
        // engine fact the base meets as well; it is the cause UssCustomPropertyText's rewrite exists for.
        [Test]
        public void Given_ACustomPropertyHoldingCubicBezier_When_TheStockImporterReadsTheRawSheet_Then_ItStoresNoValue()
        {
            // Arrange
            var uss = ":root { " + Name + ": cubic-bezier(0.2, 0.8, 0.2, 1); }";

            // Act
            var stored = StoredValueCount(uss);

            // Assert
            Assert.That(stored, Is.EqualTo(0));
        }

        [Test]
        public void Given_TheSameCustomPropertyRewritten_When_TheStockImporterReadsIt_Then_ItStoresTheQuotedText()
        {
            // Arrange
            var uss = UssCustomPropertyText.Rewrite(":root { " + Name + ": cubic-bezier(0.2, 0.8, 0.2, 1); }");

            // Act
            var stored = StoredValueCount(uss);

            // Assert
            Assert.That(stored, Is.EqualTo(1));
        }
    }
}

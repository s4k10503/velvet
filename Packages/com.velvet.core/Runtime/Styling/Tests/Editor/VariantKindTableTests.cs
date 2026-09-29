using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds the guarantee a discard-free switch over <see cref="StyleVariantKind"/> gave at compile time: every
    /// table classifying the kinds names every member. The tables are found by reflection so one added later is
    /// held without being listed here.
    /// </summary>
    [TestFixture]
    internal sealed class VariantKindTableTests
    {
        // The four classifications that were switches: relational source, breakpoint, layer, stacked source.
        private const int KnownTables = 4;

        [Test]
        public void Given_EveryVariantKindTableInTheRuntime_When_EveryNamedKindIsLookedUp_Then_NoneIsMissing()
        {
            // Arrange
            var tables = typeof(StyleVariantKind).Assembly.GetTypes()
                .SelectMany(type => type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                .Where(field => field.FieldType.IsGenericType
                    && field.FieldType.GetGenericTypeDefinition().Name == "VariantKindTable`1")
                .ToList();

            // Act
            var missing = new List<string>();
            foreach (var field in tables)
            {
                var covers = field.FieldType.GetMethod("Covers")!;
                var table = field.GetValue(null);
                foreach (StyleVariantKind kind in Enum.GetValues(typeof(StyleVariantKind)))
                {
                    if (!(bool)covers.Invoke(table, new object[] { kind }))
                    {
                        missing.Add($"{field.DeclaringType!.Name}.{field.Name} lacks {kind}");
                    }
                }
            }

            // Assert — the count rides along, since a reflection that found no table reports nothing missing.
            Assert.That((tables.Count >= KnownTables, string.Join("\n", missing)), Is.EqualTo((true, "")));
        }
    }
}

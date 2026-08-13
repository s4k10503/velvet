using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <see cref="StyleArbitraryLonghands"/> — the map from each <see cref="ArbitraryProperty"/> to the
    /// <see cref="StyleLonghand"/> slots it writes — by re-deriving it rather than restating it: every member
    /// is applied to a bare element through <see cref="StyleArbitraryValueResolver.Apply"/> and the inline
    /// slots that came away non-<see cref="StyleKeyword.Null"/> are read back beside its declared row.
    /// <see cref="StyleClassProjection"/> reads that map to decide whether an inline arbitrary layer and a
    /// USS utility class are contending for one slot. What a wrong row costs a user is stated on the one
    /// case that covered a row before this fixture, in <see cref="VariantClassProjectionPanelTests"/>.
    /// </summary>
    /// <remarks>
    /// Panel-free by design, like <see cref="MotionPropertyChannelTests"/>: every reading is of the INLINE
    /// style the resolver writes, which needs no layout pass, no stylesheet and no panel. The composed-filter
    /// family is held out of the map on purpose and gets its own case below rather than a silent skip — its
    /// rows are the inverse claim, failing when one is filled IN.
    /// </remarks>
    [TestFixture]
    internal sealed class StyleArbitraryLonghandTableTests
    {
        // The members whose row is compared to their probe directly — everything the composed-filter family
        // does not hold out. A literal rather than the complement of the family, so a family that swallowed
        // a mapped member cannot shrink both sides of the comparison together. Updated deliberately when a
        // MAPPED property is added; a filter member added to the resolver's set leaves it where it is.
        private const int MappedPropertyCount = 60;

        private readonly List<UnityEngine.Object> _spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var spawned in _spawned)
            {
                if (spawned != null)
                {
                    UnityEngine.Object.DestroyImmediate(spawned);
                }
            }
            _spawned.Clear();
        }

        // GREEN_ON_BASE(characterization): every row already agrees with what the property writes; the
        // case exists so a later row cannot drift away from it unnoticed.
        [Test]
        public void Given_EveryArbitraryPropertyOutsideTheFilterFamily_When_ItsInlineWritesAreProbed_Then_TheyMatchItsDeclaredRow()
        {
            // Arrange
            var family = ComposedFilterFamily();

            // Act
            var compared = 0;
            var problems = new List<string>();
            foreach (ArbitraryProperty property in Enum.GetValues(typeof(ArbitraryProperty)))
            {
                if (family.Contains(property))
                {
                    continue;
                }
                compared++;
                var declared = DeclaredRow(property);
                var written = ProbeInlineWrites(property);
                if (declared != written)
                {
                    problems.Add($"{property}: row=[{declared}] writes=[{written}]");
                }
            }
            if (compared != MappedPropertyCount)
            {
                problems.Add($"compared {compared} members, MappedPropertyCount says {MappedPropertyCount}");
            }

            // Assert — the population is folded into the message rather than compared beside it, so the
            // failure names the literal a maintainer has to update. The problems are joined into one string
            // for the reason MotionPropertyChannelTests joins its own.
            Assert.That(string.Join("; ", problems), Is.Empty);
        }

        // GREEN_ON_BASE(characterization): the family is already held out on purpose; the case pins that
        // decision against what its members actually write.
        [Test]
        public void Given_TheComposedFilterFamily_When_ItsRowsAreReadBesideWhatItWrites_Then_EveryRowIsEmpty()
        {
            // Arrange — the family is held out of the map on purpose, on the grounds StyleArbitraryLonghands'
            // own header states. Reading what each member writes beside its row is what keeps "empty" a
            // decision rather than a member that turns out to write nothing at all.
            var family = ComposedFilterFamily();

            // Act
            var problems = new List<string>();
            foreach (var property in family)
            {
                var declared = DeclaredRow(property);
                var written = ProbeInlineWrites(property);
                if (declared.Length != 0 || written != nameof(StyleLonghand.Filter))
                {
                    problems.Add($"{property}: row=[{declared}] writes=[{written}]");
                }
            }
            // The same inequality the case above states from the other side, so nothing can move one
            // population without moving the other: it is one partition check, carried by both cases so
            // neither depends on the other having run.
            var room = Enum.GetValues(typeof(ArbitraryProperty)).Length - MappedPropertyCount;
            if (family.Count != room)
            {
                problems.Add($"{family.Count} filter members, MappedPropertyCount leaves room for {room}");
            }

            // Assert
            Assert.That(string.Join("; ", problems), Is.Empty);
        }

        // GREEN_ON_BASE(characterization): the vocabulary already reaches every inline slot one-to-one; the
        // case keeps a later gap or a mis-aimed accessor from reading as agreement.
        [Test]
        public void Given_EveryInlineStyleSlot_When_ReachedThroughTheLonghandVocabulary_Then_ExactlyOneLonghandReachesEach()
        {
            // Arrange — the probe sees a write only through this mapping, so a slot no longhand reaches is one
            // a property could write unobserved, and two longhands reaching one slot means at least one of
            // them is aimed at the wrong accessor. A newly unreached slot is added to the hand-kept
            // vocabulary in Generators~/src/Velvet.StyleTable/UssPropertyVocabulary.cs, not to the bundled
            // stylesheets. The one slot IStyle exposes that the vocabulary omits is a SHORTHAND, which
            // StyleLonghand holds none of; the ObsoleteAttribute filter that skips it here is the one
            // PooledElementStyleGhostTests applies, on the reason stated there.
            var reached = new Dictionary<string, List<string>>();
            foreach (var slot in s_inlineSlots)
            {
                var name = slot.Value.Accessor.Name;
                if (!reached.TryGetValue(name, out var owners))
                {
                    reached[name] = owners = new List<string>();
                }
                owners.Add(slot.Key.ToString());
            }

            // Act
            var unresolved = Enum.GetValues(typeof(StyleLonghand)).Cast<StyleLonghand>()
                .Where(longhand => !s_inlineSlots.ContainsKey(longhand))
                .Select(longhand => longhand.ToString())
                .ToList();
            var shared = reached.Where(entry => entry.Value.Count > 1)
                .Select(entry => $"{entry.Key} <- {string.Join("+", entry.Value)}")
                .ToList();
            var unreached = typeof(IStyle).GetProperties()
                .Where(property => IsStyleSlot(property)
                    && property.GetCustomAttribute<ObsoleteAttribute>() == null
                    && !reached.ContainsKey(property.Name))
                .Select(property => property.Name)
                .ToList();

            // Assert — an empty vocabulary leaves every slot unreached and an empty IStyle leaves every
            // longhand unresolved, so neither side can go vacuous without the other reporting it.
            Assert.That((string.Join(",", unresolved), string.Join(",", shared), string.Join(",", unreached)),
                Is.EqualTo((string.Empty, string.Empty, string.Empty)));
        }

        // The members routed to the composed filter applier, taken from the resolver's own membership set so
        // a filter added there is covered without anyone remembering.
        private static HashSet<ArbitraryProperty> ComposedFilterFamily()
        {
            var field = typeof(StyleArbitraryValueResolver)
                    .GetField("s_filterSet", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new InvalidOperationException(
                    "StyleArbitraryValueResolver.s_filterSet no longer exists to derive the family from");
            var family = new HashSet<ArbitraryProperty>((HashSet<ArbitraryProperty>)field.GetValue(null));
            // filter-[name:args] is keyed by the registered name rather than by this property, so it never
            // reaches the projection as a layer at all and the resolver's set does not carry it.
            family.Add(ArbitraryProperty.FilterCustom);
            return family;
        }

        private static string DeclaredRow(ArbitraryProperty property)
        {
            var row = StyleArbitraryLonghands.Of(property);
            return Join(Enum.GetValues(typeof(StyleLonghand)).Cast<StyleLonghand>().Where(row.Contains));
        }

        private string ProbeInlineWrites(ArbitraryProperty property)
        {
            var probe = new VisualElement();
            StyleArbitraryValueResolver.Apply(probe, ProbeValue(property));
            var style = probe.style;
            return Join(s_inlineSlots
                .Where(slot => (StyleKeyword)slot.Value.Keyword.GetValue(slot.Value.Accessor.GetValue(style))
                    != StyleKeyword.Null)
                .Select(slot => slot.Key));
        }

        private static string Join(IEnumerable<StyleLonghand> longhands) =>
            string.Join("+", longhands.Select(longhand => longhand.ToString())
                .OrderBy(name => name, StringComparer.Ordinal));

        // The magnitude and the colour are arbitrary: which slots the payload lands in is what is under test.
        private ArbitraryStyle ProbeValue(ArbitraryProperty property)
        {
            if (property == ArbitraryProperty.FilterCustom)
            {
                var definition = ScriptableObject.CreateInstance<FilterFunctionDefinition>();
                definition.parameters = new[]
                {
                    new FilterParameterDeclaration
                    {
                        name = "amount",
                        interpolationDefaultValue = new FilterParameter(0.5f),
                    },
                };
                _spawned.Add(definition);
                return new ArbitraryStyle(property,
                    new CustomFilterValue("probe", definition, new[] { new FilterParameter(0.5f) }));
            }
            return MotionPropertyClassParser.IsColor(property)
                ? new ArbitraryStyle(property, Color.red)
                : new ArbitraryStyle(property, 7f, LengthUnit.Pixel);
        }

        private static readonly Dictionary<StyleLonghand, (PropertyInfo Accessor, PropertyInfo Keyword)>
            s_inlineSlots = BuildInlineSlots();

        // A longhand missing from this map would make every property that writes it read as writing nothing,
        // and a row naming neither would then agree with the probe; the slot-reach case above is what fails
        // when one stops resolving or lands on another longhand's accessor.
        private static Dictionary<StyleLonghand, (PropertyInfo, PropertyInfo)> BuildInlineSlots()
        {
            var slots = new Dictionary<StyleLonghand, (PropertyInfo, PropertyInfo)>();
            foreach (StyleLonghand longhand in Enum.GetValues(typeof(StyleLonghand)))
            {
                var accessor = typeof(IStyle).GetProperty(AccessorName(longhand));
                if (accessor != null && IsStyleSlot(accessor))
                {
                    slots[longhand] = (accessor, accessor.PropertyType.GetProperty("keyword"));
                }
            }
            return slots;
        }

        private static bool IsStyleSlot(PropertyInfo property) =>
            property.PropertyType.GetProperty("keyword")?.PropertyType == typeof(StyleKeyword);

        private static string AccessorName(StyleLonghand longhand)
        {
            if (longhand == StyleLonghand.UnityFontStyle)
            {
                return "unityFontStyleAndWeight";
            }
            var name = longhand.ToString();
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }
    }
}

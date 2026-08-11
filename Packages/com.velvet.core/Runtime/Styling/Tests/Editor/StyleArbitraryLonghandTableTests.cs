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
    /// slots that came away non-<see cref="StyleKeyword.Null"/> are compared against its declared row.
    /// <see cref="StyleClassProjection"/> reads that map to decide whether an inline arbitrary layer and a
    /// USS utility class are contending for one slot, so a row that names the wrong slot costs the class its
    /// win with no diagnostic: with the transform-origin row pointed at another slot,
    /// <c>origin-[10%_20%] md:origin-top-left</c> keeps painting the bracket pivot, because the layer's slot
    /// set no longer overlaps what the class claims and no floor is recorded.
    /// </summary>
    /// <remarks>
    /// Panel-free by design, like <see cref="MotionPropertyChannelTests"/>: every reading is of the INLINE
    /// style the resolver writes, which needs no layout pass, no stylesheet and no panel. The composed-filter
    /// family is held out of the map on purpose and gets its own case below rather than a silent skip. GWT,
    /// one assert per case.
    /// </remarks>
    [TestFixture]
    internal sealed class StyleArbitraryLonghandTableTests
    {
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
            var properties = Enum.GetValues(typeof(ArbitraryProperty)).Cast<ArbitraryProperty>().ToList();

            // Act
            var compared = 0;
            var disagreements = new List<string>();
            foreach (var property in properties)
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
                    disagreements.Add($"{property}: row=[{declared}] writes=[{written}]");
                }
            }

            // Assert — the compared population rides along so a loop that stopped reaching the members cannot
            // leave this green with nothing derived. The disagreements are joined into the message for the
            // reason MotionPropertyChannelTests joins its own.
            Assert.That((compared, string.Join("; ", disagreements)),
                Is.EqualTo((properties.Count - family.Count, string.Empty)));
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
            var offenders = new List<string>();
            foreach (var property in family)
            {
                var declared = DeclaredRow(property);
                var written = ProbeInlineWrites(property);
                if (declared.Length != 0 || written != nameof(StyleLonghand.Filter))
                {
                    offenders.Add($"{property}: row=[{declared}] writes=[{written}]");
                }
            }

            // Assert
            Assert.That((family.Count > 0, string.Join("; ", offenders)), Is.EqualTo((true, string.Empty)));
        }

        // GREEN_ON_BASE(characterization): the reading instrument the two cases above depend on already
        // covers the whole vocabulary; the case keeps a later gap from reading as agreement.
        [Test]
        public void Given_TheLonghandVocabulary_When_EachMemberIsMappedToItsInlineSlot_Then_EveryOneResolves()
        {
            // Arrange / Act
            var unresolved = Enum.GetValues(typeof(StyleLonghand)).Cast<StyleLonghand>()
                .Where(longhand => !s_inlineSlots.ContainsKey(longhand))
                .Select(longhand => longhand.ToString())
                .ToList();

            // Assert — an empty vocabulary would likewise report nothing unresolved, so the population the
            // mapping did reach is part of the claim.
            Assert.That((s_inlineSlots.Count > 0, string.Join(",", unresolved)),
                Is.EqualTo((true, string.Empty)));
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
        // and a row naming neither would then agree with the probe; the vocabulary case above is what fails
        // when one stops resolving.
        private static Dictionary<StyleLonghand, (PropertyInfo, PropertyInfo)> BuildInlineSlots()
        {
            var slots = new Dictionary<StyleLonghand, (PropertyInfo, PropertyInfo)>();
            foreach (StyleLonghand longhand in Enum.GetValues(typeof(StyleLonghand)))
            {
                var accessor = typeof(IStyle).GetProperty(AccessorName(longhand));
                var keyword = accessor?.PropertyType.GetProperty("keyword");
                if (accessor != null && keyword != null)
                {
                    slots[longhand] = (accessor, keyword);
                }
            }
            return slots;
        }

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

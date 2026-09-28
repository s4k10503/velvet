using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the font utility contract spanning <see cref="StyleFontClass"/> (class → intent),
    /// <see cref="StyleFontResolver"/> (intent → inline style composition), and the
    /// <see cref="VelvetFonts"/> registry (family/weight/italic resolution, faux-style fallback, and
    /// change notification).
    /// <list type="bullet">
    /// <item>A class list folds into one <see cref="FontIntent"/>: <c>font-&lt;family&gt;</c> sets the
    /// family, <c>font-thin</c>…<c>font-black</c> (and <c>font-[550]</c>) set the weight, and
    /// <c>italic</c>/<c>not-italic</c>/<c>bold-italic</c> set the italic axis. Later classes of the same
    /// facet win, and the three facets coexist so <c>font-bold italic</c> composes.</item>
    /// <item>With no registered family a request folds to the binary <c>-unity-font-style</c> threshold
    /// (weight ≥ 600 → bold); with a family it selects the weight CSS's font matching selects, among the
    /// faces of the requested style first, and reports the faux bold/italic the chosen asset cannot
    /// satisfy.</item>
    /// <item>Mutating the registry raises <see cref="VelvetFonts.FontsChanged"/>.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class StyleFontTests
    {
        [SetUp]
        public void SetUp() => VelvetFonts.Clear();

        [TearDown]
        public void TearDown() => VelvetFonts.Clear();

        #region StyleFontClass — recognition

        static IEnumerable<TestCaseData> HasFontClassCases()
        {
            yield return new TestCaseData((object)new[] { "p-4", "text-lg", "flex" }, false)
                .SetName("Given_NonFontClasses_When_CheckedForFontClass_Then_ReturnsFalse");
            yield return new TestCaseData((object)null, false)
                .SetName("Given_NullClasses_When_CheckedForFontClass_Then_ReturnsFalse");
            yield return new TestCaseData((object)new[] { "font-bold" }, true)
                .SetName("Given_FontBoldClass_When_CheckedForFontClass_Then_ReturnsTrue");
            yield return new TestCaseData((object)new[] { "italic" }, true)
                .SetName("Given_ItalicClass_When_CheckedForFontClass_Then_ReturnsTrue");
            yield return new TestCaseData((object)new[] { "bold-italic" }, true)
                .SetName("Given_BoldItalicClass_When_CheckedForFontClass_Then_ReturnsTrue");
            yield return new TestCaseData((object)new[] { "font-sans" }, true)
                .SetName("Given_FontSansClass_When_CheckedForFontClass_Then_ReturnsTrue");
        }

        [TestCaseSource(nameof(HasFontClassCases))]
        public void HasFontClass_RecognizesFontUtilityClasses(string[] classes, bool expected)
        {
            // When / Then
            Assert.That(StyleFontClass.HasFontClass(classes), Is.EqualTo(expected));
        }

        static IEnumerable<TestCaseData> IsArbitraryFontClassCases()
        {
            // Owned by StyleFontResolver → kept out of the USS class list.
            yield return new TestCaseData("font-[Inter]", true)
                .SetName("Given_BracketFamily_When_CheckedForArbitraryFontClass_Then_True");
            yield return new TestCaseData("font-[addr:Fonts/Inter]", true)
                .SetName("Given_BracketAddrFamily_When_CheckedForArbitraryFontClass_Then_True");
            yield return new TestCaseData("font-[550]", true)
                .SetName("Given_BracketWeight_When_CheckedForArbitraryFontClass_Then_True");

            // The important-modifier bang must not defeat this guard: StripImportant strips it at the 3
            // guard call sites AFTER this check runs, so the predicate itself has to recognize the bang'd
            // form or the bare bracket token leaks into the USS class list once the bang is gone.
            yield return new TestCaseData("!font-[weight:550]", true)
                .SetName("Given_BangPrefixedBracketWeight_When_CheckedForArbitraryFontClass_Then_True");

            // Non-bracket font classes stay in the list as the USS fallback; non-font brackets are unrelated.
            yield return new TestCaseData("font-bold", false)
                .SetName("Given_FontBold_When_CheckedForArbitraryFontClass_Then_False");
            yield return new TestCaseData("font-sans", false)
                .SetName("Given_FontSans_When_CheckedForArbitraryFontClass_Then_False");
            yield return new TestCaseData("w-[120px]", false)
                .SetName("Given_NonFontBracket_When_CheckedForArbitraryFontClass_Then_False");
            yield return new TestCaseData(null, false)
                .SetName("Given_Null_When_CheckedForArbitraryFontClass_Then_False");
        }

        [TestCaseSource(nameof(IsArbitraryFontClassCases))]
        public void IsArbitraryFontClass_OnlyBracketFontFormsMatch(string className, bool expected)
        {
            // When / Then
            Assert.That(StyleFontClass.IsArbitraryFontClass(className), Is.EqualTo(expected));
        }

        #endregion

        #region StyleFontClass — extraction

        [Test]
        public void Given_FamilyClass_When_Extracted_Then_SetsFamilyOnly()
        {
            Assume.That(StyleFontClass.TryExtract(new[] { "font-sans" }, out var intent), Is.True);
            Assert.That((intent.HasFamily, intent.Family, intent.HasWeight, intent.HasItalic),
                Is.EqualTo((true, "sans", false, false)));
        }

        [Test]
        public void Given_WeightKeywordClass_When_Extracted_Then_SetsWeight()
        {
            Assume.That(StyleFontClass.TryExtract(new[] { "font-semibold" }, out var intent), Is.True);
            Assert.That((intent.HasWeight, intent.Weight), Is.EqualTo((true, VelvetFontWeight.SemiBold)));
        }

        [TestCase("italic", true, TestName = "Given_ItalicClass_When_Extracted_Then_SetsItalicTrue")]
        [TestCase("not-italic", false, TestName = "Given_NotItalicClass_When_Extracted_Then_SetsItalicFalse")]
        public void Given_ItalicAxisClass_When_Extracted_Then_SetsItalicAxis(string className, bool expectedItalic)
        {
            // Given
            Assume.That(StyleFontClass.TryExtract(new[] { className }, out var intent), Is.True);
            // When / Then
            Assert.That((intent.HasItalic, intent.Italic), Is.EqualTo((true, expectedItalic)));
        }

        [Test]
        public void Given_BoldItalicClass_When_Extracted_Then_SetsBoldWeightAndItalic()
        {
            Assume.That(StyleFontClass.TryExtract(new[] { "bold-italic" }, out var intent), Is.True);
            Assert.That((intent.Weight, intent.HasWeight, intent.Italic, intent.HasItalic),
                Is.EqualTo((VelvetFontWeight.Bold, true, true, true)));
        }

        [Test]
        public void Given_BoldAndItalicSeparateClasses_When_Extracted_Then_BothCompose()
        {
            Assume.That(StyleFontClass.TryExtract(new[] { "font-bold", "italic" }, out var intent), Is.True);
            Assert.That((intent.Weight, intent.Italic), Is.EqualTo((VelvetFontWeight.Bold, true)));
        }

        [Test]
        public void Given_TwoWeightClasses_When_Extracted_Then_LastWins()
        {
            Assume.That(StyleFontClass.TryExtract(new[] { "font-bold", "font-light" }, out var intent), Is.True);
            Assert.That(intent.Weight, Is.EqualTo(VelvetFontWeight.Light));
        }

        [Test]
        public void Given_TwoFamilyClasses_When_Extracted_Then_LastWins()
        {
            Assume.That(StyleFontClass.TryExtract(new[] { "font-sans", "font-mono" }, out var intent), Is.True);
            Assert.That(intent.Family, Is.EqualTo("mono"));
        }

        [Test]
        public void Given_ArbitraryNumericWeight_When_Extracted_Then_ParsesAsWeight()
        {
            Assume.That(StyleFontClass.TryExtract(new[] { "font-[550]" }, out var intent), Is.True);
            Assert.That((intent.HasWeight, (int)intent.Weight, intent.HasFamily), Is.EqualTo((true, 550, false)));
        }

        [Test]
        public void Given_ArbitraryWeightPrefixed_When_Extracted_Then_ParsesAsWeight()
        {
            Assume.That(StyleFontClass.TryExtract(new[] { "font-[weight:250]" }, out var intent), Is.True);
            Assert.That((int)intent.Weight, Is.EqualTo(250));
        }

        [Test]
        public void Given_ArbitraryFamilyName_When_Extracted_Then_ParsesAsFamily()
        {
            Assume.That(StyleFontClass.TryExtract(new[] { "font-[Inter]" }, out var intent), Is.True);
            Assert.That((intent.HasFamily, intent.Family), Is.EqualTo((true, "Inter")));
        }

        [Test]
        public void Given_ArbitraryAddressFamily_When_Extracted_Then_KeepsAddrPrefixAsFamily()
        {
            Assume.That(StyleFontClass.TryExtract(new[] { "font-[addr:Fonts/Inter]" }, out var intent), Is.True);
            Assert.That(intent.Family, Is.EqualTo("addr:Fonts/Inter"));
        }

        [Test]
        public void Given_NoFontClass_When_Extracted_Then_ReturnsFalse()
        {
            Assert.That(StyleFontClass.TryExtract(new[] { "p-4", "text-lg" }, out _), Is.False);
        }

        #endregion

        #region StyleFontResolver — gated entry points

        [Test]
        public void Given_ElementWithInlineFontStyle_When_ClassChangeDropsTheLastFontClass_Then_InlineStyleIsCleared()
        {
            var element = new Label();
            StyleFontResolver.Apply(element, new[] { "font-bold" });
            Assume.That(element.style.unityFontStyleAndWeight.keyword, Is.Not.EqualTo(StyleKeyword.Null),
                "Precondition: font-bold set an inline -unity-font-style.");

            // The old-side check in ApplyOnClassChange must clear the inline style even though the new
            // class list carries no font class.
            StyleFontResolver.ApplyOnClassChange(element, new[] { "font-bold" }, new[] { "text-lg" });

            Assert.That(element.style.unityFontStyleAndWeight.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        #endregion

        #region StyleFontResolver — style composition

        [TestCase(false, false, FontStyle.Normal, TestName = "Given_NoFlags_When_Composed_Then_Normal")]
        [TestCase(true, false, FontStyle.Bold, TestName = "Given_BoldFlag_When_Composed_Then_Bold")]
        [TestCase(false, true, FontStyle.Italic, TestName = "Given_ItalicFlag_When_Composed_Then_Italic")]
        [TestCase(true, true, FontStyle.BoldAndItalic, TestName = "Given_BoldAndItalicFlags_When_Composed_Then_BoldAndItalic")]
        public void Given_BoldAndItalicFlags_When_Composed_Then_ProducesMatchingFontStyle(bool bold, bool italic, FontStyle expected)
        {
            // When / Then
            Assert.That(StyleFontResolver.ComputeFontStyle(bold, italic), Is.EqualTo(expected));
        }

        #endregion

        #region VelvetFonts — resolution

        [Test]
        public void Given_NoRegisteredFamily_When_HeavyWeightResolved_Then_FoldsToFauxBoldWithNoAsset()
        {
            var resolved = VelvetFonts.Resolve(family: null, VelvetFontWeight.Black, italic: false);
            Assert.That((resolved.HasAsset, resolved.ResidualBold, resolved.ResidualItalic),
                Is.EqualTo((false, true, false)));
        }

        [Test]
        public void Given_NoRegisteredFamily_When_LightItalicResolved_Then_FoldsToFauxItalicOnly()
        {
            var resolved = VelvetFonts.Resolve(family: null, VelvetFontWeight.Light, italic: true);
            Assert.That((resolved.HasAsset, resolved.ResidualBold, resolved.ResidualItalic),
                Is.EqualTo((false, false, true)));
        }

        [Test]
        public void Given_RegisteredExactWeight_When_Resolved_Then_ReturnsAssetWithNoFauxStyle()
        {
            var asset = ScriptableObject.CreateInstance<FontAsset>();
            try
            {
                VelvetFonts.Register(new VelvetFontFamily("sans",
                    new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, upright = asset }));

                var resolved = VelvetFonts.Resolve("sans", VelvetFontWeight.Normal, italic: false);
                Assert.That(resolved.Asset, Is.SameAs(asset));
                Assert.That((resolved.ResidualBold, resolved.ResidualItalic), Is.EqualTo((false, false)));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void Given_FamilyWithoutHeavyWeight_When_BoldResolved_Then_PicksClosestAndFlagsFauxBold()
        {
            var regular = ScriptableObject.CreateInstance<FontAsset>();
            try
            {
                VelvetFonts.Register(new VelvetFontFamily("sans",
                    new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, upright = regular }));

                var resolved = VelvetFonts.Resolve("sans", VelvetFontWeight.Black, italic: false);
                Assert.That(resolved.Asset, Is.SameAs(regular));
                Assert.That(resolved.ResidualBold, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(regular);
            }
        }

        [Test]
        public void Given_FamilyWithOnlyUpright_When_ItalicResolved_Then_FlagsFauxItalic()
        {
            var upright = ScriptableObject.CreateInstance<FontAsset>();
            try
            {
                VelvetFonts.Register(new VelvetFontFamily("sans",
                    new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, upright = upright }));

                var resolved = VelvetFonts.Resolve("sans", VelvetFontWeight.Normal, italic: true);
                Assert.That(resolved.Asset, Is.SameAs(upright));
                Assert.That(resolved.ResidualItalic, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(upright);
            }
        }

        [Test]
        public void Given_DefaultFamilySet_When_FamilylessRequestResolved_Then_UsesDefaultFamilyAsset()
        {
            var asset = ScriptableObject.CreateInstance<FontAsset>();
            try
            {
                VelvetFonts.Register(new VelvetFontFamily("sans",
                    new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, upright = asset }));
                VelvetFonts.DefaultFamily = "sans";

                var resolved = VelvetFonts.Resolve(family: null, VelvetFontWeight.Normal, italic: false);
                Assert.That(resolved.Asset, Is.SameAs(asset));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        // Each available set puts the weight CSS selects on the other side of the request from the nearest
        // weight, or from the heavier of two equally near ones.
        [TestCase(500, new[] { 400, 600 }, 400)]
        [TestCase(400, new[] { 350, 500 }, 500)]
        [TestCase(480, new[] { 440, 510 }, 440)]
        [TestCase(300, new[] { 250, 320 }, 250)]
        [TestCase(600, new[] { 400, 900 }, 900)]
        public void Given_AFamilyWithoutTheRequestedWeight_When_Matched_Then_CssSearchOrderDecides(
            int requested, int[] available, int expected)
        {
            // Arrange
            var family = FamilyWithWeights(available);

            // Act
            var entry = family.FindClosestWeight((VelvetFontWeight)requested);

            // Assert
            Assert.That((int)entry.weight, Is.EqualTo(expected));
        }

        // GREEN_ON_BASE(characterization): here the weight CSS selects is also the nearest one, which the
        // base already returned; these pin the order within each pass and the exact match.
        [TestCase(300, new[] { 100, 200 }, 200)]
        [TestCase(600, new[] { 700, 900 }, 700)]
        [TestCase(450, new[] { 300, 420 }, 420)]
        [TestCase(450, new[] { 600, 700 }, 600)]
        [TestCase(400, new[] { 450, 500 }, 450)]
        [TestCase(300, new[] { 500, 700 }, 500)]
        [TestCase(700, new[] { 300, 500 }, 500)]
        [TestCase(400, new[] { 300, 400, 500 }, 400)]
        [TestCase(300, new[] { 200, 300, 400 }, 300)]
        [TestCase(700, new[] { 600, 700, 800 }, 700)]
        public void Given_AFamily_When_Matched_Then_CssSearchOrderAgreesWithTheNearestWeight(
            int requested, int[] available, int expected)
        {
            // Arrange
            var family = FamilyWithWeights(available);

            // Act
            var entry = family.FindClosestWeight((VelvetFontWeight)requested);

            // Assert
            Assert.That((int)entry.weight, Is.EqualTo(expected));
        }

        private static VelvetFontFamily FamilyWithWeights(int[] weights)
        {
            var entries = new VelvetFontWeightEntry[weights.Length];
            for (var i = 0; i < weights.Length; i++)
            {
                entries[i] = new VelvetFontWeightEntry { weight = (VelvetFontWeight)weights[i] };
            }
            return new VelvetFontFamily("sans", entries);
        }

        [Test]
        public void Given_AnItalicFaceOnlyAtALighterWeight_When_BoldItalicResolved_Then_TheItalicFaceIsFauxBolded()
        {
            // Arrange — CSS narrows by font-style before weight, so the regular italic beats the bold upright.
            var regularItalic = ScriptableObject.CreateInstance<FontAsset>();
            var boldUpright = ScriptableObject.CreateInstance<FontAsset>();
            try
            {
                VelvetFonts.Register(new VelvetFontFamily("sans",
                    new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, italic = regularItalic },
                    new VelvetFontWeightEntry { weight = VelvetFontWeight.Bold, upright = boldUpright }));

                // Act
                var resolved = VelvetFonts.Resolve("sans", VelvetFontWeight.Bold, italic: true);

                // Assert
                Assert.That((resolved.Asset == regularItalic, resolved.ResidualBold, resolved.ResidualItalic),
                    Is.EqualTo((true, true, false)));
            }
            finally
            {
                Object.DestroyImmediate(regularItalic);
                Object.DestroyImmediate(boldUpright);
            }
        }

        [Test]
        public void Given_AnUprightFaceOnlyAtAHeavierWeight_When_RegularUprightResolved_Then_TheUprightFaceIsSelected()
        {
            // Arrange — the upright sibling of the italic narrowing: the regular slot holds only an italic.
            var regularItalic = ScriptableObject.CreateInstance<FontAsset>();
            var boldUpright = ScriptableObject.CreateInstance<FontAsset>();
            try
            {
                VelvetFonts.Register(new VelvetFontFamily("sans",
                    new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, italic = regularItalic },
                    new VelvetFontWeightEntry { weight = VelvetFontWeight.Bold, upright = boldUpright }));

                // Act
                var resolved = VelvetFonts.Resolve("sans", VelvetFontWeight.Normal, italic: false);

                // Assert
                Assert.That((resolved.Asset == boldUpright, resolved.ResidualBold, resolved.ResidualItalic),
                    Is.EqualTo((true, false, false)));
            }
            finally
            {
                Object.DestroyImmediate(regularItalic);
                Object.DestroyImmediate(boldUpright);
            }
        }

        [Test]
        public void Given_AnItalicFaceOnlyByAddressAtALighterWeight_When_BoldItalicResolved_Then_TheItalicFaceIsFauxBolded()
        {
            // Arrange — an Addressables key is a face too. The cache is seeded so nothing is loaded.
            var regularItalic = ScriptableObject.CreateInstance<FontAsset>();
            var boldUpright = ScriptableObject.CreateInstance<FontAsset>();
            try
            {
                SeedAddressCache("Fonts/RegularItalic", regularItalic);
                VelvetFonts.Register(new VelvetFontFamily("sans",
                    new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, italicAddress = "Fonts/RegularItalic" },
                    new VelvetFontWeightEntry { weight = VelvetFontWeight.Bold, upright = boldUpright }));

                // Act
                var resolved = VelvetFonts.Resolve("sans", VelvetFontWeight.Bold, italic: true);

                // Assert
                Assert.That((resolved.Asset == regularItalic, resolved.ResidualBold, resolved.ResidualItalic),
                    Is.EqualTo((true, true, false)));
            }
            finally
            {
                Object.DestroyImmediate(regularItalic);
                Object.DestroyImmediate(boldUpright);
            }
        }

        [Test]
        public void Given_AnUprightFaceOnlyByAddressAtAHeavierWeight_When_RegularUprightResolved_Then_TheUprightFaceIsSelected()
        {
            // Arrange
            var regularItalic = ScriptableObject.CreateInstance<FontAsset>();
            var boldUpright = ScriptableObject.CreateInstance<FontAsset>();
            try
            {
                SeedAddressCache("Fonts/BoldUpright", boldUpright);
                VelvetFonts.Register(new VelvetFontFamily("sans",
                    new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, italic = regularItalic },
                    new VelvetFontWeightEntry { weight = VelvetFontWeight.Bold, uprightAddress = "Fonts/BoldUpright" }));

                // Act
                var resolved = VelvetFonts.Resolve("sans", VelvetFontWeight.Normal, italic: false);

                // Assert
                Assert.That((resolved.Asset == boldUpright, resolved.ResidualBold, resolved.ResidualItalic),
                    Is.EqualTo((true, false, false)));
            }
            finally
            {
                Object.DestroyImmediate(regularItalic);
                Object.DestroyImmediate(boldUpright);
            }
        }

        private static void SeedAddressCache(string key, FontAsset asset)
        {
            var field = typeof(VelvetFonts).GetField("_addrCache",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var cache = (Dictionary<string, FontAsset>)field.GetValue(null);
            cache[key] = asset;
        }

        #endregion

        #region VelvetFonts — change notification

        [Test]
        public void Given_FontsChangedSubscriber_When_FamilyRegistered_Then_EventFires()
        {
            var fired = 0;
            void Handler() => fired++;
            VelvetFonts.FontsChanged += Handler;
            try
            {
                VelvetFonts.Register(new VelvetFontFamily("sans",
                    new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal }));
                Assert.That(fired, Is.EqualTo(1));
            }
            finally
            {
                VelvetFonts.FontsChanged -= Handler;
            }
        }

        #endregion

        #region VelvetFonts — representation-agnostic registration

        [Test]
        public void Given_BatchOfFamilies_When_Registered_Then_AllResolveAndDefaultIsSet()
        {
            var families = new[]
            {
                new VelvetFontFamily("sans", new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal }),
                new VelvetFontFamily("mono", new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal }),
            };

            VelvetFonts.Register(families, defaultFamily: "sans");

            Assert.That(VelvetFonts.IsRegistered("sans"), Is.True);
            Assert.That(VelvetFonts.IsRegistered("mono"), Is.True);
            Assert.That(VelvetFonts.DefaultFamily, Is.EqualTo("sans"));
        }

        [Test]
        public void Given_BatchRegistration_When_Applied_Then_FontsChangedFiresOnce()
        {
            var families = new[]
            {
                new VelvetFontFamily("sans", new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal }),
                new VelvetFontFamily("mono", new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal }),
            };

            var fired = 0;
            void Handler() => fired++;
            VelvetFonts.FontsChanged += Handler;
            try
            {
                VelvetFonts.Register(families, defaultFamily: "sans");
                Assert.That(fired, Is.EqualTo(1), "A batch registration raises FontsChanged once, not per family.");
            }
            finally
            {
                VelvetFonts.FontsChanged -= Handler;
            }
        }

        #endregion
    }
}

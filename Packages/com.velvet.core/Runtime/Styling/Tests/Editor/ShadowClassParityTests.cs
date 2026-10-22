using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the parsing contract for Velvet's <c>shadow-*</c> utility classes
    /// (<see cref="StyleShadowClass"/>): preset resolution, CSS-cascade "last class wins" (including
    /// <c>shadow-none</c> overriding an earlier preset), that <c>drop-shadow-*</c> stays out of it, the
    /// companion <c>rounded-*</c> corner-radius resolution the shadow silhouette follows, and that the C#
    /// corner-radius mirror stays in lockstep with the <c>--radius-*</c> token scale in <c>_tokens.uss</c> (a
    /// token re-alignment that forgets to update this mirror would bake the old corner into the silhouette).
    /// These are layout-independent (pure string parsing), so they assert without a panel or a reconcile. GWT,
    /// one assert per case (Assume for preconditions).
    /// </summary>
    [TestFixture]
    internal sealed class ShadowClassParityTests
    {
        // Each layer as "x y blur spread alpha", first on top, in the order CSS lists them.
        private static string Describe(ShadowSpec[] layers)
            => string.Join(", ", System.Array.ConvertAll(layers,
                l => System.FormattableString.Invariant($"{l.OffsetX} {l.OffsetY} {l.Blur} {l.Spread} {l.Color.a:0.##}")));

        // Tailwind v4.1.11's theme.css --shadow-* values, and --shadow for the bare `shadow`; every preset colour
        // is black.
        [TestCase("shadow-2xs", "0 1 0 0 0.05")]
        [TestCase("shadow-xs", "0 1 2 0 0.05")]
        [TestCase("shadow", "0 1 3 0 0.1, 0 1 2 -1 0.1")]
        [TestCase("shadow-sm", "0 1 3 0 0.1, 0 1 2 -1 0.1")]
        [TestCase("shadow-md", "0 4 6 -1 0.1, 0 2 4 -2 0.1")]
        [TestCase("shadow-lg", "0 10 15 -3 0.1, 0 4 6 -4 0.1")]
        [TestCase("shadow-xl", "0 20 25 -5 0.1, 0 8 10 -6 0.1")]
        [TestCase("shadow-2xl", "0 25 50 -12 0.25")]
        public void Given_ATailwindPreset_When_Extracted_Then_ItsLayersAreTheV4Theme(string cls, string expected)
        {
            // Act
            var found = StyleShadowClass.TryExtract(new[] { "rounded-2xl", cls }, out var spec);

            // Assert
            Assert.That(found ? Describe(spec) : "none", Is.EqualTo(expected));
        }

        [Test]
        public void Given_ShadowInner_When_Extracted_Then_ItIsTheV4ThemeLayer()
        {
            // Act — theme.css's --shadow-inner: inset 0 2px 4px 0 rgb(0 0 0 / 0.05).
            var found = StyleShadowClass.TryExtract(new[] { "shadow-inner" }, out var spec);

            // Assert
            Assert.That(found ? Describe(spec) : "none", Is.EqualTo("0 2 4 0 0.05"));
        }

        [Test]
        public void Given_ShadowInner_When_Extracted_Then_ItsLayerIsInset()
        {
            // Act
            var found = StyleShadowClass.TryExtract(new[] { "shadow-inner" }, out var spec);

            // Assert
            Assert.That(found && spec[0].Inset, Is.True);
        }

        [TestCase(0, true, TestName = "Given_AnArbitraryShadowWithTheInsetKeyword_When_Extracted_Then_ThatLayerIsInset")]
        [TestCase(1, false, TestName = "Given_AnArbitraryShadowWithTheInsetKeyword_When_Extracted_Then_TheLayerWithoutItIsOuter")]
        public void Given_AnArbitraryShadowWithTheInsetKeyword_When_Extracted_Then_OnlyThatLayerIsInset(
            int layer, bool expected)
        {
            // Act
            var found = StyleShadowClass.TryExtract(new[] { "shadow-[inset_0_2px_4px_#000000,0_1px_2px_#000000]" },
                out var spec);

            // Assert
            Assert.That(found && spec.Length == 2 ? spec[layer].Inset : !expected, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ATailwindPreset_When_Extracted_Then_ItsLayersAreBlack()
        {
            // Act
            var found = StyleShadowClass.TryExtract(new[] { "shadow-lg" }, out var spec);

            // Assert
            Assert.That(found
                    ? System.Array.FindAll(spec, l => l.Color.r != 0f || l.Color.g != 0f || l.Color.b != 0f).Length
                    : -1,
                Is.EqualTo(0));
        }

        [Test]
        public void Given_AnArbitraryShadowList_When_Extracted_Then_EachLayerIsReadInOrder()
        {
            // Arrange — two layers, the first on top.
            var classes = new[] { "shadow-[0_1px_2px_#000000,3px_4px_5px_-1px_rgba(0,0,0,0.5)]" };

            // Act
            var found = StyleShadowClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That(found ? Describe(spec) : "none", Is.EqualTo("0 1 2 0 1, 3 4 5 -1 0.5"));
        }

        // GREEN_ON_BASE(characterization): the base rejects this bracket too.
        [Test]
        public void Given_AnArbitraryShadowListWithAMalformedLayer_When_Extracted_Then_TheWholeListIsRejected()
        {
            // Arrange — the browser drops a box-shadow value it cannot read, every layer with it.
            var classes = new[] { "shadow-[0_1px_2px_#000000,1px]" };

            // Act
            var found = StyleShadowClass.TryExtract(classes, out _);

            // Assert
            Assert.That(found, Is.False);
        }

        [Test]
        public void Given_AnArbitraryShadowWithANegativeBlur_When_Extracted_Then_ItIsRejected()
        {
            // Arrange — CSS rejects a negative blur radius.
            var classes = new[] { "shadow-[0_1px_-2px_#000000]" };

            // Act
            var found = StyleShadowClass.TryExtract(classes, out _);

            // Assert
            Assert.That(found, Is.False);
        }

        [Test]
        public void Given_ShadowLgThenShadowNone_When_Extracted_Then_NoneWinsAndNoShadowIsWanted()
        {
            // Arrange: later class wins the cascade.
            var classes = new[] { "shadow-lg", "shadow-none" };

            // Act
            var found = StyleShadowClass.TryExtract(classes, out _);

            // Assert
            Assert.That(found, Is.False);
        }

        [Test]
        public void Given_ShadowNoneThenShadowLg_When_Extracted_Then_LgWins()
        {
            // Arrange
            var classes = new[] { "shadow-none", "shadow-lg" };
            StyleShadowClass.TryExtract(new[] { "shadow-lg" }, out var lg);

            // Act
            var found = StyleShadowClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That(found ? Describe(spec) : "none", Is.EqualTo(Describe(lg)));
        }

        [Test]
        public void Given_NoShadowClass_When_Extracted_Then_NoShadowIsWanted()
        {
            // Arrange
            var classes = new[] { "rounded-2xl", "bg-white" };

            // Act
            var found = StyleShadowClass.TryExtract(classes, out _);

            // Assert
            Assert.That(found, Is.False);
        }

        [Test]
        public void Given_Rounded2xl_When_CornerRadiusResolved_Then_Returns16()
        {
            // Arrange
            var classes = new[] { "shadow-lg", "rounded-2xl" };

            // Act
            var resolved = StyleShadowClass.TryResolveCornerRadius(classes, out var radius);

            // Assert — --radius-2xl == 16px (the radius scale).
            Assert.That(resolved && radius == 16f, Is.True);
        }

        [Test]
        public void Given_RoundedTl2xl_When_CornerRadiusResolved_Then_TopLeftCornerIsRead()
        {
            // Arrange: per-corner top-left utility (not swallowed by the rounded- prefix).
            var classes = new[] { "rounded-tl-2xl" };

            // Act
            var resolved = StyleShadowClass.TryResolveCornerRadius(classes, out var radius);

            // Assert — --radius-2xl == 16px (the radius scale).
            Assert.That(resolved && radius == 16f, Is.True);
        }

        [Test]
        public void Given_RoundedFull_When_CornerRadiusResolved_Then_LeftToGeometryPath()
        {
            // Arrange: rounded-full has no fixed px in the scale; the resolvedStyle path handles it.
            var classes = new[] { "rounded-full" };

            // Act
            var resolved = StyleShadowClass.TryResolveCornerRadius(classes, out _);

            // Assert
            Assert.That(resolved, Is.False);
        }

        [Test]
        public void Given_ArbitraryShadowValue_When_Extracted_Then_ParsesTheBlurLength()
        {
            // Arrange: an arbitrary box-shadow (x_y_blur_color). Blur is the third length.
            var classes = new[] { "rounded-2xl", "shadow-[0_4px_8px_#000000]" };

            // Act
            var found = StyleShadowClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That(found && spec[0].Blur == 8f, Is.True);
        }

        [Test]
        public void Given_ArbitraryShadowValue_When_Extracted_Then_OffsetsAreAssignedPositionally()
        {
            // Arrange: x=2, y=4 — CSS order is offset-x then offset-y.
            var classes = new[] { "shadow-[2px_4px_8px_#101820]" };

            // Act
            var found = StyleShadowClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That(found && spec[0].OffsetX == 2f && spec[0].OffsetY == 4f, Is.True);
        }

        [Test]
        public void Given_ArbitraryShadowWithRgbaColor_When_Extracted_Then_ParsesTheAlpha()
        {
            // Arrange: an rgba() shadow color (the token keeps its commas through the underscore split).
            var classes = new[] { "shadow-[0_4px_8px_rgba(10,20,30,0.5)]" };

            // Act
            var found = StyleShadowClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That(found && spec[0].Color.a == 0.5f, Is.True);
        }

        [Test]
        public void Given_AnArbitraryShadowWithDoubledSpaces_When_Extracted_Then_TheEmptyTokensAreSkipped()
        {
            // Arrange
            var classes = new[] { "shadow-[_0__4px_8px__#101820_]" };

            // Act
            var found = StyleShadowClass.TryExtract(classes, out var spec);

            // Assert
            Assert.That(found && spec[0].OffsetY == 4f, Is.True);
        }

        [Test]
        public void Given_ADropShadowPreset_When_Extracted_Then_NoBoxShadowIsWanted()
        {
            // Arrange — drop-shadow-* is a filter, painted by no box shadow.
            var classes = new[] { "w-full", "drop-shadow-md" };

            // Act
            var want = StyleShadowClass.TryExtract(classes, out _);

            // Assert
            Assert.That(want, Is.False);
        }

        // The presets are Tailwind v4's --inset-shadow-* values. Every layer of either form is inset.
        [TestCase("inset-shadow-2xs", "0 1 0 0 0.05 inset")]
        [TestCase("inset-shadow-xs", "0 1 1 0 0.05 inset")]
        [TestCase("inset-shadow-sm", "0 2 4 0 0.05 inset")]
        [TestCase("inset-shadow-[0_2px_4px_#00000044]", "0 2 4 0 0.27 inset")]
        public void Given_AnInsetShadowClass_When_Extracted_Then_ItsLayersAreInsetOnes(string cls, string expected)
        {
            // Act
            var found = StyleShadowClass.TryExtract(new[] { cls }, out var spec);

            // Assert
            Assert.That(
                found ? Describe(spec) + (System.Array.TrueForAll(spec, l => l.Inset) ? " inset" : " outer") : "none",
                Is.EqualTo(expected));
        }

        [Test]
        public void Given_AnInsetShadowBesideAShadow_When_Extracted_Then_TheInsetLayersAreListedFirst()
        {
            // Act — written after the shadow, and listed ahead of it all the same.
            var found = StyleShadowClass.TryExtract(new[] { "shadow-md", "inset-shadow-sm" }, out var spec);

            // Assert
            Assert.That(found ? Describe(spec) : "none", Is.EqualTo("0 2 4 0 0.05, 0 4 6 -1 0.1, 0 2 4 -2 0.1"));
        }

        [TestCase("inset-shadow-sm shadow-md shadow-none", "0 2 4 0 0.05")]
        [TestCase("inset-shadow-sm shadow-md inset-shadow-none", "0 4 6 -1 0.1, 0 2 4 -2 0.1")]
        public void Given_OneFamilysNone_When_Extracted_Then_TheOtherFamilysLayersRemain(string classes, string expected)
        {
            // Act
            var found = StyleShadowClass.TryExtract(classes.Split(' '), out var spec);

            // Assert
            Assert.That(found ? Describe(spec) : "none", Is.EqualTo(expected));
        }

        [Test]
        public void Given_AnInsetShadowClass_When_Gated_Then_HasShadowClassLetsItThrough()
        {
            // Act
            var has = StyleShadowClass.HasShadowClass(new[] { "inset-shadow-sm" });

            // Assert
            Assert.That(has, Is.True);
        }

        [Test]
        public void Given_AnInsetRingClass_When_Gated_Then_HasShadowClassLetsItThrough()
        {
            // Act
            var has = StyleShadowClass.HasShadowClass(new[] { "inset-ring" });

            // Assert
            Assert.That(has, Is.True);
        }

        // Each layer's colour as RGBA, or "current" and its alpha for the element's text colour.
        private static string Colours(ShadowSpec[] layers)
            => string.Join(", ", System.Array.ConvertAll(layers, l => float.IsNaN(l.Color.r)
                ? System.FormattableString.Invariant($"current {l.Color.a:0.##}")
                : UnityEngine.ColorUtility.ToHtmlStringRGBA(l.Color)));

        // A colour class takes the place of every layer's colour, its /N scaling the colour's alpha; a size's /N
        // replaces each layer's alpha, and scales a colour class's instead.
        [TestCase("shadow-md shadow-red-500", "FB2C36FF, FB2C36FF")]
        [TestCase("shadow-md shadow-red-500/40", "FB2C3666, FB2C3666")]
        [TestCase("shadow-md/40", "00000066, 00000066")]
        [TestCase("shadow-md/50 shadow-red-500/40", "FB2C3633, FB2C3633")]
        [TestCase("shadow-sm shadow-current/50", "current 0.5, current 0.5")]
        [TestCase("inset-shadow-sm inset-shadow-blue-500", "2B7FFFFF")]
        public void Given_AShadowColourOrAlpha_When_Extracted_Then_ItColoursTheLayers(string classes, string expected)
        {
            // Act
            var found = StyleShadowClass.TryExtract(classes.Split(' '), out var spec);

            // Assert
            Assert.That(found ? Colours(spec) : "none", Is.EqualTo(expected));
        }

        [Test]
        public void Given_AShadowColour_When_Extracted_Then_ItDoesNotColourTheInsetShadow()
        {
            // Act
            var found = StyleShadowClass.TryExtract(new[] { "inset-shadow-2xs", "shadow-red-500" }, out var spec);

            // Assert
            Assert.That(found ? Colours(spec) : "none", Is.EqualTo("0000000D"));
        }

        // inset-ring writes `inset 0 0 0 <width> <colour>`, in the element's text colour where it names none.
        [TestCase("inset-ring", "0 0 0 1 current 1")]
        [TestCase("inset-ring-2 inset-ring-red-500/40", "0 0 0 2 FB2C3666")]
        [TestCase("inset-ring-[3px]", "0 0 0 3 current 1")]
        public void Given_AnInsetRing_When_Extracted_Then_ItIsAnInsetLayerOfItsWidth(string classes, string expected)
        {
            // Act
            var found = StyleShadowClass.TryExtract(classes.Split(' '), out var spec);

            // Assert
            Assert.That(
                found && spec.Length == 1 && spec[0].Inset
                    ? System.FormattableString.Invariant($"{spec[0].OffsetX} {spec[0].OffsetY} {spec[0].Blur} {spec[0].Spread} ")
                        + Colours(spec)
                    : "none",
                Is.EqualTo(expected));
        }

        [Test]
        public void Given_AnInsetShadowAnInsetRingAndAShadow_When_Extracted_Then_TheyAreListedInThatOrder()
        {
            // Act
            var found = StyleShadowClass.TryExtract(new[] { "shadow-2xl", "inset-ring-2", "inset-shadow-sm" }, out var spec);

            // Assert
            Assert.That(found ? Describe(spec) : "none", Is.EqualTo("0 2 4 0 0.05, 0 0 0 2 1, 0 25 50 -12 0.25"));
        }

        // Each layer's family it takes its colour from the parent for, and the colour it keeps where the parent has none.
        private static string Inherited(ShadowSpec[] layers)
            => string.Join(", ", System.Array.ConvertAll(layers, l => l.InheritFrom + " " + (float.IsNaN(l.Color.r)
                ? "current"
                : UnityEngine.ColorUtility.ToHtmlStringRGBA(l.Color))));

        // -inherit takes the parent's colour and keeps each layer's own for where the parent declares none; a later
        // colour class replaces it, and it takes no modifier.
        [TestCase("shadow-md shadow-inherit", "Shadow 0000001A, Shadow 0000001A")]
        [TestCase("inset-shadow-2xs inset-shadow-inherit", "InsetShadow 0000000D")]
        [TestCase("inset-ring inset-ring-inherit", "InsetRing current")]
        [TestCase("shadow-2xs shadow-inherit shadow-red-500", "None FB2C36FF")]
        [TestCase("shadow-2xs shadow-red-500 shadow-inherit/50", "None FB2C36FF")]
        public void Given_AnInheritColourClass_When_Extracted_Then_TheLayersTakeTheParentsColour(string classes, string expected)
        {
            // Act
            var found = StyleShadowClass.TryExtract(classes.Split(' '), out var spec);

            // Assert
            Assert.That(found ? Inherited(spec) : "none", Is.EqualTo(expected));
        }

        // What a parent declares is its colour with its own size's /N folded in, as the browser substitutes the alpha
        // variable on the parent.
        [TestCase("shadow-red-500/40 shadow-lg/50", "FB2C3633")]
        [TestCase("shadow-inherit", "inherit")]
        [TestCase("shadow-lg", "none")]
        public void Given_AParentsShadowClasses_When_ItsDeclaredColourIsRead_Then_ItIsWhatAChildInherits(
            string classes, string expected)
        {
            // Act
            var declared = StyleShadowClass.DeclaredColours(classes.Split(' ')).Shadow;

            // Assert
            Assert.That(
                declared.Inherit ? "inherit" : declared.Colour is { } c ? UnityEngine.ColorUtility.ToHtmlStringRGBA(c) : "none",
                Is.EqualTo(expected));
        }

        // The colour a child inherits for the shadow family, through BoxShadowSilhouette's private resolution.
        private static string InheritedThrough(UnityEngine.UIElements.VisualElement child)
        {
            var method = typeof(BoxShadowSilhouette).GetMethod("InheritedColour",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var colour = (UnityEngine.Color?)method!.Invoke(null, new object[] { child, ShadowColourFamily.Shadow });
            return colour is { } c ? UnityEngine.ColorUtility.ToHtmlStringRGBA(c) : "none";
        }

        [TestCase("shadow-red-500", "", "FB2C36FF")]
        [TestCase("shadow-inherit", "shadow-red-500", "FB2C36FF")]
        [TestCase("inset-shadow-red-500", "", "none")]
        public void Given_AnElementUnderDeclaringAncestors_When_ItsShadowColourIsInherited_Then_ItIsTheNearestDeclared(
            string parentClasses, string grandparentClasses, string expected)
        {
            // Arrange
            var grandparent = new UnityEngine.UIElements.VisualElement();
            var parent = new UnityEngine.UIElements.VisualElement();
            var child = new UnityEngine.UIElements.VisualElement();
            grandparent.Add(parent);
            parent.Add(child);
            BoxShadowSilhouette.RecordDeclaredColours(grandparent, grandparentClasses.Split(' '));
            BoxShadowSilhouette.RecordDeclaredColours(parent, parentClasses.Split(' '));

            // Act
            var inherited = InheritedThrough(child);

            // Assert
            Assert.That(inherited, Is.EqualTo(expected));
        }

        [Test]
        public void Given_ADropShadowClass_When_Gated_Then_HasShadowClassPassesItBy()
        {
            // Arrange — drop-shadow-* is a filter, which the painted shadow has no part in.
            var classes = new[] { "drop-shadow-xl" };

            // Act
            var has = StyleShadowClass.HasShadowClass(classes);

            // Assert
            Assert.That(has, Is.False);
        }

        [Test]
        public void Given_AShadowThenADropShadowNone_When_Extracted_Then_TheBoxShadowStillApplies()
        {
            // Arrange — CSS's box-shadow and filter are separate properties, so neither family's none resets the other.
            var classes = new[] { "shadow-lg", "drop-shadow-none" };

            // Act
            var want = StyleShadowClass.TryExtract(classes, out _);

            // Assert
            Assert.That(want, Is.True);
        }

        [Test]
        public void Given_RoundedLgClass_When_ResolvingShadowCornerRadius_Then_Is8()
        {
            // Arrange/Act — rounded-lg's shadow corner radius mirrors the --radius-lg token, 8px.
            var ok = StyleShadowClass.TryResolveCornerRadius(new[] { "rounded-lg" }, out var radius);
            Assume.That(ok, Is.True, "Precondition: rounded-lg resolves a mirrored corner radius");

            // Assert
            Assert.That(radius, Is.EqualTo(8f));
        }

        [Test]
        public void Given_Rounded3xlClass_When_ResolvingShadowCornerRadius_Then_Is24()
        {
            // Arrange/Act — rounded-3xl's shadow corner radius mirrors the --radius-3xl token, 24px.
            var ok = StyleShadowClass.TryResolveCornerRadius(new[] { "rounded-3xl" }, out var radius);
            Assume.That(ok, Is.True, "Precondition: rounded-3xl resolves a mirrored corner radius");

            // Assert
            Assert.That(radius, Is.EqualTo(24f));
        }

        [Test]
        public void Given_BareRoundedClass_When_ResolvingShadowCornerRadius_Then_Is4()
        {
            // Arrange/Act — the bare `rounded` DEFAULT mirrors --radius-default (4px); a shadow on a
            // bare-rounded box must follow that corner, not fall through to a square silhouette.
            var ok = StyleShadowClass.TryResolveCornerRadius(new[] { "rounded" }, out var radius);
            Assume.That(ok, Is.True, "Precondition: bare rounded resolves a mirrored corner radius");

            // Assert
            Assert.That(radius, Is.EqualTo(4f));
        }
    }
}

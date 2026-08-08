using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Behavioural regression coverage for the two families Velvet realises from a class ARRAY rather than
    /// from USS — the inline font layer (<see cref="StyleFontResolver"/>) and the text-transform /
    /// -decoration / whitespace-pre-line / leading cascade (<c>StyleTextEffectResolver</c>) — when the class
    /// that drives one arrives through a <b>variant</b>. A payload is written onto the element's live class
    /// list by its manipulator; neither <c>font-&lt;family&gt;</c> nor any text-effect class has a USS rule
    /// behind it, so a resolver reading only the reconciled array leaves <c>dark:font-mono</c> and
    /// <c>dark:underline</c> as silent no-ops.
    /// </summary>
    /// <remarks>
    /// Each case asserts the write the resolver actually makes — the resolved Font Asset, the rich-text tag
    /// wrapped around the displayed string — rather than class membership, which is true whether or not the
    /// resolver ever ran. <c>dark:</c> needs no panel: the conditional manipulator subscribes to
    /// <see cref="VelvetTheme"/>'s theme signal when it attaches and evaluates it off-panel. Both edges go
    /// into one comparison wherever an element that never resolved at all would satisfy the post-toggle half
    /// on its own. GWT, one assert per case.
    /// </remarks>
    [TestFixture]
    internal sealed class VariantGatedTypographyThemeTests
    {
        private FontAsset _mono;
        private FontAsset _sans;

        [SetUp]
        public void SetUp()
        {
            VelvetFonts.Clear();
            _mono = ScriptableObject.CreateInstance<FontAsset>();
            _sans = ScriptableObject.CreateInstance<FontAsset>();
            VelvetFonts.Register(new VelvetFontFamily("mono",
                new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, upright = _mono }));
            VelvetFonts.Register(new VelvetFontFamily("sans",
                new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, upright = _sans }));
        }

        [TearDown]
        public void TearDown()
        {
            VelvetTheme.IsDark = false;
            VelvetFonts.Clear();
            UnityEngine.Object.DestroyImmediate(_mono);
            UnityEngine.Object.DestroyImmediate(_sans);
        }

        private static VisualElement Mount(ReconcilerScope scope, VNode node)
        {
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new[] { node });
            return scope.Root.Q<VisualElement>("card");
        }

        private bool ResolvesToMono(VisualElement element) =>
            ReferenceEquals(element.style.unityFontDefinition.value.fontAsset, _mono);

        [Test]
        public void Given_ADarkGatedFontFamily_When_TheThemeTurnsDark_Then_TheRegisteredAssetIsApplied()
        {
            // Arrange — the family axis is written only as inline style, so the bare `font-mono` the variant
            // puts on the class list resolves nothing on its own.
            using var scope = new ReconcilerScope();
            var card = Mount(scope, V.Label(className: "dark:font-mono", text: "Docs", name: "card"));
            var appliedWhileLight = ResolvesToMono(card);

            // Act
            VelvetTheme.IsDark = true;

            // Assert
            Assert.That((appliedWhileLight, ResolvesToMono(card)), Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_ADarkGatedFontFamilyApplied_When_TheThemeTurnsLight_Then_TheAssetIsCleared()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var card = Mount(scope, V.Label(className: "dark:font-mono", text: "Docs", name: "card"));
            VelvetTheme.IsDark = true;
            var appliedWhileDark = ResolvesToMono(card);

            // Act
            VelvetTheme.IsDark = false;

            // Assert — an element whose font never resolved at all would satisfy the light half alone.
            Assert.That((appliedWhileDark, ResolvesToMono(card)), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ALitDarkGatedFontFamily_When_AnUnrelatedClassPatches_Then_TheAssetSurvives()
        {
            // Arrange — a re-render brings the reconciled array back, and that array spells the payload only
            // as `dark:font-mono`. The literal `font-sans` alongside it is what makes the difference visible:
            // it opens the resolver's own gate, so a patch resolving from the reconciled array alone answers
            // `sans` and reverts the element while the theme is still dark.
            using var scope = new ReconcilerScope();
            var first = new VNode[]
                { V.Label(className: "font-sans dark:font-mono p-2", text: "Docs", name: "card") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), first);
            var card = scope.Root.Q<VisualElement>("card");
            VelvetTheme.IsDark = true;
            var appliedBeforePatch = ResolvesToMono(card);
            var second = new VNode[]
                { V.Label(className: "font-sans dark:font-mono p-4", text: "Docs", name: "card") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, first, second);

            // Assert — an element whose font never resolved would satisfy neither half, so both are read.
            Assert.That((appliedBeforePatch, ResolvesToMono(card)), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ADarkGatedArbitraryFontFamily_When_TheThemeTurnsDark_Then_NoBracketTokenIsLeftOnTheClassList()
        {
            // Arrange — the reconciled path keeps a font-[…] token off the USS class list because the
            // resolver owns it; the variant path has to reach the same two outcomes from its own toggle.
            using var scope = new ReconcilerScope();
            var card = Mount(scope, V.Label(className: "dark:font-[mono]", text: "Docs", name: "card"));

            // Act
            VelvetTheme.IsDark = true;

            // Assert — the payload resolved AND left no dead class behind; a token that merely landed on the
            // class list would fail the first half, and one that vanished entirely would fail the second.
            Assert.That((card.ClassListContains("font-[mono]"), ResolvesToMono(card)),
                Is.EqualTo((false, true)));
        }

        [Test]
        public void Given_ADarkGatedUnderline_When_TheThemeTurnsDark_Then_TheTextIsWrappedInTheUnderlineTag()
        {
            // Arrange — no bundled stylesheet carries an `underline` rule, so the displayed string is the
            // only place the decoration can show up.
            using var scope = new ReconcilerScope();
            var card = (Label)Mount(scope, V.Label(className: "dark:underline", text: "Docs", name: "card"));
            var textWhileLight = card.text;

            // Act
            VelvetTheme.IsDark = true;

            // Assert
            Assert.That((textWhileLight, card.text), Is.EqualTo(("Docs", "<u>Docs</u>")));
        }

        [Test]
        public void Given_ADarkGatedUnderlineApplied_When_TheThemeTurnsLight_Then_TheTagIsRemoved()
        {
            // Arrange
            using var scope = new ReconcilerScope();
            var card = (Label)Mount(scope, V.Label(className: "dark:underline", text: "Docs", name: "card"));
            VelvetTheme.IsDark = true;
            var textWhileDark = card.text;

            // Act
            VelvetTheme.IsDark = false;

            // Assert — a label that never got the tag would satisfy the light half alone.
            Assert.That((textWhileDark, card.text), Is.EqualTo(("<u>Docs</u>", "Docs")));
        }

        [Test]
        public void Given_ALitDarkGatedUppercase_When_AnUnrelatedClassPatches_Then_TheTransformSurvives()
        {
            // Arrange — the patch re-parses the element's own effect; from the reconciled array alone that
            // parse comes back empty and the pass then re-walks the leaf to undo the transform.
            using var scope = new ReconcilerScope();
            var first = new VNode[] { V.Label(className: "dark:uppercase p-2", text: "Docs", name: "card") };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), first);
            var card = scope.Root.Q<Label>("card");
            VelvetTheme.IsDark = true;
            var textBeforePatch = card.text;
            var second = new VNode[] { V.Label(className: "dark:uppercase p-4", text: "Docs", name: "card") };

            // Act
            scope.Reconciler.Reconcile(scope.Root, first, second);

            // Assert
            Assert.That((textBeforePatch, card.text), Is.EqualTo(("DOCS", "DOCS")));
        }

        [Test]
        public void Given_AHasClassGatedUppercase_When_TheElementMounts_Then_TheDescendantTextIsTransformed()
        {
            // Arrange — the matching descendant is present from the first render, so the has- pass lights the
            // payload while the element is still being created. Nothing re-runs the cascade afterwards, so a
            // create pass reading the reconciled array alone would leave the leaf untransformed for good.
            using var scope = new ReconcilerScope();

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(),
                new VNode[]
                {
                    V.Div(className: "has-[.flag]:uppercase", name: "card",
                        children: new VNode[] { V.Div("flag"), V.Text("Docs") })
                });

            // Assert
            Assert.That(scope.Root.Q<VisualElement>("card").Q<Label>().text, Is.EqualTo("DOCS"));
        }

        [Test]
        public void Given_AChildCombinatorPayloadOverABracketFont_When_ItLands_Then_TheChildKeepsItsFamily()
        {
            // Arrange — a [&>*]: payload lands on a child the container's own pass has already finished
            // building, so the re-sync it raises has no reconciled array for that child and would have to
            // stand the live class list in. That list cannot hold `font-[mono]` (the resolver owns the
            // bracket forms), so resolving the font from it answers "no family" and overwrites one the
            // child's own create pass got right, with no later pass to put it back.
            using var scope = new ReconcilerScope();

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(),
                new VNode[]
                {
                    V.Div(className: "[&>*]:uppercase", children: new VNode[]
                    {
                        V.Label(className: "font-[mono] font-bold", text: "Docs", name: "card"),
                    }),
                });

            // Assert — the payload reaching the child at all is what makes this an arrangement rather than
            // a decoration, so it rides along.
            var card = scope.Root.Q<VisualElement>("card");
            Assert.That((card.ClassListContains("uppercase"), ResolvesToMono(card)),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AChildCombinatorTextEffectOverATextChild_When_Mounted_Then_TheTransformReachesIt()
        {
            // Arrange — a V.Text child gets the payload on its class list like any other child, and its own
            // reconcile applies no class at any render, so a re-sync that waits for a recorded array waits
            // forever. An element child in the same markup is the control: it resolves from its first patch.
            using var scope = new ReconcilerScope();
            VNode[] Tree(string label) => new VNode[]
            {
                V.Div(className: "[&>*]:uppercase", children: new VNode?[]
                {
                    V.Label(className: "text-sm", text: label, name: "elem"),
                    V.Text("text-child"),
                }),
            };
            var first = Tree("label-child");

            // Act — read the text child after ONE render as well, because it is reached at mount and the
            // element child is not: a fix that only worked from the second render would otherwise pass.
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), first);
            var text = scope.Root[0].Children().OfType<Label>().Last();
            var textAtMount = text.text;
            scope.Reconciler.Reconcile(scope.Root, first, Tree("label-child"));

            // Assert — the element child rides along, so a walk that reached neither fails here rather than
            // reading as a transform the resolver declined to write.
            Assert.That((textAtMount, scope.Root.Q<Label>("elem").text, text.text),
                Is.EqualTo(("TEXT-CHILD", "LABEL-CHILD", "TEXT-CHILD")));
        }

        [Test]
        public void Given_AChildCombinatorFontOverAChildDeclaringNoPayload_When_ItRerenders_Then_TheFamilyResolves()
        {
            // Arrange — the child declares no gate payload, so it has no recorded array and the re-sync the
            // landing payload raises stands the font layer down. Its class content never changes afterwards
            // either, which is the other half: a font layer re-derived only from a class-array diff has
            // nothing left to run it, and the family the container asked for is lost for the child's life.
            using var scope = new ReconcilerScope();
            var first = new VNode[]
            {
                V.Div(className: "[&>*]:font-mono", children: new VNode[]
                {
                    V.Label(className: "text-sm", text: "1", name: "card"),
                }),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), first);
            var card = scope.Root.Q<VisualElement>("card");
            var second = new VNode[]
            {
                V.Div(className: "[&>*]:font-mono", children: new VNode[]
                {
                    V.Label(className: "text-sm", text: "2", name: "card"),
                }),
            };

            // Act
            scope.Reconciler.Reconcile(scope.Root, first, second);

            // Assert — the payload reaching the child rides along, so a walk that never applied it fails
            // here rather than reading as a font the resolver declined to write.
            Assert.That((card.ClassListContains("font-mono"), ResolvesToMono(card)),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AChildCombinatorPayloadOverABracketLeading_When_ItLands_Then_TheChildKeepsItsTag()
        {
            // Arrange — the same incomplete source on the text side, and it needs no second token: the live
            // class list holds neither `leading-[24px]` nor anything else this family parses, so a resolve
            // from it drops the element's own effect and re-walks the leaf to strip the tag.
            using var scope = new ReconcilerScope();

            // Act
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(),
                new VNode[]
                {
                    V.Div(className: "[&>*]:gap-2", children: new VNode[]
                    {
                        V.Label(className: "leading-[24px]", text: "Docs", name: "card"),
                    }),
                });

            // Assert
            var card = scope.Root.Q<Label>("card");
            Assert.That((card.ClassListContains("gap-2"), card.text),
                Is.EqualTo((true, "<line-height=24px>Docs</line-height>")));
        }

        [Test]
        public void Given_AHoverChannelClassOnTheLiveList_When_AWidthPayloadReSyncs_Then_TheTextIsUntouched()
        {
            // Arrange — whileHoverClass writes straight onto the live class list and raises no signal, so
            // nothing would ever undo an effect derived from it. A width payload re-syncs an element that
            // declares no gate payload at all, which is the same no-recorded-array path a [&>*]: rule takes
            // — and here the live list is not merely missing tokens but carrying one the element never
            // declared.
            using var scope = new ReconcilerScope();
            var card = (Label)Mount(scope,
                V.Label(className: "dark:w-64", whileHoverClass: "uppercase", text: "Docs", name: "card"));
            using (var over = PointerOverEvent.GetPooled())
            {
                card.SimulateEvent(over);
            }

            // Act
            VelvetTheme.IsDark = true;

            // Assert — the channel's class being live is what the case is about, so it rides along; a
            // transform taken from it would be baked into the string for the element's remaining life.
            Assert.That((card.ClassListContains("uppercase"), card.text), Is.EqualTo((true, "Docs")));
        }
    }

    /// <summary>
    /// The same contract driven by a real breakpoint crossing rather than a theme flip. A responsive payload
    /// is applied by the conditional manipulator when the resolved scope width crosses the breakpoint, which
    /// only a real panel can produce — so this runs inside a live <see cref="UnityEditor.EditorWindow"/>
    /// panel (via <see cref="PanelTestBase"/>), forces the layout pass so <c>resolvedStyle.width</c>
    /// resolves, then delivers a <see cref="GeometryChangedEvent"/> so the manipulator re-evaluates.
    /// </summary>
    [TestFixture]
    internal sealed class VariantGatedTypographyPanelTests : PanelTestBase
    {
        private const float MdBreakpoint = 768f;
        private const float WidePanel = 1000f;

        [Test]
        public void Given_AResponsiveUnderline_When_TheRootIsWiderThanMd_Then_TheTextIsWrappedInTheUnderlineTag()
        {
            // Arrange
            _window.position = new Rect(0, 0, WidePanel, 600);
            _mounted = V.Mount(_window.rootVisualElement,
                V.Label(className: "md:underline", text: "Docs", name: "card"));
            var card = _window.rootVisualElement.Q<Label>("card");

            // Act — resolve the breakpoint against the panel and let the manipulator re-read the width.
            ForcePanelUpdate(card.panel);
            using (var evt = EventBase<GeometryChangedEvent>.GetPooled())
            {
                card.panel.visualTree.SimulateEvent(evt);
            }

            // Assert — the resolved width rides along, so a panel that never reached the breakpoint fails
            // here rather than reporting inconclusive.
            Assert.That(
                (card.panel.visualTree.resolvedStyle.width >= MdBreakpoint, card.text),
                Is.EqualTo((true, "<u>Docs</u>")));
        }
    }

    /// <summary>
    /// Pins the stylesheet absence the two resolvers above are built on: a variant realises its payload by
    /// putting the bare utility on the live class list, and for these families that class has no rule behind
    /// it, which is why the reconciler routes the payload back to a resolver instead. A sheet that started
    /// declaring one of them would make the bare class meaningful and change that reasoning, silently —
    /// so it is read out of the derived table rather than asserted in a comment.
    /// </summary>
    /// <remarks>
    /// <c>StyleUtilityProperties</c> is generated from <c>Runtime/Styles/*.uss</c> and pinned against them
    /// by the generator suite's own census, so a claim about the table is a claim about the sheets.
    /// </remarks>
    [TestFixture]
    internal sealed class TypographyHasNoStylesheetRuleTests
    {
        // The weight scale and the italic alias are the only font- rules _typography.uss carries; a family
        // needs a real Font Asset, so it can only be an inline write.
        private static readonly string[] DeclaredFontClasses =
        {
            "font-thin", "font-extralight", "font-light", "font-normal", "font-medium",
            "font-semibold", "font-bold", "font-extrabold", "font-black", "font-italic",
        };

        // Every class StyleTextEffectClass.Parse folds into a non-whitespace axis, plus the one whitespace
        // value it realises itself. The other four whitespace classes ARE USS rules and are left out.
        private static readonly string[] StringAxisClasses =
        {
            "uppercase", "lowercase", "capitalize", "normal-case",
            "underline", "line-through", "overline", "no-underline",
            "whitespace-pre-line",
            "leading-none", "leading-tight", "leading-snug", "leading-normal", "leading-relaxed",
            "leading-loose",
        };

        [Test]
        public void Given_TheDerivedUtilityTable_When_ItsFontRulesAreListed_Then_NoFamilyIsAmongThem()
        {
            // Arrange
            var byClassName = (Dictionary<string, int>)typeof(StyleUtilityProperties)
                .GetField("ByClassName", BindingFlags.NonPublic | BindingFlags.Static)!
                .GetValue(null)!;

            // Act — anything font-prefixed that is not the weight scale or the italic alias is a family.
            var families = byClassName.Keys
                .Where(name => name.StartsWith("font-", StringComparison.Ordinal))
                .Where(name => Array.IndexOf(DeclaredFontClasses, name) < 0)
                .OrderBy(name => name, StringComparer.Ordinal);

            // Assert — joined rather than compared as a collection, so a mismatch names the offender.
            Assert.That(string.Join(" ", families), Is.Empty);
        }

        [Test]
        public void Given_TheDerivedUtilityTable_When_TheStringAxisClassesAreLookedUp_Then_NoneIsDeclared()
        {
            // Arrange / Act
            var declared = StringAxisClasses
                .Where(name => StyleUtilityProperties.TryGet(name, out _));

            // Assert
            Assert.That(string.Join(" ", declared), Is.Empty);
        }
    }
}

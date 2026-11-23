using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which family a weight or italic class with no family class of its own resolves against:
    /// the one the nearest ancestor with a <c>font-&lt;name&gt;</c> class names, as CSS's
    /// <c>font-weight</c> keeps an inherited <c>font-family</c>, and <see cref="VelvetFonts.DefaultFamily"/>
    /// only when no ancestor names one. Reads the asset Velvet wrote inline on the label. GWT, one assert
    /// per case.
    /// </summary>
    [TestFixture]
    internal sealed class InheritedFontFamilyPanelTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;
        private readonly List<FontAsset> _assets = new();
        private FontAsset _sansBold;
        private FontAsset _serifBold;
        private FontAsset _monoBold;
        private FontAsset _monoNormal;

        [SetUp]
        public void SetUp()
        {
            VelvetFonts.Clear();
            _sansBold = Register("sans", out _);
            _serifBold = Register("serif", out _);
            _monoBold = Register("mono", out _monoNormal);
            VelvetFonts.DefaultFamily = "sans";
            _host = new HeadlessEditorPanelHost();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
            VelvetFonts.Clear();
            foreach (var asset in _assets)
            {
                DestroyAsset(asset);
            }

            _assets.Clear();
        }

        // Registers a family with a Normal and a Bold entry and returns the Bold asset.
        private FontAsset Register(string name, out FontAsset normal)
        {
            normal = NewAsset();
            var bold = NewAsset();
            VelvetFonts.Register(new VelvetFontFamily(name,
                new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, upright = normal },
                new VelvetFontWeightEntry { weight = VelvetFontWeight.Bold, upright = bold }));
            return bold;
        }

        private FontAsset NewAsset()
        {
            var asset = FontAsset.CreateFontAsset(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            _assets.Add(asset);
            return asset;
        }

        private static void DestroyAsset(FontAsset asset)
        {
            if (asset == null)
            {
                return;
            }

            foreach (var atlas in asset.atlasTextures ?? System.Array.Empty<Texture2D>())
            {
                if (atlas != null)
                {
                    Object.DestroyImmediate(atlas);
                }
            }

            if (asset.material != null)
            {
                Object.DestroyImmediate(asset.material);
            }

            Object.DestroyImmediate(asset);
        }

        private static VNode Target(string className) => V.Label(className: className, text: "target", name: "target");

        private FontAsset TargetAsset() =>
            _host.Root.Q<Label>("target").style.unityFontDefinition.value.fontAsset;

        [Test]
        public void Given_ABoldLabelInAFamilyContainer_When_Mounted_Then_TheLabelResolvesThatFamilysBold()
        {
            // Arrange
            var tree = V.Div(className: "font-serif", children: new[] { Target("font-bold") });

            // Act
            _mounted = V.Mount(_host.Root, tree);

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_serifBold));
        }

        [Test]
        public void Given_AFamilyContainer_When_ABoldLabelIsPatchedIn_Then_TheLabelResolvesThatFamilysBold()
        {
            // Arrange
            _mounted = V.Mount(_host.Root, V.Div(className: "font-serif", children: new VNode[0]));

            // Act
            _mounted.Render(V.Div(className: "font-serif", children: new[] { Target("font-bold") }));

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_serifBold));
        }

        [Test]
        public void Given_ABoldLabelInAFamilyContainer_When_TheContainersFamilyChanges_Then_TheLabelFollows()
        {
            // Arrange
            _mounted = V.Mount(_host.Root, V.Div(className: "font-serif", children: new[] { Target("font-bold") }));

            // Act
            _mounted.Render(V.Div(className: "font-mono", children: new[] { Target("font-bold") }));

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_monoBold));
        }

        [Test]
        public void Given_NestedFamilyContainers_When_ABoldLabelMounts_Then_TheNearestFamilyDecides()
        {
            // Arrange
            var tree = V.Div(className: "font-serif", children: new VNode[]
            {
                V.Div(className: "font-mono", children: new[] { Target("font-bold") }),
            });

            // Act
            _mounted = V.Mount(_host.Root, tree);

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_monoBold));
        }

        [Test]
        public void Given_AWeightOnlyContainerInAFamilyContainer_When_ABoldLabelMounts_Then_TheOuterFamilyDecides()
        {
            // Arrange
            var tree = V.Div(className: "font-serif", children: new VNode[]
            {
                V.Div(className: "font-normal", children: new[] { Target("font-bold") }),
            });

            // Act
            _mounted = V.Mount(_host.Root, tree);

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_serifBold));
        }

        // GREEN_ON_BASE(characterization): the base resolves the default family everywhere; this pins that the
        // label leaves the former family once the container stops naming it.
        [Test]
        public void Given_ABoldLabelInAFamilyContainer_When_TheContainerDropsItsFamilyClass_Then_TheLabelFallsBackToTheDefaultFamily()
        {
            // Arrange
            _mounted = V.Mount(_host.Root, V.Div(className: "font-serif", children: new[] { Target("font-bold") }));

            // Act
            _mounted.Render(V.Div(children: new[] { Target("font-bold") }));

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_sansBold));
        }

        // GREEN_ON_BASE(characterization): the base resolves the default family everywhere; this pins that the
        // label leaves the former family once the container stops naming it.
        [Test]
        public void Given_ABoldLabelInAFamilyContainer_When_TheContainerSwapsItsFamilyForAWeight_Then_TheLabelFallsBackToTheDefaultFamily()
        {
            // Arrange
            _mounted = V.Mount(_host.Root, V.Div(className: "font-serif", children: new[] { Target("font-bold") }));

            // Act
            _mounted.Render(V.Div(className: "font-normal", children: new[] { Target("font-bold") }));

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_sansBold));
        }

        [Test]
        public void Given_ABoldLabelInAFamilyContainer_When_MountedIntoARootNoPanelHolds_Then_ItResolvesThatFamilyAndLeavesNothingQueued()
        {
            // Arrange
            var detachedRoot = new VisualElement();
            var tree = V.Div(className: "font-serif", children: new[] { Target("font-bold") });

            // Act
            _mounted = V.Mount(detachedRoot, tree);

            // Assert
            var pending = (System.Collections.Generic.IReadOnlyCollection<VisualElement>)typeof(FiberFontScope)
                .GetField("_pending", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .GetValue(_mounted.Root.Reconciler.Context.FontScope);
            var asset = detachedRoot.Q<Label>("target").style.unityFontDefinition.value.fontAsset;
            Assert.That((ReferenceEquals(asset, _serifBold), pending.Count), Is.EqualTo((true, 0)));
        }

        // The panel updates its styles, then runs its scheduler once, as one frame does.
        private void RunAFrame()
        {
            EditorPanelTestHelpers.ForcePanelUpdate(_host.Panel);
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
        }

        private static VNode FamilyContainerWithAnAuthoredMiddle(string labelClass) =>
            V.Div(className: "font-sans", children: new[]
            {
                V.Div(name: "mid", children: new[] { Target(labelClass) }),
            });

        [Test]
        public void Given_ABoldLabelInASansContainer_When_AnElementBetweenThemGainsAnInlineMonoFont_Then_TheLabelResolvesMonosBold()
        {
            // Arrange
            _mounted = V.Mount(_host.Root, FamilyContainerWithAnAuthoredMiddle("font-bold"));
            RunAFrame();

            // Act
            _host.Root.Q("mid").style.unityFontDefinition = FontDefinition.FromSDFFont(_monoNormal);
            RunAFrame();

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_monoBold));
        }

        [Test]
        public void Given_AnInlineMonoFontOnAnElementBetweenASansContainerAndALabel_When_TheLabelBecomesBold_Then_TheLabelResolvesMonosBold()
        {
            // Arrange
            _mounted = V.Mount(_host.Root, FamilyContainerWithAnAuthoredMiddle("font-normal"));
            _host.Root.Q("mid").style.unityFontDefinition = FontDefinition.FromSDFFont(_monoNormal);
            RunAFrame();

            // Act
            _mounted.Render(FamilyContainerWithAnAuthoredMiddle("font-bold"));
            RunAFrame();

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_monoBold));
        }

        [Test]
        public void Given_ABoldLabelInASansContainer_When_ABoldElementBetweenThemGainsAnInlineMonoFont_Then_TheLabelResolvesMonosBold()
        {
            // Arrange
            VNode Tree() => V.Div(className: "font-sans", children: new[]
            {
                V.Div(className: "font-bold", name: "mid", children: new[] { Target("font-bold") }),
            });
            _mounted = V.Mount(_host.Root, Tree());
            RunAFrame();

            // Act
            _host.Root.Q("mid").style.unityFontDefinition = FontDefinition.FromSDFFont(_monoNormal);
            RunAFrame();
            RunAFrame();

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_monoBold));
        }

        // GREEN_ON_BASE(characterization): the base takes the nearest font-<name> ancestor's family. Replace
        // `VelvetFonts.NamedHolds(family, parentAsset)` in Locate by `VelvetFonts.FamilyOf(parentAsset) == family` and the
        // label takes the family that holds the container's write, the default one here, since the asset is registered
        // under both.
        [Test]
        public void Given_AWeightContainerWhoseFaceTheDefaultFamilyAlsoHolds_When_ANormalLabelMountsInASerifContainer_Then_TheSerifFamilyDecides()
        {
            // Arrange
            VelvetFonts.Clear();
            var shared = NewAsset();
            var serifNormal = NewAsset();
            VelvetFonts.Register(new VelvetFontFamily("serif",
                new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, upright = serifNormal },
                new VelvetFontWeightEntry { weight = VelvetFontWeight.Bold, upright = shared }));
            VelvetFonts.Register(new VelvetFontFamily("sans",
                new VelvetFontWeightEntry { weight = VelvetFontWeight.Normal, upright = NewAsset() },
                new VelvetFontWeightEntry { weight = VelvetFontWeight.Bold, upright = shared }));
            VelvetFonts.DefaultFamily = "sans";
            var tree = V.Div(className: "font-serif", children: new VNode[]
            {
                V.Div(className: "font-bold", children: new[] { Target("font-normal") }),
            });

            // Act
            _mounted = V.Mount(_host.Root, tree);
            RunAFrame();

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(serifNormal));
        }

        [Test]
        public void Given_ALabelWithAnInlineMonoFontInASansContainer_When_ItBecomesBold_Then_TheLabelResolvesMonosBold()
        {
            // Arrange
            _mounted = V.Mount(_host.Root, V.Div(className: "font-sans", children: new[] { Target("font-normal") }));
            RunAFrame();
            _host.Root.Q<Label>("target").style.unityFontDefinition = FontDefinition.FromSDFFont(_monoNormal);

            // Act
            _mounted.Render(V.Div(className: "font-sans", children: new[] { Target("font-bold") }));
            RunAFrame();

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_monoBold));
        }

        // GREEN_ON_BASE(characterization): the family a label names itself still wins over an ancestor's.
        [Test]
        public void Given_ALabelNamingItsOwnFamily_When_MountedInAFamilyContainer_Then_ItsOwnFamilyDecides()
        {
            // Arrange
            var tree = V.Div(className: "font-serif", children: new[] { Target("font-mono font-bold") });

            // Act
            _mounted = V.Mount(_host.Root, tree);

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_monoBold));
        }

        // GREEN_ON_BASE(characterization): with no ancestor naming a family, DefaultFamily still decides.
        [Test]
        public void Given_NoAncestorNamingAFamily_When_ABoldLabelMounts_Then_TheDefaultFamilyDecides()
        {
            // Arrange
            var tree = V.Div(className: "font-bold", children: new[] { Target("font-bold") });

            // Act
            _mounted = V.Mount(_host.Root, tree);

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_sansBold));
        }

        #region The scope's own bookkeeping

        private static readonly FontIntent s_boldIntent =
            new(null, false, VelvetFontWeight.Bold, true, false, false);

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static object ReadField(FiberFontScope scope, string name)
            => typeof(FiberFontScope).GetField(name, Private).GetValue(scope);

        private static void CallPrivate(FiberFontScope scope, string name)
            => typeof(FiberFontScope).GetMethod(name, Private).Invoke(scope, null);

        // A scope that has been told the element its follow is scheduled on, as the first drain of a pass tells it.
        private static FiberFontScope ScopeAnchoredAt(VisualElement anchor)
        {
            var scope = new FiberFontScope();
            scope.Drain(anchor);
            return scope;
        }

        // GREEN_ON_BASE(characterization): the base drops the entries of scopes that are gone when a scope joins the
        // registry. Drop `s_scopes.RemoveAll(static entry => !entry.TryGetTarget(out _));` and the entry stays.
        [Test]
        public void Given_ARegistryEntryWhoseScopeIsGone_When_AScopeIsCreated_Then_TheEntryIsDropped()
        {
            // Arrange
            var registry = (List<System.WeakReference<FiberFontScope>>)typeof(FiberFontScope)
                .GetField("s_scopes", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var gone = new System.WeakReference<FiberFontScope>(null);
            registry.Add(gone);

            // Act
            _ = new FiberFontScope();

            // Assert
            Assert.That(registry, Has.No.Member(gone));
        }

        // GREEN_ON_BASE(characterization): the base holds an inheritor with no named family back for a few reads after
        // the registry changes. Flip the first `!=` of `entry.Named != null || InheritedFamily(element) != null` and
        // it resolves at once instead.
        [Test]
        public void Given_AnInheritorWithNoNamedFamily_When_TheRegistryChanges_Then_ItWaitsBeforeItResolvesAgain()
        {
            // Arrange
            var scope = new FiberFontScope();
            var element = new VisualElement();
            scope.Inheritors[element] = new FiberFontScope.Inheritor(s_boldIntent, null, null, 0);

            // Act
            CallPrivate(scope, "Refresh");

            // Assert
            Assert.That(scope.Inheritors[element].Wait, Is.GreaterThan(0));
        }

        // GREEN_ON_BASE(characterization): the base starts reading the parent's font again once a registry change
        // leaves an inheritor with no named family waiting. Set `_wantsFollow = true;` in that branch of Refresh to
        // false and no read is scheduled.
        [Test]
        public void Given_AnInheritorWithNoNamedFamilyOnAnAnchoredScope_When_TheRegistryChanges_Then_TheFontReadIsScheduled()
        {
            // Arrange
            var scope = ScopeAnchoredAt(new VisualElement());
            scope.Inheritors[new VisualElement()] = new FiberFontScope.Inheritor(s_boldIntent, null, null, 0);

            // Act
            CallPrivate(scope, "Refresh");

            // Assert
            Assert.That(ReadField(scope, "_follow"), Is.Not.Null);
        }

        // GREEN_ON_BASE(characterization): the base owes no further read once the scheduled one is made. Set
        // `_wantsFollow = false;` in Follow to true and the next drain starts a read it did not need.
        [Test]
        public void Given_AScopeOwingAFontRead_When_TheReadIsScheduled_Then_NoneIsOwedAnyMore()
        {
            // Arrange
            var scope = ScopeAnchoredAt(new VisualElement());
            typeof(FiberFontScope).GetField("_wantsFollow", Private).SetValue(scope, true);

            // Act
            CallPrivate(scope, "Follow");

            // Assert
            Assert.That(ReadField(scope, "_wantsFollow"), Is.False);
        }

        // GREEN_ON_BASE(characterization): the base lets go of the scheduled read once no inheritor lacks a named
        // family, so the next one that does can start another. Remove `_follow = null;` and the paused read stays
        // and blocks it; start `unnamed` as true in Tick and the read is never let go.
        [Test]
        public void Given_AScheduledFontRead_When_NoInheritorLacksANamedFamilyAnyMore_Then_TheReadIsLetGo()
        {
            // Arrange
            var scope = ScopeAnchoredAt(new VisualElement());
            typeof(FiberFontScope).GetField("_wantsFollow", Private).SetValue(scope, true);
            CallPrivate(scope, "Follow");

            // Act
            CallPrivate(scope, "Tick");

            // Assert
            Assert.That(ReadField(scope, "_follow"), Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base counts an inheritor's wait down by one a read. Flip the `-` of
        // `entry.Wait - 1` and it counts up.
        [Test]
        public void Given_AnInheritorWithReadsToWait_When_TheFontIsReadOnce_Then_ItWaitsOneReadLess()
        {
            // Arrange
            var scope = new FiberFontScope();
            var element = new VisualElement();
            scope.Inheritors[element] = new FiberFontScope.Inheritor(s_boldIntent, null, null, 2);

            // Act
            CallPrivate(scope, "Tick");

            // Assert
            Assert.That(scope.Inheritors[element].Wait, Is.EqualTo(1));
        }


        // GREEN_ON_BASE(characterization): the base resolves a bold label under font-sans to sans Bold. Drop
        // `InlineFontLedger.Wrote(element, asset, author)` from the asset branch of ApplyResolved and the serif write the
        // label's former class made is read as the author's, and the label stays serif.
        [Test]
        public void Given_ALabelNamingSerifAndBold_When_ItsClassBecomesBoldAlone_Then_TheSansContainersFamilyDecides()
        {
            // Arrange
            VNode Tree(string className) => V.Div(className: "font-sans", children: new[] { Target(className) });
            _mounted = V.Mount(_host.Root, Tree("font-serif font-bold"));
            RunAFrame();

            // Act
            _mounted.Render(Tree("font-bold"));
            RunAFrame();

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_sansBold));
        }

        [Test]
        public void Given_ABoldLabelWhoseInlineFontTheAuthorSetAfterTheClassWrote_When_TheFontClassIsRemoved_Then_TheInlineFontStays()
        {
            // Arrange
            _mounted = V.Mount(_host.Root, V.Div(children: new[] { Target("font-bold") }));
            RunAFrame();
            _host.Root.Q<Label>("target").style.unityFontDefinition = FontDefinition.FromSDFFont(_monoNormal);

            // Act
            _mounted.Render(V.Div(children: new[] { Target(string.Empty) }));
            RunAFrame();

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_monoNormal));
        }

        [Test]
        public void Given_ALabelWhoseInlineFontTheBoldClassReplaced_When_TheFontClassIsRemoved_Then_TheAuthorsInlineFontIsBack()
        {
            // Arrange
            _mounted = V.Mount(_host.Root, V.Div(children: new[] { Target(string.Empty) }));
            RunAFrame();
            _host.Root.Q<Label>("target").style.unityFontDefinition = FontDefinition.FromSDFFont(_monoNormal);
            _mounted.Render(V.Div(children: new[] { Target("font-bold") }));
            RunAFrame();

            // Act
            _mounted.Render(V.Div(children: new[] { Target(string.Empty) }));
            RunAFrame();

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_monoNormal));
        }


        // GREEN_ON_BASE(characterization): the base resolves a bold label under a font-<name> ancestor the moment that
        // class changes, with a weight element between them. Settle the pending inheritors in `_pending`'s own order
        // instead of SettleInTreeOrder and the label can settle before the element between and read its old font.
        [Test]
        public void Given_AWeightElementBetweenAFamilyContainerAndABoldLabel_When_TheContainersFamilyChanges_Then_TheLabelFollowsAtOnce()
        {
            // Arrange
            VNode Tree(string containerClass) => V.Div(className: containerClass, children: new VNode[]
            {
                V.Div(className: "font-normal", children: new[] { Target("font-bold") }),
            });
            _mounted = V.Mount(_host.Root, Tree("font-serif"));
            RunAFrame();

            // Act
            _mounted.Render(Tree("font-mono"));

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_monoBold));
        }

        // GREEN_ON_BASE(characterization): the base writes no asset for a family nothing is registered under. Drop
        // `!VelvetFonts.Resolve(family, VelvetFontWeight.Normal, false).HasAsset` from `kept` in Locate and the label takes
        // the default family's Bold, the font the unnamed container inherits.
        [Test]
        public void Given_AFamilyContainerNamingNoRegisteredFamily_When_ABoldLabelMounts_Then_TheLabelWritesNoAsset()
        {
            // Arrange
            var tree = V.Div(className: "font-ghost", children: new[] { Target("font-bold") });

            // Act
            _mounted = V.Mount(_host.Root, tree);
            RunAFrame();
            RunAFrame();

            // Assert
            Assert.That(_host.Root.Q<Label>("target").style.unityFontDefinition.keyword, Is.EqualTo(StyleKeyword.Null));
        }

        [Test]
        public void Given_AFontListNamingTwoFamilies_When_AskedWhetherItHoldsTheSecondsFont_Then_ItDoes()
        {
            // Act
            var holds = VelvetFonts.NamedHolds("serif,mono", _monoBold);

            // Assert
            Assert.That(holds, Is.True);
        }

        [Test]
        public void Given_AFontListNamingTwoFamilies_When_AskedWhetherItHoldsAThirdFamilysFont_Then_ItDoesNot()
        {
            // Act
            var holds = VelvetFonts.NamedHolds("serif,mono", _sansBold);

            // Assert
            Assert.That(holds, Is.False);
        }

        [Test]
        public void Given_ALabelWhoseInlineFontTheBoldClassReplacedAndALineBoxCopyOfThatWrite_When_TheFontClassIsRemoved_Then_TheAuthorsInlineFontIsBack()
        {
            // Arrange
            _mounted = V.Mount(_host.Root, V.Div(children: new[] { Target(string.Empty) }));
            RunAFrame();
            var label = _host.Root.Q<Label>("target");
            label.style.unityFontDefinition = FontDefinition.FromSDFFont(_monoNormal);
            _mounted.Render(V.Div(children: new[] { Target("font-bold") }));
            RunAFrame();
            var user = new object();
            var copy = LineBoxFont.Acquire(user, _monoBold, 24f, 16f);
            label.style.unityFontDefinition = FontDefinition.FromSDFFont(copy);

            // Act
            _mounted.Render(V.Div(children: new[] { Target(string.Empty) }));
            RunAFrame();
            LineBoxFont.Release(user, copy);

            // Assert
            Assert.That(TargetAsset(), Is.SameAs(_monoNormal));
        }

        #endregion
    }
}

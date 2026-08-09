using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the four text-input props <c>V.TextField</c> declares beyond the password flag —
    /// placeholder, maxLength, isReadOnly, isDelayed.
    /// <list type="bullet">
    /// <item>Each reaches the element through a reconcile of the factory's node, not only through a
    /// direct applier call.</item>
    /// <item>Each, once dropped by a later render, restores what the element was constructed with. The
    /// drops are measured on a subclass built away from every default, so an implementation coalescing
    /// to UI Toolkit's own constants fails them.</item>
    /// <item>A member no render has declared — including the password flag, which predates the other
    /// four — is left where a <c>refCallback:</c> put it when a later render redeclares its
    /// neighbours.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class TextFieldInputPropTests : ReconcilerTestFixture
    {
        // Built away from TextField's own defaults on all four members, so the value a drop must restore
        // differs from the constant an implementation would otherwise coalesce to.
        internal sealed class PrefilledTextField : TextField
        {
            public const string BuiltPlaceholder = "built-in hint";
            public const int BuiltMaxLength = 8;

            public PrefilledTextField()
            {
                textEdition.placeholder = BuiltPlaceholder;
                maxLength = BuiltMaxLength;
                isReadOnly = true;
                isDelayed = true;
            }
        }

        [Test]
        public void Given_ADeclaredPlaceholder_When_TheFieldIsReconciled_Then_TheElementCarriesIt()
        {
            // Arrange
            var tree = new VNode[] { V.TextField(placeholder: "Search") };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(((TextField)Root!.ElementAt(0)).textEdition.placeholder, Is.EqualTo("Search"));
        }

        [Test]
        public void Given_ADeclaredMaxLength_When_TheFieldIsReconciled_Then_TheElementCarriesIt()
        {
            // Arrange
            var tree = new VNode[] { V.TextField(maxLength: 12) };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(((TextField)Root!.ElementAt(0)).maxLength, Is.EqualTo(12));
        }

        // The limit is written through TextField's own maxLength. Writing textEdition.maxLength instead
        // was rejected: it clips the displayed text when the limit narrows and leaves it clipped when the
        // limit widens again, and restoring a dropped limit is a widening whenever the element was built
        // with a looser one than the render declared — so that spelling strands the field showing a
        // truncated value. This is the reading that separates the two.
        [Test]
        public void Given_AMaxLengthThatClippedTheValue_When_ALaterRenderDropsIt_Then_TheClippedCharactersComeBack()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Custom<PrefilledTextField>(props: new FiberElementProps
                {
                    FieldValue = "abcdefgh",
                    TextField = new TextFieldSettings(MaxLength: 3),
                }),
            };
            var newTree = new VNode[]
            {
                V.Custom<PrefilledTextField>(props: new FiberElementProps { FieldValue = "abcdefgh" }),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            var whileClipped = element.Q<TextElement>()!.text;

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the clipped reading is folded in because the restored one alone is what an
            // implementation that never clipped in the first place would also produce.
            Assert.That(
                (whileClipped, element.Q<TextElement>()!.text),
                Is.EqualTo(("abc", "abcdefgh")));
        }

        [Test]
        public void Given_ADeclaredReadOnlyFlag_When_TheFieldIsReconciled_Then_TheElementCarriesIt()
        {
            // Arrange
            var tree = new VNode[] { V.TextField(isReadOnly: true) };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(((TextField)Root!.ElementAt(0)).isReadOnly, Is.True);
        }

        [Test]
        public void Given_ADeclaredDelayedFlag_When_TheFieldIsReconciled_Then_TheElementCarriesIt()
        {
            // Arrange
            var tree = new VNode[] { V.TextField(isDelayed: true) };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(((TextField)Root!.ElementAt(0)).isDelayed, Is.True);
        }

        [Test]
        public void Given_APlaceholderDeclaredEmpty_When_TheFieldIsReconciled_Then_TheElementCarriesAnEmptyOne()
        {
            // Arrange — an empty string is a declared empty hint, not an absent one, so it has to reach a
            // field built with a hint of its own and clear it.
            var tree = new VNode[]
            {
                V.Custom<PrefilledTextField>(props: new FiberElementProps
                {
                    TextField = new TextFieldSettings(Placeholder: string.Empty),
                }),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(((TextField)Root!.ElementAt(0)).textEdition.placeholder, Is.Empty);
        }

        [Test]
        public void Given_ADeclaredPlaceholder_When_ALaterRenderDropsIt_Then_TheElementReadsTheOneItWasBuiltWith()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Custom<PrefilledTextField>(props: new FiberElementProps
                {
                    TextField = new TextFieldSettings(Placeholder: "declared"),
                }),
            };
            var newTree = new VNode[] { V.Custom<PrefilledTextField>() };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            var whileDeclared = element.textEdition.placeholder;

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the identity term separates a restore from a remount, which would satisfy the
            // reading while the tree holds a different element.
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), element), whileDeclared, element.textEdition.placeholder),
                Is.EqualTo((true, "declared", PrefilledTextField.BuiltPlaceholder)));
        }

        [Test]
        public void Given_ADeclaredMaxLength_When_ALaterRenderDropsIt_Then_TheElementReadsTheOneItWasBuiltWith()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Custom<PrefilledTextField>(props: new FiberElementProps
                {
                    TextField = new TextFieldSettings(MaxLength: 3),
                }),
            };
            var newTree = new VNode[] { V.Custom<PrefilledTextField>() };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            var whileDeclared = element.maxLength;

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — same identity term, and for the same reason.
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), element), whileDeclared, element.maxLength),
                Is.EqualTo((true, 3, PrefilledTextField.BuiltMaxLength)));
        }

        [Test]
        public void Given_ADeclaredReadOnlyFlag_When_ALaterRenderDropsIt_Then_TheElementReadsTheOneItWasBuiltWith()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Custom<PrefilledTextField>(props: new FiberElementProps
                {
                    TextField = new TextFieldSettings(IsReadOnly: false),
                }),
            };
            var newTree = new VNode[] { V.Custom<PrefilledTextField>() };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            var whileDeclared = element.isReadOnly;

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — same identity term, and for the same reason.
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), element), whileDeclared, element.isReadOnly),
                Is.EqualTo((true, false, true)));
        }

        [Test]
        public void Given_ADeclaredDelayedFlag_When_ALaterRenderDropsIt_Then_TheElementReadsTheOneItWasBuiltWith()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Custom<PrefilledTextField>(props: new FiberElementProps
                {
                    TextField = new TextFieldSettings(IsDelayed: false),
                }),
            };
            var newTree = new VNode[] { V.Custom<PrefilledTextField>() };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);
            var whileDeclared = element.isDelayed;

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — same identity term, and for the same reason.
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), element), whileDeclared, element.isDelayed),
                Is.EqualTo((true, false, true)));
        }

        [Test]
        public void Given_AFieldThatNeverDeclaredAnyOfThem_When_AbsentSettingsAreApplied_Then_NothingIsWritten()
        {
            // Arrange
            var field = new PrefilledTextField();

            // Act
            FiberPropApplier.ApplyTextField(field, null);

            // Assert
            Assert.That(
                (field.textEdition.placeholder, field.maxLength, field.isReadOnly, field.isDelayed),
                Is.EqualTo((PrefilledTextField.BuiltPlaceholder, PrefilledTextField.BuiltMaxLength, true, true)));
        }

        // The five below share one sequence: a render declares one member, the refCallback — which the
        // create path invokes after applying props, so the record is taken before it — assigns another, and
        // a second render redeclares only the first. The undeclared member is nobody's to write, so it has
        // to survive. One callback instance stands in both trees, so the patch does not re-run it
        // (ReconcilerContext.InvokeRefCallback's identity skip) and a second assignment cannot stand in for
        // a survival.
        [Test]
        public void Given_APasswordFlagWrittenFromARefCallback_When_ALaterRenderRedeclaresThePlaceholder_Then_TheFlagSurvives()
        {
            // Arrange
            Func<VisualElement, Action> setFlag = el =>
            {
                ((TextField)el).isPasswordField = true;
                return () => { };
            };
            var oldTree = new VNode[] { V.TextField(placeholder: "Search", refCallback: setFlag) };
            var newTree = new VNode[] { V.TextField(placeholder: "Find", refCallback: setFlag) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the identity term separates a survival from a remount, whose fresh element would
            // run the callback again and read true however the applier behaved.
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), element), element.isPasswordField),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_APlaceholderWrittenFromARefCallback_When_ALaterRenderRedeclaresTheMaxLength_Then_TheHintSurvives()
        {
            // Arrange
            Func<VisualElement, Action> setHint = el =>
            {
                ((TextField)el).textEdition.placeholder = "from ref";
                return () => { };
            };
            var oldTree = new VNode[] { V.TextField(maxLength: 12, refCallback: setHint) };
            var newTree = new VNode[] { V.TextField(maxLength: 13, refCallback: setHint) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — same identity term, and for the same reason.
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), element), element.textEdition.placeholder),
                Is.EqualTo((true, "from ref")));
        }

        [Test]
        public void Given_AMaxLengthWrittenFromARefCallback_When_ALaterRenderRedeclaresThePlaceholder_Then_TheLimitSurvives()
        {
            // Arrange
            Func<VisualElement, Action> setLimit = el =>
            {
                ((TextField)el).maxLength = 4;
                return () => { };
            };
            var oldTree = new VNode[] { V.TextField(placeholder: "Search", refCallback: setLimit) };
            var newTree = new VNode[] { V.TextField(placeholder: "Find", refCallback: setLimit) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — same identity term, and for the same reason.
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), element), element.maxLength),
                Is.EqualTo((true, 4)));
        }

        [Test]
        public void Given_AReadOnlyFlagWrittenFromARefCallback_When_ALaterRenderRedeclaresThePlaceholder_Then_TheFlagSurvives()
        {
            // Arrange
            Func<VisualElement, Action> setFlag = el =>
            {
                ((TextField)el).isReadOnly = true;
                return () => { };
            };
            var oldTree = new VNode[] { V.TextField(placeholder: "Search", refCallback: setFlag) };
            var newTree = new VNode[] { V.TextField(placeholder: "Find", refCallback: setFlag) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — same identity term, and for the same reason.
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), element), element.isReadOnly),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ADelayedFlagWrittenFromARefCallback_When_ALaterRenderRedeclaresThePlaceholder_Then_TheFlagSurvives()
        {
            // Arrange
            Func<VisualElement, Action> setFlag = el =>
            {
                ((TextField)el).isDelayed = true;
                return () => { };
            };
            var oldTree = new VNode[] { V.TextField(placeholder: "Search", refCallback: setFlag) };
            var newTree = new VNode[] { V.TextField(placeholder: "Find", refCallback: setFlag) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var element = (TextField)Root!.ElementAt(0);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — same identity term, and for the same reason.
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), element), element.isDelayed),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_APooledFieldWhoseLastTenantDeclaredAPlaceholder_When_ItsNextTenantWritesOneFromARefCallback_Then_TheHintSurvives()
        {
            // Arrange — the first tenancy records a placeholder default and then unmounts, which is what
            // hands the element to the shared pool; the second declares only the length limit.
            var declaring = new VNode[] { V.TextField(placeholder: "previous tenant") };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), declaring);
            var pooled = (TextField)Root!.ElementAt(0);
            Reconciler.Reconcile(Root, declaring, Array.Empty<VNode>());
            Func<VisualElement, Action> setHint = el =>
            {
                ((TextField)el).textEdition.placeholder = "from ref";
                return () => { };
            };
            var oldTree = new VNode[] { V.TextField(maxLength: 12, refCallback: setHint) };
            var newTree = new VNode[] { V.TextField(maxLength: 13, refCallback: setHint) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler.Reconcile(Root, oldTree, newTree);

            // Assert — the identity term is what makes this a reading of the recycled element; a fresh one
            // would carry no record from either tenancy and satisfy the hint on its own.
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), pooled),
                    ((TextField)Root!.ElementAt(0)).textEdition.placeholder),
                Is.EqualTo((true, "from ref")));
        }
    }
}

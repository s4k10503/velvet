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
        public void Given_a_declared_placeholder_When_the_field_is_reconciled_Then_the_element_shows_it()
        {
            // Arrange
            var tree = new VNode[] { V.TextField(placeholder: "Search") };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(((TextField)Root!.ElementAt(0)).textEdition.placeholder, Is.EqualTo("Search"));
        }

        [Test]
        public void Given_a_declared_max_length_When_the_field_is_reconciled_Then_the_element_carries_it()
        {
            // Arrange
            var tree = new VNode[] { V.TextField(maxLength: 12) };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(((TextField)Root!.ElementAt(0)).maxLength, Is.EqualTo(12));
        }

        [Test]
        public void Given_a_declared_read_only_flag_When_the_field_is_reconciled_Then_the_element_refuses_edits()
        {
            // Arrange
            var tree = new VNode[] { V.TextField(isReadOnly: true) };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(((TextField)Root!.ElementAt(0)).isReadOnly, Is.True);
        }

        [Test]
        public void Given_a_declared_delayed_flag_When_the_field_is_reconciled_Then_the_element_commits_on_blur()
        {
            // Arrange
            var tree = new VNode[] { V.TextField(isDelayed: true) };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(((TextField)Root!.ElementAt(0)).isDelayed, Is.True);
        }

        [Test]
        public void Given_a_placeholder_declared_empty_When_the_field_is_reconciled_Then_the_element_shows_no_hint()
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
        public void Given_a_declared_placeholder_When_a_later_render_drops_it_Then_the_element_reads_the_one_it_was_built_with()
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
        public void Given_a_declared_max_length_When_a_later_render_drops_it_Then_the_element_reads_the_one_it_was_built_with()
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
        public void Given_a_declared_read_only_flag_When_a_later_render_drops_it_Then_the_element_reads_the_one_it_was_built_with()
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
        public void Given_a_declared_delayed_flag_When_a_later_render_drops_it_Then_the_element_reads_the_one_it_was_built_with()
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
        public void Given_a_field_that_never_declared_any_of_them_When_absent_settings_are_applied_Then_nothing_is_written()
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
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Fails when a manipulator that holds children by reference can take a value back off one without
    /// asking whether that value is still its own. Four were written that way and each was reviewed and
    /// merged; nothing failed, because the defect only shows when a pooled child is re-rented under a
    /// second container — see <see cref="PerChildManipulatorOwnershipTests"/> for what a user then sees.
    /// Also holds the claim's own bookkeeping, which no case there can see: a table left out of the pure
    /// element side-tables, and an entry a release fails to drop.
    /// </summary>
    /// <remarks>
    /// Both the subject set and the requirement are derived from the code. The subjects are the
    /// manipulators <c>ReconcilerContext</c> keys by container element, which is how a class list gets one
    /// attached and therefore how two containers come to hold the same child; among those, the ones
    /// declaring a field that holds <c>VisualElement</c>s. A fifth family wired the same way is a subject
    /// the day it is written, with no list here to update.
    /// </remarks>
    [TestFixture]
    internal sealed class PerChildManipulatorClaimGuardTests
    {
        private static PropertyInfo[] ContextProperties()
            => typeof(ReconcilerContext).GetProperties(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        private static bool KeyedByElement(Type type, out Type value)
        {
            value = typeof(void);
            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(Dictionary<,>))
            {
                return false;
            }
            var arguments = type.GetGenericArguments();
            value = arguments[1];
            return arguments[0] == typeof(VisualElement);
        }

        /// <summary>The manipulators a container's own class list gets attached, keyed by that container.</summary>
        private static IReadOnlyList<Type> ContextTrackedManipulators()
            => ContextProperties()
                .Select(property => KeyedByElement(property.PropertyType, out var value) ? value : null)
                .Where(value => value != null
                    && typeof(Manipulator).IsAssignableFrom(value)
                    && value.Assembly == typeof(V).Assembly)
                .Distinct()
                .OrderBy(value => value!.Name, StringComparer.Ordinal)
                .ToList()!;

        /// <summary>
        /// A field that holds elements rather than one element: the tracking list a stale sweep walks.
        /// </summary>
        private static bool HoldsChildrenByReference(Type type)
            => type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Any(field => Holds(field.FieldType));

        private static bool Holds(Type type)
            => (type.IsArray && type.GetElementType() == typeof(VisualElement))
                || (type.IsGenericType && type.GetGenericArguments().Any(argument => argument == typeof(VisualElement)));

        private static bool AsksTheClaim(ModuleDefinition module, Type type)
        {
            var definition = module.GetType(type.FullName!.Replace('+', '/'));
            return definition != null
                && definition.Methods
                    .Where(method => method.HasBody)
                    .SelectMany(method => method.Body.Instructions)
                    .Any(instruction => instruction.Operand is MethodReference reference
                        && reference.DeclaringType.FullName == typeof(StyleChildOwnership).FullName
                        && reference.Name == nameof(StyleChildOwnership.TryRelease));
        }

        [Test]
        public void Given_EveryContextTrackedManipulatorHoldingChildren_When_ItsBodiesAreRead_Then_EachAsksTheClaimBeforeTurningAValueOff()
        {
            // Arrange
            using var runtime = ModuleDefinition.ReadModule(typeof(V).Assembly.Location);
            var subjects = ContextTrackedManipulators().Where(HoldsChildrenByReference).ToList();

            // Act
            var silent = subjects
                .Where(type => !AsksTheClaim(runtime, type))
                .Select(type => type.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            // Assert — the subject count rides along because a derivation that matched nothing would agree
            // with a codebase where every manipulator asks.
            Assert.That((subjects.Count > 1, string.Join("\n", silent)), Is.EqualTo((true, string.Empty)),
                "these track children by reference and reset a value on one that has left, with nothing "
                + "asking whether the value is still theirs; route every turn-off through "
                + nameof(StyleChildOwnership) + " against a claim table on ReconcilerContext");
        }

        [Test]
        public void Given_AChildTheOwnerReleased_When_TheClaimTableIsRead_Then_TheEntryIsGone()
        {
            // Arrange — a reparent puts the child through no cleanup pass, so the release is the only thing
            // that can drop its claim; an entry left behind holds that element for the life of the context.
            var ctx = new ReconcilerContext();
            var container = new VisualElement();
            container.Add(new VisualElement());
            var child = new VisualElement();
            container.Add(child);
            var gap = new StyleGapManipulator(ctx, 16f, GapAxis.Horizontal, false, false);
            container.AddManipulator(gap);
            var claimed = ctx.ChildBoxOwners.ContainsKey(child);
            container.Remove(child);
            new VisualElement().Add(child);

            // Act
            gap.Apply();

            // Assert — that the container had claimed it rides along, since a table that never held the
            // element and one the release emptied read the same.
            Assert.That((claimed, ctx.ChildBoxOwners.ContainsKey(child)), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_EveryPerChildClaimTable_When_TheContextIsBuilt_Then_EachIsEnrolledInThePureElementSideTables()
        {
            // Arrange — an unenrolled claim table is swept by neither element cleanup nor dispose, so it
            // holds every element it ever claimed and answers "mine" for a recycled one forever.
            var ctx = new ReconcilerContext();
            var enrolled = (IDictionary[])typeof(ReconcilerContext)
                .GetField("_pureElementSideTables", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(ctx)!;
            var tables = ContextProperties()
                .Where(property => property.PropertyType == typeof(Dictionary<VisualElement, Manipulator>))
                .ToList();

            // Act
            var loose = tables
                .Where(property => !enrolled.Any(table => ReferenceEquals(table, property.GetValue(ctx))))
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            // Assert — the table count rides along, for the same reason as above.
            Assert.That((tables.Count > 1, string.Join("\n", loose)), Is.EqualTo((true, string.Empty)),
                "add each to _pureElementSideTables so a claim dies with the element that carries it");
        }
    }
}

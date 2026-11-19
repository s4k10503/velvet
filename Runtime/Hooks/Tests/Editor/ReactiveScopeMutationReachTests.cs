using System;
using System.Linq;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies which variables the weaver reads as changing without their identity doing so, by reading the
    /// <see cref="MemoizationReportAttribute"/> it leaves on each component method: a scope reading such a variable
    /// is declined, and the report names the variable.
    /// <list type="bullet">
    /// <item>A stable ref handed to a call that may change it is such a variable, and so is a local the ref is copied
    /// into and that local is handed on; a variable merely assigned or read next to one is not.</item>
    /// <item>A struct local a member is called on is such a variable unless the struct is readonly.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class ReactiveScopeMutationReachTests
    {
        private static void Poke(object value)
        {
        }

        private static void Poke(object first, object second)
        {
        }

        private static string Describe(object value) => value.ToString();

        private static string Touch(string value) => value;

        [AttributeUsage(AttributeTargets.Struct)]
        private sealed class MarkAttribute : Attribute
        {
        }

        // Null where the method carries no report, which neither expected answer is.
        private static bool? DeclinesALabel(string methodName)
            => ReactiveScopeTestSupport.ReportOf(typeof(ReactiveScopeMutationReachTests), methodName)?.Bailouts
                .Any(note => note.StartsWith("the call to V.Text", StringComparison.Ordinal)
                    && note.Contains(", which an opaque value or a mutation reaches"));

        [Component]
        private static VNode HandsARefOnAsTheLastArgument()
        {
            var (first, _) = Hooks.UseState(0);
            var held = Hooks.UseRef<string>(() => "held");
            Poke(held);
            return V.Div("handed", V.Text(Describe(first)), V.Text(held.Current));
        }

        // GREEN_ON_BASE(characterization): the base declines a scope reading a stable ref that was handed to a call as its last argument.
        // Reading `<=` as `<` where `TaintContainersIn` walks an argument's instructions leaves out the read of the
        // ref right before the call and keeps that scope cached.
        [Test]
        public void Given_AStableRefHandedToACallAsItsLastArgument_When_Compiled_Then_TheLabelReadingItIsDeclined()
        {
            // Act
            var declined = DeclinesALabel(nameof(HandsARefOnAsTheLastArgument));

            // Assert
            Assert.That(declined, Is.True);
        }

        [Component]
        private static VNode ReadsARefRightAfterACall(string name)
        {
            var (first, _) = Hooks.UseState(0);
            var held = Hooks.UseRef<string>(() => "held");
            _ = string.Concat(Touch(name), held.Current);
            return V.Div("after", V.Text(Describe(first)), V.Text(held.Current));
        }

        // GREEN_ON_BASE(characterization): the base keeps a scope reading a stable ref that is read right after a call it was not handed.
        // Reading `-` as `+` where `PropagateMutation` bounds each argument takes in the instruction after the call,
        // which reads the ref, and declines that scope.
        [Test]
        public void Given_AStableRefReadRightAfterACallItWasNotHanded_When_Compiled_Then_TheLabelReadingItIsCached()
        {
            // Act
            var declined = DeclinesALabel(nameof(ReadsARefRightAfterACall));

            // Assert
            Assert.That(declined, Is.False);
        }

        [Component]
        private static VNode AssignsALocalRightBeforeARefIsRead(string name)
        {
            var (first, _) = Hooks.UseState(0);
            var held = Hooks.UseRef<string>(() => "held");
            var label = name;
            Poke(held.Current!, label);
            return V.Div("assigned", V.Text(Describe(first)), V.Text(label));
        }

        // GREEN_ON_BASE(characterization): the base keeps a scope reading a local assigned right before a stable ref is read.
        // Reading `-` as `+` where `CarryValue` bounds what the local is assigned from takes in the read of the ref
        // after the store, makes the local a stable container, and declines that scope once the local is handed on.
        [Test]
        public void Given_ALocalAssignedFromAPropRightBeforeAStableRefIsRead_When_Compiled_Then_TheLabelReadingItIsCached()
        {
            // Act
            var declined = DeclinesALabel(nameof(AssignsALocalRightBeforeARefIsRead));

            // Assert
            Assert.That(declined, Is.False);
        }

        [Component]
        private static VNode HandsOnACopyOfARef()
        {
            var (first, _) = Hooks.UseState(0);
            var held = Hooks.UseRef<string>(() => "held");
            var alias = held;
            Poke(alias);
            return V.Div("copied", V.Text(Describe(first)), V.Text(alias.Current));
        }

        // GREEN_ON_BASE(characterization): the base declines a scope reading a copy of a stable ref that was handed to a call.
        // Reading `!=` as `==` where `RangeHoldsContainer` reads a variable leaves the copy out of the stable
        // containers, so the call changes nothing the scope reads and the scope stays cached.
        [Test]
        public void Given_ACopyOfAStableRefHandedToACall_When_Compiled_Then_TheLabelReadingTheCopyIsDeclined()
        {
            // Act
            var declined = DeclinesALabel(nameof(HandsOnACopyOfARef));

            // Assert
            Assert.That(declined, Is.True);
        }

        private readonly struct Tag
        {
            public readonly string Name;

            public Tag(string name)
            {
                Name = name;
            }

            public string Shout() => Name + "!";
        }

        [Component]
        private static VNode CallsAMemberOfAReadonlyStruct(string name, int other)
        {
            var tag = new Tag(name);
            var shout = tag.Shout();
            return V.Div("readonly", V.Text(shout), V.Text(Convert.ToString(other)));
        }

        // GREEN_ON_BASE(characterization): the base keeps a scope reading what a member of a readonly struct local returned.
        // Reading `==` as `!=` where `OfReadOnlyStruct` tests the resolved type reads the call as one that may change
        // the local and declines that scope.
        [Test]
        public void Given_AMemberOfAReadonlyStructLocalCalled_When_Compiled_Then_TheLabelReadingItsResultIsCached()
        {
            // Act
            var declined = DeclinesALabel(nameof(CallsAMemberOfAReadonlyStruct));

            // Assert
            Assert.That(declined, Is.False);
        }

        [Mark]
        private struct Counter
        {
            public int Count;

            public void Bump()
            {
                Count++;
            }
        }

        [Component]
        private static VNode CallsAMemberOfAMutableStruct(int other)
        {
            var counter = new Counter();
            counter.Bump();
            return V.Div("mutable", V.Text(Convert.ToString(counter.Count)), V.Text(Convert.ToString(other)));
        }

        // GREEN_ON_BASE(characterization): the base declines a scope reading a struct local a member that may change it was called on.
        // The struct carries an attribute that is not `IsReadOnlyAttribute`. Returning true at the end of
        // `OfReadOnlyStruct`, or reading `==` as `!=` where it looks for `IsReadOnlyAttribute`, takes the struct for a
        // readonly one and keeps that scope cached.
        [Test]
        public void Given_AMemberOfAMutableStructCarryingAnAttributeCalled_When_Compiled_Then_TheLabelReadingTheLocalIsDeclined()
        {
            // Act
            var declined = DeclinesALabel(nameof(CallsAMemberOfAMutableStruct));

            // Assert
            Assert.That(declined, Is.True);
        }
    }
}

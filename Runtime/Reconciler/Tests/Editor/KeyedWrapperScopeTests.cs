using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that an explicitly keyed wrapper keeps what it encloses apart from its siblings' subtrees.
    /// <list type="bullet">
    /// <item>Under one keyed scope, a keyed Fragment and a keyed Provider carrying the same key each keep
    /// their own element across a re-render, and so do a Fragment keyed <c>"1"</c> and an unkeyed Fragment
    /// at index 1.</item>
    /// <item>A component rendered by a keyed memo, keyed or not, keeps its state when the memo moves among its
    /// keyed siblings, and its own re-render still reads the Provider above the memo.</item>
    /// </list>
    /// <see cref="FiberKeyingTests"/> owns the scope strings these positions are composed from.
    /// </summary>
    [TestFixture]
    internal sealed class KeyedWrapperScopeTests
    {
        private MountedTree? _mounted;
        private static StateUpdater<int> s_setTick;
        private static StateUpdater<bool> s_setPrepended;
        private static readonly ComponentContext<string> Theme = ComponentContext<string>.Create("default");

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
        }

        private static string LabelNames(VisualElement container) =>
            string.Join("|", container.Query<Label>().ToList().Select(label => label.name));

        [Component]
        private static VNode SameKeyFragmentAndProviderHost()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(children: new VNode?[]
            {
                V.Fragment(key: "outer", children: new VNode?[]
                {
                    V.Fragment(key: "b", children: new VNode?[] { V.Label(name: "fragment", text: tick.ToString()) }),
                    V.Provider(Theme, "inside", key: "b", children: new VNode?[]
                    {
                        V.Label(name: "provider", text: tick.ToString()),
                    }),
                }),
            });
        }

        [Test]
        public void Given_SameKeyedFragmentAndProviderUnderOneScope_When_TheHostReRenders_Then_EachKeepsItsOwnElement()
        {
            // Arrange
            var container = new VisualElement();
            _mounted = V.Mount(container, V.Component(SameKeyFragmentAndProviderHost, key: "host"));
            var fragmentLabel = container.Q<Label>("fragment");

            // Act
            s_setTick.Invoke(1);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (LabelNames(container), ReferenceEquals(container.Q<Label>("fragment"), fragmentLabel)),
                Is.EqualTo(("fragment|provider", true)));
        }

        [Component]
        private static VNode KeyedAndPositionalFragmentHost()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(children: new VNode?[]
            {
                V.Fragment(key: "outer", children: new VNode?[]
                {
                    V.Fragment(key: "1", children: new VNode?[] { V.Label(name: "keyed", text: tick.ToString()) }),
                    V.Fragment(children: new VNode?[] { V.Label(name: "positional", text: tick.ToString()) }),
                }),
            });
        }

        [Test]
        public void Given_FragmentKeyedOneBesideUnkeyedFragmentAtIndexOne_When_TheHostReRenders_Then_EachKeepsItsOwnElement()
        {
            // Arrange
            var container = new VisualElement();
            _mounted = V.Mount(container, V.Component(KeyedAndPositionalFragmentHost, key: "host"));
            var keyedLabel = container.Q<Label>("keyed");

            // Act
            s_setTick.Invoke(1);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(
                (LabelNames(container), ReferenceEquals(container.Q<Label>("keyed"), keyedLabel)),
                Is.EqualTo(("keyed|positional", true)));
        }

        [Component]
        private static VNode Row()
        {
            var (count, setCount) = Hooks.UseState(0);
            return V.Div(children: new VNode?[]
            {
                V.Button(name: "inc", onClick: () => setCount.Invoke(c => c + 1)),
                V.Label(name: "out", text: count.ToString()),
            });
        }

        [Component]
        private static VNode MemoizedRowsHost()
        {
            var (prepended, setPrepended) = Hooks.UseState(false);
            s_setPrepended = setPrepended;
            var ids = prepended ? new[] { "a", "b" } : new[] { "b" };
            return V.Div(children: ids
                .Select(id => (VNode?)V.MemoizedWithKey(id, () => V.Component(Row, key: id), id))
                .ToArray());
        }

        [Component]
        private static VNode MemoizedUnkeyedRowsHost()
        {
            var (prepended, setPrepended) = Hooks.UseState(false);
            s_setPrepended = setPrepended;
            var ids = prepended ? new[] { "a", "b" } : new[] { "b" };
            return V.Div(children: ids
                .Select(id => (VNode?)V.MemoizedWithKey(id, () => V.Component(Row), id))
                .ToArray());
        }

        [Test]
        public void Given_UnkeyedComponentInsideKeyedMemo_When_AnotherMemoIsInsertedBeforeIt_Then_TheComponentKeepsItsState()
        {
            // Arrange
            var container = new VisualElement();
            _mounted = V.Mount(container, V.Component(MemoizedUnkeyedRowsHost, key: "host"));
            container.Q<Button>("inc").SimulateClick();

            // Act
            s_setPrepended.Invoke(true);
            _mounted.FlushStateForTest();

            // Assert
            var outputs = string.Join(",", container.Query<Label>(name: "out").ToList().Select(label => label.text));
            Assert.That(outputs, Is.EqualTo("0,1"));
        }

        [Component]
        private static VNode ThemeReader()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            var theme = Hooks.UseContext(Theme);
            return V.Label(name: "theme", text: theme + tick);
        }

        [Component]
        private static VNode ProvidedKeyedMemoHost()
            => V.Div(children: new VNode?[]
            {
                V.Provider(Theme, "provided", children: new VNode?[]
                {
                    V.MemoizedWithKey("memo", () => V.Component(ThemeReader), 1),
                }),
            });

        // GREEN_ON_BASE(characterization): the base places a keyed memo's inner by its index from both walks.
        // Once the expansion walk places it by the memo's key, the spine walk has to as well to keep this passing.
        [Test]
        public void Given_ConsumerInsideKeyedMemoUnderProvider_When_TheConsumerReRendersAlone_Then_ItStillReadsTheProvider()
        {
            // Arrange
            var container = new VisualElement();
            _mounted = V.Mount(container, V.Component(ProvidedKeyedMemoHost, key: "host"));

            // Act
            s_setTick.Invoke(1);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(container.Q<Label>("theme")?.text, Is.EqualTo("provided1"));
        }

        // GREEN_ON_BASE(characterization): the base keeps a keyed component's state when its keyed memo moves.
        // A registry position that took the memo's index rather than its key would remount the row here.
        [Test]
        public void Given_KeyedComponentInsideKeyedMemo_When_AnotherMemoIsInsertedBeforeIt_Then_TheComponentKeepsItsState()
        {
            // Arrange
            var container = new VisualElement();
            _mounted = V.Mount(container, V.Component(MemoizedRowsHost, key: "host"));
            container.Q<Button>("inc").SimulateClick();

            // Act
            s_setPrepended.Invoke(true);
            _mounted.FlushStateForTest();

            // Assert
            var outputs = string.Join(",", container.Query<Label>(name: "out").ToList().Select(label => label.text));
            Assert.That(outputs, Is.EqualTo("0,1"));
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies where a <c>V.Memoized</c>'s dependency-cache entry is filed — under the component whose
    /// output holds the memo, a Portal it sits under within that output and the container the memo's output
    /// lands in, as well as its position there — and that Velvet drops the entry when it tears down that
    /// component, that Portal or that container. <see cref="ComponentContainerIdentityTests"/> owns the
    /// container member of the component registry's key, and <see cref="PortalChildFiberContinuityTests"/>
    /// its Portal member.
    /// </summary>
    [TestFixture]
    internal sealed class MemoCacheCollisionTests : ReconcilerTestFixture
    {
        private MountedTree _mounted;

        public override void SetUp()
        {
            base.SetUp();
            s_left = 0;
            s_right = 0;
            s_setShown = default;
            s_bumpHost = default;
            s_bumps.Clear();
            s_setShowFirstOwner = default;
            s_bodyMemoRuns = 0;
            s_setBody = default;
            s_portalTarget = null;
            s_setPortedDivShown = default;
            s_setTab = default;
            s_setGeneration = default;
            s_retargetRuns = 0;
            s_throwNow = false;
            s_content = null;
            s_fallback = null;
            s_keyedChildRuns = 0;
            s_setKeyedChild = default;
            RuntimeStateProbe.ClearPortalRegistry();
        }

        public override void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            RuntimeStateProbe.ClearPortalRegistry();
            base.TearDown();
        }

        private static int s_left;
        private static int s_right;

        private static string TextIn(VisualElement container)
            => container?.Q<Label>()?.text ?? "<absent>";

        // What the cache and its two indexes hold, read by reflection: the entries; the positions indexed and the
        // entries filed under them; the fibers and elements indexed and the entries filed under them. -1 where
        // the field does not resolve.
        private static (int Entries, int Positions, int PositionFilings, int Referents, int ReferentFilings) Held(
            FiberMemoCache cache)
        {
            var entries = Index(cache, "_cache");
            var byPosition = Index(cache, "_entriesByPosition");
            var byReferent = Index(cache, "_entriesByReferent");
            return (entries?.Count ?? -1, byPosition?.Count ?? -1, Filings(byPosition), byReferent?.Count ?? -1,
                Filings(byReferent));
        }

        private static IDictionary Index(FiberMemoCache cache, string field)
            => typeof(FiberMemoCache).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(cache) as IDictionary;

        private static int Filings(IDictionary index)
            => index == null ? -1 : index.Values.Cast<IEnumerable>().Sum(filed => filed.Cast<object>().Count());

        private static FiberMemoCache CacheOf(MountedTree mounted) => mounted.Root.Reconciler.Context.FiberMemoCache;

        [Test]
        public void Given_TwoContainersEachHoldingAnUnkeyedMemoAtTheirFirstChild_When_TheyMount_Then_EachRunsItsOwnFactory()
        {
            // Arrange
            var left = 0;
            var right = 0;
            var tree = new VNode[]
            {
                V.Div(name: "left", children: new VNode?[]
                {
                    V.Memoized(() => { left++; return V.Label(text: "left"); }, 1),
                }),
                V.Div(name: "right", children: new VNode?[]
                {
                    V.Memoized(() => { right++; return V.Label(text: "right"); }, 1),
                }),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(
                (left, right, TextIn(Root.Q<VisualElement>("left")), TextIn(Root.Q<VisualElement>("right"))),
                Is.EqualTo((1, 1, "left", "right")));
        }

        [Component]
        private static VNode LeftPanel()
            => V.Memoized(() => { s_left++; return V.Label(text: "left"); }, 1);

        [Component]
        private static VNode RightPanel()
            => V.Memoized(() => { s_right++; return V.Label(text: "right"); }, 1);

        [Test]
        public void Given_TwoComponentsRenderingIntoOneContainer_When_TheyMount_Then_EachMemoRunsItsOwnFactory()
        {
            // Arrange
            var tree = new VNode[] { V.Component(LeftPanel), V.Component(RightPanel) };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(
                (s_left, s_right, ((Label)Root.ElementAt(1)).text),
                Is.EqualTo((1, 1, "right")));
        }

        [Component(Compiler = false)]
        private static VNode HomeShell()
            => V.Div(name: "home-shell", children: new VNode[]
            {
                V.Memoized(() => { s_left++; return V.Label(name: "backdrop", text: "home"); }, new object[0]),
                V.Outlet(),
            });

        [Component(Compiler = false)]
        private static VNode BattleShell()
            => V.Div(name: "battle-shell", children: new VNode[]
            {
                V.Memoized(() => { s_right++; return V.Label(name: "backdrop", text: "battle"); }, new object[0]),
                V.Outlet(),
            });

        [Component(Compiler = false)]
        private static VNode RoutedMemoShells(RouterLocation location)
            => V.Provider(RouterContext.Location, location, children: new VNode[]
            {
                V.Provider(RouterContext.Depth, 0, children: new VNode[] { V.Outlet() }),
            });

        private static Router ShellRouter() => new(new[]
        {
            new RouteDefinition
            {
                Path = "hdd", Element = V.Component(HomeShell),
                Children = new[] { new RouteDefinition { Path = "home" } },
            },
            new RouteDefinition
            {
                Path = "prebattle", Element = V.Component(BattleShell),
                Children = new[] { new RouteDefinition { Path = "difficulty" } },
            },
        });

        private VNode[] ShowRoute(Router router, string path, VNode[] before)
        {
            router.NavigateAsync(path).GetAwaiter().GetResult();
            var after = new VNode[] { V.Component(RoutedMemoShells, router.CurrentLocation, key: "app") };
            Reconciler.Reconcile(Root, before, after);
            return after;
        }

        [Test]
        public void Given_TwoRouteShellsWithAnUnkeyedMemoFirst_When_NavigationReplacesTheShell_Then_TheNewShellBuildsItsOwnBackdrop()
        {
            // Arrange
            using var router = ShellRouter();
            var before = ShowRoute(router, "/hdd/home", Array.Empty<VNode>());
            var first = Root.Q<Label>("backdrop")?.text;

            // Act
            ShowRoute(router, "/prebattle/difficulty", before);

            // Assert
            Assert.That((first, Root.Q<VisualElement>("home-shell") == null,
                    Root.Q<VisualElement>("battle-shell")?.Q<Label>("backdrop")?.text, s_left, s_right),
                Is.EqualTo(("home", true, "battle", 1, 1)));
        }

        [Test]
        public void Given_AMemoizedRouteShellThatWasUnmounted_When_NavigationReturnsToIt_Then_ItsBackdropIsBuiltAgain()
        {
            // Arrange
            using var router = ShellRouter();
            var home = ShowRoute(router, "/hdd/home", Array.Empty<VNode>());
            var battle = ShowRoute(router, "/prebattle/difficulty", home);
            var between = Root.Q<Label>("backdrop")?.text;

            // Act
            ShowRoute(router, "/hdd/home", battle);

            // Assert
            Assert.That((between, Root.Q<VisualElement>("battle-shell") == null,
                    Root.Q<VisualElement>("home-shell")?.Q<Label>("backdrop")?.text, s_left, s_right),
                Is.EqualTo(("battle", true, "home", 2, 1)));
        }

        [Test]
        public void Given_TwoUnkeyedFragmentsEachHoldingAMemoAtTheirFirstChild_When_TheyMount_Then_EachRunsItsOwnFactory()
        {
            // Arrange
            var left = 0;
            var right = 0;
            var tree = new VNode[]
            {
                V.Fragment(new VNode?[] { V.Memoized(() => { left++; return V.Label(text: "left"); }, 1) }),
                V.Fragment(new VNode?[] { V.Memoized(() => { right++; return V.Label(text: "right"); }, 1) }),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(
                (left, right, ((Label)Root.ElementAt(1)).text),
                Is.EqualTo((1, 1, "right")));
        }

        [Test]
        public void Given_AnExplicitKeySpellingTheNeighbouringMemoScope_When_BothMount_Then_TheKeyedMemoRunsItsOwnFactory()
        {
            // Arrange — "m0" is what FiberKeying.MemoScope composes for an unkeyed memo at node index 0.
            var unkeyed = 0;
            var keyed = 0;
            var tree = new VNode[]
            {
                V.Memoized(() => { unkeyed++; return V.Label(text: "unkeyed"); }, 1),
                V.MemoizedWithKey(FiberKeying.MemoScope(null, 0), () => { keyed++; return V.Label(text: "keyed"); }, 1),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(
                (unkeyed, keyed, ((Label)Root.ElementAt(1)).text),
                Is.EqualTo((1, 1, "keyed")));
        }

        [Test]
        public void Given_OneExplicitKeyWrittenIntoTwoContainers_When_TheyMount_Then_EachRunsItsOwnFactory()
        {
            // Arrange
            var left = 0;
            var right = 0;
            var tree = new VNode[]
            {
                V.Div(name: "left", children: new VNode?[]
                {
                    V.MemoizedWithKey("section", () => { left++; return V.Label(text: "left"); }, 1),
                }),
                V.Div(name: "right", children: new VNode?[]
                {
                    V.MemoizedWithKey("section", () => { right++; return V.Label(text: "right"); }, 1),
                }),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(
                (left, right, TextIn(Root.Q<VisualElement>("left")), TextIn(Root.Q<VisualElement>("right"))),
                Is.EqualTo((1, 1, "left", "right")));
        }

        [Test]
        public void Given_TwoContainersWhoseMemosCarryDifferentDependencies_When_ReRenderedWithBothUnchanged_Then_NeitherRunsItsFactoryAgain()
        {
            // Arrange — unequal dependency arrays, so a shared entry misses on whichever memo reaches it
            // second, which reaches ReturnRetiredTree with the other's inner instead of returning it.
            var left = 0;
            var right = 0;
            VNode[] Tree() => new VNode[]
            {
                V.Div(name: "left", children: new VNode?[]
                {
                    V.Memoized(() => { left++; return V.Label(text: "left"); }, "one"),
                }),
                V.Div(name: "right", children: new VNode?[]
                {
                    V.Memoized(() => { right++; return V.Label(text: "right"); }, "two"),
                }),
            };
            var first = Tree();
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), first);

            // Act
            Reconciler.Reconcile(Root, first, Tree());

            // Assert
            Assert.That((left, right), Is.EqualTo((1, 1)));
        }

        // GREEN_ON_BASE(characterization): an explicit memo key already ignores the sibling position it
        // is written at. Qualifying the key by its declaring component and its container leaves that
        // alone, and filing the key in place of its own index is what keeps it that way.
        [Test]
        public void Given_AKeyedMemoMovedToAnotherSiblingPosition_When_ReRendered_Then_ItsFactoryDoesNotRunAgain()
        {
            // Arrange
            var calls = 0;
            var first = new VNode[]
            {
                V.MemoizedWithKey("section", () => { calls++; return V.Label(text: "section"); }, 1),
            };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), first);

            // Act — a new sibling ahead of it moves the memo to the second position.
            var second = new VNode[]
            {
                V.Label(text: "banner"),
                V.MemoizedWithKey("section", () => { calls++; return V.Label(text: "section"); }, 1),
            };
            Reconciler.Reconcile(Root, first, second);

            // Assert
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void Given_TwoListFragmentsWhoseIdsOverlap_When_TheyMount_Then_EachRowShowsItsOwnList()
        {
            // Arrange — V.List gives each row the selector's key, so both lists hold a memo keyed "a".
            var tree = new VNode[]
            {
                V.ListFragment(new[] { "a" }, id => id, id => V.Memoized(() => V.Label(text: "favourite " + id), id), key: "favourites"),
                V.ListFragment(new[] { "a", "b" }, id => id, id => V.Memoized(() => V.Label(text: "all " + id), id), key: "all"),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert
            Assert.That(
                string.Join(",", Root.Query<Label>().ToList().Select(label => label.text)),
                Is.EqualTo("favourite a,all a,all b"));
        }

        [Test]
        public void Given_OneMemoKeyInTwoKeyedFragmentsWithDifferentDependencies_When_TheyMount_Then_TheFirstsSubtreeStillHoldsWhatItRented()
        {
            // Arrange
            ElementNode first = null;
            var tree = new VNode[]
            {
                V.Fragment(new VNode?[] { V.MemoizedWithKey("section", () => first = V.Label(text: "first"), "one") }, key: "first"),
                V.Fragment(new VNode?[] { V.MemoizedWithKey("section", () => V.Label(text: "second"), "two") }, key: "second"),
            };

            // Act
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);

            // Assert — the first label's text rides along, since what goes wrong is a bag going back to the pool
            // while its label is on screen.
            Assert.That(
                (((Label)Root.ElementAt(0)).text, first.Props != null && RentedFromThePool(first.Props)),
                Is.EqualTo(("first", true)));
        }

        private static int s_keyedChildRuns;
        private static StateUpdater<int> s_setKeyedChild;

        [Component(Compiler = false)]
        private static VNode KeyedMemoChild()
        {
            var (_, setChild) = Hooks.UseState(0);
            s_setKeyedChild = setChild;
            return V.MemoizedWithKey("section", () => { s_keyedChildRuns++; return V.Label(text: "section"); }, 1);
        }

        [Component(Compiler = false)]
        private static VNode KeyedFragmentAroundAComponent()
        {
            var (_, bumpHost) = Hooks.UseState(0);
            s_bumpHost = bumpHost;
            return V.Div(children: new VNode?[] { V.Fragment(new VNode?[] { V.Component(KeyedMemoChild) }, key: "group") });
        }

        // GREEN_ON_BASE(characterization): the base keyed a memo by its key alone, which every walk spells alike.
        // The child's own render starts its walk afresh and its parent's reaches it under the keyed Fragment, so
        // qualifying the key by the scope FiberKeying composes for elements is what reddens this.
        [Test]
        public void Given_AKeyedMemoInAComponentUnderAKeyedFragment_When_TheComponentReRendersAloneAndThenWithItsParent_Then_ItsFactoryDoesNotRunAgain()
        {
            // Arrange
            _mounted = V.Mount(new VisualElement(), V.Component(KeyedFragmentAroundAComponent, key: "host"));

            // Act
            s_setKeyedChild.Invoke(1);
            _mounted.FlushStateForTest();
            s_bumpHost.Invoke(1);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(s_keyedChildRuns, Is.EqualTo(1));
        }

        private static StateUpdater<bool> s_setShown;

        [Component]
        private static VNode VanishingSection()
            => V.Memoized(() => V.Label(text: "section"), 1);

        [Component]
        private static VNode VanishingHost()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setShown = setShown;
            return V.Div(name: "outside", children: new VNode?[]
            {
                shown ? V.Component(VanishingSection) : null,
            });
        }

        [Test]
        public void Given_AComponentThatDeclaredAMemo_When_ItUnmounts_Then_TheCacheHoldsNothingOfIt()
        {
            // Arrange — the memo's container is the host's element, which stays.
            _mounted = V.Mount(new VisualElement(), V.Component(VanishingHost, key: "host"));

            // Act
            s_setShown.Invoke(false);
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();

            // Assert
            Assert.That(Held(CacheOf(_mounted)), Is.EqualTo((0, 0, 0, 0, 0)));
        }

        private static readonly ComponentContext<string> Tint = ComponentContext<string>.Create("default");
        private static StateUpdater<int> s_bumpHost;
        private static readonly List<StateUpdater<int>> s_bumps = new();

        [Component(Compiler = false)]
        private static VNode TintConsumer()
        {
            var (count, bump) = Hooks.UseState(0);
            Hooks.UseRef<object>(() => { s_bumps.Add(bump); return s_bumps; });
            return V.Label(name: "seen", text: Hooks.UseContext(Tint) + "#" + count);
        }

        private static string SeenIn(VisualElement host, string container)
            => host.Q<VisualElement>(container)?.Q<Label>("seen")?.text ?? "<absent>";

        [Component(Compiler = false)]
        private static VNode InnerDivHost()
        {
            var (count, bumpHost) = Hooks.UseState(0);
            s_bumpHost = bumpHost;
            return V.Provider(Tint, "provided", new VNode[]
            {
                V.Label(name: "host", text: "h" + count),
                V.Memoized(
                    () => V.Div(name: "inner", children: new VNode?[] { V.Component(TintConsumer) }),
                    Array.Empty<object>()),
            });
        }

        // GREEN_ON_BASE(characterization): a consumer's own re-render already finds the Provider above its memo.
        // The base reaches the memo through a cache key both walkers compose. The consumer is registered under
        // the memo's inner element rather than the memo's own container, so a spine that looked the memo up by
        // the container it looks the consumer up under would miss it here.
        [Test]
        public void Given_AConsumerBehindAMemoWhoseInnerOpensItsContainer_When_TheConsumerReRendersOnItsOwnState_Then_ItReadsTheEnclosingProvider()
        {
            // Arrange
            var host = new VisualElement();
            _mounted = V.Mount(host, V.Component(InnerDivHost, key: "host"));

            // Act
            s_bumps[0].Invoke(1);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(SeenIn(host, "inner"), Is.EqualTo("provided#1"));
        }

        // GREEN_ON_BASE(characterization): the base finds a memo by a key that does not name its node.
        // So a node the host's re-render built afresh is found there as readily as the one that ran the Factory.
        [Test]
        public void Given_AMemoHitByAFreshNodeOnItsHostsReRender_When_AConsumerBehindItReRendersOnItsOwnState_Then_ItReadsTheEnclosingProvider()
        {
            // Arrange — the host builds a fresh MemoNode on every render, and its empty dependency array hits.
            var host = new VisualElement();
            _mounted = V.Mount(host, V.Component(InnerDivHost, key: "host"));
            s_bumpHost.Invoke(1);
            _mounted.FlushStateForTest();

            // Act
            s_bumps[0].Invoke(1);
            _mounted.FlushStateForTest();

            // Assert — the host's own label is read with it, since a host that never re-rendered leaves the
            // node the Factory ran for in its committed tree.
            Assert.That(
                host.Q<Label>("host")?.text + " " + SeenIn(host, "inner"),
                Is.EqualTo("h1 provided#1"));
        }

        [Component(Compiler = false)]
        private static VNode RecomputingHost()
        {
            var (count, bumpHost) = Hooks.UseState(0);
            s_bumpHost = bumpHost;
            return V.Provider(Tint, "provided", new VNode[]
            {
                V.Label(name: "host", text: "h" + count),
                V.Memoized(
                    () => V.Div(name: "inner", children: new VNode?[] { V.Component(TintConsumer) }),
                    count),
            });
        }

        // GREEN_ON_BASE(characterization): the base finds a memo by a key that does not name its node.
        // So the node a recompute was handed is found there as readily as the one before it.
        [Test]
        public void Given_AMemoRecomputedOnItsHostsReRender_When_AConsumerBehindItReRendersOnItsOwnState_Then_ItReadsTheEnclosingProvider()
        {
            // Arrange — the host's count is the memo's dependency, so its re-render recomputes the memo.
            var host = new VisualElement();
            _mounted = V.Mount(host, V.Component(RecomputingHost, key: "host"));
            s_bumpHost.Invoke(1);
            _mounted.FlushStateForTest();

            // Act
            s_bumps[0].Invoke(1);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(
                host.Q<Label>("host")?.text + " " + SeenIn(host, "inner"),
                Is.EqualTo("h1 provided#1"));
        }

        private static bool s_throwNow;

        [Component(Compiler = false)]
        private static VNode ThrowsWhenAsked()
        {
            if (s_throwNow) throw new InvalidOperationException("thrown in the host's render");
            return V.Label(text: "fine");
        }

        [Component(Compiler = false)]
        private static VNode HostWithABoundary()
        {
            var (count, bumpHost) = Hooks.UseState(0);
            s_bumpHost = bumpHost;
            return V.Provider(Tint, "provided", new VNode[]
            {
                V.Label(name: "host", text: "h" + count),
                V.Memoized(
                    () => V.Div(name: "inner", children: new VNode?[] { V.Component(TintConsumer) }),
                    Array.Empty<object>()),
                V.ErrorBoundary(_ => V.Label(name: "fallback", text: "fallback"), new VNode?[] { V.Component(ThrowsWhenAsked) }),
            });
        }

        // GREEN_ON_BASE(characterization): the base finds a memo by a key that does not name its node.
        // A render the boundary caught in is discarded after it handed the memo a node, and the base finds the
        // memo from the node the host kept as readily as from that one.
        [Test]
        public void Given_AMemoHitInAHostRenderABoundaryCaughtIn_When_AConsumerBehindItReRendersOnItsOwnState_Then_ItReadsTheEnclosingProvider()
        {
            // Arrange
            var host = new VisualElement();
            _mounted = V.Mount(host, V.Component(HostWithABoundary, key: "host"));
            s_throwNow = true;
            s_bumpHost.Invoke(1);
            _mounted.FlushStateForTest();

            // Act
            s_bumps[0].Invoke(1);
            _mounted.FlushStateForTest();

            // Assert — whether the boundary caught is read with it, since a render that commits keeps the node
            // it handed the memo.
            Assert.That(
                (host.Q<Label>("fallback") != null, SeenIn(host, "inner")),
                Is.EqualTo((true, "provided#1")));
        }

        [Component(Compiler = false)]
        private static VNode PortalProviderHost()
        {
            var (count, bumpHost) = Hooks.UseState(0);
            s_bumpHost = bumpHost;
            return V.Div(children: new VNode?[]
            {
                V.Label(name: "host", text: "h" + count),
                V.Portal(s_portalTarget, new VNode?[]
                {
                    V.Provider(Tint, "provided", new VNode[]
                    {
                        V.Memoized(
                            () => V.Div(name: "inner", children: new VNode?[] { V.Component(TintConsumer) }),
                            Array.Empty<object>()),
                    }),
                }),
            });
        }

        // GREEN_ON_BASE(characterization): the base finds a memo by a key that does not name its node.
        // The Portal's children are walked as they were when the Portal first mounted them, and the base finds
        // the memo from that render's node as readily as from the ones the host's later renders handed it.
        [Test]
        public void Given_APortalHeldMemoUnderAProvider_When_ItsHostReRendersTwiceAndThenAConsumerBehindItReRendersOnItsOwnState_Then_ItReadsTheProvider()
        {
            // Arrange
            s_portalTarget = new VisualElement();
            var host = new VisualElement();
            _mounted = V.Mount(host, V.Component(PortalProviderHost, key: "host"));
            for (var count = 1; count <= 2; count++)
            {
                s_bumpHost.Invoke(count);
                _mounted.FlushStateForTest();
            }

            // Act
            s_bumps[0].Invoke(1);
            _mounted.FlushStateForTest();

            // Assert — the host's own label is read with it, since a host that did not re-render twice leaves the
            // node the Portal mounted among the last two the memo was handed.
            Assert.That(
                host.Q<Label>("host")?.text + " " + SeenIn(s_portalTarget, "inner"),
                Is.EqualTo("h2 provided#1"));
        }

        // The two memos sit at the first child of their own containers under one component, so they share every
        // member of their key but the container; only the first container is under a Provider.
        [Component(Compiler = false)]
        private static VNode TwoContainersOneUnderAProvider()
        {
            var (count, bumpHost) = Hooks.UseState(0);
            s_bumpHost = bumpHost;
            return V.Div(children: new VNode?[]
            {
                V.Label(name: "host", text: "h" + count),
                V.Provider(Tint, "first", new VNode[]
                {
                    V.Div(name: "first", children: new VNode?[]
                    {
                        V.Memoized(() => V.Div(children: new VNode?[] { V.Component(TintConsumer) }), Array.Empty<object>()),
                    }),
                }),
                V.Div(name: "second", children: new VNode?[]
                {
                    V.Memoized(() => V.Div(children: new VNode?[] { V.Component(TintConsumer) }), Array.Empty<object>()),
                }),
            });
        }

        [Test]
        public void Given_TwoContainersHoldingAMemoAtTheirFirstChild_When_TheHostReRendersAndTheSecondConsumerThenReRendersOnItsOwnState_Then_ItReadsNoProviderOfTheFirst()
        {
            // Arrange — the host's re-render builds fresh MemoNodes, and their empty dependency arrays hit.
            var host = new VisualElement();
            _mounted = V.Mount(host, V.Component(TwoContainersOneUnderAProvider, key: "host"));
            s_bumpHost.Invoke(1);
            _mounted.FlushStateForTest();

            // Act — consumers record their setters in mount order, so the second is the one outside the Provider.
            s_bumps[1].Invoke(1);
            _mounted.FlushStateForTest();

            // Assert — the host's own label is read with it, since a host that never re-rendered leaves each memo
            // naming the node its committed tree holds.
            Assert.That(
                host.Q<Label>("host")?.text + " " + SeenIn(host, "second"),
                Is.EqualTo("h1 default#1"));
        }

        // As TwoContainersOneUnderAProvider, with a Provider of its own around the second container and a boundary
        // between the two, which the render it catches in does not get past.
        [Component(Compiler = false)]
        private static VNode TwoContainersAroundABoundary()
        {
            var (count, bumpHost) = Hooks.UseState(0);
            s_bumpHost = bumpHost;
            return V.Div(children: new VNode?[]
            {
                V.Provider(Tint, "first", new VNode[]
                {
                    V.Div(name: "first", children: new VNode?[]
                    {
                        V.Memoized(() => V.Div(children: new VNode?[] { V.Component(TintConsumer) }), Array.Empty<object>()),
                    }),
                }),
                V.ErrorBoundary(_ => V.Label(name: "fallback", text: "fallback"), new VNode?[] { V.Component(ThrowsWhenAsked) }),
                V.Provider(Tint, "second", new VNode[]
                {
                    V.Div(name: "second", children: new VNode?[]
                    {
                        V.Memoized(() => V.Div(children: new VNode?[] { V.Component(TintConsumer) }), Array.Empty<object>()),
                    }),
                }),
            });
        }

        [Test]
        public void Given_TwoContainersHoldingAMemoAtTheirFirstChildAroundABoundary_When_ItCatchesInTheHostsRenderAndTheSecondConsumerThenReRenders_Then_ItReadsItsOwnProvider()
        {
            // Arrange
            var host = new VisualElement();
            _mounted = V.Mount(host, V.Component(TwoContainersAroundABoundary, key: "host"));
            s_throwNow = true;
            s_bumpHost.Invoke(1);
            _mounted.FlushStateForTest();

            // Act
            s_bumps[1].Invoke(1);
            _mounted.FlushStateForTest();

            // Assert — whether the boundary caught is read with it, since a render that commits leaves each memo
            // naming the node its committed tree holds.
            Assert.That(
                (host.Q<Label>("fallback") != null, SeenIn(host, "second")),
                Is.EqualTo((true, "second#1")));
        }

        [Component(Compiler = false)]
        private static VNode MemoBesideTheConsumer()
            => V.Div(children: new VNode?[]
            {
                V.Provider(Tint, "beside", new VNode[]
                {
                    V.Memoized(() => V.Label(text: "memo"), Array.Empty<object>()),
                }),
                V.Provider(Tint, "provided", new VNode[]
                {
                    V.Div(name: "own", children: new VNode?[] { V.Component(TintConsumer) }),
                }),
            });

        // GREEN_ON_BASE(characterization): the base's spine already walked past a memo holding no consumer.
        // It found nothing in that memo's inner and went on to the Provider the consumer sits under.
        [Test]
        public void Given_AMemoAheadOfAConsumerItDoesNotEnclose_When_TheConsumerReRendersOnItsOwnState_Then_ItReadsItsOwnProvider()
        {
            // Arrange
            var host = new VisualElement();
            _mounted = V.Mount(host, V.Component(MemoBesideTheConsumer, key: "host"));

            // Act
            s_bumps[0].Invoke(1);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(SeenIn(host, "own"), Is.EqualTo("provided#1"));
        }

        private static readonly MemoNode s_writtenThrice = V.Memoized(
            () => V.Provider(Tint, "provided", new VNode[] { V.Component(TintConsumer) }),
            Array.Empty<object>());

        [Component(Compiler = false)]
        private static VNode ThreeContainersOfOneNode()
            => V.Div(children: new VNode?[]
            {
                V.Div(name: "first", children: new VNode?[] { s_writtenThrice }),
                V.Div(name: "second", children: new VNode?[] { s_writtenThrice }),
                V.Div(name: "third", children: new VNode?[] { s_writtenThrice }),
            });

        // GREEN_ON_BASE(characterization): the base filed all three occurrences under one key.
        // It rendered the first occurrence's inner into every container, so the three consumers share its nodes.
        [Test]
        public void Given_OneMemoNodeWrittenIntoThreeContainers_When_TheMiddleConsumerReRendersOnItsOwnState_Then_ItReadsTheProviderTheNodeEncloses()
        {
            // Arrange
            var host = new VisualElement();
            _mounted = V.Mount(host, V.Component(ThreeContainersOfOneNode, key: "host"));

            // Act — consumers record their setters in mount order, so the second is the middle one.
            s_bumps[1].Invoke(1);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(
                SeenIn(host, "first") + " " + SeenIn(host, "second") + " " + SeenIn(host, "third"),
                Is.EqualTo("provided#0 provided#1 provided#0"));
        }

        private static StateUpdater<bool> s_setShowFirstOwner;

        private static readonly MemoNode s_sharedByTwoOwners = V.Memoized(
            () => V.Provider(Tint, "provided", new VNode[] { V.Component(TintConsumer) }),
            Array.Empty<object>());

        // Keyed, so each Div is matched with itself when the first owner leaves rather than by position.
        [Component(Compiler = false)]
        private static VNode FirstOwner()
            => V.Div(key: "first", name: "first", children: new VNode?[] { s_sharedByTwoOwners });

        [Component(Compiler = false)]
        private static VNode SecondOwner()
            => V.Div(key: "second", name: "second", children: new VNode?[] { s_sharedByTwoOwners });

        [Component(Compiler = false)]
        private static VNode TwoOwnersOfOneNode()
        {
            var (showFirst, setShowFirst) = Hooks.UseState(true);
            s_setShowFirstOwner = setShowFirst;
            return V.Div(children: new VNode?[]
            {
                showFirst ? V.Component(FirstOwner, key: "first") : null,
                V.Component(SecondOwner, key: "second"),
            });
        }

        // GREEN_ON_BASE(characterization): the base kept one entry for both owners' occurrences.
        // Nothing on the base drops an entry when its component unmounts, so the second owner's consumer is
        // found after the first owner leaves.
        [Test]
        public void Given_OneMemoNodeDeclaredByTwoComponents_When_TheFirstUnmounts_Then_TheSecondsConsumerStillReadsTheProviderTheNodeEncloses()
        {
            // Arrange
            var host = new VisualElement();
            _mounted = V.Mount(host, V.Component(TwoOwnersOfOneNode, key: "host"));
            s_setShowFirstOwner.Invoke(false);
            _mounted.FlushStateForTest();
            _mounted.FlushEffectsForTest();

            // Act
            s_bumps[1].Invoke(1);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(SeenIn(host, "first") + " " + SeenIn(host, "second"), Is.EqualTo("<absent> provided#1"));
        }

        private static int s_bodyMemoRuns;
        private static StateUpdater<int> s_setBody;

        [Component(Compiler = false)]
        private static VNode MemoAtBodyRoot()
        {
            var (_, setBody) = Hooks.UseState(0);
            s_setBody = setBody;
            return V.Memoized(() => { s_bodyMemoRuns++; return V.Label(text: "body"); }, 1);
        }

        // GREEN_ON_BASE(characterization): the base already kept this memo's entry across the component's re-render.
        // Keying it by `Path` rather than `SlotPath` is what reddens this.
        [Test]
        public void Given_AMemoAtAComponentBodysRoot_When_TheComponentReRendersOnItsOwnState_Then_ItsFactoryDoesNotRunAgain()
        {
            // Arrange
            _mounted = V.Mount(new VisualElement(), V.Component(MemoAtBodyRoot, key: "host"));

            // Act
            s_setBody.Invoke(1);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(s_bodyMemoRuns, Is.EqualTo(1));
        }

        private static VisualElement s_portalTarget;

        [Component(Compiler = false)]
        private static VNode TwoPortalsIntoOneTarget()
            => V.Div(children: new VNode?[]
            {
                V.Portal(s_portalTarget, new VNode?[] { V.Memoized(() => V.Label(text: "first"), 1) }),
                V.Portal(s_portalTarget, new VNode?[] { V.Memoized(() => V.Label(text: "second"), 1) }),
            });

        [Test]
        public void Given_TwoPortalsOfOneComponentIntoOneTarget_When_TheyMount_Then_EachShowsItsOwnMemo()
        {
            // Arrange
            s_portalTarget = new VisualElement();

            // Act
            _mounted = V.Mount(new VisualElement(), V.Component(TwoPortalsIntoOneTarget, key: "host"));

            // Assert
            Assert.That(
                string.Join(",", s_portalTarget.Query<Label>().ToList().Select(label => label.text)),
                Is.EqualTo("first,second"));
        }

        private static StateUpdater<bool> s_setPortedDivShown;

        [Component(Compiler = false)]
        private static VNode PortalHoldingADiv()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setPortedDivShown = setShown;
            return V.Div(children: new VNode?[]
            {
                V.Portal(s_portalTarget, new VNode?[]
                {
                    shown ? V.Div(children: new VNode?[] { V.Memoized(() => V.Label(text: "ported"), 1) }) : null,
                }),
            });
        }

        [Test]
        public void Given_ADivInAPortalsChildrenHoldingAMemo_When_TheDivLeavesAndThePortalStays_Then_TheCacheHoldsNothingOfIt()
        {
            // Arrange — the memo sits under the Portal and in the Div, so its entry names both.
            s_portalTarget = new VisualElement();
            _mounted = V.Mount(new VisualElement(), V.Component(PortalHoldingADiv, key: "host"));

            // Act
            s_setPortedDivShown.Invoke(false);
            _mounted.FlushStateForTest();

            // Assert
            Assert.That(Held(CacheOf(_mounted)), Is.EqualTo((0, 0, 0, 0, 0)));
        }

        private static StateUpdater<int> s_setTab;

        // The middle tab holds no Button, so the first tab's goes back to the pool before the last tab rents one.
        [Component(Compiler = false)]
        private static VNode Tabs()
        {
            var (tab, setTab) = Hooks.UseState(0);
            s_setTab = setTab;
            return V.Div(children: new VNode?[]
            {
                tab == 1
                    ? V.Label(text: "no button")
                    : V.Button(children: new VNode?[]
                    {
                        V.Memoized(() => V.Label(name: "content", text: "tab " + tab), 1),
                    }),
            });
        }

        [Test]
        public void Given_APooledButtonHandedToALaterRender_When_ItHoldsAMemoWhereTheFirstHeldOne_Then_ItShowsThatRendersSubtree()
        {
            // Arrange
            var host = new VisualElement();
            _mounted = V.Mount(host, V.Component(Tabs, key: "host"));
            var firstButton = host.Q<Button>();
            s_setTab.Invoke(1);
            _mounted.FlushStateForTest();

            // Act
            s_setTab.Invoke(2);
            _mounted.FlushStateForTest();

            // Assert — the rented Button is compared too, since a fresh one has no entry to reach.
            var laterButton = host.Q<Button>();
            Assert.That(
                (ReferenceEquals(firstButton, laterButton), laterButton?.Q<Label>("content")?.text ?? "<absent>"),
                Is.EqualTo((true, "tab 2")));
        }

        private static StateUpdater<int> s_setGeneration;

        [Component(Compiler = false)]
        private static VNode ChurningRows()
        {
            var (generation, setGeneration) = Hooks.UseState(0);
            s_setGeneration = setGeneration;
            var rows = new VNode?[3];
            for (var i = 0; i < rows.Length; i++)
            {
                var key = generation + "-" + i;
                rows[i] = V.Div(key: key, children: new VNode?[] { V.Memoized(() => V.Label(text: key), key) });
            }
            return V.Div(children: rows);
        }

        // GREEN_ON_BASE(characterization): the base never held more than one entry for these rows.
        // It filed every row's memo under one key. Leaving the container's eviction out is what reddens this.
        [Test]
        public void Given_ThreeKeyedRowsEachHoldingAMemo_When_TheyAreReplacedTenTimes_Then_TheCacheHoldsOnlyWhatTheLiveRowsName()
        {
            // Arrange
            _mounted = V.Mount(new VisualElement(), V.Component(ChurningRows, key: "host"));

            // Act
            for (var generation = 1; generation <= 10; generation++)
            {
                s_setGeneration.Invoke(generation);
                _mounted.FlushStateForTest();
            }

            // Assert — one entry per live row, filed under its position, and under its component and its row.
            var held = Held(CacheOf(_mounted));
            Assert.That(
                (held.Entries <= 3, held.Positions <= 3, held.PositionFilings <= 3, held.Referents <= 4, held.ReferentFilings <= 6),
                Is.EqualTo((true, true, true, true, true)));
        }

        private static int s_retargetRuns;

        private static VNode[] RetargetedPortal()
            => new VNode[]
            {
                V.Portal("memo-retarget", children: new VNode?[]
                {
                    V.Memoized(() => { s_retargetRuns++; return V.Label(text: "retargeted"); }, 1),
                }),
            };

        [Test]
        public void Given_ARegistryPortalRetargetedAwayAndBack_When_ItsChildrenMountEachTime_Then_ItsMemoComputesEachTime()
        {
            // Arrange
            var first = new VisualElement();
            var second = new VisualElement();
            FiberPortalRegistry.Register("memo-retarget", first);
            var mounted = RetargetedPortal();
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), mounted);
            LogAssert.Expect(LogType.Warning, "[FiberPortalRegistry] Id \"memo-retarget\" is already registered. Overwriting.");
            FiberPortalRegistry.Register("memo-retarget", second);
            var moved = RetargetedPortal();
            Reconciler.Reconcile(Root, mounted, moved);

            // Act
            LogAssert.Expect(LogType.Warning, "[FiberPortalRegistry] Id \"memo-retarget\" is already registered. Overwriting.");
            FiberPortalRegistry.Register("memo-retarget", first);
            Reconciler.Reconcile(Root, moved, RetargetedPortal());

            // Assert — where the children ended up rides along, since a Portal that never moved computes once.
            Assert.That((s_retargetRuns, first.childCount, second.childCount), Is.EqualTo((3, 1, 0)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Given_AMemoRecomputeReusingItsCachedLeaf_When_ThePreviousResultRetires_Then_TheCurrentLeafKeepsItsProps(bool freshParent)
        {
            // Arrange
            ElementNode leaf = null;
            var builds = 0;
            VNode Build()
            {
                builds++;
                leaf ??= V.Label(text: "retained");
                return freshParent ? V.Div(children: new VNode[] { leaf }) : leaf;
            }
            var oldTree = new VNode[] { V.Memoized(Build, 1) };
            Reconciler.Reconcile(Root, Array.Empty<VNode>(), oldTree);
            var before = TextIn(Root);

            // Act
            Reconciler.Reconcile(Root, oldTree, new VNode[] { V.Memoized(Build, 2) });

            // Assert
            Assert.That((before, TextIn(Root), leaf.Props.Text, builds),
                Is.EqualTo(("retained", "retained", "retained", 2)));
        }

        private static bool RentedFromThePool(FiberElementProps props)
            => ((HashSet<FiberElementProps>)typeof(VNodePool)
                .GetField("s_ownedProps", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null)).Contains(props);

        // GREEN_ON_BASE(characterization): the base's disposal already returned every cached inner to the pool.
        // Leaving that return out of `DisposeAndReturnCachedTrees` is what reddens this.
        [Test]
        public void Given_AReconcilerHoldingACachedInner_When_ItIsDisposed_Then_ThePropsTheInnerRentedGoBackToThePool()
        {
            // Arrange
            var reconciler = new Reconciler();
            ElementNode cached = null;
            reconciler.Reconcile(new VisualElement(), Array.Empty<VNode>(), new VNode[]
            {
                V.Memoized(() => cached = V.Label(text: "cached"), 1),
            });
            var props = cached.Props;

            // Act
            reconciler.Dispose();

            // Assert — the bag is compared to null too, since a label that rented none has nothing to return.
            Assert.That((props != null, props != null && RentedFromThePool(props)), Is.EqualTo((true, false)));
        }

        // The props bags, single-event arrays and node arrays the pool counts as rented out.
        private static (int Props, int EventArrays, int NodeArrays) Rented()
        {
            int Count(string field)
            {
                var set = typeof(VNodePool).GetField(field, BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                return (int)set.GetType().GetProperty("Count").GetValue(set);
            }
            return (Count("s_ownedProps"), Count("s_ownedSingleEventArrays"), Count("s_ownedNodeArrays"));
        }

        // The memo's subtree rents one of each: V.List a node array, and the Button a props bag and an event array.
        [Component(Compiler = false)]
        private static VNode SectionRentingEachPart()
            => V.Memoized(
                () => V.Div(children: V.List(new[] { "open" }, id => id, id => V.Button(text: id, onClick: () => { }))),
                1);

        [Component(Compiler = false)]
        private static VNode HostOfTheRentingSection()
            => V.Div(children: new VNode?[] { V.Component(SectionRentingEachPart) });

        // GREEN_ON_BASE(characterization): the base kept every entry until its reconciler was disposed.
        // Its disposal returned every cached inner, so nothing a memo rented stayed with the pool.
        [Test]
        public void Given_AMountHoldingAComponentWithAMemo_When_ItIsDisposed_Then_ThePoolCountsNothingTheMemoRentedAsRentedOut()
        {
            // Arrange — the component's unmount lets its entry go before the memo cache itself is disposed.
            var before = Rented();
            var mounted = V.Mount(new VisualElement(), V.Component(HostOfTheRentingSection, key: "host"));

            // Act
            mounted.Dispose();

            // Assert
            var after = Rented();
            Assert.That(
                (after.Props - before.Props, after.EventArrays - before.EventArrays, after.NodeArrays - before.NodeArrays),
                Is.EqualTo((0, 0, 0)));
        }

        private static ElementNode s_content;
        private static ElementNode s_fallback;

        // The memo's subtree holds a Suspense, whose fallback is built beside its children though only one is shown.
        [Component(Compiler = false)]
        private static VNode SectionHoldingASuspense()
            => V.Memoized(
                () => V.Div(children: new VNode?[]
                {
                    V.Suspense(s_fallback = V.Label(text: "loading"), new VNode?[] { s_content = V.Label(text: "content") }),
                }),
                1);

        [Component(Compiler = false)]
        private static VNode HostOfTheSuspenseSection()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setShown = setShown;
            return V.Div(children: new VNode?[] { shown ? V.Component(SectionHoldingASuspense) : null });
        }

        private static Stack<FiberElementProps> PooledPropsBags()
            => (Stack<FiberElementProps>)typeof(VNodePool)
                .GetField("s_propsPool", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);

        // GREEN_ON_BASE(characterization): the base let no entry go before its reconciler's disposal.
        // So nothing it cached reached the pool while the component unmounted; returning the subtree an eviction
        // lets go of, rather than releasing it, is what reddens this.
        [Test]
        public void Given_AComponentWhoseMemoHeldASuspense_When_ItUnmounts_Then_ThePoolHandsOutNothingThatSubtreeHeld()
        {
            // Arrange — the pool starts empty, so a bag the unmount gave back to it is among what it hands out next.
            PooledPropsBags().Clear();
            _mounted = V.Mount(new VisualElement(), V.Component(HostOfTheSuspenseSection, key: "host"));
            var held = new[] { s_content.Props, s_fallback.Props };
            s_setShown.Invoke(false);
            _mounted.FlushStateForTest();

            // Act
            var handedOut = new List<FiberElementProps>();
            for (var pooled = PooledPropsBags().Count; pooled > 0; pooled--)
            {
                handedOut.Add(VNodePool.RentProps());
            }
            handedOut.ForEach(VNodePool.ReturnProps);

            // Assert — the bags are compared to null too, since a label that rented none has nothing to hand out.
            Assert.That(
                (held.All(props => props != null), held.Any(handedOut.Contains)),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AReconcilerHoldingMemoEntries_When_ItIsDisposed_Then_TheCacheHoldsNothing()
        {
            // Arrange
            var reconciler = new Reconciler();
            reconciler.Reconcile(new VisualElement(), Array.Empty<VNode>(), new VNode[]
            {
                V.Div(children: new VNode?[] { V.Memoized(() => V.Label(text: "held"), 1) }),
            });
            var cache = reconciler.Context.FiberMemoCache;

            // Act
            reconciler.Dispose();

            // Assert
            Assert.That(Held(cache), Is.EqualTo((0, 0, 0, 0, 0)));
        }
    }
}

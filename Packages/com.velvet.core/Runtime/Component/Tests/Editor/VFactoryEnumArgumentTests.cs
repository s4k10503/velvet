using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what <c>V.Portal(focusOrder:)</c>, <c>V.WorldSpace(focusOrder:)</c>,
    /// <c>V.Particles(playOn:)</c>, <c>V.Draggable(movement:)</c>, <c>V.AnimatePresence(mode:)</c> and
    /// <c>V.Route(loaderMode:)</c> do with their enum argument:
    /// <list type="bullet">
    /// <item>A value naming no member of the enum is refused synchronously with an
    /// <see cref="ArgumentOutOfRangeException"/> that names the parameter, carries the value, and names
    /// the API and the enum in its message.</item>
    /// <item>Every member still reaches what the factory builds.</item>
    /// </list>
    /// <see cref="VPortalFactoryTests"/> specifies the same of <c>V.Portal(layer:)</c>, whose check
    /// these follow.
    /// </summary>
    [TestFixture]
    internal sealed class VFactoryEnumArgumentTests
    {
        private const PanelFocusOrder UnnamedFocusOrder = (PanelFocusOrder)99;
        private const PlayTrigger UnnamedPlayTrigger = (PlayTrigger)99;
        private const DragMovement UnnamedMovement = (DragMovement)99;
        private const AnimatePresenceMode UnnamedMode = (AnimatePresenceMode)99;
        private const LoaderMode UnnamedLoaderMode = (LoaderMode)99;

        [Component]
        private static VNode RouteElementRender() => V.Div();

        #region V.Portal(focusOrder:)

        [Test]
        public void Given_AFocusOrderNamingNoMember_When_VPortalIsCalled_Then_ItThrowsArgumentOutOfRange()
        {
            // Act + Assert
            Assert.That(() => V.Portal(UILayer.Overlay, focusOrder: UnnamedFocusOrder),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Given_AFocusOrderNamingNoMember_When_VPortalThrows_Then_TheParamNameIsFocusOrder()
        {
            // Act
            var ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => V.Portal(UILayer.Overlay, focusOrder: UnnamedFocusOrder));

            // Assert
            Assert.That(ex.ParamName, Is.EqualTo("focusOrder"));
        }

        [Test]
        public void Given_AFocusOrderNamingNoMember_When_VPortalThrows_Then_TheMessageNamesTheApiAndTheEnum()
        {
            // Act
            var ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => V.Portal(UILayer.Overlay, focusOrder: UnnamedFocusOrder));

            // Assert
            Assert.That((ex.Message.Contains("V.Portal"), ex.Message.Contains("PanelFocusOrder")),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AFocusOrderNamingNoMember_When_VPortalThrows_Then_TheActualValueIsTheCast()
        {
            // Act
            var ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => V.Portal(UILayer.Overlay, focusOrder: UnnamedFocusOrder));

            // Assert
            Assert.That(ex.ActualValue, Is.EqualTo(UnnamedFocusOrder));
        }

        // GREEN_ON_BASE(characterization): every PanelFocusOrder member reaches the portal node on the base.
        // What it answers for is the refusal this branch adds: narrow that check to one member
        // and this case goes red.
        [Test]
        public void Given_EveryMemberOfPanelFocusOrder_When_VPortalIsCalled_Then_EachBuildsANodeCarryingIt()
        {
            // Arrange
            var members = (PanelFocusOrder[])Enum.GetValues(typeof(PanelFocusOrder));

            // Act
            var carried = new List<string>();
            foreach (var member in members)
            {
                carried.Add(V.Portal(UILayer.Overlay, focusOrder: member).FocusOrder.ToString());
            }

            // Assert
            Assert.That(string.Join(",", carried), Is.EqualTo(string.Join(",", members.Select(m => m.ToString()))));
        }

        #endregion

        #region V.WorldSpace(focusOrder:)

        [Test]
        public void Given_AFocusOrderNamingNoMember_When_VWorldSpaceIsCalled_Then_ItThrowsArgumentOutOfRange()
        {
            // Act + Assert
            Assert.That(() => V.WorldSpace(Vector3.zero, focusOrder: UnnamedFocusOrder),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Given_AFocusOrderNamingNoMember_When_VWorldSpaceThrows_Then_TheParamNameIsFocusOrder()
        {
            // Act
            var ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => V.WorldSpace(Vector3.zero, focusOrder: UnnamedFocusOrder));

            // Assert
            Assert.That(ex.ParamName, Is.EqualTo("focusOrder"));
        }

        [Test]
        public void Given_AFocusOrderNamingNoMember_When_VWorldSpaceThrows_Then_TheMessageNamesTheApiAndTheEnum()
        {
            // Act
            var ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => V.WorldSpace(Vector3.zero, focusOrder: UnnamedFocusOrder));

            // Assert
            Assert.That((ex.Message.Contains("V.WorldSpace"), ex.Message.Contains("PanelFocusOrder")),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AFocusOrderNamingNoMember_When_VWorldSpaceThrows_Then_TheActualValueIsTheCast()
        {
            // Act
            var ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => V.WorldSpace(Vector3.zero, focusOrder: UnnamedFocusOrder));

            // Assert
            Assert.That(ex.ActualValue, Is.EqualTo(UnnamedFocusOrder));
        }

        // GREEN_ON_BASE(characterization): every PanelFocusOrder member reaches the world-space node on the base.
        // What it answers for is the refusal this branch adds: narrow that check to one member
        // and this case goes red.
        [Test]
        public void Given_EveryMemberOfPanelFocusOrder_When_VWorldSpaceIsCalled_Then_EachBuildsANodeCarryingIt()
        {
            // Arrange
            var members = (PanelFocusOrder[])Enum.GetValues(typeof(PanelFocusOrder));

            // Act
            var carried = new List<string>();
            foreach (var member in members)
            {
                carried.Add(V.WorldSpace(Vector3.zero, focusOrder: member).FocusOrder.ToString());
            }

            // Assert
            Assert.That(string.Join(",", carried), Is.EqualTo(string.Join(",", members.Select(m => m.ToString()))));
        }

        #endregion

        #region V.Particles(playOn:)

        [Test]
        public void Given_APlayOnNamingNoMember_When_VParticlesIsCalled_Then_ItThrowsArgumentOutOfRange()
        {
            // Act + Assert
            Assert.That(() => V.Particles(effect: null, playOn: UnnamedPlayTrigger),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Given_APlayOnNamingNoMember_When_VParticlesThrows_Then_TheParamNameIsPlayOn()
        {
            // Act
            var ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => V.Particles(effect: null, playOn: UnnamedPlayTrigger));

            // Assert
            Assert.That(ex.ParamName, Is.EqualTo("playOn"));
        }

        [Test]
        public void Given_APlayOnNamingNoMember_When_VParticlesThrows_Then_TheMessageNamesTheApiAndTheEnum()
        {
            // Act
            var ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => V.Particles(effect: null, playOn: UnnamedPlayTrigger));

            // Assert
            Assert.That((ex.Message.Contains("V.Particles"), ex.Message.Contains("PlayTrigger")),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_APlayOnNamingNoMember_When_VParticlesThrows_Then_TheActualValueIsTheCast()
        {
            // Act
            var ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => V.Particles(effect: null, playOn: UnnamedPlayTrigger));

            // Assert
            Assert.That(ex.ActualValue, Is.EqualTo(UnnamedPlayTrigger));
        }

        // GREEN_ON_BASE(characterization): every PlayTrigger member reaches the particles settings on the base.
        // What it answers for is the refusal this branch adds: narrow that check to one member
        // and this case goes red.
        [Test]
        public void Given_EveryMemberOfPlayTrigger_When_VParticlesIsCalled_Then_EachBuildsANodeCarryingIt()
        {
            // Arrange
            var members = (PlayTrigger[])Enum.GetValues(typeof(PlayTrigger));

            // Act
            var carried = new List<string>();
            foreach (var member in members)
            {
                carried.Add(V.Particles(effect: null, playOn: member).Props?.Particles?.PlayOn.ToString() ?? "null");
            }

            // Assert
            Assert.That(string.Join(",", carried), Is.EqualTo(string.Join(",", members.Select(m => m.ToString()))));
        }

        #endregion

        #region V.Draggable(movement:)

        [Test]
        public void Given_AMovementNamingNoMember_When_VDraggableIsCalled_Then_ItThrowsArgumentOutOfRange()
        {
            // Act + Assert
            Assert.That(() => V.Draggable("source", movement: UnnamedMovement),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Given_AMovementNamingNoMember_When_VDraggableThrows_Then_TheParamNameIsMovement()
        {
            // Act
            var ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => V.Draggable("source", movement: UnnamedMovement));

            // Assert
            Assert.That(ex.ParamName, Is.EqualTo("movement"));
        }

        [Test]
        public void Given_AMovementNamingNoMember_When_VDraggableThrows_Then_TheMessageNamesTheApiAndTheEnum()
        {
            // Act
            var ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => V.Draggable("source", movement: UnnamedMovement));

            // Assert
            Assert.That((ex.Message.Contains("V.Draggable"), ex.Message.Contains("DragMovement")),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AMovementNamingNoMember_When_VDraggableThrows_Then_TheActualValueIsTheCast()
        {
            // Act
            var ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => V.Draggable("source", movement: UnnamedMovement));

            // Assert
            Assert.That(ex.ActualValue, Is.EqualTo(UnnamedMovement));
        }

        // GREEN_ON_BASE(characterization): every DragMovement member reaches the draggable settings on the base.
        // What it answers for is the refusal this branch adds: narrow that check to one member
        // and this case goes red.
        [Test]
        public void Given_EveryMemberOfDragMovement_When_VDraggableIsCalled_Then_EachBuildsANodeCarryingIt()
        {
            // Arrange
            var members = (DragMovement[])Enum.GetValues(typeof(DragMovement));

            // Act
            var carried = new List<string>();
            foreach (var member in members)
            {
                carried.Add(V.Draggable("source", movement: member).Props?.Draggable?.Movement.ToString() ?? "null");
            }

            // Assert
            Assert.That(string.Join(",", carried), Is.EqualTo(string.Join(",", members.Select(m => m.ToString()))));
        }

        #endregion

        #region V.AnimatePresence(mode:)

        [Test]
        public void Given_AModeNamingNoMember_When_VAnimatePresenceIsCalled_Then_ItThrowsArgumentOutOfRange()
        {
            // Act + Assert
            Assert.That(() => V.AnimatePresence(mode: UnnamedMode),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Given_AModeNamingNoMember_When_VAnimatePresenceThrows_Then_TheParamNameIsMode()
        {
            // Act
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => V.AnimatePresence(mode: UnnamedMode));

            // Assert
            Assert.That(ex.ParamName, Is.EqualTo("mode"));
        }

        [Test]
        public void Given_AModeNamingNoMember_When_VAnimatePresenceThrows_Then_TheMessageNamesTheApiAndTheEnum()
        {
            // Act
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => V.AnimatePresence(mode: UnnamedMode));

            // Assert
            Assert.That((ex.Message.Contains("V.AnimatePresence"), ex.Message.Contains("AnimatePresenceMode")),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_AModeNamingNoMember_When_VAnimatePresenceThrows_Then_TheActualValueIsTheCast()
        {
            // Act
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => V.AnimatePresence(mode: UnnamedMode));

            // Assert
            Assert.That(ex.ActualValue, Is.EqualTo(UnnamedMode));
        }

        // GREEN_ON_BASE(characterization): every AnimatePresenceMode member reaches the presence node on the base.
        // What it answers for is the refusal this branch adds: narrow that check to one member
        // and this case goes red.
        [Test]
        public void Given_EveryMemberOfAnimatePresenceMode_When_VAnimatePresenceIsCalled_Then_EachBuildsANodeCarryingIt()
        {
            // Arrange
            var members = (AnimatePresenceMode[])Enum.GetValues(typeof(AnimatePresenceMode));

            // Act
            var carried = new List<string>();
            foreach (var member in members)
            {
                carried.Add(V.AnimatePresence(mode: member).Mode.ToString());
            }

            // Assert
            Assert.That(string.Join(",", carried), Is.EqualTo(string.Join(",", members.Select(m => m.ToString()))));
        }

        #endregion

        #region V.Route(loaderMode:)

        [Test]
        public void Given_ALoaderModeNamingNoMember_When_VRouteIsCalled_Then_ItThrowsArgumentOutOfRange()
        {
            // Act + Assert
            Assert.That(
                () => V.Route("reports", element: V.Component(RouteElementRender), loaderMode: UnnamedLoaderMode),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Given_ALoaderModeNamingNoMember_When_VRouteThrows_Then_TheParamNameIsLoaderMode()
        {
            // Act
            var ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => V.Route("reports", element: V.Component(RouteElementRender), loaderMode: UnnamedLoaderMode));

            // Assert
            Assert.That(ex.ParamName, Is.EqualTo("loaderMode"));
        }

        [Test]
        public void Given_ALoaderModeNamingNoMember_When_VRouteThrows_Then_TheMessageNamesTheApiAndTheEnum()
        {
            // Act
            var ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => V.Route("reports", element: V.Component(RouteElementRender), loaderMode: UnnamedLoaderMode));

            // Assert
            Assert.That((ex.Message.Contains("V.Route"), ex.Message.Contains("LoaderMode")),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ALoaderModeNamingNoMember_When_VRouteThrows_Then_TheActualValueIsTheCast()
        {
            // Act
            var ex = Assert.Throws<ArgumentOutOfRangeException>(
                () => V.Route("reports", element: V.Component(RouteElementRender), loaderMode: UnnamedLoaderMode));

            // Assert
            Assert.That(ex.ActualValue, Is.EqualTo(UnnamedLoaderMode));
        }
        // GREEN_ON_BASE(characterization): every LoaderMode member reaches the route definition on the base.
        // What it answers for is the refusal this branch adds: narrow that check to one member
        // and this case goes red.
        [Test]
        public void Given_EveryMemberOfLoaderMode_When_VRouteIsCalled_Then_EachBuildsADefinitionCarryingIt()
        {
            // Arrange
            var members = (LoaderMode[])Enum.GetValues(typeof(LoaderMode));

            // Act
            var carried = new List<string>();
            foreach (var member in members)
            {
                carried.Add(V.Route("reports", element: V.Component(RouteElementRender), loaderMode: member)
                    .LoaderMode.ToString());
            }

            // Assert
            Assert.That(string.Join(",", carried), Is.EqualTo(string.Join(",", members.Select(m => m.ToString()))));
        }

        #endregion
    }
}

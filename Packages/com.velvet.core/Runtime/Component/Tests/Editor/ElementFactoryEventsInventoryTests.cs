using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.Experimental;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds every <c>V.*</c> factory that returns an element node, and every initializer builder, to the one
    /// event rule: an <c>events:</c> array reaches the node's bindings, behind the factory's own
    /// <c>onClick:</c> / <c>onValueChanged:</c> binding where it has one. The factories are found by what
    /// they return rather than listed, so one added later is held to the rule without being named here.
    /// </summary>
    /// <remarks>
    /// Each case calls the factories by reflection, so it compiles against a <c>V</c> that has no
    /// <c>events</c> parameter at all and reports that factory as missing it.
    /// </remarks>
    [TestFixture]
    internal sealed class ElementFactoryEventsInventoryTests
    {
        private const string EventsParameter = "events";

        // The factory callbacks that bind through the node's event array; a factory's other callbacks
        // (onCreated:, the drag callbacks) are not event bindings.
        private static readonly string[] OwnBindingParameters = { "onClick", "onValueChanged" };

        private static readonly PointerDownBinding Sentinel = new() { Handler = _ => { } };

        private static void Noop() { }

        private static void Noop<T>(T _) { }

        // Long-form factories only: the params shorthand overloads take a class string and children alone.
        private static IEnumerable<MethodInfo> ElementFactories() =>
            typeof(V).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(method => typeof(BaseElementNode).IsAssignableFrom(method.ReturnType))
                .Where(method => !method.GetParameters().Any(parameter => parameter.IsDefined(typeof(ParamArrayAttribute))))
                .Select(method => method.IsGenericMethodDefinition ? method.MakeGenericMethod(typeof(VisualElement)) : method);

        // A parameter with no default is the subject of a factory whose subject may be null (a camera, an
        // effect, a target) or an id string; neither is what this fixture reads.
        private static object? Argument(ParameterInfo parameter)
        {
            if (parameter.HasDefaultValue)
            {
                var value = parameter.DefaultValue;
                var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
                return value != null && type.IsEnum && value.GetType() != type ? Enum.ToObject(type, value) : value;
            }
            if (parameter.ParameterType == typeof(string)) return "id";
            return parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null;
        }

        private static Delegate OwnCallback(Type delegateType)
        {
            if (delegateType == typeof(Action)) return new Action(Noop);
            var noop = typeof(ElementFactoryEventsInventoryTests)
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .Single(method => method.Name == nameof(Noop) && method.IsGenericMethodDefinition)
                .MakeGenericMethod(delegateType.GetGenericArguments()[0]);
            return noop.CreateDelegate(delegateType);
        }

        // Hands back what a factory rented, since no reconcile retires these nodes; VFactoryRefusalRentalTests
        // states what a rented-out set that grows across fixtures costs a later allocation measurement.
        private static void Retire(object? node)
        {
            if (node is not BaseElementNode element) return;
            VNodePool.ReturnProps(element.Props);
            VNodePool.ReturnEventArray(element.Events);
        }

        private static string? Describe(MethodInfo factory, bool withOwnCallback)
        {
            var parameters = factory.GetParameters();
            if (parameters.All(parameter => parameter.Name != EventsParameter))
            {
                return factory.Name + ": no events parameter";
            }

            var arguments = parameters.Select(parameter => parameter.Name switch
            {
                EventsParameter => new FiberEventBinding[] { Sentinel },
                _ when withOwnCallback && OwnBindingParameters.Contains(parameter.Name) =>
                    OwnCallback(parameter.ParameterType),
                _ => Argument(parameter),
            }).ToArray();

            object? node;
            try
            {
                node = factory.Invoke(null, arguments);
            }
            catch (TargetInvocationException ex)
            {
                return factory.Name + ": threw " + ex.InnerException?.GetType().Name;
            }

            try
            {
                var events = ((BaseElementNode)node!).Events;
                if (!withOwnCallback)
                {
                    return events.Contains(Sentinel) ? null : factory.Name + ": dropped the events array";
                }
                return events.Length == 2 && events[0] != null && events[0] != Sentinel && events[1] == Sentinel
                    ? null
                    : factory.Name + ": " + string.Join(" ", events.Select(binding => binding?.EventId ?? "null"));
            }
            finally
            {
                Retire(node);
            }
        }

        [Test]
        public void Given_EveryElementFactory_When_CalledWithAnEventsArray_Then_TheNodeCarriesItsBindings()
        {
            // Arrange
            var factories = ElementFactories().ToArray();

            // Act
            var failures = factories
                .Select(factory => Describe(factory, withOwnCallback: false))
                .Where(failure => failure != null);

            // Assert — the count rides along so a reflection that found no factory cannot pass.
            Assert.That((factories.Length > 1, string.Join("\n", failures)), Is.EqualTo((true, string.Empty)));
        }

        [Test]
        public void Given_EveryElementFactoryWithItsOwnCallback_When_CalledWithItAndAnEventsArray_Then_ItsOwnBindingLeadsTheCallersBindings()
        {
            // Arrange
            var factories = ElementFactories()
                .Where(factory => factory.GetParameters().Any(parameter => OwnBindingParameters.Contains(parameter.Name)))
                .ToArray();

            // Act
            var failures = factories
                .Select(factory => Describe(factory, withOwnCallback: true))
                .Where(failure => failure != null);

            // Assert — the floor keeps a reflection that matched none of these factories from passing.
            Assert.That((factories.Length >= 8, string.Join("\n", failures)), Is.EqualTo((true, string.Empty)));
        }

        [Test]
        public void Given_EveryInitializerBuilder_When_ItsEventsAreSet_Then_TheBuiltNodeCarriesThem()
        {
            // Arrange
            var events = typeof(VBuilder).GetProperty("Events");
            var builders = typeof(VBuilder).Assembly.GetTypes()
                .Where(type => typeof(VBuilder).IsAssignableFrom(type) && !type.IsAbstract)
                .Select(type => type.IsGenericTypeDefinition ? type.MakeGenericType(typeof(VisualElement)) : type)
                .ToArray();

            // Act
            var failures = new List<string>();
            foreach (var type in builders)
            {
                if (events == null)
                {
                    failures.Add(type.Name + ": VBuilder has no Events property");
                    continue;
                }
                var builder = (VBuilder)Activator.CreateInstance(type, new object?[] { null })!;
                events.SetValue(builder, new FiberEventBinding[] { Sentinel });
                var node = builder.Build();
                if (!(node is BaseElementNode element && element.Events.Contains(Sentinel)))
                {
                    failures.Add(type.Name + ": dropped the events array");
                }
                Retire(node);
            }

            // Assert
            Assert.That((builders.Length > 1, string.Join("\n", failures)), Is.EqualTo((true, string.Empty)));
        }
    }
}

#if UNITY_EDITOR
#nullable enable
using System;
using System.Reflection;

namespace Velvet
{
    /// <summary>
    /// Metadata and invocation support for a discovered <c>[VelvetPreview]</c> story.
    /// <para>
    /// A story method is parameterless or takes one supported args value. The preview window creates controls
    /// for supported writable members and rebuilds the story when one is edited.
    /// </para>
    /// </summary>
    public sealed class VelvetPreviewStory
    {
        /// <summary>Display name shown in a story list (the attribute's Name, else the method name).</summary>
        public string Name { get; }

        /// <summary>Grouping heading the story sits under (the attribute's Group, else the declaring type name).</summary>
        public string Group { get; }

        /// <summary>Identifier (<c>Group/Name</c>) used to address a story and remember a selection.</summary>
        public string Id { get; }

        /// <summary>Preferred mount width in reference pixels; <c>0</c> means fill the host.</summary>
        public int Width { get; }

        /// <summary>Preferred mount height in reference pixels; <c>0</c> means fill the host.</summary>
        public int Height { get; }

        /// <summary>The assembly the story method lives in — used to resolve its preview-setup environment.</summary>
        public Assembly? Assembly { get; }

        /// <summary>The story's single args-parameter type, or <c>null</c> when the story is parameterless.</summary>
        public Type? ArgsType { get; }

        private readonly MethodInfo _method;

        internal VelvetPreviewStory(MethodInfo method, VelvetPreviewAttribute attribute)
        {
            _method = method ?? throw new ArgumentNullException(nameof(method));
            Name = string.IsNullOrEmpty(attribute.Name) ? method.Name : attribute.Name;
            Group = string.IsNullOrEmpty(attribute.Group) ? method.DeclaringType?.Name ?? "Preview" : attribute.Group;
            Width = attribute.Width;
            Height = attribute.Height;
            Assembly = method.DeclaringType?.Assembly;
            Id = Group + "/" + Name;

            var parameters = method.GetParameters();
            ArgsType = parameters.Length == 1 ? parameters[0].ParameterType : null;
        }

        /// <summary>
        /// Invokes the story with its default args. Use <see cref="CreateDefaultArgs"/> and
        /// <see cref="Build(object)"/> to supply edited args.
        /// </summary>
        public VNode? Build() => Build(ArgsType == null ? null : CreateDefaultArgs());

        /// <summary>
        /// Invokes an args-story with <paramref name="args"/>; parameterless stories ignore it. The story's own
        /// exception is rethrown without the reflection wrapper.
        /// </summary>
        public VNode? Build(object? args)
        {
            var invokeArgs = ArgsType == null ? null : new[] { args };
            try
            {
                return _method.Invoke(null, invokeArgs) as VNode;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }

        /// <summary>
        /// Creates the default <see cref="ArgsType"/> value. The result is <c>null</c> for a parameterless story
        /// and may also be null for a nullable args type.
        /// </summary>
        public object? CreateDefaultArgs() => ArgsType == null ? null : Activator.CreateInstance(ArgsType);
    }
}
#endif

#if UNITY_EDITOR
using System;
using System.Reflection;

namespace Velvet
{
    /// <summary>
    /// One discovered <c>[VelvetPreview]</c> story: the metadata a preview tool lists, plus a thunk that
    /// produces a fresh <see cref="VNode"/> tree each time the story is mounted.
    /// <para>
    /// A story method is parameterless (renders one fixed view) or takes a single "args" object — a
    /// class/struct/record of editable props (the Storybook <c>args</c>). For an args-story the preview window
    /// reflects the args type into live control knobs and re-mounts with the edited instance.
    /// </para>
    /// </summary>
    public sealed class VelvetPreviewStory
    {
        /// <summary>Display name shown in a story list (the attribute's Name, else the method name).</summary>
        public string Name { get; }

        /// <summary>Grouping heading the story sits under (the attribute's Group, else the declaring type name).</summary>
        public string Group { get; }

        /// <summary>Stable identifier (<c>Group/Name</c>) used to address a story and remember a selection.</summary>
        public string Id { get; }

        /// <summary>Preferred mount width in reference pixels; <c>0</c> means fill the host.</summary>
        public int Width { get; }

        /// <summary>Preferred mount height in reference pixels; <c>0</c> means fill the host.</summary>
        public int Height { get; }

        /// <summary>The assembly the story method lives in — used to resolve its preview-setup environment.</summary>
        public Assembly Assembly { get; }

        /// <summary>The story's single args-parameter type, or <c>null</c> when the story is parameterless.</summary>
        public Type ArgsType { get; }

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
        /// Builds a fresh VNode tree at the story's default args. A parameterless story is invoked directly; an
        /// args-story is invoked with a default-constructed args instance (so the capture harness and the first
        /// mount show the story at its declared default values). Use <see cref="CreateDefaultArgs"/> +
        /// <see cref="Build(object)"/> to drive it with edited args.
        /// </summary>
        public VNode Build() => Build(ArgsType == null ? null : CreateDefaultArgs());

        /// <summary>
        /// Builds a fresh VNode tree, passing <paramref name="args"/> to an args-story (or no argument to a
        /// parameterless one — <paramref name="args"/> is ignored there). Rethrows the story's own exception
        /// (unwrapped from the reflection <see cref="TargetInvocationException"/>) so a caller surfaces a clean
        /// message.
        /// </summary>
        public VNode Build(object args)
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
        /// Default-constructs an instance of <see cref="ArgsType"/> (honoring its field/property initializers),
        /// or returns <c>null</c> for a parameterless story. The preview window seeds its live control state from
        /// this.
        /// </summary>
        public object CreateDefaultArgs() => ArgsType == null ? null : Activator.CreateInstance(ArgsType);
    }
}
#endif

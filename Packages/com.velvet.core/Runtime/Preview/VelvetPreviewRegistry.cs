#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    /// <summary>
    /// Discovers preview stories and their assembly setup methods from loaded assemblies that reference Velvet.
    /// </summary>
    public static class VelvetPreviewRegistry
    {
        private static List<VelvetPreviewStory>? s_cachedStories;

        private static readonly Dictionary<Assembly, List<MethodInfo>> s_setupCache = new();

        /// <summary>
        /// Discovered valid stories from the project's non-test assemblies, ordered by group then name.
        /// </summary>
        /// <exception cref="InvalidOperationException">Two stories share a <c>Group/Name</c> id.</exception>
        public static List<VelvetPreviewStory> DiscoverStories() =>
            s_cachedStories ??= DiscoverStoriesIn(NonTestVelvetAssemblies());

        /// <summary>
        /// Discovers valid stories from <paramref name="assemblies"/>. Invalid discovered signatures are skipped
        /// with a warning; a duplicate id throws, naming every collision.
        /// </summary>
        internal static List<VelvetPreviewStory> DiscoverStoriesIn(IEnumerable<Assembly> assemblies)
        {
            var stories = new List<VelvetPreviewStory>();
            foreach (var method in MethodsWith<VelvetPreviewAttribute>(assemblies))
            {
                if (!IsValidStory(method))
                {
                    Debug.LogWarning(
                        $"[VelvetPreview] '{Describe(method)}' is ignored: a [VelvetPreview] method must be " +
                        "static, non-generic, return VNode, and take either no parameters or a single args object " +
                        "(a struct / record / class with a public parameterless constructor).");
                    continue;
                }

                stories.Add(new VelvetPreviewStory(method, method.GetCustomAttribute<VelvetPreviewAttribute>()));
            }

            stories.Sort((a, b) =>
            {
                var byGroup = string.CompareOrdinal(a.Group, b.Group);
                return byGroup != 0 ? byGroup : string.CompareOrdinal(a.Name, b.Name);
            });
            RefuseDuplicateIds(stories);
            return stories;
        }

        /// <summary>
        /// Runs every valid <c>[VelvetPreviewSetup]</c> environment <paramref name="assembly"/> declares,
        /// ordered by declaring type then method name, and takes the <see cref="VelvetStyleHints.PreviewStyleSheet"/>
        /// each one publishes as it returns. Returns <c>null</c> for a <c>null</c> assembly.
        /// </summary>
        public static VelvetPreviewEnvironment? RunSetupFor(Assembly? assembly)
        {
            if (assembly == null) return null;
            var teardowns = new List<IDisposable>();
            var styleSheets = new List<StyleSheet>();
            foreach (var setup in ResolveSetups(assembly))
            {
                var teardown = Invoke(setup);
                if (teardown != null) teardowns.Add(teardown);
                var sheet = VelvetStyleHints.Take();
                if (sheet != null) styleSheets.Add(sheet);
            }

            return new VelvetPreviewEnvironment(teardowns, styleSheets);
        }

        private static List<MethodInfo> ResolveSetups(Assembly assembly)
        {
            if (s_setupCache.TryGetValue(assembly, out var cached)) return cached;

            var setups = new List<MethodInfo>();
            foreach (var method in MethodsWith<VelvetPreviewSetupAttribute>(new[] { assembly }))
            {
                if (IsValidSetup(method))
                {
                    setups.Add(method);
                    continue;
                }

                Debug.LogWarning(
                    $"[VelvetPreview] '{Describe(method)}' is ignored: a [VelvetPreviewSetup] method must be " +
                    "static, non-generic, parameterless, and return void, IDisposable, or Action.");
            }

            setups.Sort((a, b) =>
            {
                var byType = string.CompareOrdinal(a.DeclaringType?.FullName, b.DeclaringType?.FullName);
                return byType != 0 ? byType : string.CompareOrdinal(a.Name, b.Name);
            });
            s_setupCache[assembly] = setups;
            return setups;
        }

        // Test fixture stories are scaffolding, not project UI; keep their assemblies out of the preview and
        // capture registries.
        private static IEnumerable<Assembly> NonTestVelvetAssemblies()
        {
            var velvet = typeof(VelvetPreviewRegistry).Assembly.GetName().Name;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.IsDynamic) continue;
                if (!ReferencesVelvet(assembly, velvet)) continue;
                if (ReferencesTestRunner(assembly)) continue;
                yield return assembly;
            }
        }

        // Failure to enumerate one assembly or type must not hide stories from the remaining assemblies.
        private static IEnumerable<MethodInfo> MethodsWith<TAttribute>(IEnumerable<Assembly> assemblies)
            where TAttribute : Attribute
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var assembly in assemblies)
            {
                if (assembly == null || assembly.IsDynamic) continue;

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = Array.FindAll(ex.Types, t => t != null);
                }
                catch
                {
                    continue;
                }

                foreach (var type in types)
                {
                    MethodInfo[] methods;
                    try
                    {
                        methods = type.GetMethods(flags);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var method in methods)
                    {
                        if (method.IsDefined(typeof(TAttribute), false)) yield return method;
                    }
                }
            }
        }

        // Refused rather than resolved to one of the stories, as Storybook refuses an index holding two stories
        // under one id.
        private static void RefuseDuplicateIds(List<VelvetPreviewStory> stories)
        {
            var first = new Dictionary<string, VelvetPreviewStory>();
            var collisions = new List<string>();
            foreach (var story in stories)
            {
                if (first.TryAdd(story.Id, story)) continue;
                collisions.Add(
                    $"Duplicate stories with id '{story.Id}': '{Describe(first[story.Id].Method)}' and " +
                    $"'{Describe(story.Method)}'. Give one a distinct Name or Group.");
            }

            if (collisions.Count > 0)
            {
                throw new InvalidOperationException("[VelvetPreview] " + string.Join("\n", collisions));
            }
        }

        private static bool ReferencesVelvet(Assembly assembly, string velvetName)
        {
            if (assembly.GetName().Name == velvetName) return true;
            try
            {
                foreach (var referenced in assembly.GetReferencedAssemblies())
                {
                    if (referenced.Name == velvetName) return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static bool ReferencesTestRunner(Assembly assembly)
        {
            try
            {
                foreach (var referenced in assembly.GetReferencedAssemblies())
                {
                    if (referenced.Name == "UnityEngine.TestRunner"
                        || referenced.Name == "UnityEditor.TestRunner"
                        || referenced.Name == "nunit.framework")
                    {
                        return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static IDisposable? Invoke(MethodInfo setup)
        {
            object result;
            try
            {
                result = setup.Invoke(null, null);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                Debug.LogError($"[VelvetPreview] preview setup '{Describe(setup)}' threw; the exception follows.");
                Debug.LogException(ex.InnerException);
                return null;
            }

            return result switch
            {
                IDisposable disposable => disposable,
                Action teardown => new ActionDisposable(teardown),
                _ => null,
            };
        }

        private static bool IsValidStory(MethodInfo method)
        {
            if (!method.IsStatic
                || method.IsGenericMethodDefinition
                || (method.DeclaringType?.IsGenericTypeDefinition ?? false)
                || !typeof(VNode).IsAssignableFrom(method.ReturnType))
            {
                return false;
            }

            var parameters = method.GetParameters();
            return parameters.Length switch
            {
                0 => true,
                1 => IsValidArgsType(parameters[0].ParameterType),
                _ => false,
            };
        }

        // The scalar rejections come first: an int, an enum and a string would otherwise reach the
        // value-type arm or the constructor check and pass. They do not exhaust what does — a DateTime
        // or a Guid is accepted — so this narrows the single-parameter shape rather than deciding it.
        private static bool IsValidArgsType(Type type)
        {
            if (type.IsByRef || type.IsPointer || type.IsPrimitive || type.IsEnum || type == typeof(string)) return false;
            if (type.ContainsGenericParameters || type.IsAbstract) return false;
            if (type.IsValueType) return true;
            return type.GetConstructor(Type.EmptyTypes) != null;
        }

        private static bool IsValidSetup(MethodInfo method) =>
            method.IsStatic
            && !method.IsGenericMethodDefinition
            && !(method.DeclaringType?.IsGenericTypeDefinition ?? false)
            && method.GetParameters().Length == 0
            && (method.ReturnType == typeof(void)
                || typeof(IDisposable).IsAssignableFrom(method.ReturnType)
                || method.ReturnType == typeof(Action));

        private static string Describe(MethodInfo method) =>
            (method.DeclaringType?.FullName ?? "?") + "." + method.Name;

        private sealed class ActionDisposable : IDisposable
        {
            private Action? _teardown;
            public ActionDisposable(Action teardown) => _teardown = teardown;

            public void Dispose()
            {
                var teardown = _teardown;
                _teardown = null;
                teardown?.Invoke();
            }
        }
    }
}
#endif

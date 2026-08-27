#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Velvet
{
    /// <summary>
    /// Discovers preview stories and their assembly setup methods from loaded assemblies that reference Velvet.
    /// </summary>
    public static class VelvetPreviewRegistry
    {
        private static List<VelvetPreviewStory>? s_cachedStories;

        private static readonly Dictionary<Assembly, MethodInfo?> s_setupCache = new();

        /// <summary>
        /// Discovered valid stories from the project's non-test assemblies, ordered by group then name.
        /// </summary>
        public static List<VelvetPreviewStory> DiscoverStories() =>
            s_cachedStories ??= DiscoverStoriesIn(NonTestVelvetAssemblies());

        /// <summary>
        /// Discovers valid stories from <paramref name="assemblies"/>. Invalid discovered signatures are skipped
        /// with a warning.
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
            DropDuplicateIds(stories);
            return stories;
        }

        /// <summary>
        /// Resolves and runs the <c>[VelvetPreviewSetup]</c> environment for <paramref name="assembly"/>. Returns
        /// its teardown handle when it supplied one; otherwise returns <c>null</c>. Honors at most one setup per
        /// assembly.
        /// </summary>
        public static IDisposable? RunSetupFor(Assembly? assembly)
        {
            if (assembly == null) return null;
            var chosen = ResolveSetup(assembly);
            return chosen == null ? null : Invoke(chosen);
        }

        private static MethodInfo? ResolveSetup(Assembly assembly)
        {
            if (s_setupCache.TryGetValue(assembly, out var cached)) return cached;

            MethodInfo? chosen = null;
            foreach (var method in MethodsWith<VelvetPreviewSetupAttribute>(new[] { assembly }))
            {
                if (!IsValidSetup(method))
                {
                    Debug.LogWarning(
                        $"[VelvetPreview] '{Describe(method)}' is ignored: a [VelvetPreviewSetup] method must be " +
                        "static, non-generic, parameterless, and return void, IDisposable, or Action.");
                    continue;
                }

                if (chosen != null)
                {
                    Debug.LogWarning(
                        $"[VelvetPreview] '{Describe(method)}' is ignored: assembly '{assembly.GetName().Name}' " +
                        $"already declares a preview setup ('{Describe(chosen)}').");
                    continue;
                }

                chosen = method;
            }

            s_setupCache[assembly] = chosen;
            return chosen;
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

        // Two equal ids mean a capture would overwrite a PNG and a selection-restore would be ambiguous, so the
        // first occurrence (discovery is already sorted) is kept and the rest are reported and removed.
        private static void DropDuplicateIds(List<VelvetPreviewStory> stories)
        {
            var seen = new HashSet<string>();
            for (var i = 0; i < stories.Count; i++)
            {
                if (seen.Add(stories[i].Id)) continue;
                Debug.LogWarning(
                    $"[VelvetPreview] duplicate story id '{stories[i].Id}' is ignored: another story already " +
                    "uses that Group/Name. Give it a distinct Name or Group.");
                stories.RemoveAt(i);
                i--;
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
                Debug.LogError($"[VelvetPreview] preview setup '{Describe(setup)}' threw: {ex.InnerException}");
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

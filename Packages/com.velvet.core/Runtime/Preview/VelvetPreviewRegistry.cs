#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Velvet
{
    /// <summary>
    /// The single source of preview stories. Discovers every <c>[VelvetPreview]</c> method and the
    /// <c>[VelvetPreviewSetup]</c> environment its assembly opts into, by plain reflection over the loaded
    /// assemblies that reference Velvet — so the live editor window and the headless capture path both drive off
    /// one registry and a story authored once renders identically in either.
    /// </summary>
    public static class VelvetPreviewRegistry
    {
        // Discovered stories, computed once per domain. A domain reload tears down the AppDomain and resets every
        // static, so a recompile rebuilds this on the next access with no manual invalidation needed; the
        // expensive AppDomain-wide scan therefore runs at most once per editor session between reloads.
        private static List<VelvetPreviewStory> s_cachedStories;

        // Per-assembly resolved [VelvetPreviewSetup] method (null value = scanned, none found), cached so a story
        // re-mounting many times (e.g. a controls knob edited per keystroke) does not re-scan the assembly each
        // mount. Reset for free by a domain reload, like s_cachedStories.
        private static readonly Dictionary<Assembly, MethodInfo> s_setupCache = new();

        /// <summary>
        /// All valid preview stories declared by the project's own (non-test) assemblies, ordered by group then
        /// name so a story list is stable across reloads. Test-runner assemblies are excluded so fixture stories
        /// authored for unit tests never leak into the preview window or the capture set.
        /// </summary>
        public static List<VelvetPreviewStory> DiscoverStories() =>
            s_cachedStories ??= DiscoverStoriesIn(NonTestVelvetAssemblies());

        /// <summary>
        /// Discovers the valid stories declared in <paramref name="assemblies"/> only. The public
        /// <see cref="DiscoverStories"/> calls this with the project's non-test assemblies; tests call it with
        /// their own assembly to exercise discovery despite the production test-assembly exclusion. An invalid
        /// signature (non-static, parameterized, generic, or not returning <see cref="VNode"/>) is skipped with a
        /// warning rather than silently dropped, so a mistyped story is noticed.
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
        /// Resolves the <c>[VelvetPreviewSetup]</c> environment for <paramref name="assembly"/>, runs it, and
        /// returns a disposable that tears it back down — or <c>null</c> when the assembly declares no setup.
        /// Honors at most one setup per assembly (a second is ignored with a warning).
        /// </summary>
        public static IDisposable RunSetupFor(Assembly assembly)
        {
            if (assembly == null) return null;
            var chosen = ResolveSetup(assembly);
            return chosen == null ? null : Invoke(chosen);
        }

        // The assembly's single [VelvetPreviewSetup] method (or null), resolved once and cached. The scan +
        // validation warnings run only on the first resolve per assembly; subsequent mounts read the cache.
        private static MethodInfo ResolveSetup(Assembly assembly)
        {
            if (s_setupCache.TryGetValue(assembly, out var cached)) return cached;

            MethodInfo chosen = null;
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

        // The project's own assemblies that reference Velvet, minus any that reference the Unity test runner or
        // NUnit: a test assembly's fixture stories are scaffolding for unit tests, not project UI, so they must
        // not surface in the preview window or be written out by the capture harness.
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

        // Enumerates every method tagged with TAttribute across the given assemblies. A load/reflection failure
        // on one assembly does not abort the scan.
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

        private static IDisposable Invoke(MethodInfo setup)
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

            // Either parameterless (a fixed view) or a single "args" object the window turns into control knobs.
            var parameters = method.GetParameters();
            return parameters.Length switch
            {
                0 => true,
                1 => IsValidArgsType(parameters[0].ParameterType),
                _ => false,
            };
        }

        // An args type must be a non-primitive the window can default-construct and reflect: a struct (always has
        // a parameterless ctor) or a concrete class/record with a public parameterless ctor. Primitives, string,
        // enums, by-ref / pointer parameters, open generics, and abstract types (cannot be instantiated) are
        // rejected so the single-parameter shape always means a real, constructible args object.
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

        /// <summary>Adapts an <see cref="Action"/> teardown returned by a setup method to <see cref="IDisposable"/>.</summary>
        private sealed class ActionDisposable : IDisposable
        {
            private Action _teardown;
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

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Velvet
{
    // Shared synchronous Addressables loader with per-key caching, behind the utility-class
    // arbitrary-value forms that name an asset by Addressables key (bg-[addr:…],
    // font-[addr:…]). Loading is synchronous (AsyncOperationHandle<T>.WaitForCompletion)
    // and the outcome — including a failed null — is pinned in the caller's cache for the panel
    // lifetime, so repeated reconciles do not re-issue (and re-refcount) the load.
    internal static class AddressableAssetCache
    {
        // Returns true and the loaded asset when key resolves to a T;
        // false (with a warning logged under warnTag) otherwise. The result is cached in
        // cache; a failed load is cached as null to avoid retrying every reconcile.
        public static bool TryLoad<T>(string key, Dictionary<string, T> cache, string warnTag, out T asset)
            where T : UnityEngine.Object
        {
            asset = null;
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }

            if (cache.TryGetValue(key, out asset))
            {
                return asset != null;
            }

            try
            {
                var handle = Addressables.LoadAssetAsync<T>(key);
                asset = handle.WaitForCompletion();
                if (handle.Status != AsyncOperationStatus.Succeeded || asset == null)
                {
                    FiberLogger.LogWarning(warnTag,
                        $"Addressables.LoadAssetAsync<{typeof(T).Name}>(\"{key}\") did not return a {typeof(T).Name}. Verify the address is registered in an AddressableAssetGroup.");
                    cache[key] = null;
                    asset = null;
                    return false;
                }
            }
            catch (Exception ex)
            {
                FiberLogger.LogWarning(warnTag,
                    $"Addressables.LoadAssetAsync<{typeof(T).Name}>(\"{key}\") threw {ex.GetType().Name}: {ex.Message}");
                cache[key] = null;
                asset = null;
                return false;
            }

            cache[key] = asset;
            return true;
        }
    }
}

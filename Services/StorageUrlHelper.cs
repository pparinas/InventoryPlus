using System;
using Supabase;

namespace InventoryPlus.Services
{
    /// <summary>
    /// Resolves a stored image value (a bare storage path, or a legacy full URL from
    /// before a bucket was made public -- including an expired signed URL) to a fresh,
    /// permanent public URL. Mirrors the path-extraction logic already proven working
    /// for account_settings.logo_url in SettingsService, generalized for any public bucket.
    /// </summary>
    public static class StorageUrlHelper
    {
        public static string? ResolvePublicUrl(Client supabase, string bucket, string? storedValue)
        {
            var path = ExtractStoragePath(storedValue, bucket);
            if (string.IsNullOrEmpty(path)) return storedValue;

            try
            {
                return supabase.Storage.From(bucket).GetPublicUrl(path);
            }
            catch
            {
                return storedValue;
            }
        }

        private static string? ExtractStoragePath(string? urlOrPath, string bucket)
        {
            if (string.IsNullOrEmpty(urlOrPath)) return null;
            if (!urlOrPath.StartsWith("https://")) return urlOrPath;

            foreach (var marker in new[] { $"/object/public/{bucket}/", $"/object/sign/{bucket}/" })
            {
                var idx = urlOrPath.IndexOf(marker, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    var p = urlOrPath.Substring(idx + marker.Length);
                    var q = p.IndexOf('?');
                    return q >= 0 ? p.Substring(0, q) : p;
                }
            }
            return null;
        }
    }
}

using System;
using InventoryPlus.Models;

namespace InventoryPlus.Services
{
    public class SettingsService
    {
        private readonly Supabase.Client _supabase;

        public SettingsService(Supabase.Client supabase)
        {
            _supabase = supabase;
        }

        private string _companyName = "InventoryPlus";
        public string CompanyName 
        { 
            get => _companyName; 
            set 
            {
                if (_companyName != value)
                {
                    _companyName = value;
                    NotifyStateChanged();
                }
            } 
        }

        private string? _customLogoUrl = null;
        private string? _customLogoPath = null;

        public string? CustomLogoUrl
        {
            get => _customLogoUrl;
            set
            {
                if (_customLogoUrl == value) return;
                _customLogoUrl = value;
                // Also track the raw storage path for saving back to the DB.
                // If it's a full URL (public or signed), extract just the path segment.
                // If it's null/empty, clear the path. If it's already a path, keep as-is.
                if (string.IsNullOrEmpty(value))
                    _customLogoPath = null;
                else if (!value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    _customLogoPath = value; // already a plain path
                else
                    _customLogoPath = ExtractStoragePath(value, "branding") ?? _customLogoPath;
                NotifyStateChanged();
            }
        }

        private bool _useLogoForBranding = false;
        public bool UseLogoForBranding
        {
            get => _useLogoForBranding;
            set
            {
                if (_useLogoForBranding != value)
                {
                    _useLogoForBranding = value;
                    NotifyStateChanged();
                }
            }
        }

        // Extracts just the object path from any URL format or returns value as-is if it's already a path
        private static string? ExtractStoragePath(string? urlOrPath, string bucket)
        {
            if (string.IsNullOrEmpty(urlOrPath)) return null;
            if (!urlOrPath.StartsWith("https://")) return urlOrPath; // already a relative path
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

        public async Task LoadAsync(string userId)
        {
            if (!Guid.TryParse(userId, out var ownerGuid)) return;
            try
            {
                var response = await _supabase.From<AccountSettings>()
                    .Where(s => s.OwnerGuid == ownerGuid)
                    .Get();
                var result = response.Models.FirstOrDefault();
                if (result != null)
                {
                    _companyName = result.CompanyName;
                    _useLogoForBranding = result.UseLogoForBranding;

                    var path = ExtractStoragePath(result.LogoUrl, "branding");
                    _customLogoPath = path;
                    if (!string.IsNullOrEmpty(path))
                    {
                        try
                        {
                            _customLogoUrl = await _supabase.Storage
                                .From("branding")
                                .CreateSignedUrl(path, 60 * 60 * 24 * 7); // 7-day signed URL
                        }
                        catch
                        {
                            _customLogoUrl = null;
                        }
                    }
                    else
                    {
                        _customLogoUrl = null;
                    }

                    NotifyStateChanged();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Settings load error: {ex.Message}");
            }
        }

        public async Task SaveAsync(string userId)
        {
            if (!Guid.TryParse(userId, out var ownerGuid)) return;
            try
            {
                // Always save the storage PATH (not a full URL) so records stay valid long-term
                var settings = new AccountSettings
                {
                    OwnerGuid = ownerGuid,
                    CompanyName = _companyName,
                    LogoUrl = _customLogoPath ?? string.Empty,
                    UseLogoForBranding = _useLogoForBranding
                };
                await _supabase.From<AccountSettings>().Upsert(settings);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Settings save error: {ex.Message}");
            }
        }

        public event Action? OnStateChanged;
        
        private void NotifyStateChanged() => OnStateChanged?.Invoke();
    }
}

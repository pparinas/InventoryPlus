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
        public string? CustomLogoUrl
        {
            get => _customLogoUrl;
            set
            {
                if (_customLogoUrl != value)
                {
                    _customLogoUrl = value;
                    NotifyStateChanged();
                }
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
                    _customLogoUrl = string.IsNullOrEmpty(result.LogoUrl) ? null : result.LogoUrl;
                    _useLogoForBranding = result.UseLogoForBranding;
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
                var settings = new AccountSettings
                {
                    OwnerGuid = ownerGuid,
                    CompanyName = _companyName,
                    LogoUrl = _customLogoUrl ?? string.Empty,
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

using System;

namespace InventoryPlus.Services
{
    public class SettingsService
    {
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

        public event Action? OnStateChanged;
        
        private void NotifyStateChanged() => OnStateChanged?.Invoke();
    }
}

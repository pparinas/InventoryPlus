using System;

namespace InventoryPlus.Models
{
    public class AppNotification
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Icon { get; set; } = "fa-solid fa-bell";
        public string Type { get; set; } = "info"; // info, warning, success, danger
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public bool IsRead { get; set; } = false;
    }
}

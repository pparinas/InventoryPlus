namespace InventoryPlus.Services
{
    public class ToastMessage
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Message { get; set; } = "";
        public string Type { get; set; } = "success"; // success | error | info
    }

    public class ToastService
    {
        private readonly List<ToastMessage> _toasts = new();
        public IReadOnlyList<ToastMessage> Toasts => _toasts;

        public event Action? OnChanged;

        public void Show(string message, string type = "success")
        {
            var toast = new ToastMessage { Message = message, Type = type };
            _toasts.Add(toast);
            OnChanged?.Invoke();
            _ = RemoveAfterDelay(toast);
        }

        private async Task RemoveAfterDelay(ToastMessage toast)
        {
            await Task.Delay(3500);
            Remove(toast);
        }

        public void Remove(ToastMessage toast)
        {
            _toasts.Remove(toast);
            OnChanged?.Invoke();
        }
    }
}

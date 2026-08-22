using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using InventoryPlus.Models;
using InventoryPlus.Services;

namespace InventoryPlus.Pages
{
    public partial class OrderConfirmation : ComponentBase
    {
        [Parameter] public string Slug { get; set; } = "";
        [Parameter] public int OrderNumber { get; set; }
        [Inject] public Supabase.Client SupabaseClient { get; set; } = default!;

        protected bool isLoading = true;
        protected Order? order;

        protected override async Task OnInitializedAsync()
        {
            try
            {
                var service = new PublicMenuService(SupabaseClient);
                var menu = await service.ResolveMenuAsync(Slug);
                if (menu != null)
                {
                    var resp = await SupabaseClient.From<Order>()
                        .Where(o => o.OwnerGuid == menu.OwnerGuid && o.OrderNumber == OrderNumber)
                        .Get();
                    order = resp.Models.FirstOrDefault();
                    if (order != null)
                    {
                        var itemsResp = await SupabaseClient.From<OrderItem>()
                            .Where(i => i.OrderId == order.Guid)
                            .Get();
                        order.Items = itemsResp.Models;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"OrderConfirmation load error: {ex.Message}");
                order = null;
            }
            isLoading = false;
        }
    }
}

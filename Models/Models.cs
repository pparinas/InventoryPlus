using System;
using System.Collections.Generic;
using System.Linq;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using Newtonsoft.Json;

namespace InventoryPlus.Models
{
    [Table("ingredients")]
    public class Ingredient : BaseModel
    {
        [PrimaryKey("id", false)]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Column("owner_id")]
        public string OwnerId { get; set; } = string.Empty;

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("unit")]
        public string Unit { get; set; } = string.Empty;

        [Column("stock")]
        public double Stock { get; set; }

        [Column("cost_per_unit")]
        public double CostPerUnit { get; set; }

        [Column("type")]
        public string Type { get; set; } = "Ingredient"; // "Ingredient" or "Necessity"
    }

    [Table("product_ingredients")]
    public class ProductIngredient : BaseModel
    {
        [PrimaryKey("id", false)]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Column("owner_id")]
        public string OwnerId { get; set; } = string.Empty;

        [Column("product_id")]
        public Guid ProductId { get; set; }

        [Column("ingredient_id")]
        public Guid IngredientId { get; set; }

        [Column("quantity_required")]
        public double QuantityRequired { get; set; }
        
        [Reference(typeof(Ingredient))]
        public Ingredient? Ingredient { get; set; }
    }

    [Table("products")]
    public class Product : BaseModel
    {
        [PrimaryKey("id", false)]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Column("owner_id")]
        public string OwnerId { get; set; } = string.Empty;

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("selling_price")]
        public double SellingPrice { get; set; }

        [Column("tax_rate")]
        public double TaxRate { get; set; }

        [Column("image_url")]
        public string ImageUrl { get; set; } = string.Empty;
        
        [Reference(typeof(ProductIngredient))]
        public List<ProductIngredient> RequiredIngredients { get; set; } = new();

        [JsonIgnore]
        public int AvailableCount => CalculateAvailableCount();
        [JsonIgnore]
        public double TotalCost => RequiredIngredients.Sum(i => i.QuantityRequired * (i.Ingredient?.CostPerUnit ?? 0));
        [JsonIgnore]
        public double ProfitMargin => SellingPrice - TotalCost;
        [JsonIgnore]
        public double ProfitMarginPercentage => SellingPrice > 0 ? ProfitMargin / SellingPrice : 0;

        private int CalculateAvailableCount()
        {
            if (RequiredIngredients == null || RequiredIngredients.Count == 0) return 0;
            var availableCounts = RequiredIngredients.Select(req => 
                req.Ingredient != null && req.QuantityRequired > 0 
                ? (int)(req.Ingredient.Stock / req.QuantityRequired) 
                : 0);
            return availableCounts.Min();
        }
    }

    [Table("sales")]
    public class Sale : BaseModel
    {
        [PrimaryKey("id", false)]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Column("owner_id")]
        public string OwnerId { get; set; } = string.Empty;

        [Column("product_id")]
        public Guid ProductId { get; set; }

        [Column("product_name")]
        public string ProductName { get; set; } = string.Empty;

        [Column("quantity_sold")]
        public int QuantitySold { get; set; }

        [Column("total_amount")]
        public double TotalAmount { get; set; }

        [Column("tax_amount")]
        public double TaxAmount { get; set; }

        [Column("profit_amount")]
        public double ProfitAmount { get; set; }

        [Column("date")]
        public DateTime Date { get; set; } = DateTime.Now;

        [Column("note")]
        public string Note { get; set; } = string.Empty;

        [Column("payment_method")]
        public string PaymentMethod { get; set; } = "Cash"; // "Cash" or "GCash"
    }

    public class SystemUser
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = "User";
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? SubscriptionExpiresAt { get; set; }
    }
}

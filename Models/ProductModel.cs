using System;
using System.Collections.Generic;
using System.Linq;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;
using Newtonsoft.Json;
using InventoryPlus.Services;

namespace InventoryPlus.Models
{
    [Table("products")]
    public class Product : BaseModel
    {
        [PrimaryKey("guid", true)]
        public Guid Guid { get; set; } = Guid.NewGuid();

        [Column("owner_guid")]
        public Guid OwnerGuid { get; set; }

        [Column("name")]
        public string Name { get; set; } = string.Empty;

        [Column("selling_price")]
        public double SellingPrice { get; set; }

        [Column("tax_rate")]
        public double TaxRate { get; set; }

        [Column("image_url")]
        public string ImageUrl { get; set; } = string.Empty;

        // To persist: add 'category' column to Supabase, uncomment [Column], remove [JsonIgnore]
        [JsonIgnore]
        public string Category { get; set; } = "Other";

        [Column("has_ingredients")]
        public bool HasIngredients { get; set; } = true;

        [Column("stock_count")]
        public double StockCount { get; set; }

        [Column("is_archived")]
        public bool IsArchived { get; set; } = false;

        [Reference(typeof(ProductIngredient))]
        public List<ProductIngredient> RequiredIngredients { get; set; } = new();

        [JsonIgnore]
        public int AvailableCount => CalculateAvailableCount();

        /// <summary>
        /// Total cost per unit sold, respecting unit conversions.
        /// e.g. if ingredient is stored in kg at ₱50/kg and product uses 20g,
        /// the cost contribution is ConvertSafe(20, "g", "kg") * 50 = 0.02 * 50 = ₱1.
        /// </summary>
        [JsonIgnore]
        public double TotalCost => RequiredIngredients.Sum(i =>
        {
            if (i.Ingredient == null) return 0;
            var convertedQty = UnitConverter.ConvertSafe(
                i.QuantityRequired,
                i.EffectiveUsageUnit,
                i.Ingredient.Unit);
            return convertedQty * i.Ingredient.CostPerUnit;
        });

        [JsonIgnore]
        public double ProfitMargin => SellingPrice - TotalCost;
        [JsonIgnore]
        public double ProfitMarginPercentage => SellingPrice > 0 ? ProfitMargin / SellingPrice : 0;

        private int CalculateAvailableCount()
        {
            if (!HasIngredients)
                return (int)StockCount;
            if (RequiredIngredients == null || RequiredIngredients.Count == 0) return 0;

            var availableCounts = RequiredIngredients.Select(req =>
            {
                if (req.Ingredient == null || req.QuantityRequired <= 0) return 0;

                // Convert the required quantity to the ingredient's stock unit
                var requiredInStockUnit = UnitConverter.ConvertSafe(
                    req.QuantityRequired,
                    req.EffectiveUsageUnit,
                    req.Ingredient.Unit);

                if (requiredInStockUnit <= 0) return 0;
                return (int)(req.Ingredient.Stock / requiredInStockUnit);
            });

            return availableCounts.Min();
        }
    }
}

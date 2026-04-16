using System;
using Supabase.Postgrest.Attributes;
using Supabase.Postgrest.Models;

namespace InventoryPlus.Models
{
    [Table("product_ingredients")]
    public class ProductIngredient : BaseModel
    {
        [PrimaryKey("guid", true)]
        public Guid Guid { get; set; } = Guid.NewGuid();

        [Column("owner_guid")]
        public Guid OwnerId { get; set; }

        [Column("product_id")]
        public Guid ProductId { get; set; }

        [Column("ingredient_id")]
        public Guid IngredientId { get; set; }

        [Column("quantity_required")]
        public double QuantityRequired { get; set; }

        /// <summary>
        /// The unit used when specifying usage per product (e.g. "g" when stock is in "kg").
        /// Empty string means use the ingredient's own unit (no conversion).
        /// </summary>
        [Column("usage_unit")]
        public string UsageUnit { get; set; } = string.Empty;

        [Reference(typeof(Ingredient))]
        public Ingredient? Ingredient { get; set; }

        /// <summary>
        /// The unit actually used for this requirement.
        /// Defaults to the ingredient's own unit if no override is set.
        /// </summary>
        public string EffectiveUsageUnit =>
            string.IsNullOrEmpty(UsageUnit)
                ? (Ingredient?.Unit ?? string.Empty)
                : UsageUnit;
    }
}

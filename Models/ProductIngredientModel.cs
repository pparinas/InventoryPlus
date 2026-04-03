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

        [Reference(typeof(Ingredient))]
        public Ingredient? Ingredient { get; set; }
    }
}

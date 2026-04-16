using System;
using System.Collections.Generic;
using System.Linq;

namespace InventoryPlus.Services
{
    public enum UnitCategory
    {
        Weight,
        Volume,
        Count,
        Custom
    }

    public static class UnitConverter
    {
        // Conversion factors relative to the SI base unit for each category:
        //   Weight  → kilogram (kg)
        //   Volume  → litre (L)
        //   Count   → piece (pcs)  [no real conversion, just grouping]

        private static readonly Dictionary<string, (UnitCategory Category, double ToBase)> _units =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // ── Weight ────────────────────────────────────────────────────
                { "kg",   (UnitCategory.Weight, 1.0) },
                { "g",    (UnitCategory.Weight, 0.001) },
                { "mg",   (UnitCategory.Weight, 0.000001) },
                { "lb",   (UnitCategory.Weight, 0.45359237) },
                { "lbs",  (UnitCategory.Weight, 0.45359237) },
                { "oz",   (UnitCategory.Weight, 0.028349523125) },

                // ── Volume ────────────────────────────────────────────────────
                { "l",    (UnitCategory.Volume, 1.0) },
                { "liter",(UnitCategory.Volume, 1.0) },
                { "litre",(UnitCategory.Volume, 1.0) },
                { "ml",   (UnitCategory.Volume, 0.001) },
                { "fl oz",(UnitCategory.Volume, 0.0295735) },
                { "floz", (UnitCategory.Volume, 0.0295735) },
                { "cup",  (UnitCategory.Volume, 0.236588) },
                { "cups", (UnitCategory.Volume, 0.236588) },
                { "tbsp", (UnitCategory.Volume, 0.0147868) },
                { "tsp",  (UnitCategory.Volume, 0.00492892) },
                { "gal",  (UnitCategory.Volume, 3.78541) },
                { "gallon",(UnitCategory.Volume, 3.78541) },
                { "pt",   (UnitCategory.Volume, 0.473176) },
                { "pint", (UnitCategory.Volume, 0.473176) },
                { "qt",   (UnitCategory.Volume, 0.946353) },
                { "quart",(UnitCategory.Volume, 0.946353) },

                // ── Count ─────────────────────────────────────────────────────
                { "pcs",   (UnitCategory.Count, 1.0) },
                { "pc",    (UnitCategory.Count, 1.0) },
                { "piece", (UnitCategory.Count, 1.0) },
                { "pieces",(UnitCategory.Count, 1.0) },
                { "dozen", (UnitCategory.Count, 12.0) },
                { "doz",   (UnitCategory.Count, 12.0) },
                { "box",   (UnitCategory.Count, 1.0) },   // box = 1 box (user defines quantity)
                { "boxes", (UnitCategory.Count, 1.0) },
                { "pack",  (UnitCategory.Count, 1.0) },
                { "packs", (UnitCategory.Count, 1.0) },
                { "pair",  (UnitCategory.Count, 2.0) },
                { "pairs", (UnitCategory.Count, 2.0) },
                { "bag",   (UnitCategory.Count, 1.0) },
                { "sachet",(UnitCategory.Count, 1.0) },
                { "tin",   (UnitCategory.Count, 1.0) },
                { "can",   (UnitCategory.Count, 1.0) },
                { "bottle",(UnitCategory.Count, 1.0) },
                { "roll",  (UnitCategory.Count, 1.0) },
            };

        // ── Per-category ordered unit lists for dropdowns ─────────────────────

        private static readonly string[] _weightUnits  = { "kg", "g", "mg", "lb", "oz" };
        private static readonly string[] _volumeUnits  = { "L", "mL", "cup", "tbsp", "tsp", "fl oz", "gal", "pt", "qt" };
        private static readonly string[] _countUnits   = { "pcs", "dozen", "pack", "box", "bag", "sachet", "pair", "tin", "can", "bottle", "roll" };

        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Returns the category of a unit string, or Custom if not recognised.</summary>
        public static UnitCategory GetCategory(string? unit)
        {
            if (string.IsNullOrWhiteSpace(unit)) return UnitCategory.Custom;
            return _units.TryGetValue(unit.Trim(), out var info) ? info.Category : UnitCategory.Custom;
        }

        /// <summary>True if both units are in the same non-Custom category.</summary>
        public static bool CanConvert(string? fromUnit, string? toUnit)
        {
            if (string.IsNullOrWhiteSpace(fromUnit) || string.IsNullOrWhiteSpace(toUnit)) return false;
            if (string.Equals(fromUnit.Trim(), toUnit.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
            var fromCat = GetCategory(fromUnit);
            var toCat   = GetCategory(toUnit);
            return fromCat != UnitCategory.Custom && toCat != UnitCategory.Custom && fromCat == toCat;
        }

        /// <summary>
        /// Converts a value from one unit to another.
        /// Returns the original value if conversion is not possible (same-category check failed or unknown unit).
        /// Never throws.
        /// </summary>
        public static double ConvertSafe(double value, string? fromUnit, string? toUnit)
        {
            if (string.IsNullOrWhiteSpace(fromUnit) || string.IsNullOrWhiteSpace(toUnit)) return value;

            var from = fromUnit.Trim();
            var to   = toUnit.Trim();

            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return value;

            if (!_units.TryGetValue(from, out var fromInfo) || !_units.TryGetValue(to, out var toInfo))
                return value; // unknown unit — fall back

            if (fromInfo.Category != toInfo.Category) return value; // incompatible categories — fall back

            // value → base unit → target unit
            return value * fromInfo.ToBase / toInfo.ToBase;
        }

        /// <summary>Returns ordered unit strings for the given category (used in dropdowns).</summary>
        public static string[] GetUnitsForCategory(UnitCategory category) => category switch
        {
            UnitCategory.Weight => _weightUnits,
            UnitCategory.Volume => _volumeUnits,
            UnitCategory.Count  => _countUnits,
            _                   => Array.Empty<string>()
        };

        /// <summary>All known unit categories (for category dropdowns).</summary>
        public static UnitCategory[] AllCategories { get; } =
        {
            UnitCategory.Weight,
            UnitCategory.Volume,
            UnitCategory.Count,
            UnitCategory.Custom
        };

        /// <summary>Human-readable label for a category.</summary>
        public static string CategoryLabel(UnitCategory cat) => cat switch
        {
            UnitCategory.Weight => "Weight (kg, g, lb…)",
            UnitCategory.Volume => "Volume (L, mL, cup…)",
            UnitCategory.Count  => "Count (pcs, dozen…)",
            _                   => "Custom / Other"
        };

        /// <summary>
        /// Returns a human-friendly conversion hint string, e.g. "1 g = 0.001 kg".
        /// Returns empty string when no conversion is needed or possible.
        /// </summary>
        public static string ConversionHint(string? usageUnit, string? stockUnit)
        {
            if (string.IsNullOrWhiteSpace(usageUnit) || string.IsNullOrWhiteSpace(stockUnit)) return "";
            if (string.Equals(usageUnit.Trim(), stockUnit.Trim(), StringComparison.OrdinalIgnoreCase)) return "";
            if (!CanConvert(usageUnit, stockUnit)) return "";

            var converted = ConvertSafe(1.0, usageUnit, stockUnit);
            var formatted = converted >= 0.001 ? converted.ToString("G4") : converted.ToString("G3");
            return $"1 {usageUnit} = {formatted} {stockUnit}";
        }
    }
}

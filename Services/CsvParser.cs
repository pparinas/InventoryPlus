using System.Collections.Generic;
using System.Text;

namespace InventoryPlus.Services
{
    /// <summary>
    /// Minimal RFC4180-ish CSV reader: handles quoted fields, escaped quotes ("") and commas inside quotes.
    /// Not a general-purpose CSV library — just enough for the fixed templates this app ships.
    /// </summary>
    public static class CsvParser
    {
        public static List<Dictionary<string, string>> ParseWithHeader(string csvText)
        {
            var rows = ParseRows(csvText);
            var result = new List<Dictionary<string, string>>();
            if (rows.Count == 0) return result;

            var headers = rows[0];
            for (int r = 1; r < rows.Count; r++)
            {
                var row = rows[r];
                if (row.Count == 1 && row[0].Length == 0) continue; // skip blank trailing line
                var dict = new Dictionary<string, string>();
                for (int c = 0; c < headers.Count; c++)
                {
                    dict[headers[c].Trim()] = c < row.Count ? row[c].Trim() : string.Empty;
                }
                result.Add(dict);
            }
            return result;
        }

        private static List<List<string>> ParseRows(string text)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else field.Append(c);
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        break;
                    case ',':
                        row.Add(field.ToString());
                        field.Clear();
                        break;
                    case '\r':
                        break;
                    case '\n':
                        row.Add(field.ToString());
                        field.Clear();
                        rows.Add(row);
                        row = new List<string>();
                        break;
                    default:
                        field.Append(c);
                        break;
                }
            }

            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }

            return rows;
        }
    }
}

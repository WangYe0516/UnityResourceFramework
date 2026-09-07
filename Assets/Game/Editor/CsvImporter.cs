using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ResourceFramework
{
    // No Unity dependency: this exact parser also runs in the command-line regression suite.
    public static class CsvImporter
    {
        public static ModuleEnvelope ParseModule(string module, string csv, int schemaVersion, string contentVersion)
        {
            Type rowType = ConfigCodec.TypeFor(module);
            var rows = ReadRows(csv);
            if (rows.Count == 0) throw new ConfigException(module + ": missing CSV header");
            string[] header = rows[0];
            if (header.Length > 0) header[0] = header[0].TrimStart('\uFEFF');
            var fields = rowType.GetFields(BindingFlags.Instance | BindingFlags.Public)
                .ToDictionary(field => field.Name, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string column in header)
            {
                if (!seen.Add(column)) throw new ConfigException(module + ": duplicate column " + column);
                if (!fields.ContainsKey(column)) throw new ConfigException(module + ": unknown column " + column);
            }
            foreach (string name in fields.Keys)
                if (!seen.Contains(name)) throw new ConfigException(module + ": missing column " + name);
            var items = new JArray();
            for (int row = 1; row < rows.Count; row++)
            {
                if (rows[row].Length == 1 && rows[row][0].Length == 0) continue;
                if (rows[row].Length != header.Length)
                    throw new ConfigException(module + ": record " + (row + 1) + " has " + rows[row].Length + " cells, expected " + header.Length);
                var item = new JObject();
                for (int column = 0; column < header.Length; column++)
                {
                    string name = header[column];
                    string path = module + ": record " + (row + 1) + ", " + name;
                    item[name] = ParseCell(rows[row][column], fields[name].FieldType, name, path);
                }
                items.Add(item);
            }
            var envelope = new ModuleEnvelope {
                module = module, schemaVersion = schemaVersion, contentVersion = contentVersion, items = items
            };
            // Shared codec enforces nested types, unknown fields, numeric bounds and local semantics.
            return ConfigCodec.ParseModule(ConfigCodec.WriteModule(envelope));
        }

        private static JToken ParseCell(string cell, Type type, string name, string path)
        {
            if (cell == "null" && (name == "talentBoardId" || name == "startsAtUtc" || name == "endsAtUtc" || name == "pity"))
                return JValue.CreateNull();
            if (type == typeof(string)) return new JValue(cell);
            if (cell.Length == 0) throw new ConfigException(path + ": a typed cell cannot be empty; use [] for an empty array");
            if ((type == typeof(int) || type == typeof(long) || type == typeof(float))
                && !Regex.IsMatch(cell.Trim(), @"^-?(0|[1-9][0-9]*)(\.[0-9]+)?([eE][+-]?[0-9]+)?$"))
                throw new ConfigException(path + ": expected a JSON decimal number (no leading zeroes, hex, NaN or Infinity)");
            JToken token;
            try
            {
                using (var reader = new JsonTextReader(new StringReader(cell)))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    reader.MaxDepth = 64;
                    token = JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    if (reader.Read()) throw new ConfigException(path + ": trailing JSON content");
                }
            }
            catch (JsonException ex) { throw new ConfigException(path + ": invalid JSON cell: " + ex.Message); }
            if ((type == typeof(int) || type == typeof(long)) && token.Type != JTokenType.Integer)
                throw new ConfigException(path + ": expected integer, not a quoted number or fraction");
            if (type == typeof(float) && token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
                throw new ConfigException(path + ": expected numeric JSON value");
            if (type == typeof(bool) && token.Type != JTokenType.Boolean)
                throw new ConfigException(path + ": expected true or false");
            if (type.IsArray && token.Type != JTokenType.Array)
                throw new ConfigException(path + ": expected JSON array");
            if (!type.IsPrimitive && !type.IsArray && token.Type != JTokenType.Object)
                throw new ConfigException(path + ": expected JSON object");
            return token;
        }

        public static List<string[]> ReadRows(string csv)
        {
            if (csv == null) throw new ArgumentNullException(nameof(csv));
            var result = new List<string[]>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool inQuotes = false, afterQuote = false, hasRecord = false;
            int line = 1;
            int start = csv.Length > 0 && csv[0] == '\uFEFF' ? 1 : 0;
            for (int i = start; i < csv.Length; i++)
            {
                char ch = csv[i];
                if (inQuotes)
                {
                    if (ch == '"')
                    {
                        if (i + 1 < csv.Length && csv[i + 1] == '"') { cell.Append('"'); i++; }
                        else { inQuotes = false; afterQuote = true; }
                    }
                    else { cell.Append(ch); if (ch == '\n') line++; }
                    continue;
                }
                if (afterQuote && ch != ',' && ch != '\r' && ch != '\n')
                    throw new ConfigException("CSV line " + line + ": unexpected text after closing quote");
                if (ch == '"')
                {
                    if (cell.Length != 0) throw new ConfigException("CSV line " + line + ": quote inside an unquoted field");
                    inQuotes = true; hasRecord = true;
                }
                else if (ch == ',')
                {
                    row.Add(cell.ToString()); cell.Clear(); afterQuote = false; hasRecord = true;
                }
                else if (ch == '\r' || ch == '\n')
                {
                    if (ch == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n') i++;
                    row.Add(cell.ToString()); result.Add(row.ToArray());
                    row.Clear(); cell.Clear(); afterQuote = false; hasRecord = false; line++;
                }
                else { cell.Append(ch); hasRecord = true; }
            }
            if (inQuotes) throw new ConfigException("CSV line " + line + ": unterminated quoted field");
            if (hasRecord || cell.Length > 0 || row.Count > 0)
            { row.Add(cell.ToString()); result.Add(row.ToArray()); }
            return result;
        }
    }
}

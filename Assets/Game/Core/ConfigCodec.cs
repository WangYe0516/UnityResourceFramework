using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ResourceFramework
{
    public static class ConfigCodec
    {
        private static readonly Dictionary<string, Type> Types = new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            {"character", typeof(CharacterData)}, {"equipment", typeof(EquipmentData)},
            {"ability", typeof(AbilityData)}, {"talent_board", typeof(TalentBoardData)},
            {"perfume", typeof(PerfumeModifierData)}, {"material", typeof(MaterialData)},
            {"recipe", typeof(RecipeData)}, {"currency", typeof(CurrencyData)}, {"gacha", typeof(GachaPoolData)}
        };
        public static readonly string[] SupportedCapabilities = {
            "ability_descriptors", "perfume_descriptors", "fixed_craft_outputs", "weighted_gacha", "hard_pity"
        };
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings {
            MissingMemberHandling = MissingMemberHandling.Error, TypeNameHandling = TypeNameHandling.None,
            Culture = CultureInfo.InvariantCulture, DateParseHandling = DateParseHandling.None,
            MaxDepth = 64
        };
        public static string[] ModuleNames { get { return Types.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray(); } }
        public static Type TypeFor(string module)
        {
            if (module == null || !Types.TryGetValue(module, out Type type))
                throw new ConfigException("Unknown module: " + module);
            return type;
        }
        public static void RegisterModule<T>(string module) where T : ResourceData
        {
            if (Types.ContainsKey(module)) throw new ConfigException("Duplicate module registration: " + module);
            Types.Add(module, typeof(T));
        }
        public static JObject ParseObject(string json)
        {
            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    reader.MaxDepth = 64;
                    var result = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    if (reader.Read()) throw new ConfigException("Trailing JSON content");
                    return result;
                }
            }
            catch (JsonException ex) { throw new ConfigException("Invalid JSON: " + ex.Message); }
        }
        public static ModuleEnvelope ParseModule(string json)
        {
            JObject obj = ParseObject(json);
            CheckProperties(obj, new[] {"module", "schemaVersion", "contentVersion", "items"}, "module");
            RequireType(obj["module"], JTokenType.String, "module");
            RequireType(obj["schemaVersion"], JTokenType.Integer, "schemaVersion");
            RequireType(obj["contentVersion"], JTokenType.String, "contentVersion");
            RequireType(obj["items"], JTokenType.Array, "items");
            var envelope = obj.ToObject<ModuleEnvelope>(JsonSerializer.Create(Settings));
            if (envelope.schemaVersion != 1) throw new ConfigException("Unsupported schemaVersion: " + envelope.schemaVersion);
            if (string.IsNullOrWhiteSpace(envelope.contentVersion)) throw new ConfigException("Empty contentVersion");
            Type type = TypeFor(envelope.module);
            foreach (JToken row in envelope.items)
            {
                ValidateShape(row, type, envelope.module);
                ResourceValidator.ValidateLocal(ReadRow(row, type), envelope.module);
            }
            return envelope;
        }
        public static ConfigCollection ParseCollection(string json)
        {
            JObject obj = ParseObject(json);
            CheckProperties(obj, new[] {"format", "contentVersion", "requiredCapabilities", "modules", "demo"}, "collection");
            RequireType(obj["modules"], JTokenType.Array, "modules");
            RequireType(obj["contentVersion"], JTokenType.String, "contentVersion");
            RequireType(obj["requiredCapabilities"], JTokenType.Array, "requiredCapabilities");
            foreach (JToken capability in (JArray)obj["requiredCapabilities"])
            {
                RequireType(capability, JTokenType.String, "requiredCapabilities[]");
                if (!SupportedCapabilities.Contains((string)capability))
                    throw new ConfigException("Unsupported capability: " + capability);
            }
            var result = obj.ToObject<ConfigCollection>(JsonSerializer.Create(Settings));
            result.modules = ((JArray)obj["modules"]).Select(x => ParseModule(x.ToString(Formatting.None))).ToArray();
            if (result.modules.Length == 0 || result.modules.Any(x => x.contentVersion != result.contentVersion))
                throw new ConfigException("Collection version mismatch or empty modules");
            return result;
        }
        public static string WriteModule(ModuleEnvelope module)
        { return JsonConvert.SerializeObject(module, Formatting.Indented, Settings); }
        internal static ResourceData ReadRow(JToken row, Type type)
        {
            try { return (ResourceData)row.ToObject(type, JsonSerializer.Create(Settings)); }
            catch (Exception ex) when (ex is JsonException || ex is OverflowException)
            { throw new ConfigException("Invalid row: " + ex.Message); }
        }
        internal static T Clone<T>(T value)
        { return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value, Settings), Settings); }
        private static void CheckProperties(JObject obj, IEnumerable<string> names, string path)
        {
            var allowed = new HashSet<string>(names, StringComparer.Ordinal);
            foreach (JProperty property in obj.Properties())
                if (!allowed.Contains(property.Name)) throw new ConfigException(path + "." + property.Name + ": unknown field");
        }
        private static void RequireType(JToken value, JTokenType type, string path)
        { if (value == null || value.Type != type) throw new ConfigException(path + ": expected " + type); }
        internal static void ValidateShape(JToken token, Type type, string path)
        {
            if (token == null || token.Type == JTokenType.Null)
            {
                if (path.EndsWith(".pity", StringComparison.Ordinal) || path.EndsWith(".talentBoardId", StringComparison.Ordinal)
                    || path.EndsWith(".startsAtUtc", StringComparison.Ordinal) || path.EndsWith(".endsAtUtc", StringComparison.Ordinal)) return;
                throw new ConfigException(path + ": null/missing field");
            }
            if (type == typeof(string)) { RequireType(token, JTokenType.String, path); return; }
            if (type == typeof(bool)) { RequireType(token, JTokenType.Boolean, path); return; }
            if (type == typeof(int) || type == typeof(long))
            {
                RequireType(token, JTokenType.Integer, path);
                try { if (type == typeof(int)) { int v = token.Value<int>(); } else { long v = token.Value<long>(); } }
                catch (Exception) { throw new ConfigException(path + ": integer overflow"); }
                return;
            }
            if (type == typeof(float))
            {
                if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float) throw new ConfigException(path + ": expected number");
                double v = token.Value<double>();
                if (double.IsNaN(v) || double.IsInfinity(v) || v > float.MaxValue || v < -float.MaxValue)
                    throw new ConfigException(path + ": non-finite/out-of-range number");
                return;
            }
            if (type.IsArray)
            {
                RequireType(token, JTokenType.Array, path);
                int i = 0;
                foreach (JToken item in (JArray)token) ValidateShape(item, type.GetElementType(), path + "[" + i++ + "]");
                return;
            }
            RequireType(token, JTokenType.Object, path);
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public);
            CheckProperties((JObject)token, fields.Select(x => x.Name), path);
            foreach (FieldInfo field in fields)
            {
                if (token[field.Name] == null && field.DeclaringType == typeof(ResourceData)
                    && field.Name != "id" && field.Name != "name") continue;
                ValidateShape(token[field.Name], field.FieldType, path + "." + field.Name);
            }
        }
        public static JObject ExportRowSchema(string module)
        {
            var schema = ShapeSchema(TypeFor(module));
            schema["$schema"] = "https://json-schema.org/draft/2020-12/schema";
            schema["title"] = module;
            return schema;
        }
        private static JObject ShapeSchema(Type type)
        {
            if (type == typeof(string)) return new JObject { ["type"] = "string" };
            if (type == typeof(bool)) return new JObject { ["type"] = "boolean" };
            if (type == typeof(int)) return new JObject { ["type"] = "integer", ["minimum"] = int.MinValue, ["maximum"] = int.MaxValue };
            if (type == typeof(long)) return new JObject { ["type"] = "integer", ["minimum"] = -ResourceValidator.MaxAmount, ["maximum"] = ResourceValidator.MaxAmount };
            if (type == typeof(float)) return new JObject { ["type"] = "number", ["minimum"] = -float.MaxValue, ["maximum"] = float.MaxValue };
            if (type.IsArray) return new JObject { ["type"] = "array", ["items"] = ShapeSchema(type.GetElementType()) };
            var properties = new JObject(); var required = new JArray();
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                JObject property = ShapeSchema(field.FieldType);
                if (new[] {"pity", "talentBoardId", "startsAtUtc", "endsAtUtc"}.Contains(field.Name))
                    property = new JObject { ["anyOf"] = new JArray(property, new JObject { ["type"] = "null" }) };
                properties[field.Name] = property;
                if (field.DeclaringType != typeof(ResourceData) || field.Name == "id" || field.Name == "name") required.Add(field.Name);
            }
            return new JObject { ["type"] = "object", ["additionalProperties"] = false, ["required"] = required, ["properties"] = properties };
        }
    }
}

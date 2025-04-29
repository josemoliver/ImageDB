using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;

namespace ImageDB
{
    public class JsonConverter
    {
        public static string ConvertNumericAndBooleanValuesToString(string json)
        {
            // Parse the JSON document
            JsonDocument doc = JsonDocument.Parse(json);
            var rootElement = doc.RootElement;

            // Recursively convert all numeric and boolean values to strings
            JsonElement transformedElement = TransformJsonElement(rootElement);

            // Serialize the modified JSON to a human-readable string with UTF-8 characters
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // Allow UTF-8 characters
            };

            return JsonSerializer.Serialize(transformedElement, options);
        }

        private static JsonElement TransformJsonElement(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var objectDict = new System.Collections.Generic.Dictionary<string, JsonElement>();
                    foreach (var property in element.EnumerateObject())
                    {
                        objectDict[property.Name] = TransformJsonElement(property.Value);
                    }
                    return JsonDocument.Parse(JsonSerializer.Serialize(objectDict)).RootElement;
                case JsonValueKind.Array:
                    var arrayList = new System.Collections.Generic.List<JsonElement>();
                    foreach (var item in element.EnumerateArray())
                    {
                        arrayList.Add(TransformJsonElement(item));
                    }
                    return JsonDocument.Parse(JsonSerializer.Serialize(arrayList)).RootElement;
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return JsonDocument.Parse($"\"{element.ToString()}\"").RootElement; // Convert numeric and boolean to string
                default:
                    return element; // For other types (null, string, etc.), return as is
            }
        }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace GrevHome.Storage;

/// <summary>
/// Shared System.Text.Json option presets. Services previously constructed their own
/// equivalent <see cref="JsonSerializerOptions"/> instance; centralizing them here avoids
/// redeclaring the same settings in every file and matches the framework guidance to
/// reuse a single cached instance per configuration rather than allocate one per type.
/// </summary>
internal static class JsonDefaults
{
    /// <summary>Indented output with otherwise default settings.</summary>
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true
    };

    /// <summary>Indented output built on <see cref="JsonSerializerDefaults.Web"/> (camelCase, case-insensitive reads).</summary>
    public static readonly JsonSerializerOptions IndentedWeb = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <summary>Indented output that serializes enums as their string names instead of numbers.</summary>
    public static readonly JsonSerializerOptions IndentedWithStringEnums = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) }
    };
}

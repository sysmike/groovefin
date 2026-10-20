using System.Text.Json.Serialization;

namespace Groovefin.Core.Session;

/// <summary>Source-generated JSON metadata for the session file.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SavedSession))]
internal sealed partial class SessionJsonContext : JsonSerializerContext;

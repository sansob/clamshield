using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Clamshield.Models;

namespace Clamshield.Services;

/// <summary>Output of <c>--cli --json</c>. Property names are part of the command line contract.</summary>
public sealed record CliThreat([property: JsonPropertyName("path")] string Path, [property: JsonPropertyName("virus")] string Virus);
public sealed record CliSuspicious([property: JsonPropertyName("Pid")] int Pid, [property: JsonPropertyName("Name")] string Name, [property: JsonPropertyName("Path")] string Path, [property: JsonPropertyName("warning")] string? Warning);
public sealed record CliResult(
    [property: JsonPropertyName("scanned")] int Scanned,
    [property: JsonPropertyName("infected")] int Infected,
    [property: JsonPropertyName("seconds")] double Seconds,
    [property: JsonPropertyName("threats")] List<CliThreat> Threats,
    [property: JsonPropertyName("suspiciousPrograms")] List<CliSuspicious>? SuspiciousPrograms);

/// <summary>
/// Generated JSON code for everything the app stores. Reflection-based serialization does not survive
/// native AOT, so every stored type must be listed here.
/// </summary>
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(List<ScanRecord>))]
[JsonSerializable(typeof(List<QuarantineItem>))]
[JsonSerializable(typeof(Blocklist.Store), TypeInfoPropertyName = "BlocklistStore")]
[JsonSerializable(typeof(TrustedFiles.Store), TypeInfoPropertyName = "TrustedStore")]
[JsonSerializable(typeof(PersistenceWatch.BaselineData))]
[JsonSerializable(typeof(CliResult))]
internal sealed partial class AppJson : JsonSerializerContext
{
    public static JsonTypeInfo<T> Info<T>() => (JsonTypeInfo<T>)Default.GetTypeInfo(typeof(T))!;
}

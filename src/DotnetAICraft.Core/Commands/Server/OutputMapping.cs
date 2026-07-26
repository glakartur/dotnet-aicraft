using System.Text.Json;
using DotnetAICraft.Models;
using DotnetAICraft.Output;

namespace DotnetAICraft.Commands.Server;

internal static class OutputMapping
{
    internal static void WriteError(ErrorInfo? error, OutputFormat format = OutputFormat.Text, string fallbackCode = "UNKNOWN_ERROR", string fallbackMessage = "Unknown daemon error.")
    {
        var code = error?.Code ?? fallbackCode;
        var message = error?.Message ?? fallbackMessage;
        var details = error?.Details;

        if (format == OutputFormat.Json)
            JsonOutput.WriteError(code, message, details);
        else
            TextOutput.WriteError(code, message, details);
    }

    internal static void Write(object? value, OutputFormat format = OutputFormat.Text)
    {
        if (format == OutputFormat.Json)
        {
            JsonOutput.Write(value);
            return;
        }

        // For text format, try to interpret common shapes.
        if (value is null)
            return;

        // Try JSON payload shapes returned by daemon responses first.
        if (value is JsonElement el)
        {
            if (TryWriteKnownTextShape(el))
                return;

            // Fallback: emit as JSON.
            JsonOutput.Write(value);
            return;
        }

        // Anonymous fallback objects (e.g. { running = false, solutionPath }): serialize then re-parse.
        try
        {
            var json = JsonOutput.Serialize(value);
            using var doc = JsonDocument.Parse(json);
            var elClone = doc.RootElement.Clone();
            if (TryWriteKnownTextShape(elClone))
                return;

            // Fallback: emit raw JSON.
            JsonOutput.Write(value);
        }
        catch
        {
            JsonOutput.Write(value);
        }
    }

    private static bool TryWriteKnownTextShape(JsonElement element)
    {
        var status = TryDeserialize<DaemonStatus>(element);
        if (status is not null && !string.IsNullOrEmpty(status.SolutionPath))
        {
            TextOutput.WriteServerStatus(status);
            return true;
        }

        if (TryGetBoolean(element, "shutdownInitiated", out var shutdownInitiated))
        {
            TextOutput.WriteServerStop(shutdownInitiated);
            return true;
        }

        return false;
    }

    private static bool TryGetBoolean(JsonElement element, string propertyName, out bool value)
    {
        value = false;
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property))
            return false;

        if (property.ValueKind == JsonValueKind.True)
        {
            value = true;
            return true;
        }

        if (property.ValueKind == JsonValueKind.False)
            return true;

        return false;
    }

    private static T? TryDeserialize<T>(JsonElement element)
    {
        try
        {
            return JsonOutput.Deserialize<T>(element);
        }
        catch
        {
            return default;
        }
    }
}

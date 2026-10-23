using System.Globalization;
using System.Text.Json.Nodes;

namespace Groovefin.Core.Discord;

/// <summary>The JSON bodies Groovefin sends to Discord.</summary>
public static class DiscordMessages
{
    /// <summary>Discord's activity type for "Listening to …".</summary>
    public const int ListeningType = 2;

    /// <summary>The key of the Groovefin icon among the application's Rich Presence art assets.</summary>
    public const string IconAsset = "groovefin";

    /// <summary>
    /// Discord's <c>status_display_type</c> that shows <c>state</c> (the artists) in the member list's
    /// "Listening to …" instead of the application name.
    /// </summary>
    public const int StatusDisplayState = 1;

    /// <summary>Discord's limit for the text fields.</summary>
    public const int MaxTextLength = 128;

    public static string Handshake(string applicationId) =>
        new JsonObject { ["v"] = 1, ["client_id"] = applicationId }.ToJsonString();

    /// <summary>Sets the activity of process <paramref name="processId"/>; <see langword="null"/> clears it.</summary>
    public static string SetActivity(int processId, DiscordActivity? activity, string nonce)
    {
        var args = new JsonObject { ["pid"] = processId };
        if (activity is not null)
        {
            args["activity"] = Activity(activity);
        }

        return new JsonObject { ["cmd"] = "SET_ACTIVITY", ["args"] = args, ["nonce"] = nonce }.ToJsonString();
    }

    private static JsonObject Activity(DiscordActivity activity)
    {
        var json = new JsonObject { ["type"] = ListeningType, ["details"] = Fit(activity.Details) };
        if (!string.IsNullOrWhiteSpace(activity.State))
        {
            json["state"] = Fit(activity.State);
            json["status_display_type"] = StatusDisplayState;
        }

        if (activity.Start is { } start)
        {
            var timestamps = new JsonObject { ["start"] = start.ToUnixTimeMilliseconds() };
            if (activity.End is { } end)
            {
                timestamps["end"] = end.ToUnixTimeMilliseconds();
            }

            json["timestamps"] = timestamps;
        }

        var assets = new JsonObject { ["large_image"] = IconAsset };
        if (!string.IsNullOrWhiteSpace(activity.LargeText))
        {
            assets["large_text"] = Fit(activity.LargeText);
        }

        json["assets"] = assets;
        return json;
    }

    /// <summary>
    /// Discord takes 2 to 128 characters: longer text is cut with an ellipsis, a single character gets an
    /// invisible second one.
    /// </summary>
    internal static string Fit(string text)
    {
        var trimmed = text.Trim();
        var info = new StringInfo(trimmed);
        if (info.LengthInTextElements > MaxTextLength)
        {
            return info.SubstringByTextElements(0, MaxTextLength - 1) + "…";
        }

        return trimmed.Length < 2 ? trimmed + "​" : trimmed;
    }
}

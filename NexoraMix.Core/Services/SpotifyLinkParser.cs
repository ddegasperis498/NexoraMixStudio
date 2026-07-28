using System.Text.RegularExpressions;
using NexoraMix.Core.Models;

namespace NexoraMix.Core.Services;

public static partial class SpotifyLinkParser
{
    [GeneratedRegex(@"(?:https?://open\.spotify\.com/(?:intl-[a-z]{2}/)?|spotify:)(track|playlist|album)[/:]([A-Za-z0-9]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SpotifyRegex();

    public static bool TryParse(string? input, out SpotifyResource resource)
    {
        resource = new SpotifyResource(SpotifyResourceType.Unknown, string.Empty, string.Empty);
        if (string.IsNullOrWhiteSpace(input)) return false;

        var match = SpotifyRegex().Match(input.Trim());
        if (!match.Success) return false;

        var type = match.Groups[1].Value.ToLowerInvariant() switch
        {
            "track" => SpotifyResourceType.Track,
            "playlist" => SpotifyResourceType.Playlist,
            "album" => SpotifyResourceType.Album,
            _ => SpotifyResourceType.Unknown
        };

        var id = match.Groups[2].Value;
        resource = new SpotifyResource(type, id, $"https://open.spotify.com/{match.Groups[1].Value.ToLowerInvariant()}/{id}");
        return type != SpotifyResourceType.Unknown;
    }
}

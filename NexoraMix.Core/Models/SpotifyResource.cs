namespace NexoraMix.Core.Models;

public enum SpotifyResourceType
{
    Track,
    Playlist,
    Album,
    Unknown
}

public sealed record SpotifyResource(SpotifyResourceType Type, string Id, string CanonicalUrl);

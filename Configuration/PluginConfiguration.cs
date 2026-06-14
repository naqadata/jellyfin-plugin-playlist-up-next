using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.PlaylistUpNext.Configuration;

/// <summary>
/// Stores Playlist Up Next behavior settings.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the maximum number of playlist candidates returned by default.
    /// </summary>
    public int DefaultLimit { get; set; } = 20;

    /// <summary>
    /// Gets or sets a value indicating whether playlists with no watched progress should offer their first item.
    /// </summary>
    public bool IncludeUnstartedPlaylists { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether finished playlists should wrap back to the first item.
    /// </summary>
    public bool WrapAtEnd { get; set; }

    /// <summary>
    /// Gets or sets playlist names that should not be considered.
    /// </summary>
    public string[] ExcludedPlaylistNames { get; set; } = ["My List"];
}

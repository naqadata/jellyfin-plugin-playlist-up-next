using MediaBrowser.Model.Dto;

namespace Jellyfin.Plugin.PlaylistUpNext.Models;

/// <summary>
/// Playlist-aware up-next entry with selection context.
/// </summary>
public class PlaylistUpNextEntryDto
{
    /// <summary>
    /// Gets or sets the playlist id.
    /// </summary>
    public Guid PlaylistId { get; set; }

    /// <summary>
    /// Gets or sets the playlist name.
    /// </summary>
    public string PlaylistName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the selected item's zero-based index in the playlist.
    /// </summary>
    public int PlaylistIndex { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the selected item is outside the playlist.
    /// </summary>
    public bool IsExternalResumeItem { get; set; }

    /// <summary>
    /// Gets or sets the playlist's unplayed item count.
    /// </summary>
    public int UnplayedItemCount { get; set; }

    /// <summary>
    /// Gets or sets the reason this item was selected.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the date of the playlist progress that caused this selection.
    /// </summary>
    public DateTime? LastPlayedDate { get; set; }

    /// <summary>
    /// Gets or sets the selected item.
    /// </summary>
    public BaseItemDto Item { get; set; } = new();
}

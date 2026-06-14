using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.PlaylistUpNext.Models;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.PlaylistUpNext.Controllers;

/// <summary>
/// Provides playlist-ordered up-next candidates.
/// </summary>
[ApiController]
[Route("[controller]")]
[Authorize]
public class PlaylistUpNextController : ControllerBase
{
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly IPlaylistManager _playlistManager;
    private readonly ILibraryManager _libraryManager;
    private readonly IDtoService _dtoService;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaylistUpNextController"/> class.
    /// </summary>
    public PlaylistUpNextController(
        IUserManager userManager,
        IUserDataManager userDataManager,
        IPlaylistManager playlistManager,
        ILibraryManager libraryManager,
        IDtoService dtoService)
    {
        _userManager = userManager;
        _userDataManager = userDataManager;
        _playlistManager = playlistManager;
        _libraryManager = libraryManager;
        _dtoService = dtoService;
    }

    /// <summary>
    /// Gets one playlist-ordered up-next item per playlist.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="limit">Maximum number of items to return.</param>
    /// <param name="includeUnstarted">Whether playlists with no user progress should return their first item.</param>
    /// <param name="wrapAtEnd">Whether a finished playlist should wrap to its first item.</param>
    /// <returns>A Jellyfin item query result.</returns>
    [HttpGet("{userId}")]
    public ActionResult<QueryResult<BaseItemDto>> GetItems(
        Guid userId,
        [FromQuery] int? limit,
        [FromQuery] bool? includeUnstarted,
        [FromQuery] bool? wrapAtEnd)
    {
        User? user = _userManager.GetUserById(userId);
        if (user is null)
        {
            return NotFound();
        }

        IReadOnlyList<PlaylistUpNextEntryDto> entries = BuildEntries(user, limit, includeUnstarted, wrapAtEnd);
        return new QueryResult<BaseItemDto>(entries.Select(i => i.Item).ToArray());
    }

    /// <summary>
    /// Gets playlist-ordered up-next items with playlist context.
    /// </summary>
    /// <param name="userId">User id.</param>
    /// <param name="limit">Maximum number of items to return.</param>
    /// <param name="includeUnstarted">Whether playlists with no user progress should return their first item.</param>
    /// <param name="wrapAtEnd">Whether a finished playlist should wrap to its first item.</param>
    /// <returns>Playlist-aware up-next entries.</returns>
    [HttpGet("{userId}/Entries")]
    public ActionResult<QueryResult<PlaylistUpNextEntryDto>> GetEntries(
        Guid userId,
        [FromQuery] int? limit,
        [FromQuery] bool? includeUnstarted,
        [FromQuery] bool? wrapAtEnd)
    {
        User? user = _userManager.GetUserById(userId);
        if (user is null)
        {
            return NotFound();
        }

        IReadOnlyList<PlaylistUpNextEntryDto> entries = BuildEntries(user, limit, includeUnstarted, wrapAtEnd);
        return new QueryResult<PlaylistUpNextEntryDto>(entries);
    }

    private IReadOnlyList<PlaylistUpNextEntryDto> BuildEntries(
        User user,
        int? limit,
        bool? includeUnstarted,
        bool? wrapAtEnd)
    {
        var config = Plugin.Instance?.Configuration;
        int take = Math.Clamp(limit ?? config?.DefaultLimit ?? 20, 1, 100);
        bool offerUnstarted = includeUnstarted ?? config?.IncludeUnstartedPlaylists ?? false;
        bool wrapFinished = wrapAtEnd ?? config?.WrapAtEnd ?? false;
        HashSet<string> excludedNames = new(config?.ExcludedPlaylistNames ?? [], StringComparer.OrdinalIgnoreCase);
        DtoOptions dtoOptions = CreateDtoOptions();
        IReadOnlyList<PlaylistItemProgress> resumeItems = GetResumeItems(user);

        return _playlistManager.GetPlaylists(user.Id)
            .Where(i => !excludedNames.Contains(i.Name))
            .Select(i => TryCreateEntry(i, user, dtoOptions, resumeItems, offerUnstarted, wrapFinished))
            .Where(i => i is not null)
            .Select(i => i!)
            .OrderByDescending(i => i.LastPlayedDate ?? DateTime.MinValue)
            .ThenBy(i => i.PlaylistName, StringComparer.OrdinalIgnoreCase)
            .Take(take)
            .ToArray();
    }

    private PlaylistUpNextEntryDto? TryCreateEntry(
        Playlist playlist,
        User user,
        DtoOptions dtoOptions,
        IReadOnlyList<PlaylistItemProgress> resumeItems,
        bool includeUnstarted,
        bool wrapAtEnd)
    {
        IReadOnlyList<BaseItem> items = playlist
            .GetChildren(user, true, new InternalItemsQuery(user))
            .Where(i => !i.IsFolder && i.IsVisible(user))
            .ToArray();

        if (items.Count == 0)
        {
            return null;
        }

        List<PlaylistItemProgress> progress = items
            .Select((item, index) => new PlaylistItemProgress(
                item,
                index,
                _userDataManager.GetUserDataDto(item, user) ?? new UserItemDataDto { Key = item.Id.ToString("N") }))
            .ToList();

        int unplayedItemCount = progress.Count(i => !i.UserData.Played);

        PlaylistItemProgress? resume = progress
            .Where(i => i.UserData.PlaybackPositionTicks > 0 && !i.UserData.Played)
            .OrderByDescending(i => i.UserData.LastPlayedDate ?? DateTime.MinValue)
            .FirstOrDefault();

        if (resume is not null)
        {
            return CreateEntry(playlist, user, dtoOptions, resume, "resume-item", resume.UserData.LastPlayedDate, unplayedItemCount);
        }

        PlaylistItemProgress? anchor = progress
            .Where(i => i.UserData.Played)
            .OrderByDescending(i => i.UserData.LastPlayedDate ?? DateTime.MinValue)
            .FirstOrDefault();

        if (anchor is null)
        {
            return includeUnstarted ? CreateEntry(playlist, user, dtoOptions, progress[0], "first-unstarted", null, unplayedItemCount) : null;
        }

        PlaylistItemProgress? next = progress
            .Skip(anchor.Index + 1)
            .FirstOrDefault(i => !i.UserData.Played);

        if (next is not null)
        {
            PlaylistItemProgress? externalResume = resumeItems
                .Where(i => i.UserData.LastPlayedDate > (anchor.UserData.LastPlayedDate ?? DateTime.MinValue))
                .Where(i => SharesTitleFamily(i.Item, progress.Select(p => p.Item)))
                .FirstOrDefault();

            if (externalResume is not null)
            {
                return CreateEntry(playlist, user, dtoOptions, externalResume, "external-resume-item", externalResume.UserData.LastPlayedDate, unplayedItemCount, next.Index, true);
            }

            return CreateEntry(playlist, user, dtoOptions, next, "next-after-played", anchor.UserData.LastPlayedDate, unplayedItemCount);
        }

        return wrapAtEnd ? CreateEntry(playlist, user, dtoOptions, progress[0], "wrapped-to-start", anchor.UserData.LastPlayedDate, unplayedItemCount) : null;
    }

    private IReadOnlyList<PlaylistItemProgress> GetResumeItems(User user)
    {
        return _libraryManager
            .GetItemList(new InternalItemsQuery(user)
            {
                IsResumable = true,
                Recursive = true
            })
            .Where(i => !i.IsFolder && i.IsVisible(user))
            .Select(i => new PlaylistItemProgress(
                i,
                -1,
                _userDataManager.GetUserDataDto(i, user) ?? new UserItemDataDto { Key = i.Id.ToString("N") }))
            .Where(i => i.UserData.PlaybackPositionTicks > 0 && !i.UserData.Played)
            .OrderByDescending(i => i.UserData.LastPlayedDate ?? DateTime.MinValue)
            .ToArray();
    }

    private static bool SharesTitleFamily(BaseItem candidate, IEnumerable<BaseItem> playlistItems)
    {
        HashSet<string> candidateTokens = GetTitleFamilyTokens(candidate);
        if (candidateTokens.Count == 0)
        {
            return false;
        }

        return playlistItems.Any(i => candidateTokens.Intersect(GetTitleFamilyTokens(i)).Count() >= 2);
    }

    private static HashSet<string> GetTitleFamilyTokens(BaseItem item)
    {
        string title = item is Episode episode ? episode.SeriesName : item.Name;
        return title
            .Split([' ', ':', '-', '_', '.', ',', '(', ')'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(i => i.ToLowerInvariant())
            .Where(i => i.Length > 2)
            .Where(i => i is not "the" and not "and" and not "season" and not "episode")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private PlaylistUpNextEntryDto CreateEntry(
        Playlist playlist,
        User user,
        DtoOptions dtoOptions,
        PlaylistItemProgress progress,
        string reason,
        DateTime? lastPlayedDate,
        int unplayedItemCount,
        int? playlistIndex = null,
        bool isExternalResumeItem = false)
    {
        return new PlaylistUpNextEntryDto
        {
            PlaylistId = playlist.Id,
            PlaylistName = playlist.Name,
            PlaylistIndex = playlistIndex ?? progress.Index,
            IsExternalResumeItem = isExternalResumeItem,
            UnplayedItemCount = unplayedItemCount,
            Reason = reason,
            LastPlayedDate = lastPlayedDate,
            Item = _dtoService.GetBaseItemDto(progress.Item, dtoOptions, user)
        };
    }

    private static DtoOptions CreateDtoOptions()
    {
        return new DtoOptions
        {
            Fields =
            [
                ItemFields.PrimaryImageAspectRatio,
                ItemFields.MediaSourceCount,
                ItemFields.Overview,
                ItemFields.ParentId,
                ItemFields.PlayAccess
            ],
            ImageTypeLimit = 1,
            ImageTypes =
            [
                ImageType.Thumb,
                ImageType.Backdrop,
                ImageType.Primary
            ]
        };
    }

    private sealed record PlaylistItemProgress(BaseItem Item, int Index, UserItemDataDto UserData);
}

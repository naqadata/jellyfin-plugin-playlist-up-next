# Jellyfin Playlist Up Next

Playlist Up Next is a Jellyfin server plugin that exposes playlist-ordered resume candidates.

It does not modify Jellyfin clients by itself. Stock Jellyfin Roku cannot render playlist-aware home content from a server plugin alone; the Roku app has to call this plugin's API. This plugin provides the server API a Roku fork, an upstream Roku PR, or a web-client customization can consume.

## Related Projects

- [Naqafin for Roku](https://github.com/naqadata/naqafin-roku): Roku client that consumes this plugin's `Playlist Up Next` endpoint.
- [Jellyfin Plugin Auto Generate Captions](https://github.com/naqadata/jellyfin-plugin-auto-generate-captions): separate companion plugin used by Naqafin for on-demand generated subtitle sessions.
- [Naqafin Caption Worker](https://github.com/naqadata/naqafin-caption-worker): optional CUDA worker for the auto-generated captions plugin.

## Client Support

This plugin is designed to work with [Naqafin for Roku](https://github.com/naqadata/naqafin-roku), an unofficial Roku client forked from the official Jellyfin Roku client.

Naqafin currently consumes this endpoint by blending playlist candidates into its existing Continue Watching and Next Up home rows, with playlist labels and playlist-aware playback. It no longer adds a dedicated `Playlist Up Next` row.

Stock Jellyfin Roku cannot display this plugin's playlist candidates by installing only this server plugin. Until equivalent support is accepted upstream or implemented by another client, Naqafin is the intended Roku client for this plugin.

This plugin is independent from the generated-caption stack. It can be installed alongside [Jellyfin Plugin Auto Generate Captions](https://github.com/naqadata/jellyfin-plugin-auto-generate-captions), but neither plugin requires the other.

## Behavior

For each playlist visible to the user, the plugin scans items in playlist order and picks one candidate:

- If the user has any partially watched item in the playlist, return the most recently played partial item.
- Otherwise, find the most recently played item in the playlist and return the next later unplayed item.
- If the user's most recent resumable item is newer than the playlist's own progress, return that resumable item and keep the playlist index pointed at the next playlist item.
- Optionally include the first item from unstarted playlists.
- Optionally wrap a finished playlist back to the first item.

The plugin returns at most one item per playlist and sorts playlists by most recent progress. The `/Entries` endpoint also returns the selected item's zero-based playlist index, playback state, selection reason, unplayed item count, and whether the selected item is an external resume item so clients can play the resume item first and then continue from the playlist queue.

`PlaybackState` is the client-facing routing field:

- `resume`: the playlist has an in-progress item or a newer matching external resume item.
- `next`: the playlist has no in-progress item and the selected item is the next thing to watch.

`Reason` gives a more specific diagnostic value such as `resume-item`, `external-resume-item`, or `next-after-played`.

## API

Return normal Jellyfin item DTOs:

```text
GET /PlaylistUpNext/{userId}?limit=20
```

Return item DTOs with playlist context:

```text
GET /PlaylistUpNext/{userId}/Entries?limit=20
```

Useful query parameters:

- `limit`: maximum results, 1-100.
- `playbackState`: optional filter; valid values are `resume` and `next`.
- `includeUnstarted`: `true` to return the first item from playlists with no progress.
- `wrapAtEnd`: `true` to return the first item when a playlist is complete.

## Manual Install

Build and copy the DLL to Jellyfin's plugin directory:

```bash
dotnet build Jellyfin.Plugin.PlaylistUpNext.csproj -c Release
sudo mkdir -p /var/lib/jellyfin/plugins/PlaylistUpNext_0.1.0
sudo cp bin/Release/net9.0/Jellyfin.Plugin.PlaylistUpNext.dll /var/lib/jellyfin/plugins/PlaylistUpNext_0.1.0/
sudo chown -R jellyfin:jellyfin /var/lib/jellyfin/plugins/PlaylistUpNext_0.1.0
sudo systemctl restart jellyfin
```

If Jellyfin runs in Docker, copy the DLL into the container's mounted config path, usually:

```text
/config/plugins/PlaylistUpNext_0.1.0/Jellyfin.Plugin.PlaylistUpNext.dll
```

## GitHub Repository Install

Jellyfin plugin repositories are server-side. Roku does not install this plugin.

The manifest points to the packaged zip committed under `dist/` on the `main` branch.

In Jellyfin admin dashboard, add this plugin repository URL:

```text
https://raw.githubusercontent.com/naqadata/jellyfin-plugin-playlist-up-next/main/manifest.json
```

Then install the plugin from Jellyfin's plugin catalog and restart the server.

## Packaging

Create a new release package with an explicit version and changelog:

```bash
./scripts/package.sh 0.1.1 "Describe the release"
```

The script writes `dist/Jellyfin.Plugin.PlaylistUpNext_<version>.zip` and adds a matching `manifest.json` version entry with checksum and timestamp.

Release artifacts are treated as immutable once pushed. The script refuses to overwrite an existing zip or manifest version unless `--force` is passed, and it rejects versions lower than the latest manifest version.

## Roku

The official Roku app cannot be extended by a Jellyfin server plugin to show playlist-aware home content. To make this appear on Roku, one of these has to happen:

- Use [Naqafin for Roku](https://github.com/naqadata/naqafin-roku), which includes client support for this endpoint and folds results into Continue Watching / Next Up.
- Patch or fork the upstream Jellyfin Roku app to call `/PlaylistUpNext/{userId}` or `/PlaylistUpNext/{userId}/Entries` and render the returned candidates.
- Get that support accepted upstream in the official Roku client.
- Use a web-client-only plugin/theme approach, which helps browser/iOS/Android web-shell clients but not native Roku.

The implemented server endpoint is the piece a Roku client change would consume.

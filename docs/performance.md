# Performance

Groovefin has to stay smooth with large libraries (the target is 50,000 tracks). This page describes
how the app keeps the work small, how to measure it, and the measured results.

## How large libraries stay fast

**Paged loading.** No page loads a whole library. Lists ask the server for one page at a time and
load the next page when the user scrolls near the end:

| Page | Page size | View |
|---|---|---|
| Songs | 200 tracks (`SongsViewModel.PageSize`) | `ListView` with item recycling; rows are built once per container (`SongTableRow`) |
| Albums | 100 albums (`AlbumsViewModel.PageSize`) | `ItemsView` with a `UniformGridLayout` |
| Artists | 100 artists (`ArtistsViewModel.PageSize`) | `ItemsView` |

Sorting and filtering happen on the server, so the client never sorts 50,000 items.

**Virtualization.** All long lists are virtualized: only the visible containers (plus a small
buffer) exist, and they are recycled while scrolling. `Artwork` ignores results for a container that
was recycled while its image was loading.

**Images.**
- Covers are requested at the size they are shown (width buckets, `ImageRequest.BucketWidth`) and
  decoded at that size (`DecodePixelWidth`).
- Decoded bitmaps are kept in a 300-entry LRU cache in memory (`Artwork`).
- The files are kept in a disk cache with a size limit (Settings → Storage, default 500 MB), with
  least recently used files evicted first (`DiskImageCache`).

**Nothing blocks the UI thread.**
- Network calls are asynchronous everywhere.
- Settings, session and image cache files are read and written with asynchronous file APIs.
- The image cache runs even its hit path (file check and use timestamp) on the thread pool, and
  updates a file's timestamp at most once a minute.
- `Artwork` opens cached files with `FileRandomAccessStream.OpenAsync`.
- The only blocking waits are on closing the window: the session save and the last playback reports
  get at most 2 seconds each.

## Measuring

**Start-up and memory.** Every start writes a line like this to the log (Settings → Storage → Log
files → Open folder):

```
Start-up took 812 ms; working set 142 MB, managed heap 18 MB
```

It is measured from process start until the window is shown and the last session is restored.

**Scrolling.** With a large library, on the Songs page and on the Albums grid:

1. Scroll with the mouse wheel, drag the scroll bar from top to bottom, and press End and Home.
2. Watch for blank rows that stay blank, stutter, or a frozen window.
3. In Task Manager, check that memory levels off instead of growing with every screen of items.

For details, record a trace with **Windows Performance Recorder** (CPU usage, XAML frame analysis)
or use `dotnet-counters monitor -n Groovefin` for GC and allocation rates.

## Results

| Date | Version | Machine | Library | Start-up | Working set after start | Songs scroll | Album grid scroll |
|---|---|---|---|---|---|---|---|
| | | | | | | | |

Add a row for each release candidate. Use a release build (MSIX or the unpackaged ZIP), not a
debug build from Visual Studio.

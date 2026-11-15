# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A small Windows desktop app (WinUI 3, .NET 10, x64 only) that shows the live USD→MXN exchange rate, a two-way amount converter, and a 30-day sparkline. It is built **unpackaged and self-contained** (`WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`), so the output runs without installing the Windows App SDK runtime or MSIX.

## Commands

```sh
dotnet build                      # Debug build -> bin/Debug/net10.0-windows10.0.19041.0/win-x64/
dotnet run                        # build and launch
dotnet publish -c Release         # trimmed + ReadyToRun self-contained build -> bin/Release/.../win-x64/publish/
```

There are no tests, linters, or a solution file; the single `UsdMxn.csproj` is the whole project.

CI: `.github/workflows/ci.yml` runs the trimmed Release publish on pushes to `main` and on pull requests. There are no tests, so a passing publish is the whole check.

Releases: pushing a `v*` tag runs `.github/workflows/release.yml` (windows-latest), which publishes with `-p:Version=<tag without v>`, zips the output as `UsdMxn-<version>-win-x64.zip`, and creates a GitHub release. Running the workflow manually builds the zip as an artifact without releasing.

## Architecture

- `RateService.cs` — static, UI-free fetch layer returning an immutable `RateSnapshot`. It tries the Yahoo Finance chart endpoint first (live rate, previous close, ~1 month of daily closes; `Live = true`) and falls back to open.er-api.com (daily rate only, no history; `Live = false`). It returns `null` only when both fail. Previous close comes from Yahoo's `regularMarketChangePercent` when present, so the day change matches quote sites; the last close in the history is replaced by the live rate.
- `MainWindow.xaml.cs` — all UI logic in code-behind (no MVVM). Key behaviors spread across event handlers:
  - Refresh runs every 5 minutes on a `DispatcherQueueTimer`, guarded by `_busy`. The timer is **stopped while minimized**, and on restore or activation a refresh fires only if the last attempt is stale. When the network comes back, it retries only if the last attempt failed.
  - When a refresh gets only the daily fallback, it keeps the previous snapshot's `History`/`PrevClose` so the sparkline doesn't vanish.
  - The converter decides direction by `FocusState`: only the focused `TextBox` drives conversion, and programmatic writes land on the unfocused one. This is what stops the two `TextChanged` handlers from feeding back into each other.
  - The sparkline is a `Polyline` recomputed from `History` on every size change.
  - Custom title bar (`ExtendsContentIntoTitleBar` + `TitleBarArea`), Mica backdrop, and a DPI-scaled initial size of 420×580 via `GetDpiForWindow` P/Invoke. The window is forced out of maximized on first activation.

## Constraints to preserve

- **Low resource usage is a design goal.** The csproj turns off server/concurrent GC, TieredPGO, EventSource, metrics, and HTTP activity propagation. One shared `HttpClient` is used with no cookies. Don't add background work that runs while minimized.
- **Release builds are trimmed** (`PublishTrimmed`). JSON is parsed with the `JsonDocument` DOM on purpose; reflection-based `JsonSerializer.Deserialize<T>` would need a source-generated `JsonSerializerContext` to be trim-safe. Check any new dependency or reflection use against trimming.
- **`InvariantGlobalization=true`**: culture-specific formatting isn't available, so all number and time formatting uses `CultureInfo.InvariantCulture` (`Inv`). Keep doing that.
- The custom `CopyAppResourcesToPublish` target copies `UsdMxn.pri` and the `*.xbf` files into the publish folder. An unpackaged publish leaves them out and the app fails to load its XAML without them, so don't remove the target.
- `Assets\app.ico` must be copied to output, because it is loaded at runtime from `AppContext.BaseDirectory` for the window icon.

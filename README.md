# USD × MXN

A tiny Windows desktop app that shows the live US dollar → Mexican peso exchange rate.

- Large headline rate, change vs. previous close, and the inverse rate
- Two-way converter (type in either the USD or MXN box)
- 30-day sparkline with low/high range
- Auto-refresh every 5 minutes (paused while minimized), `F5` to refresh manually
- Mica backdrop and a compact custom title bar

Built with WinUI 3 (Windows App SDK) on .NET 10, tuned for low memory and CPU use.

## Data sources

1. [Yahoo Finance](https://finance.yahoo.com/quote/MXN=X/) chart API (`MXN=X`): live market rate plus daily history.
2. [ExchangeRate-API open access](https://www.exchangerate-api.com/docs/free) (`open.er-api.com`): daily rate, used as a fallback when the live feed is unavailable.

Rates are for information only.

## Download

Grab the latest `UsdMxn-<version>-win-x64.zip` from [Releases](https://github.com/alberto-sanro/UsdMxn/releases), unzip, and run `UsdMxn.exe`.

## Requirements

- Windows 10 version 1809 (build 17763) or later, x64
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build

## Build and run

```sh
dotnet build                 # Debug build
dotnet run                   # build and launch
dotnet publish -c Release    # trimmed, ReadyToRun build
```

The Release publish output (`bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/`) is self-contained and unpackaged. Copy the folder anywhere and run `UsdMxn.exe`; nothing needs to be installed on the target machine.

## Releasing

Push a version tag and the [Release workflow](.github/workflows/release.yml) builds the zip and publishes a GitHub release with generated notes (tags with a suffix, like `v1.1.0-beta`, become prereleases):

```sh
git tag v1.0.0
git push origin v1.0.0
```

## License

[MIT](LICENSE)

# Zero Breach for Windows

An independent English desktop interface for [interplains/zero-breach](https://github.com/interplains/zero-breach), for people who prefer a window to a command line.

## Use

Download **[ZeroBreach.exe](https://github.com/michal78/zero-breach-gui/releases/latest/download/ZeroBreach.exe)** from the latest release and place it in a writable local folder. Open it, close your browsers if possible, choose a standard or quick scan, and select **Start scan**. Python, .NET and administrator access are not required.

The application checks GitHub for the latest stable official scanner, verifies the publisher's detached signatures and signed SHA-256 manifest, then scans the current Windows account. Progress shows real stages rather than an estimated percentage. You can cancel a scan.

Results explain findings, evidence strength, next steps, coverage and gaps. **No known indicators found** does not prove a device is free of spyware. This is a targeted indicator check, not a complete antivirus or forensic examination. Optional upstream pattern-engine checks are not included.

If an update fails, an explicitly labelled saved-version action is available only when a cached scanner passes verification. The application never silently substitutes an older or unverified download. A new upstream report format may require a GUI update; an incompatible report is an error, never a clean result.

## Files and privacy

Only the executable is distributed. At run time, it creates `ZeroBreach-data` beside itself for the extracted Python runtime, verified scanner cache, and instance lock. The .NET host may also extract native runtime components into its normal temporary cache. Move the executable to a writable local folder if its current folder cannot hold application data.

Scan results stay in memory unless you save a report. Temporary run files are removed after completion or cancellation; a cleanup failure displays the retained location. A previous interrupted run may leave temporary files, which the application reports at the next startup. Closing the application during a run first cancels the scanner.

Readable exports defang matched indicators. **Original JSON preserves upstream raw values**, including indicator addresses, usernames, local paths and browser-profile details. Share reports securely and never visit indicator addresses to investigate them.

Official downloads use network access; scan data is not uploaded. The scanner's local socket guard is not an operating-system network sandbox. Findings are not removed or repaired automatically. A credible targeting concern warrants qualified security help regardless of the result.

## Build

Windows x64, PowerShell, and the .NET SDK specified in `global.json` are required to build. They are not required by the end user.

```powershell
./scripts/Build-Portable.ps1
```

The build script obtains a pinned official CPython 3.13.16 embeddable archive, validates its SHA-256, restores locked dependencies, runs tests, and publishes **artifacts/ZeroBreach.exe** as one self-contained file. Required licences, the trusted public key and runtime are embedded. **About & notices** shows third-party notices in the app.

For an SDK installed outside PATH:

```powershell
./scripts/Build-Portable.ps1 -DotNet C:/path/to/dotnet.exe
```

Tests:

```powershell
dotnet test tests/ZeroBreach.Tests
```

Explicit live integration check (downloads and runs the official scanner on the current account; does not retain or print raw reports):

```powershell
dotnet run --project tools/ZeroBreach.Smoke -- .tools/smoke/ZeroBreach-data
dotnet run --project tools/ZeroBreach.Smoke -- .tools/smoke/ZeroBreach-data --full
```

The GUI executable is initially unsigned. Publisher signature verification applies to the downloaded scanner, not the GUI. A trusted signing certificate can be added to the release process separately.

## Automated builds and releases

The GitHub Action **Windows build and release** builds and tests pull requests to `main`. Every push to `main`, including a merged pull request, builds and tests on Windows, then publishes a release containing **ZeroBreach.exe** and **SHA256SUMS.txt**. A failed build or test prevents publication. The repository is public, so published releases are public too.

Versions use the project's major/minor version and its base patch plus the workflow run number (for example `v0.1.3`). Pull-request runs may leave gaps between published version numbers. Rerunning a workflow keeps its version and never replaces a published executable with different bytes. Release tags point to the exact tested commit. A draft is used while uploading, then published after all files are present. Publication is queued one release at a time, so overlapping builds cannot move the latest download backwards.

The workflow uses GitHub's built-in token; no personal access token is needed. Only the release job has repository write permission. The **Run workflow** button performs a build with a downloadable artifact without creating a release.

Offline release regression tests execute the actual publication script with a simulated GitHub CLI, without creating tags or releases:

```powershell
python -m unittest discover -s tests/workflows -v
```

These development tests require Python and Bash (Git for Windows provides Bash); neither is an extra end-user requirement.

## Trust and attribution

Trusted scanner signing-key fingerprint: `C24E C92B 12D6 670A 2516 065F 9B4D 5754 99AF A53C`. See `assets/trust/PROVENANCE.md`, `assets/runtime/PROVENANCE.md`, and `THIRD-PARTY-NOTICES.md`.

The scanner is Apache-2.0; its indicator data is CC0. This GUI is not endorsed by the upstream project, its publisher, or the organisations whose research it cites.

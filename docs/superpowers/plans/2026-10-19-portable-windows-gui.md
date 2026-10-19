# Zero Breach Portable Windows GUI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a portable, English Windows GUI that verifies the latest official Zero Breach scanner, runs it locally, and explains its results faithfully.

**Architecture:** A WPF desktop application orchestrates a release service, an isolated scan runner, a strict report interpreter, and an export service. A self-contained .NET single-file executable embeds the CPython runtime, trust assets, and notices. It extracts the runtime locally and downloads verified scanners at run time. Signature verification uses a managed OpenPGP library with an embedded trusted public key, avoiding a user-installed GnuPG dependency.

**Tech Stack:** C#, WPF, .NET 10 LTS, CPython embeddable x64, BouncyCastle.Cryptography, xUnit, PowerShell packaging.

**Spec:** `docs/superpowers/specs/2026-10-19-portable-windows-gui-design.md`

## Global Constraints

- All application labels, status messages, errors, explanations, and generated report headings must be English.
- Target Windows 10 and Windows 11, x64.
- Installation, administrator privileges, and a separately installed Python or .NET runtime must not be required.
- Run the unmodified official scanner with the bundled Python runtime.
- Reverify cached bytes before execution; never execute unsigned or incorrectly signed downloads.
- No automatic scan on application launch, telemetry, remediation, elevation, or automatic scan-history retention.
- Do not represent a scan with missing checks, malformed output, cancellation, or process failure as proof of safety.
- Preserve upstream evidence strength and indicator roles; defang suspicious addresses.
- Create and push project source to a private GitHub repository. Do not make the repository public, publish a public release, or create an upstream pull request.
- Deliver one `ZeroBreach.exe`, with no companion files required from the user. Runtime extraction and downloaded scanner files at run time are allowed.

## Review Focus

1. A valid signature from the wrong key must be rejected, including when a release supplies a replacement public key.
2. A hash-valid cached script whose signature has been removed or modified must never execute.
3. A future or malformed report must fail clearly instead of displaying zero findings.
4. An interrupted download or cancellation must leave the last verified cache intact and must release the portable-directory lock.
5. Unicode paths, locked report files, and a read-only application folder must produce useful English errors without losing the user's exported report.

## File Structure

- `src/ZeroBreach.Core/Updates/`: release discovery, bounded HTTPS downloads, OpenPGP trust, and atomic verified cache.
- `src/ZeroBreach.Core/Scanning/`: isolated process launch, cancellation, report handling, and temporary-file cleanup.
- `src/ZeroBreach.Core/Reports/`: parsed report model, validation, evidence interpretation, defanging, and readable exports.
- `src/ZeroBreach.App/`: WPF window, view model, English copy, application lifetime, and portable storage lock.
- `tests/ZeroBreach.Tests/`: security, report, process, and update integration tests, with synthetic fixtures.
- `assets/trust/`: pinned release public key and its provenance.
- `scripts/Build-Portable.ps1`: repeatable runtime acquisition, embedding, self-contained single-file publishing, and notice inclusion.
- `README.md`, `THIRD-PARTY-NOTICES.md`, `.gitignore`: user/build instructions, attribution, and generated-file exclusions.

## Task 1: Report contract and faithful interpretation

**Files:** `src/ZeroBreach.Core/Reports/{ScanReport,ReportReader,EvidencePresenter,ReportExporter}.cs`; `tests/ZeroBreach.Tests/ReportTests.cs`; `tests/ZeroBreach.Tests/Fixtures/`.

**Interfaces:** `ReportReader.Parse(string json, int exitCode) -> ScanReport`; `EvidencePresenter.Present(ScanReport report) -> ReportPresentation`; `ReportExporter.ToText(ScanReport report) -> string`. `ScanReport` retains original JSON, scanner version, scan timestamp, findings, checked areas, and coverage gaps. `ReportPresentation` provides English summary, evidence cards, gaps, and next steps.

- [ ] Read the released scanner's report-writing code and capture its actual JSON contract; create minimal synthetic fixtures matching that contract, without copying private local reports into tests.
- [ ] Write failing tests: no-match output contains `No known indicators found` and a limitations caveat; a driver-only or referenced-domain fixture never claims infection; spyware-file and persistence fixtures retain stronger evidence; gaps remain visible; addresses are defanged.
- [ ] Write failing tests that reject truncated JSON, missing essential fields, inconsistent exit codes, and an incompatible schema. Unknown optional evidence values retain neutral original detail.
- [ ] Run the report tests and observe the missing behaviours fail.
- [ ] Implement the parser, presentation model, and readable export against the observed upstream contract.
- [ ] Run the report tests and confirm all pass.

## Task 2: Authenticated release updates and cache

**Files:** `src/ZeroBreach.Core/Updates/{ReleaseClient,SignatureVerifier,VerifiedCache}.cs`; `assets/trust/`; `tests/ZeroBreach.Tests/UpdateTests.cs`.

**Interfaces:** `ReleaseClient.FetchLatestAsync(CancellationToken) -> ReleaseDescriptor`; `SignatureVerifier.Verify(byte[] content, byte[] signature) -> void`; `VerifiedCache.UpdateAsync(IProgress<string>, CancellationToken) -> VerifiedScanner`; `VerifiedCache.OpenSavedAsync(CancellationToken) -> VerifiedScanner`. `VerifiedScanner` exposes a verified script path and release tag. The runner accepts this descriptor, rather than an arbitrary downloaded filename.

- [ ] Retrieve the documented publisher key and independently compare its complete fingerprint with the publisher verification page. Embed only the approved public key. Record source URLs and fingerprint.
- [ ] Create security fixtures using disposable test-only signing keys; keep their trust configuration confined to tests. Write failing tests for modified content, wrong-key signatures, unsigned manifests, duplicate manifest entries, mismatched hashes, cache tampering, and missing signatures.
- [ ] Write failing update tests for prerelease/draft metadata, unexpected asset URLs/names, oversize responses, interrupted downloads, timeout, and cancellation. Assert the last verified cache survives each failed update.
- [ ] Run the update tests and observe rejection/integrity behaviours fail before implementation.
- [ ] Implement bounded HTTPS release downloads with GitHub API identification, explicit allowed download hosts, stable-release validation, and cancellation. Verify the signed manifest and script using the pinned key; require exact manifest filename/hash matching.
- [ ] Implement unique staging directories and atomic cache promotion. Reverify saved script, signature, and manifest before returning a saved descriptor. Do not fall back automatically after an update failure.
- [ ] Run the update/security tests and verify a real official release through the same production service.

## Task 3: Portable scan execution and application lifetime

**Files:** `src/ZeroBreach.Core/Scanning/{ScanRunner,PortableWorkspace}.cs`; `tests/ZeroBreach.Tests/ScanRunnerTests.cs`.

**Interfaces:** `PortableWorkspace.Acquire(string root) -> IDisposable`; `ScanRunner.RunAsync(VerifiedScanner scanner, bool quick, IProgress<string> progress, CancellationToken token) -> ScanReport`. Runtime resolution is restricted to the bundled runtime under the application directory. Diagnostics are bounded and available separately from the primary report.

- [ ] Write failing tests using harmless child-process fixtures: exit 0/2/1 handling, absent or malformed reports, cancellation, process-tree termination, and bounded diagnostic output.
- [ ] Write failing tests for paths containing spaces/non-ASCII characters, a second instance holding the lock, read-only portable storage, and cleanup failures. Failed cleanup must return the retained location as a visible warning. Test embedded-runtime extraction, interrupted extraction, traversal rejection, and integrity verification before execution.
- [ ] Run the process/workspace tests and observe the missing behaviours fail.
- [ ] Implement a per-portable-directory exclusive lock and writeability probe. Extract the embedded runtime into `ZeroBreach-data` with bounded, traversal-safe extraction and integrity checks before promotion and subsequent execution. Launch bundled Python with isolated imports, UTF-8 and structured argument passing; use unique temporary report paths. Drain stdout/stderr asynchronously.
- [ ] Terminate the process tree on cancellation and await termination before releasing the lock. Validate report/exit-code consistency, retain only in-memory results by default, and report cleanup failures.
- [ ] Run the scan/process test suite and a controlled real scanner smoke run with the bundled runtime.

## Task 4: English Windows interface

**Files:** `src/ZeroBreach.App/{App.xaml,App.xaml.cs,MainWindow.xaml,MainWindow.xaml.cs,MainViewModel.cs}`; UI-state tests under `tests/ZeroBreach.Tests/` as practical.

**Interfaces:** View model state is `Ready`, `Updating`, `Scanning`, `Completed`, `Cancelled`, or `Failed`. Commands are `Start`, `Cancel`, `ScanWithSavedVersion`, `SaveReadableReport`, and `SaveJsonReport`. The view model consumes the services from Tasks 1–3.

- [ ] Write failing state-transition tests: no scan at startup; running state disables a duplicate start; cancellation cannot become completed; failed updates expose saved-version action only when a cached version can be verified; retry resets stale results.
- [ ] Run state tests and observe the missing behaviours fail.
- [ ] Build a keyboard-accessible WPF interface with a clear primary action, full/quick choice and explanation, stage/elapsed-time display, indeterminate progress, and cancellation.
- [ ] Render the English result summary, evidence cards, coverage/gaps, and next steps using Task 1 presentation. Show version used and optional-pattern exclusions. Keep paths selectable and evidence addresses non-clickable.
- [ ] Implement user-chosen text/JSON exports with a clear local-path privacy notice; handle export errors without clearing the displayed result. Do not save history automatically. Cancel active work safely on window close.
- [ ] Run state tests and visually exercise startup, update, scanning, completion, gaps, findings, errors, cancellation, and export. Use synthetic reports for finding states and a real scan for integration.

## Task 5: Portable packaging and final verification

**Files:** `scripts/Build-Portable.ps1`, `README.md`, `THIRD-PARTY-NOTICES.md`, project/solution configuration, `.gitignore`.

**Interfaces:** `scripts/Build-Portable.ps1` produces `artifacts/ZeroBreach.exe` as the complete end-user deliverable. Python, trust assets, and notices are embedded; tests and temporary reports are excluded. An in-app About/notices action exposes required notices.

- [ ] Obtain a .NET 10 SDK locally if the installed SDK cannot target it. Pin all resolved dependency versions and enable locked restore.
- [ ] Fetch a supported official CPython x64 embeddable runtime at a pinned version; verify acquisition against recorded official provenance/hash before packaging. Include the runtime licence and isolated path configuration.
- [ ] Implement repeatable self-contained single-file Windows publishing, including native library extraction as required by WPF. Embed CPython and its integrity manifest as resources. Include GUI/scanner/runtime attribution, the publisher-key provenance, user instructions, scan limitations, update fallback explanation, and build requirements.
- [ ] Run the complete automated test suite, release publish, and packaging script; verify the executable contains required embedded assets and no private reports.
- [ ] Copy only `ZeroBreach.exe` into an otherwise empty writable folder with spaces/non-ASCII characters. Launch it without companion files, verify runtime extraction and official download/signatures, run a local scan, test cancellation and export, and verify restart/cache handling and About/notices. Check that runtime tampering is rejected or repaired from the embedded trusted resource.
- [ ] Review the implementation against every spec section, including rejection paths. Report any untested Windows version, clean-machine coverage, or lack of GUI code-signing explicitly.
- [ ] Deliver the single portable executable and source/build instructions with the observed test and smoke-test results. Push source and documentation to the private repository; keep scan results and build outputs out of Git.

## Execution

Recommended method: implement directly in this chat, task by task. The services share a small set of contracts, and one implementer can validate the complete download-to-report flow without coordinating multiple workers. No subagents are required for implementation.

The user explicitly authorised creating a Git repository and a private GitHub repository. Initialise this folder on `main`, commit the design and plan, create the private `zero-breach-gui` repository under the authenticated user's account, and push project source there. Verify private visibility. Build artifacts and local scan reports remain out of Git; never change visibility or publish publicly without explicit user authorisation.

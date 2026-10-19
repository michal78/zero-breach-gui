# Verification of the initial portable build

## Deliverable

`artifacts/ZeroBreach.exe`, English Windows x64 desktop GUI. Only this executable is required for distribution. Its Python runtime, trust assets, licences and .NET runtime are embedded. Runtime extraction and verified scanner downloads create local application data during use.

Build command: `scripts/Build-Portable.ps1`. SDK: 10.0.402. Official embedded CPython: 3.13.16. The executable is initially unsigned.

## Evidence

- 47 automated tests pass, including official valid signatures, modified or wrong-key signatures, checksum ambiguity, redirected/oversize downloads, stalled response bodies, cancellation, cache tampering, runtime archive traversal and tampering, report/exit-code consistency, evidence grading, campaign-window dates, Unicode process arguments, and instance locking.
- An independent read-only code review identified update-deadline/fallback defects. Regression tests reproduced them and passed after correction. The campaign-date statement and workspace-creation error were corrected in the same pass.
- Official signed scanner v0.1.4 completed four controlled browser fixtures: negative/positive data in quick/full modes, returning the expected 0/2 exit codes. All four actual upstream JSON reports were parsed and exported successfully.
- These integrations exposed the released scanner's ` (single-file build)` version suffix, which is now normalised explicitly and covered by a regression test.
- Complete quick and full service pipelines downloaded/authenticated or revalidated the real release, extracted the bundled Python runtime, scanned a synthetic browser profile, parsed/exported the result in memory, cleaned temporary reports, cancelled a subsequent scan, and revalidated the saved release. The full pipeline's file-fingerprint phase completed in 38 seconds on the available machine.
- The executable was launched from a directory containing spaces and `æ` without companion binaries. The English startup screen, instance-lock error, live scan status, quick-mode selection, cancellation, synthetic finding presentation, coverage/gap tab and export dialog were inspected through Windows computer-use.
- Readable exports defang evidence. Original JSON deliberately preserves upstream raw values and has a visible privacy warning.

## Limits

- The available Windows machine was used; a clean Windows 10 installation was not tested separately.
- A broad scan of the current account was started and later deliberately stopped because its browser-cache walk was very long. It is not counted as a completed clean scan. Controlled full/quick runs of the unmodified signed scanner completed. The UI cancellation path was also exercised on the real account.
- The native export dialog was opened, while full click-through file saving was not confirmed because the Windows automation helper could not reliably address the owned file-dialog controls. Report generation was verified by automated tests and actual upstream-report integration checks.
- GUI code signing requires a publisher certificate and is not included. Downloaded scanner signatures are independently enforced.
- No live spyware was used. Positive cases are synthetic local browser records, and no indicator address was contacted.

## Implementation decisions and their implications

- Work uses a feature branch in the existing checkout, which preserves the approved project location. There is no separate worktree copy; changes must be managed through that branch.
- The small WPF window coordinates a separately tested state machine rather than duplicating state in an additional view model. If the window grows, presentation isolation should be revisited.
- Original JSON retains raw upstream evidence, while human text is defanged. Original reports must be shared securely and their indicator addresses must not be opened.
- Campaign-window accuracy and actionable first-run errors were treated as release issues because users rely on those explanations. Both have regression coverage.
- The portable-directory lock is held until application exit, protecting extracted runtime and cache as well as a running scan. A second instance must use another directory or wait for the first to close.
- Test coverage does not establish compatibility with every Windows environment. Untested versions and clean-machine deployment remain explicit limits rather than inferred success.
- Temporary database snapshots are directed to each run's directory through `TMPDIR`, while the original `TEMP` and `TMP` scan roots are preserved. Cancellation cleanup can therefore remove snapshots as well as the requested JSON report.

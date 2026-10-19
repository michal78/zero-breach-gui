# Zero Breach Portable Windows GUI

Date: 2026-10-19
Status: Approved and implemented, including the user's single-executable distribution and private repository requirements. Validation limits are recorded in docs/verification.md.

## Purpose

Make the official interplains/zero-breach scanner usable by nontechnical Windows users. A user downloads one executable, opens the application, starts a scan, and receives an understandable account of findings and scan limitations.

The user explicitly requires an English interface. All application labels, status messages, errors, explanations, and generated report headings must be English.

## Distribution and implementation

- Target Windows 10 and Windows 11, x64, as the proposed initial platform.
- Deliver one portable `ZeroBreach.exe`, with no companion files required from the user. Installation, administrator privileges, and a separately installed Python or .NET runtime must not be required. Embed the Python runtime, trust assets, and required notices in the executable; use a self-contained .NET single-file publish. The executable may extract internal components and store verified scanner downloads locally at run time.
- Use a native .NET Windows desktop interface with a self-contained deployment and a bundled, supported CPython embeddable runtime. Execute the official scanner as a separate process.
- Keep extracted runtime components, downloaded scanner versions, and application state in a `ZeroBreach-data` directory beside the executable. If that directory is not writable, explain how to move the executable to a writable local location. Verify embedded runtime integrity before execution and prevent concurrent extraction. Temporary extraction performed by the .NET host is allowed; single-file delivery does not mean the running program creates no local files.
- Include applicable runtime licences and upstream attribution in the distribution.
- Signing the GUI executable requires a separately available publisher certificate and is not a prerequisite for the initial local deliverable. Scanner signature verification is required independently.

## User experience

The initial screen explains that the application checks this Windows account for known AdFection/Candiru spyware indicators. It recommends closing browsers for better coverage, without closing them automatically.

The main action is **Start scan**. A full standard upstream scan is the default. A clearly labelled quick option may skip file fingerprint checks and must explain the resulting reduced coverage before the scan starts.

Starting a scan performs these stages:

1. Check the latest stable official GitHub release.
2. Download and verify the scanner when a new release is available.
3. Run the scanner locally and collect its structured report.
4. Display a plain-language result, limitations, and next steps.

Show stage names and elapsed time. Use indeterminate progress when the scanner cannot provide reliable progress; never invent a percentage. Keep the window responsive. Allow cancellation and terminate the scanner process tree when requested. A cancelled run is never shown as a completed clean scan.

Do not scan merely because the application was opened. Once the user starts a scan, version checking, verification, execution, and result display require no additional routine decisions.

## Release discovery and verification

- Discover stable releases through the official GitHub API for `interplains/zero-breach`, excluding drafts and prereleases.
- Download the official single-file scanner, its detached signature, and the checksum manifest and its signature from that release.
- Ship a trusted public release key with the GUI and pin the complete expected fingerprint. The currently documented fingerprint is `C24E C92B 12D6 670A 2516 065F 9B4D 5754 99AF A53C`.
- Verify signatures against the bundled trusted key, and check the scanner hash against the verified manifest. A downloaded key must never replace the trusted key automatically.
- Stage downloads separately and promote a version to the runnable cache only after verification succeeds. Reverify cached bytes before execution.
- Apply timeouts, download size limits, expected asset names, and HTTPS-only download rules. Reject unexpected metadata or ambiguous checksum entries.
- Use an application lock to prevent concurrent updates or scans against shared portable data.
- A failed update never silently downgrades or executes unverified content. When a previously verified version exists, offer an explicit **Scan with saved version** action, showing its version and the reason the update failed. With no verified version available, explain the failure and offer retry.
- Display the scanner version used in every result and report.

## Scanner boundary

Run the unmodified official scanner with the bundled Python runtime, UTF-8 output, and a unique JSON report path. Pass arguments through a structured process API rather than shell interpolation. Keep Python imports isolated from unrelated user installations and working-directory modules.

The scanner runs with the current user's permissions. The application does not enable logging, change security settings, remove files, repair findings, close browsers, or elevate itself.

The wrapper makes network requests to obtain official releases. It does not upload scan data or make indicator lookups. The scanner retains its upstream local, read-only behaviour; the wrapper does not claim to provide an operating-system network sandbox.

Interpret exit code 0 as no matched indicator, 2 as a match, and 1 as setup failure, subject to validating the report. An absent, truncated, incompatible, or inconsistent report is an error or incomplete result, never a clean result. Preserve diagnostics for troubleshooting without displaying raw console output as the primary experience.

Optional upstream pattern engines and extra dependencies are outside the initial scope. Explain any relevant coverage exclusions instead of implying that all upstream optional capabilities ran.

## Result interpretation

Display a prominent summary followed by **Findings**, **What was checked**, **What could not be checked**, and **Recommended next steps**.

Keep the upstream distinctions between spyware file matches, persistence evidence, connection records, DNS records, configuration, saved references, and legitimate vulnerable-driver matches. Preserve evidence strength, indicator role, source, location, and timestamps when available.

Do not equate a saved reference, advertisement-related trace, or legitimate vulnerable driver with spyware infection. Do not combine weak evidence into a stronger claim. Unknown report values receive a neutral explanation with their original detail, rather than being guessed into a severity category.

With no matches, use wording such as **No known indicators found**, accompanied by an explanation that this does not prove the computer is free of spyware. Incomplete checks remain visible beside the result. Use text and icons as well as colour to communicate status.

Keep suspicious domains and IP addresses defanged in displays and exports. Do not turn evidence into clickable network links. Provide selectable file locations and a detail view for expert review.

## Reports and privacy

Allow the user to save an English, readable report and the original JSON report to a location they choose. Include scanner version, scan time, scan mode, findings, coverage gaps, and next steps. Explain that reports can contain local usernames and file paths before export.

Keep scan work files local. Do not retain scan history automatically. Remove temporary run files after successful consumption or cancellation where possible. If cleanup fails, show the retained location. Preserve an explicitly exported report.

## Component responsibilities

- Desktop UI: screen state, accessible controls, progress, cancellation, and result presentation.
- Release service: release discovery, bounded downloads, signature and checksum verification, and verified cache management.
- Scan runner: isolated child process, report collection, cancellation, and diagnostic capture.
- Report interpreter: schema validation and faithful mapping to English explanations.
- Export service: readable report generation and original JSON export.

## Validation and deliverables

Verify signature rejection for modified assets, cache tampering, failed or interrupted updates, and invalid metadata. Test report interpretation with no matches, serious findings, weak evidence, driver matches, coverage gaps, malformed reports, and unexpected exit codes. Verify cancellation and paths containing spaces or non-ASCII characters.

Build the Windows portable package and exercise release download, verification, and a real local scan on the available Windows machine. Exercise the GUI's principal states and export flow. State any remaining clean-machine or Windows-version validation limitations explicitly.

Deliver source code, repeatable build instructions, third-party notices accessible from the executable, and the portable `ZeroBreach.exe`. Create a Git repository and a private GitHub repository for this project, and push project source there. Keep the GitHub repository private until the user explicitly authorises public visibility. Do not create an upstream pull request or publish a public release as part of this scope.

## Reference

Official upstream: https://github.com/interplains/zero-breach

Release-signing documentation: https://github.com/interplains/zero-breach/blob/main/SIGNING.md

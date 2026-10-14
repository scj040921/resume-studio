# Sync Version Confirmation Implementation Plan

> **For agentic workers:** Implement in this session using the existing coding-change-closed-loop and test-driven-development workflows; request an independent read-only review before publishing.

**Goal:** Require the user to select the final whole-snapshot version whenever the two sides differ, after showing current sizes, changes since the last successful synchronization, and actual file modification times.

**Architecture:** Keep canonical data hashes as the change detector and use a common canonical UTF-8 JSON size for comparison. Extend local/remote read metadata and the backward-compatible baseline; enforce confirmation in SyncCoordinator and use one reusable native dialog for every non-equal plan. Recheck both sides after confirmation before changing data.

**Tech Stack:** Existing C#/.NET 8 WinUI 3 client, Android WebView/Java client, Java 21 local backend, HTTP WebDAV, and existing console/native test harnesses. No additional dependencies.

## Global Constraints

- Equal content does not show a dialog or overwrite data.
- First upload, one-sided changes, divergent changes, and missing cloud versions require explicit Push/Pull confirmation.
- Missing cloud versions cannot be selected for download; Cancel is the default dialog button.
- Old baseline JSON remains readable; absent size history is labeled rather than fabricated.
- File modification times are informational. Missing server times remain unknown; snapshot generation time is separately labeled.
- Credentials remain local; cancellation does not create a baseline or upload/import a snapshot.
- Existing strong ETag/content revision and local CAS checks remain effective after the dialog.
- Preserve the user's fork configuration and upstream's newer source behavior. PR #2 was merged. Merge latest upstream d321710 first, then publish this feature in a new PR.

## Task 1: Comparison, metadata, and confirmation enforcement

Files: `Services/SyncProtocol.cs`, `SyncCoordinator.cs`, `SyncBackendStore.cs`, `SyncWebDav.cs`, `AppPreferences.cs`, Java `SyncStore.java` / `Store.java`, `tests/Sync.Tests/ConfirmationCoverage.cs`, existing integration and backend tests.

- [x] Add tests that call `ExecuteAsync(plan)` on a first-upload or changed plan and require IOException with zero writes. Verify explicit Push and Pull choices, cancellation, equal-size changes, stale plans, legacy baselines, and canonical size invariance.
- [x] Run `dotnet run --project tests/Sync.Tests/Sync.Tests.csproj -c Release --no-restore` and observe the missing confirmation failure.
- [x] Add `SyncPlan.RequiresConfirmation`, optional local/remote modification metadata, and nullable `SyncBaseline.DataBytes`; compute size with the a separate shared size format while preserving existing hashes.
- [x] Make every non-Unchanged execution require an explicit Push/Pull resolution; allow either existing version regardless of the suggested direction, then recheck the original local hash and remote revision.
- [x] Return actual latest synchronized-file modification time from the local snapshot endpoint. Read remote HTTP Last-Modified when available and label missing time. Avoid refreshing unchanged preferences while inspecting sync.
- [x] Run rebuilt-backend integration tests and metadata assertions with `scripts/test-features.ps1`.

Execution invariant:

```csharp
if (plan.RequiresConfirmation && resolution is null)
    throw new IOException("请先确认要保留的版本。");
```

## Task 2: Windows and Android comparison dialogs and publication

Files: `MainWindow.Sync.cs`, `MainWindow.NavigationVerification.cs`, README and synchronization protocol documentation.

- [x] Add a native dialog with two version cards, current canonical data sizes, signed size deltas from the last successful baseline, actual file modification times, snapshot creation time when useful, and collection counts.
- [x] Use buttons `保留本机并上传`, `保留云端并下载`, and `取消`; disable the download button when no remote version exists. Keep Cancel as DefaultButton and call ExecuteAsync only after a selection.
- [x] Show `无需同步` when equal. Extend native verification to check dialog values, buttons, missing-cloud behavior, and cancellation; save a dialog preview.
- [x] Build Portable output and run `scripts/test-navigation-ui.ps1`; independently review the final diff and resolve material findings.
- [x] Commit and upload a new fork branch, validate that exact revision, push, create a new upstream PR, and attach it to the chat.

The published description must disclose that cloud file time can be unavailable and that no-ETag compatibility retains its documented concurrent-write limitation.

## Upstream integration and Android parity

- [x] Diff synchronization changes between 00fe1dc and d321710 before merging.
- [x] Merge upstream baseline in commit 7f79147; resolved baseline tree matches d321710 exactly.
- [x] Preserve interviewStudy extension, wildcard-condition compatibility retry, Android support and release changes.
- [x] Extend Android inspect/display/execute with sizes, nullable deltas, actual file times and explicit direction for every changed plan.
- [x] Change background synchronization into checks that wait for user selection.
- [x] Verify cross-platform size fixtures and Android/Windows round trips, including cancellation, stale plans and legacy packages.
- [x] Build both clients; verify native Windows dialog and Android UI where a device is available.
- [x] Obtain independent review, fix material issues, commit, upload fork and create new upstream PR.

## Verification evidence (2026-10-15)

- Windows portable distribution and signed Android APK built in an ASCII-path verification worktree because native packaging tools reject the original Chinese path.
- `test-features.ps1`: navigation, platform capture, sync (strong/missing ETag), Java validation/CAS/history/backup/recovery passed.
- `test-android-sync.ps1`: 61 actual HTTP Windows/Android checks, shared exact-size fixtures, baseline compatibility and both opposite-direction choices passed.
- Actual Android app.js action/dialog verified with a minimal host DOM: Cancel focus, Back cancellation, both directions, equality, missing cloud and unknown metadata.
- Windows native verification: 47 checks including actual comparison dialog, missing-cloud buttons, cancellation and metadata passed.
- Independent read-only review found no Critical/Important defects; repeated equal baseline rewrite was optimized and regression-tested.
- No connected Android device was available; production APK instrumentation and actual background job validation remain unexecuted locally. Their acceptance tests were updated and the fixture lifetime was fixed so the server remains available during the background check.

# Security Notes

180Hz Setup Hub should be treated as a privileged setup tool because it installs and upgrades software.

## Security already applied

- Process execution uses `ProcessStartInfo.ArgumentList`, not shell command strings.
- Package operations are locked to the trusted `winget` source and exact package IDs.
- Package IDs are allowlisted with a strict safe-character pattern before commands run.
- Runtime config, logs, and downloads stay under the selected local 180Hz Setup Hub data folder.
- The app does not need to inspect or capture the user's desktop or browser windows.
- User-started check/install/update queues can be stopped, and the active `winget` process tree is cancelled through a cancellation token.

## Next hardening milestones

- Sign the 180Hz Setup Hub executable and installer with a code-signing certificate.
- Verify self-update downloads with SHA-256 checksums and signature validation before install.
- Store a signed package catalog so modified local config can be detected.
- Add a clear admin privilege prompt only when an install actually requires elevation.
- Keep logs local, redact sensitive paths where possible, and provide a one-click log clear option.
- Add a security review checklist before every release.

## Threat model

Main risks:

- Malicious package catalog edits.
- Tampered app self-update releases.
- Command argument injection.
- Untrusted download sources.
- Overly broad admin permissions.

The current version reduces command injection and untrusted-source risk. Self-update signing and catalog integrity are the next important pieces before public distribution.

# Pro integration boundaries

This document is an implementation plan. Key Vault, WDAC catalog generation and GitLab orchestration are not implemented in the community executable.

## Public contracts

- `IFileSigner.SignFileAsync` is asynchronous and accepts cancellation. A provider owns access to its key and returns a `SigningResult`; it must report signature/timestamp failures. `NativeCodeSigner` is the community implementation using the Windows certificate store.
- `PackageSigningService.SignAsync` signs the deploy script. A null provider means signing was explicitly disabled. An enabled provider's failure or cancellation stops the build; it never becomes an unsigned success.
- `IntuneUploadService` already accepts a token-provider delegate. A commercial host can supply its identity flow without embedding vault credentials into community settings or replacing Graph payload code.
- `PackagePreflight` validates the PSADT runtime files and script before the shared build/publish path. `IUploadProgress` carries progress without requiring a view model.

The current services are in a Windows-targeted assembly. A supported headless host and versioned standalone contracts package remain work to complete before promising a stable third-party plugin API. The app does not scan folders for arbitrary extension DLLs.

## Key Vault authentication

The desktop flow should authenticate a user to a selected vault and distinguish the certificate used for Intune app-registration authentication from a code-signing certificate. Validate permissions, expiry, private-key availability and certificate purpose separately. Keep secret values and tokens out of saved settings, package metadata and diagnostic logs.

Support for an exportable PFX retrieved from a vault is distinct from signing with a key that stays in the vault. The latter needs a remote signing provider; for Intune client authentication, MSAL supports a client-assertion callback that can obtain a signature from Key Vault. Do not advertise non-exportable-key support through a provider that downloads a PFX. [MSAL client assertions](https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/web-apps-apis/confidential-client-assertions)

Release checks: interactive sign-in, denied access, expired/rotated certificates, unavailable vault, cancellation and a publish attempt whose required signing fails. Local certificate support remains available in Community.

## WDAC catalog generation

A catalog for installer payloads alone can miss files created during installation or application use. The guided Pro flow should prepare an isolated capture device, collect installation and runtime files, let the packager inspect the inclusion list, then sign and validate the resulting catalog. Microsoft documents Package Inspector capture, catalog signing, deployment and signer policy requirements. [App Control catalog guidance](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/deployment/deploy-catalog-files-to-support-appcontrol)

Treat catalog generation, catalog signing and policy deployment as distinct operations. Build an immutable manifest of the captured file hashes, certificate identity and package version; content changes after capture must invalidate the result. Avoid broad policy changes as a side effect of building a package.

Release checks: unsigned installed binaries, files created on first launch, missing coverage, changed binaries after capture, signing failure and validation on a device with a representative App Control policy. Export the catalog and evidence only after those stages succeed.

## GitLab pipelines

Repository synchronization and pipeline execution are separate capabilities. The desktop integration should show the project, target branch and commit before pushing, then track the pipeline ID, status and artifacts. Promotion must use the exact successful build artifact and its manifest, rather than rebuilding mutable input at deployment time.

Use dedicated Windows runners for Windows packaging/capture. For unattended Azure access, prefer workload federation with a narrowly scoped GitLab ID-token trust over a long-lived secret. Desktop repository API access may require a separately scoped GitLab credential. GitLab's built-in Key Vault secret integration has edition/runner requirements that must be checked for the customer's installation. [GitLab Key Vault integration](https://docs.gitlab.com/ci/secrets/azure_key_vault/)

Release checks: invalid project/ref, denied access, expired credentials, duplicate requests, cancelled/failed jobs, lost connectivity, artifact hash mismatch and manual promotion. Provide machine-readable results and a nonzero exit status on failure when the headless host is implemented; no headless command is currently advertised.

# September 2026 code review

This review covered the WPF workflows, editor state, packaging services, authentication, generated PowerShell, Graph requests, Azure content uploads and assignment handling. Parallel reviews were followed by focused local checks and integration review. The changes below address concrete defects found in those paths.

## Corrected behavior

| Area | Change |
| --- | --- |
| Field actions | Browse, Search and Copy sit beside fields in separate columns, with consistent spacing and heights. Application Detail also shows the minimum Windows release. |
| Editor saves | A save acknowledges the exact buffer version written. Typing during a save remains unsaved; overlapping writes serialize. New-package and publish actions resolve unsaved edits first. |
| Editor reload | An automatic reload checks the clean buffer version atomically before replacing it. New edits retain the on-disk conflict indication. |
| Editor resources | Files above 8 MB are directed to the external editor. Collapsed tree folders defer descendant control creation; refresh requests coalesce and stale package results are ignored. |
| Editor origin | The native bridge accepts messages from the local editor origin only. External web links open in the system browser. |
| Remote detection | Discovery belongs to its package and device. Changing either clears stale results; install remains busy through its follow-up discovery. |
| Share operations | Package creation/upgrades perform filesystem and metadata work on a worker thread. Remote copy progress samples less frequently and enumerates filenames without allocating a complete array. |
| PowerShell literals | ASCII and typographic quote characters are escaped correctly. Metadata round-trips preserve the original text. |
| Authentication | A failed sign-in leaves the last successful identity intact. App registration requires its certificate. Session-bound upload token providers stop after an account change; certificate lifetime covers in-flight token acquisition. |
| Tenant cache | Authentication changes invalidate the cached app list, and stale requests cannot repopulate it. |
| Detection rules | Switching version detection to Exists clears the legacy version flag. Unsupported file-string detection is removed; creation-date comparisons survive edits. |
| Requirements | Graph payloads use the modern Windows-release and architecture properties, distinguishing Windows 10 from Windows 11 while retaining saved settings labels. |
| Assignments | All assignment pages are read, exclusions keep their group identity and label, and ambiguous or failed name lookups cannot silently create or choose a group. |
| Graph retries | Non-idempotent POST requests retry only explicit throttling. Inconclusive mutation outcomes require checking the existing object before retrying. |
| Azure upload | SAS URLs refresh during block and commit retries, including the final block. Error messages omit signed URLs and raw storage response bodies. |
| Publish outcomes | Assignment waits for Intune's published state. Incomplete follow-up work and uncertain final publication retain the known app and show an actionable result rather than an unconditional success or rollback claim. |
| Connection checks | Read probes report read access; they do not claim to prove write permissions. Documentation separates delegated and application permissions. |

## Validation

- The application and standalone preview utility cross-compile with .NET 10.
- 35 existing packaging, path, preflight, signing and script checks passed from a temporary validation folder.
- 27 parser/metadata checks passed, covering ASCII and typographic quotes without executing PowerShell scripts.
- 93 focused Graph checks covered detection, request payloads, OS readback, cache invalidation, assignment pagination and exclusions.
- 70 Graph/upload checks used local HTTP responses and synthetic ZIP content to exercise retry, renewal, cancellation and error reporting.
- 97 follow-up checks covered incomplete assignments, ambiguous group lookups, uncertain publication, commit readback, timeout, cancellation and warning presentation.
- Separate editor and authentication checks used the actual source with fake browser, identity, store and file dependencies. The actual embedded JavaScript was also exercised for save and reload races.
- XAML parsing and layout inspection check that text fields and action buttons no longer share a grid cell.

The `Packman.Tests` project was removed at the maintainer's request. Windows CI retains the offline application build, native preview utility and packaged application startup/shutdown check. The focused checks above were temporary review tools; they are not a permanent CI regression suite.

No live Intune tenant changes, certificate-store authentication, installation execution or remote-device deployment were performed. Before a release, validate interactive editing and scaling on Windows, a real MSI/EXE package, app-registration permissions, install/uninstall on a disposable device, and a pilot Intune assignment. A code review does not establish those environment-specific results.

## Contract references

- [PowerShell quoting rules](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.core/about/about_quoting_rules)
- [Win32 app properties and publishing state](https://learn.microsoft.com/en-us/graph/api/resources/intune-apps-win32lobapp?view=graph-rest-beta)
- [File-system detection](https://learn.microsoft.com/en-us/graph/api/resources/intune-apps-win32lobappfilesystemdetection?view=graph-rest-beta)
- [Group membership permissions](https://learn.microsoft.com/en-us/graph/api/group-post-members?view=graph-rest-beta)
- [Graph throttling](https://learn.microsoft.com/en-us/graph/throttling)
- [Renewing an upload URL](https://learn.microsoft.com/en-us/graph/api/intune-apps-mobileappcontentfile-renewupload?view=graph-rest-1.0)
- [Azure block-list commit semantics](https://learn.microsoft.com/en-us/rest/api/storageservices/put-block-list)

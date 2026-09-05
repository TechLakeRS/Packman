# Packman desktop UI

The UI centers on the working sequence: select an installer, prepare its PSADT package, test it, configure Intune, review and publish. It retains the Windows Fluent shell and amber accent. All existing navigation destinations remain available, including standalone publishing, remote testing and directory tools.

## Design changes

- Package: source selection precedes the fields it can populate. An adjacent summary reflects edits; generated packages retain script, test and folder actions. Upgrade remains a separate mode that preserves prior script edits.
- Configure: detection and assignment sections use the full content width. Requirements and return codes are expandable. The middle step explicitly makes no changes in Intune.
- Review: shows tenant, package, command lines, detection, audience, per-package group creation, requirements and return codes, with direct routes to edit or test. Publish errors remain visible.
- Applications: search is separate from filters, names have a bounded width for ellipsis, and rows have more vertical space.
- Application Detail: keeps overview, package integrity, detection editing, deployments, assignments and membership tools. “Republish content” accurately describes replacement of content for the same app. “Delete from Intune” makes deletion distinct from uninstalling on devices.
- Remote Test: preserves install, uninstall, detection discovery, context, staging cleanup and live output. The editable computer picker now has the WPF `PART_EditableTextBox` required by its template. Context radio groups are scoped to each control instance.
- Settings: six implemented sections, with a full-width setup hint and persistent save feedback. Sections inherit the existing view model; appearance still saves immediately. Planned features no longer occupy navigation space.
- Editor: preserves Monaco, dirty tabs, save/reload/revert and external editor support, with more readable tab labels and keyboard focus.

## Shared components

`Styles.xaml` owns type, buttons, fields, editable dropdowns, focus and definition rows. `SectionHeader` wraps captions beneath headings. `ReturnCodeEditor` is shared by Settings and Configure. `PublishStatusControl` and `PublishStepList` share progress, cancellation and result presentation across both publishing entry points. `GroupPickerControl` retains a separate intent for every selected group and renders long group names in bounded rows.

## Packaging and Intune behavior

No UI action publishes while merely configuring a package. The shared build pipeline validates the PSADT launcher, script and module manifest before signing or invoking IntuneWinAppUtil. Script parsing detects syntax errors and exact generated EXE flag placeholders without executing the script. This does not verify vendor switches, dependencies, signatures, device requirements or successful installation; remote and pilot testing remain necessary.

System/User execution context, MSI/file/registry detection, requirements, return-code mappings, group intents, signing, content upload, supersedence and the existing cleanup/error handling remain supported. Intune installation must complete without user input. The UI explains the distinction between attended testing and unattended deployment; it does not silently rewrite saved commands.

Sources reviewed on 2026-09-06:

- [Microsoft: Win32 app configuration](https://learn.microsoft.com/en-us/intune/app-management/deployment/add-win32)
- [PSADT: deployment modes](https://psappdeploytoolkit.com/docs/explanation/deployment-modes)
- [PSADT: command-line parameters](https://psappdeploytoolkit.com/docs/reference/command-line-parameters)

The existing scope limits remain: wizard detection uses one rule; standalone publishing accepts multiple rules; assignment filters, dependencies and All Devices/All Users targets are not added by this redesign. These are useful future workflow additions, not extra UI dependencies.

## Verification

The solution can be cross-compiled with .NET 10 and `EnableWindowsTargeting=true`. The non-Windows test path needs the additional packages described in the test project; the repository's offline feed is unchanged. The pure regression suite covers package generation/upgrades, Graph payloads and preflight behavior.

Windows CI additionally loads and renders representative views, all Settings sections, Application Detail tabs and the editable computer dropdown in both palettes. It exports PNG previews as the `ui-previews` artifact. These layout tests do not authenticate, upload, test a remote device or execute a deployment script. Manual Windows validation should include keyboard navigation, 125%/150% display scaling, long tenant/group names, an actual MSI/EXE package, install/uninstall on a test device, and a pilot Intune assignment.

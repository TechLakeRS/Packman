# Contributing to Packman

Packman is a Windows desktop application for preparing PSADT packages and managing Win32 applications in Intune. The community edition includes the workflows documented in [README.md](README.md). See [ROADMAP.md](ROADMAP.md) for the boundary between community work and planned commercial integrations.

## Build and validate

Use Windows with the .NET 10 SDK. Visual Studio is optional.

```powershell
dotnet restore Packman.sln
dotnet build Packman.sln -c Release --no-restore
dotnet run --file scripts/RenderUiPreviews.cs -c Release -- artifacts/ui-previews
```

The repository's NuGet feed is the vendored `packages/` directory. Keep package references and the vendored feed in sync when changing dependencies. Preserve their licenses and notices.

The standalone Windows preview utility renders the main views, both themes, Settings sections and Application Detail tabs. CI attaches the screenshots as `ui-previews` and launches/closes the packaged application. Previews use sample data and do not connect to a tenant or deploy an application. The solution contains only the desktop project; validate behavior changes with focused reproducible checks and report the evidence in the pull request.

## Making a change

For a bug, explain what you did, what happened and what you expected. For a larger feature, describe the packaging problem in an issue before building a new workflow.

Keep view state in view models, reusable behavior in services and shared appearance in `Themes/Styles.xaml`. Reuse the existing controls and dependency patterns. Avoid putting Graph calls or packaging logic in XAML event handlers.

Test behavior that can lose edits, alter generated scripts, change Graph payloads or affect cancellation and cleanup. For UI changes, inspect both themes with realistic long names and the minimum window size. Include validation results in the pull request and update the user instructions when the workflow changes.

Use a test tenant and disposable Windows device for deployment validation. Remove access tokens, certificate material, tenant identifiers and personal information from example files, screenshots and logs submitted publicly. Report vulnerabilities privately as described in [SECURITY.md](SECURITY.md).

## License and commercial work

Contributions to this repository are provided under its existing [Apache 2.0 license](LICENSE). Bundled dependencies retain their own terms. Paid integration implementations are maintained separately; the public core and its documented extension contracts remain usable without a commercial subscription.

The planned Pro products are not required to build or run the community app. Do not add unavailable paid actions to community navigation or publish proprietary integration code here.

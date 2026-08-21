# PSADT v4 Template

This is the template folder Packman copies when creating or upgrading a package.
Set **Settings → Network Paths → PSADT Template Path** to this folder (or your own
network copy).

## Structure

```
PSADT/
  Application/                     <- copied into each new package
    Invoke-AppDeployToolkit.ps1    <- v4 deploy script (Packman edits the metadata)
    Files/                         <- source installers are copied here
    SupportFiles/
```

Packman creates each package as `{Vendor}_{AppName}/{version}/` holding three
folders: `Application/` (this template), `Intune/` (the .intunewin is built here
at upload time) and `Icon/` (the archived app icon).

## Important

This template ships only the deploy **script**. To actually build a `.intunewin`,
the `Application` folder also needs the full PSAppDeployToolkit v4 runtime
(`Invoke-AppDeployToolkit.exe` and the `PSAppDeployToolkit` module). Drop those
into `Application/` here (or point the template path at a network share that
already contains them).

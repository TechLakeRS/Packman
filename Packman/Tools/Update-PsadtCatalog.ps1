<#
.SYNOPSIS
    Regenerates the PSADT function catalogue CSVs from the installed PSAppDeployToolkit module.

.DESCRIPTION
    Packman's editor gets its completions, hovers and signature help from
    PSADT_v4_Functions.csv, which is a snapshot of the module's own metadata.
    Run this after upgrading PSAppDeployToolkit and commit the resulting diff.

.EXAMPLE
    .\Tools\Update-PsadtCatalog.ps1
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = 'Stop'

$module = Get-Module -ListAvailable -Name PSAppDeployToolkit |
    Sort-Object Version -Descending | Select-Object -First 1
if (-not $module) {
    throw "PSAppDeployToolkit is not installed. Install-Module PSAppDeployToolkit"
}

Import-Module $module.Path -Force
Write-Host "Reading PSAppDeployToolkit $($module.Version) from $($module.ModuleBase)"

$common = [System.Management.Automation.PSCmdlet]::CommonParameters +
          [System.Management.Automation.PSCmdlet]::OptionalCommonParameters

function Format-CsvField([object]$Value) {
    $text = if ($null -eq $Value) { '' } else { [string]$Value }
    $text = ($text -replace '\s+', ' ').Trim()
    if ($text -match '[",]') { '"' + $text.Replace('"', '""') + '"' } else { $text }
}

$commands = Get-Command -Module PSAppDeployToolkit -CommandType Function, Cmdlet | Sort-Object Name

$detailed = [System.Collections.Generic.List[string]]::new()
$reference = [System.Collections.Generic.List[string]]::new()
$detailed.Add('Function,Synopsis,Parameter,Type,Mandatory,IsSwitch,Aliases,ParameterSets,AcceptsPipeline,Description')
$reference.Add('Function,Synopsis,Syntax')

foreach ($command in $commands) {
    $help = Get-Help $command.Name -Full -ErrorAction SilentlyContinue
    $synopsis = Format-CsvField $help.Synopsis
    $name = $command.Name

    $syntax = ($command.ParameterSets | ForEach-Object { "$name $($_.ToString())" }) -join ' | '
    $reference.Add("$name,$synopsis,$(Format-CsvField $syntax)")

    $parameters = $command.Parameters.Values | Where-Object { $common -notcontains $_.Name }
    if (-not $parameters) {
        $detailed.Add("$name,$synopsis,(none),,,,,,,")
        continue
    }

    foreach ($parameter in $parameters) {
        $attributes = $parameter.Attributes |
            Where-Object { $_ -is [System.Management.Automation.ParameterAttribute] }

        $mandatory = [bool]($attributes | Where-Object { $_.Mandatory })
        $pipeline = [bool]($attributes |
            Where-Object { $_.ValueFromPipeline -or $_.ValueFromPipelineByPropertyName })
        $isSwitch = $parameter.SwitchParameter

        # Get-Help carries the prose; the metadata above carries everything else.
        $description = ($help.parameters.parameter |
            Where-Object { $_.name -eq $parameter.Name } |
            Select-Object -First 1).description.Text -join ' '

        $detailed.Add((@(
            $name
            $synopsis
            (Format-CsvField $parameter.Name)
            (Format-CsvField $parameter.ParameterType.Name)
            $mandatory.ToString().ToUpper()
            $isSwitch.ToString().ToUpper()
            (Format-CsvField ($parameter.Aliases -join ', '))
            (Format-CsvField (($parameter.ParameterSets.Keys | Sort-Object) -join ', '))
            $pipeline.ToString().ToUpper()
            (Format-CsvField $description)
        ) -join ','))
    }
}

$detailedPath = Join-Path $OutputDirectory 'PSADT_v4_Functions.csv'
$referencePath = Join-Path $OutputDirectory 'PSADT_v4_Functions_Reference.csv'
$detailed | Set-Content -Path $detailedPath -Encoding UTF8
$reference | Set-Content -Path $referencePath -Encoding UTF8

Write-Host "Wrote $($commands.Count) functions, $($detailed.Count - 1) parameter rows"
Write-Host "  $detailedPath"
Write-Host "  $referencePath"

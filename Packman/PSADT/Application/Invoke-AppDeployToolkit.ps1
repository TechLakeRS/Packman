<#

.SYNOPSIS
PSAppDeployToolkit - This script performs the installation or uninstallation of an application(s).

.DESCRIPTION
- The script is provided as a template to perform an install, uninstall, or repair of an application(s).
- The script either performs an "Install", "Uninstall", or "Repair" deployment type.
- The install deployment type is broken down into 3 main sections/phases: Pre-Install, Install, and Post-Install.

The script imports the PSAppDeployToolkit module which contains the logic and functions required to install or uninstall an application.

.PARAMETER DeploymentType
The type of deployment to perform.

.PARAMETER DeployMode
Specifies whether the installation should be run in Interactive (shows dialogs), Silent (no dialogs), NonInteractive (dialogs without prompts) mode, or Auto (shows dialogs if a user is logged on, device is not in the OOBE, and there's no running apps to close).

Silent mode is automatically set if it is detected that the process is not user interactive, no users are logged on, the device is in Autopilot mode, or there's specified processes to close that are currently running.

.PARAMETER SuppressRebootPassThru
Suppresses the 3010 return code (requires restart) from being passed back to the parent process (e.g. SCCM) if detected from an installation. If 3010 is passed back to SCCM, a reboot prompt will be triggered.

.PARAMETER TerminalServerMode
Changes to "user install mode" and back to "user execute mode" for installing/uninstalling applications for Remote Desktop Session Hosts/Citrix servers.

.PARAMETER DisableLogging
Disables logging to file for the script.

.EXAMPLE
powershell.exe -File Invoke-AppDeployToolkit.ps1

.EXAMPLE
powershell.exe -File Invoke-AppDeployToolkit.ps1 -DeployMode Silent

.EXAMPLE
powershell.exe -File Invoke-AppDeployToolkit.ps1 -DeploymentType Uninstall

.EXAMPLE
Invoke-AppDeployToolkit.exe -DeploymentType Install -DeployMode Silent

.INPUTS
None. You cannot pipe objects to this script.

.OUTPUTS
None. This script does not generate any output.

.NOTES
Toolkit Exit Code Ranges:
- 60000 - 68999: Reserved for built-in exit codes in Invoke-AppDeployToolkit.ps1, and Invoke-AppDeployToolkit.exe
- 69000 - 69999: Recommended for user customized exit codes in Invoke-AppDeployToolkit.ps1
- 70000 - 79999: Recommended for user customized exit codes in PSAppDeployToolkit.Extensions module.

.LINK
https://psappdeploytoolkit.com

#>

[CmdletBinding()]
param
(
    # Default is 'Install'.
    [Parameter(Mandatory = $false)]
    [ValidateSet('Install', 'Uninstall', 'Repair')]
    [System.String]$DeploymentType,

    # Default is 'Auto'. Don't hard-code this unless required.
    [Parameter(Mandatory = $false)]
    [ValidateSet('Auto', 'Interactive', 'NonInteractive', 'Silent')]
    [System.String]$DeployMode,

    [Parameter(Mandatory = $false)]
    [System.Management.Automation.SwitchParameter]$SuppressRebootPassThru,

    [Parameter(Mandatory = $false)]
    [System.Management.Automation.SwitchParameter]$TerminalServerMode,

    [Parameter(Mandatory = $false)]
    [System.Management.Automation.SwitchParameter]$DisableLogging
)


##================================================
## MARK: Variables
##================================================

# Zero-Config MSI support is provided when "AppName" is null or empty.
# By setting the "AppName" property, Zero-Config MSI will be disabled.
$adtSession = @{
    # App variables.
    AppVendor = ''
    AppName = ''
    AppVersion = ''
    AppArch = ''
    AppLang = 'EN'
    AppRevision = '01'
    AppSuccessExitCodes = @(0)
    AppRebootExitCodes = @(1641, 3010)
    AppProcessesToClose = @()  # Example: @('excel', @{ Name = 'winword'; Description = 'Microsoft Word' })
    AppScriptVersion = '1.0.0'
    AppScriptDate = '2025-10-21'
    AppScriptAuthor = '<author name>'
    RequireAdmin = $true

    # Install Titles (Only set here to override defaults set by the toolkit).
    InstallName = ''
    InstallTitle = ''

    # Script variables.
    DeployAppScriptFriendlyName = $MyInvocation.MyCommand.Name
    DeployAppScriptParameters = $PSBoundParameters
    DeployAppScriptVersion = '4.1.7'
}

function Install-ADTDeployment
{
    [CmdletBinding()]
    param
    (
    )

    ##================================================
    ## MARK: Pre-Install
    ##================================================
    $adtSession.InstallPhase = "Pre-$($adtSession.DeploymentType)"

    ## Show Welcome Message, close processes if specified, allow up to 3 deferrals, verify there is enough disk space to complete the install, and persist the prompt.
    $saiwParams = @{
        AllowDefer = $true
        DeferTimes = 3
        CheckDiskSpace = $true
        PersistPrompt = $true
    }
    if ($adtSession.AppProcessesToClose.Count -gt 0)
    {
        $saiwParams.Add('CloseProcesses', $adtSession.AppProcessesToClose)
    }
    Show-ADTInstallationWelcome @saiwParams

    ## Show Progress Message (with the default message).
    Show-ADTInstallationProgress

    ## <Perform Pre-Installation tasks here>


    ##================================================
    ## MARK: Install
    ##================================================
    $adtSession.InstallPhase = $adtSession.DeploymentType

    ## Handle Zero-Config MSI installations.
   

    ## <Perform Installation tasks here>


    ##================================================
    ## MARK: Post-Install
    ##================================================
    $adtSession.InstallPhase = "Post-$($adtSession.DeploymentType)"

    ## <Perform Post-Installation tasks here>


    ## Display a message at the end of the install.
    if (!$adtSession.UseDefaultMsi)
    {
        Show-ADTInstallationPrompt -Message 'You can customize text to appear at the end of an install or remove it completely for unattended installations.' -ButtonRightText 'OK' -Icon Information -NoWait
    }
}

function Uninstall-ADTDeployment
{
    [CmdletBinding()]
    param
    (
    )

    ##================================================
    ## MARK: Pre-Uninstall
    ##================================================
    $adtSession.InstallPhase = "Pre-$($adtSession.DeploymentType)"

    ## If there are processes to close, show Welcome Message with a 60 second countdown before automatically closing.
    if ($adtSession.AppProcessesToClose.Count -gt 0)
    {
        Show-ADTInstallationWelcome -CloseProcesses $adtSession.AppProcessesToClose -CloseProcessesCountdown 60
    }

    ## Show Progress Message (with the default message).
    Show-ADTInstallationProgress

    ## <Perform Pre-Uninstallation tasks here>


    ##================================================
    ## MARK: Uninstall
    ##================================================
    $adtSession.InstallPhase = $adtSession.DeploymentType


    ## <Perform Uninstallation tasks here>


    ##================================================
    ## MARK: Post-Uninstallation
    ##================================================
    $adtSession.InstallPhase = "Post-$($adtSession.DeploymentType)"

    ## <Perform Post-Uninstallation tasks here>
}

function Repair-ADTDeployment
{
    [CmdletBinding()]
    param
    (
    )

    ##================================================
    ## MARK: Pre-Repair
    ##================================================
    $adtSession.InstallPhase = "Pre-$($adtSession.DeploymentType)"

    ## If there are processes to close, show Welcome Message with a 60 second countdown before automatically closing.
    if ($adtSession.AppProcessesToClose.Count -gt 0)
    {
        Show-ADTInstallationWelcome -CloseProcesses $adtSession.AppProcessesToClose -CloseProcessesCountdown 60
    }

    ## Show Progress Message (with the default message).
    Show-ADTInstallationProgress

    ## <Perform Pre-Repair tasks here>


    ##================================================
    ## MARK: Repair
    ##================================================
    $adtSession.InstallPhase = $adtSession.DeploymentType

    ## Handle Zero-Config MSI repairs.
    if ($adtSession.UseDefaultMsi)
    {
        $ExecuteDefaultMSISplat = @{ Action = $adtSession.DeploymentType; FilePath = $adtSession.DefaultMsiFile }
        if ($adtSession.DefaultMstFile)
        {
            $ExecuteDefaultMSISplat.Add('Transforms', $adtSession.DefaultMstFile)
        }
        Start-ADTMsiProcess @ExecuteDefaultMSISplat
    }

    ## <Perform Repair tasks here>


    ##================================================
    ## MARK: Post-Repair
    ##================================================
    $adtSession.InstallPhase = "Post-$($adtSession.DeploymentType)"

    ## <Perform Post-Repair tasks here>
}


##================================================
## MARK: Initialization
##================================================

# Set strict error handling across entire operation.
$ErrorActionPreference = [System.Management.Automation.ActionPreference]::Stop
$ProgressPreference = [System.Management.Automation.ActionPreference]::SilentlyContinue
Set-StrictMode -Version 1

# Import the module and instantiate a new session.
try
{
    # Import the module locally if available, otherwise try to find it from PSModulePath.
    if (Test-Path -LiteralPath "$PSScriptRoot\PSAppDeployToolkit\PSAppDeployToolkit.psd1" -PathType Leaf)
    {
        Get-ChildItem -LiteralPath "$PSScriptRoot\PSAppDeployToolkit" -Recurse -File | Unblock-File -ErrorAction Ignore
        Import-Module -FullyQualifiedName @{ ModuleName = "$PSScriptRoot\PSAppDeployToolkit\PSAppDeployToolkit.psd1"; Guid = '8c3c366b-8606-4576-9f2d-4051144f7ca2'; ModuleVersion = '4.1.7' } -Force
    }
    else
    {
        Import-Module -FullyQualifiedName @{ ModuleName = 'PSAppDeployToolkit'; Guid = '8c3c366b-8606-4576-9f2d-4051144f7ca2'; ModuleVersion = '4.1.7' } -Force
    }

    # Open a new deployment session, replacing $adtSession with a DeploymentSession.
    $iadtParams = Get-ADTBoundParametersAndDefaultValues -Invocation $MyInvocation
    $adtSession = Remove-ADTHashtableNullOrEmptyValues -Hashtable $adtSession
    $adtSession = Open-ADTSession @adtSession @iadtParams -PassThru
}
catch
{
    $Host.UI.WriteErrorLine((Out-String -InputObject $_ -Width ([System.Int32]::MaxValue)))
    exit 60008
}


##================================================
## MARK: Invocation
##================================================

# Commence the actual deployment operation.
try
{
    # Import any found extensions before proceeding with the deployment.
    Get-ChildItem -LiteralPath $PSScriptRoot -Directory | & {
        process
        {
            if ($_.Name -match 'PSAppDeployToolkit\..+$')
            {
                Get-ChildItem -LiteralPath $_.FullName -Recurse -File | Unblock-File -ErrorAction Ignore
                Import-Module -Name $_.FullName -Force
            }
        }
    }

    # Invoke the deployment and close out the session.
    & "$($adtSession.DeploymentType)-ADTDeployment"
    Close-ADTSession
}
catch
{
    # An unhandled error has been caught.
    $mainErrorMessage = "An unhandled error within [$($MyInvocation.MyCommand.Name)] has occurred.`n$(Resolve-ADTErrorRecord -ErrorRecord $_)"
    Write-ADTLogEntry -Message $mainErrorMessage -Severity 3

    ## Error details hidden from the user by default. Show a simple dialog with full stack trace:
    # Show-ADTDialogBox -Text $mainErrorMessage -Icon Stop -NoWait

    ## Or, a themed dialog with basic error message:
    # Show-ADTInstallationPrompt -Message "$($adtSession.DeploymentType) failed at line $($_.InvocationInfo.ScriptLineNumber), char $($_.InvocationInfo.OffsetInLine):`n$($_.InvocationInfo.Line.Trim())`n`nMessage:`n$($_.Exception.Message)" -ButtonRightText OK -Icon Error -NoWait

    Close-ADTSession -ExitCode 60001
}


# SIG # Begin signature block
# MIItzQYJKoZIhvcNAQcCoIItvjCCLboCAQExDzANBglghkgBZQMEAgEFADB5Bgor
# BgEEAYI3AgEEoGswaTA0BgorBgEEAYI3AgEeMCYCAwEAAAQQH8w7YFlLCE63JNLG
# KX7zUQIBAAIBAAIBAAIBAAIBADAxMA0GCWCGSAFlAwQCAQUABCDuVb8da3qKnl3x
# gHwce0DMONhThhNV79PUzeEm2HyAZqCCJ9UwggWNMIIEdaADAgECAhAOmxiO+dAt
# 5+/bUOIIQBhaMA0GCSqGSIb3DQEBDAUAMGUxCzAJBgNVBAYTAlVTMRUwEwYDVQQK
# EwxEaWdpQ2VydCBJbmMxGTAXBgNVBAsTEHd3dy5kaWdpY2VydC5jb20xJDAiBgNV
# BAMTG0RpZ2lDZXJ0IEFzc3VyZWQgSUQgUm9vdCBDQTAeFw0yMjA4MDEwMDAwMDBa
# Fw0zMTExMDkyMzU5NTlaMGIxCzAJBgNVBAYTAlVTMRUwEwYDVQQKEwxEaWdpQ2Vy
# dCBJbmMxGTAXBgNVBAsTEHd3dy5kaWdpY2VydC5jb20xITAfBgNVBAMTGERpZ2lD
# ZXJ0IFRydXN0ZWQgUm9vdCBHNDCCAiIwDQYJKoZIhvcNAQEBBQADggIPADCCAgoC
# ggIBAL/mkHNo3rvkXUo8MCIwaTPswqclLskhPfKK2FnC4SmnPVirdprNrnsbhA3E
# MB/zG6Q4FutWxpdtHauyefLKEdLkX9YFPFIPUh/GnhWlfr6fqVcWWVVyr2iTcMKy
# unWZanMylNEQRBAu34LzB4TmdDttceItDBvuINXJIB1jKS3O7F5OyJP4IWGbNOsF
# xl7sWxq868nPzaw0QF+xembud8hIqGZXV59UWI4MK7dPpzDZVu7Ke13jrclPXuU1
# 5zHL2pNe3I6PgNq2kZhAkHnDeMe2scS1ahg4AxCN2NQ3pC4FfYj1gj4QkXCrVYJB
# MtfbBHMqbpEBfCFM1LyuGwN1XXhm2ToxRJozQL8I11pJpMLmqaBn3aQnvKFPObUR
# WBf3JFxGj2T3wWmIdph2PVldQnaHiZdpekjw4KISG2aadMreSx7nDmOu5tTvkpI6
# nj3cAORFJYm2mkQZK37AlLTSYW3rM9nF30sEAMx9HJXDj/chsrIRt7t/8tWMcCxB
# YKqxYxhElRp2Yn72gLD76GSmM9GJB+G9t+ZDpBi4pncB4Q+UDCEdslQpJYls5Q5S
# UUd0viastkF13nqsX40/ybzTQRESW+UQUOsxxcpyFiIJ33xMdT9j7CFfxCBRa2+x
# q4aLT8LWRV+dIPyhHsXAj6KxfgommfXkaS+YHS312amyHeUbAgMBAAGjggE6MIIB
# NjAPBgNVHRMBAf8EBTADAQH/MB0GA1UdDgQWBBTs1+OC0nFdZEzfLmc/57qYrhwP
# TzAfBgNVHSMEGDAWgBRF66Kv9JLLgjEtUYunpyGd823IDzAOBgNVHQ8BAf8EBAMC
# AYYweQYIKwYBBQUHAQEEbTBrMCQGCCsGAQUFBzABhhhodHRwOi8vb2NzcC5kaWdp
# Y2VydC5jb20wQwYIKwYBBQUHMAKGN2h0dHA6Ly9jYWNlcnRzLmRpZ2ljZXJ0LmNv
# bS9EaWdpQ2VydEFzc3VyZWRJRFJvb3RDQS5jcnQwRQYDVR0fBD4wPDA6oDigNoY0
# aHR0cDovL2NybDMuZGlnaWNlcnQuY29tL0RpZ2lDZXJ0QXNzdXJlZElEUm9vdENB
# LmNybDARBgNVHSAECjAIMAYGBFUdIAAwDQYJKoZIhvcNAQEMBQADggEBAHCgv0Nc
# Vec4X6CjdBs9thbX979XB72arKGHLOyFXqkauyL4hxppVCLtpIh3bb0aFPQTSnov
# Lbc47/T/gLn4offyct4kvFIDyE7QKt76LVbP+fT3rDB6mouyXtTP0UNEm0Mh65Zy
# oUi0mcudT6cGAxN3J0TU53/oWajwvy8LpunyNDzs9wPHh6jSTEAZNUZqaVSwuKFW
# juyk1T3osdz9HNj0d1pcVIxv76FQPfx2CWiEn2/K2yCNNWAcAgPLILCsWKAOQGPF
# mCLBsln1VWvPJ6tsds5vIy30fnFqI2si/xK4VC0nftg62fC2h5b9W9FcrBjDTZ9z
# twGpn1eqXijiuZQwggWxMIIDmaADAgECAhBEMZxfkegWL04Ac/ZquHHYMA0GCSqG
# SIb3DQEBCwUAMFMxCzAJBgNVBAYTAkVVMSkwJwYDVQQKDCBFVVJPUEVBTiBTWVNU
# RU0gT0YgQ0VOVFJBTCBCQU5LUzEZMBcGA1UEAwwQRVNDQi1QS0kgUk9PVCBDQTAe
# Fw0xMTA2MjExMDM1MzRaFw00MTA2MjExMDM1MzRaMFMxCzAJBgNVBAYTAkVVMSkw
# JwYDVQQKDCBFVVJPUEVBTiBTWVNURU0gT0YgQ0VOVFJBTCBCQU5LUzEZMBcGA1UE
# AwwQRVNDQi1QS0kgUk9PVCBDQTCCAiIwDQYJKoZIhvcNAQEBBQADggIPADCCAgoC
# ggIBAJ51BFcodq55BaqPGUxWba8rwWZ2yB7qT8KSCIIRgzf1wJcBnePuAExh3tDC
# iY5xiXxpsNi/ivzqYJIUJGihlxXPmNT857DVnZo5dkbx9aF5kcZnXEU4eM1SKz9n
# P6g9rCgbBafw45EcCUOqN28RiXfZ+FKkUTLNyn4KYVQMmJQTBsyttihaS1PIc5aI
# 5401lhpHzMadnuNKhIDi3qRB7A2ZVRqAijpiPe+ilXCYesKlqFIVfXrZgJPe94KF
# 2H5uuup2aYqN1bAyNiWuF7hefgqLvevp+kbbAjhjewX9HTcsF41unJIfXUMiY4Dd
# X3ncSZZJrxxgLw0Axq4smgtly8vOarQrzpwRmbuCZc5icbaPSVazaM3EpUzfGvAR
# iFbFmH84b298dBmd5lQYqxJ5zqyS5wX+cjrUyp3KdEgOuehKrudLIhxoJF8k5OUH
# N4LkuuLgDrifYi//HlEA1GiDwFmSpKwbEU0gh4i+b2YuZ+BTiMEu1kdI0DaSeLwX
# Xl0+nk9YqJfKrqS6nmqfV8lrQhavHLxBl6AAdocyWH5bM1sWhYejy2i3808A17TU
# fTMEQO/3wsmJDCzjPp3v9GCCv+6LARgRXopJFDFHqznFMBGBVD80PKe5lmI2b8zs
# xsLLafA4kkCuLl19tZfu1VSnzriGMNEvT+CZmUsNO2Fhg1U9AgMBAAGjgYAwfjA8
# BgNVHSAENTAzMDEGBFUdIAAwKTAnBggrBgEFBQcCARYbaHR0cDovL3BraS5lc2Ni
# LmV1L3BvbGljaWVzMA8GA1UdEwEB/wQFMAMBAf8wDgYDVR0PAQH/BAQDAgEGMB0G
# A1UdDgQWBBTVhR1pY5coyVnm0WcHzVS83AJ+6jANBgkqhkiG9w0BAQsFAAOCAgEA
# FUMGgyf2XNPqjrZ0LH0DEWcJHwDzF0Ka1ew8lq8uRqx/K4yz+90tMt4dkh3wI9r3
# o6R+aG6aD2EtLYqmVmZbptFuukVWIAtw6JdL1sAqkTbHtisr+z25RNH7j9W/7ciR
# f/AJQE5dTdNdXJkEn3pVMCG7nZ4M6Z8JpnGoUUWI5VPEYRRhrHJ/WpQL9im26H7a
# s1ojyoO63MaA4AY6fnzzETFdEJy5JSrCGkWcsUg7cKZk2fUHQcfsgAqWgJNrGZHs
# 1PzJ2dnGhbzuJq3fct2dEkI5PqKw78KvtDdhU0uO0uN5XQXrcnJJZnl2V0PUZm/z
# hASUaVJhgd2WxbCDrjw3NUYVqy/ylGzbjuWmdyCtcbv18vGpgBScuOcM6+QijTJ2
# JvyNRHmqtpBKdCO1drmE5LDZGH2502gYSYpX08ySE1VYHbSVV6Q+JVdIfStVntJM
# Qf97jqX/y8UsKK73JdqZTkW5uitao6zl8wityxwXtvLoOJZvO6JAKmJcWM+Iky1C
# On6EsqIWgpdiXxoYYlF5ldMYf/YU6Lc1pfIEU62f4jGKp/qWb4eSTwVwuFq3SmT1
# wGWSRra9Tr5CTCPfq+4L6o590+zPy+68F29mlNp18kxlgN0g7TGOT/N36pG6NOlA
# jYh8GtFCbMz3Vj8qCLHkwQxJvIJY3FpfsQ63bfwjadkwgga0MIIEnKADAgECAhAN
# x6xXBf8hmS5AQyIMOkmGMA0GCSqGSIb3DQEBCwUAMGIxCzAJBgNVBAYTAlVTMRUw
# EwYDVQQKEwxEaWdpQ2VydCBJbmMxGTAXBgNVBAsTEHd3dy5kaWdpY2VydC5jb20x
# ITAfBgNVBAMTGERpZ2lDZXJ0IFRydXN0ZWQgUm9vdCBHNDAeFw0yNTA1MDcwMDAw
# MDBaFw0zODAxMTQyMzU5NTlaMGkxCzAJBgNVBAYTAlVTMRcwFQYDVQQKEw5EaWdp
# Q2VydCwgSW5jLjFBMD8GA1UEAxM4RGlnaUNlcnQgVHJ1c3RlZCBHNCBUaW1lU3Rh
# bXBpbmcgUlNBNDA5NiBTSEEyNTYgMjAyNSBDQTEwggIiMA0GCSqGSIb3DQEBAQUA
# A4ICDwAwggIKAoICAQC0eDHTCphBcr48RsAcrHXbo0ZodLRRF51NrY0NlLWZloMs
# VO1DahGPNRcybEKq+RuwOnPhof6pvF4uGjwjqNjfEvUi6wuim5bap+0lgloM2zX4
# kftn5B1IpYzTqpyFQ/4Bt0mAxAHeHYNnQxqXmRinvuNgxVBdJkf77S2uPoCj7GH8
# BLuxBG5AvftBdsOECS1UkxBvMgEdgkFiDNYiOTx4OtiFcMSkqTtF2hfQz3zQSku2
# Ws3IfDReb6e3mmdglTcaarps0wjUjsZvkgFkriK9tUKJm/s80FiocSk1VYLZlDwF
# t+cVFBURJg6zMUjZa/zbCclF83bRVFLeGkuAhHiGPMvSGmhgaTzVyhYn4p0+8y9o
# HRaQT/aofEnS5xLrfxnGpTXiUOeSLsJygoLPp66bkDX1ZlAeSpQl92QOMeRxykvq
# 6gbylsXQskBBBnGy3tW/AMOMCZIVNSaz7BX8VtYGqLt9MmeOreGPRdtBx3yGOP+r
# x3rKWDEJlIqLXvJWnY0v5ydPpOjL6s36czwzsucuoKs7Yk/ehb//Wx+5kMqIMRvU
# BDx6z1ev+7psNOdgJMoiwOrUG2ZdSoQbU2rMkpLiQ6bGRinZbI4OLu9BMIFm1UUl
# 9VnePs6BaaeEWvjJSjNm2qA+sdFUeEY0qVjPKOWug/G6X5uAiynM7Bu2ayBjUwID
# AQABo4IBXTCCAVkwEgYDVR0TAQH/BAgwBgEB/wIBADAdBgNVHQ4EFgQU729TSunk
# Bnx6yuKQVvYv1Ensy04wHwYDVR0jBBgwFoAU7NfjgtJxXWRM3y5nP+e6mK4cD08w
# DgYDVR0PAQH/BAQDAgGGMBMGA1UdJQQMMAoGCCsGAQUFBwMIMHcGCCsGAQUFBwEB
# BGswaTAkBggrBgEFBQcwAYYYaHR0cDovL29jc3AuZGlnaWNlcnQuY29tMEEGCCsG
# AQUFBzAChjVodHRwOi8vY2FjZXJ0cy5kaWdpY2VydC5jb20vRGlnaUNlcnRUcnVz
# dGVkUm9vdEc0LmNydDBDBgNVHR8EPDA6MDigNqA0hjJodHRwOi8vY3JsMy5kaWdp
# Y2VydC5jb20vRGlnaUNlcnRUcnVzdGVkUm9vdEc0LmNybDAgBgNVHSAEGTAXMAgG
# BmeBDAEEAjALBglghkgBhv1sBwEwDQYJKoZIhvcNAQELBQADggIBABfO+xaAHP4H
# PRF2cTC9vgvItTSmf83Qh8WIGjB/T8ObXAZz8OjuhUxjaaFdleMM0lBryPTQM2qE
# JPe36zwbSI/mS83afsl3YTj+IQhQE7jU/kXjjytJgnn0hvrV6hqWGd3rLAUt6vJy
# 9lMDPjTLxLgXf9r5nWMQwr8Myb9rEVKChHyfpzee5kH0F8HABBgr0UdqirZ7bowe
# 9Vj2AIMD8liyrukZ2iA/wdG2th9y1IsA0QF8dTXqvcnTmpfeQh35k5zOCPmSNq1U
# H410ANVko43+Cdmu4y81hjajV/gxdEkMx1NKU4uHQcKfZxAvBAKqMVuqte69M9J6
# A47OvgRaPs+2ykgcGV00TYr2Lr3ty9qIijanrUR3anzEwlvzZiiyfTPjLbnFRsjs
# Yg39OlV8cipDoq7+qNNjqFzeGxcytL5TTLL4ZaoBdqbhOhZ3ZRDUphPvSRmMThi0
# vw9vODRzW6AxnJll38F0cuJG7uEBYTptMSbhdhGQDpOXgpIUsWTjd6xpR6oaQf/D
# Jbg3s6KCLPAlZ66RzIg9sC+NJpud/v4+7RWsWCiKi9EOLLHfMR2ZyJ/+xhCx9yHb
# xtl5TPau1j/1MIDpMPx0LckTetiSuEtQvLsNz3Qbp7wGWqbIiOWCnb5WqxL3/BAP
# vIXKUjPSxyZsq8WhbaM2tszWkPZPubdcMIIG7TCCBNWgAwIBAgIQCoDvGEuN8QWC
# 0cR2p5V0aDANBgkqhkiG9w0BAQsFADBpMQswCQYDVQQGEwJVUzEXMBUGA1UEChMO
# RGlnaUNlcnQsIEluYy4xQTA/BgNVBAMTOERpZ2lDZXJ0IFRydXN0ZWQgRzQgVGlt
# ZVN0YW1waW5nIFJTQTQwOTYgU0hBMjU2IDIwMjUgQ0ExMB4XDTI1MDYwNDAwMDAw
# MFoXDTM2MDkwMzIzNTk1OVowYzELMAkGA1UEBhMCVVMxFzAVBgNVBAoTDkRpZ2lD
# ZXJ0LCBJbmMuMTswOQYDVQQDEzJEaWdpQ2VydCBTSEEyNTYgUlNBNDA5NiBUaW1l
# c3RhbXAgUmVzcG9uZGVyIDIwMjUgMTCCAiIwDQYJKoZIhvcNAQEBBQADggIPADCC
# AgoCggIBANBGrC0Sxp7Q6q5gVrMrV7pvUf+GcAoB38o3zBlCMGMyqJnfFNZx+wvA
# 69HFTBdwbHwBSOeLpvPnZ8ZN+vo8dE2/pPvOx/Vj8TchTySA2R4QKpVD7dvNZh6w
# W2R6kSu9RJt/4QhguSssp3qome7MrxVyfQO9sMx6ZAWjFDYOzDi8SOhPUWlLnh00
# Cll8pjrUcCV3K3E0zz09ldQ//nBZZREr4h/GI6Dxb2UoyrN0ijtUDVHRXdmncOOM
# A3CoB/iUSROUINDT98oksouTMYFOnHoRh6+86Ltc5zjPKHW5KqCvpSduSwhwUmot
# uQhcg9tw2YD3w6ySSSu+3qU8DD+nigNJFmt6LAHvH3KSuNLoZLc1Hf2JNMVL4Q1O
# pbybpMe46YceNA0LfNsnqcnpJeItK/DhKbPxTTuGoX7wJNdoRORVbPR1VVnDuSeH
# VZlc4seAO+6d2sC26/PQPdP51ho1zBp+xUIZkpSFA8vWdoUoHLWnqWU3dCCyFG1r
# oSrgHjSHlq8xymLnjCbSLZ49kPmk8iyyizNDIXj//cOgrY7rlRyTlaCCfw7aSURO
# wnu7zER6EaJ+AliL7ojTdS5PWPsWeupWs7NpChUk555K096V1hE0yZIXe+giAwW0
# 0aHzrDchIc2bQhpp0IoKRR7YufAkprxMiXAJQ1XCmnCfgPf8+3mnAgMBAAGjggGV
# MIIBkTAMBgNVHRMBAf8EAjAAMB0GA1UdDgQWBBTkO/zyMe39/dfzkXFjGVBDz2GM
# 6DAfBgNVHSMEGDAWgBTvb1NK6eQGfHrK4pBW9i/USezLTjAOBgNVHQ8BAf8EBAMC
# B4AwFgYDVR0lAQH/BAwwCgYIKwYBBQUHAwgwgZUGCCsGAQUFBwEBBIGIMIGFMCQG
# CCsGAQUFBzABhhhodHRwOi8vb2NzcC5kaWdpY2VydC5jb20wXQYIKwYBBQUHMAKG
# UWh0dHA6Ly9jYWNlcnRzLmRpZ2ljZXJ0LmNvbS9EaWdpQ2VydFRydXN0ZWRHNFRp
# bWVTdGFtcGluZ1JTQTQwOTZTSEEyNTYyMDI1Q0ExLmNydDBfBgNVHR8EWDBWMFSg
# UqBQhk5odHRwOi8vY3JsMy5kaWdpY2VydC5jb20vRGlnaUNlcnRUcnVzdGVkRzRU
# aW1lU3RhbXBpbmdSU0E0MDk2U0hBMjU2MjAyNUNBMS5jcmwwIAYDVR0gBBkwFzAI
# BgZngQwBBAIwCwYJYIZIAYb9bAcBMA0GCSqGSIb3DQEBCwUAA4ICAQBlKq3xHCcE
# ua5gQezRCESeY0ByIfjk9iJP2zWLpQq1b4URGnwWBdEZD9gBq9fNaNmFj6Eh8/Ym
# RDfxT7C0k8FUFqNh+tshgb4O6Lgjg8K8elC4+oWCqnU/ML9lFfim8/9yJmZSe2F8
# AQ/UdKFOtj7YMTmqPO9mzskgiC3QYIUP2S3HQvHG1FDu+WUqW4daIqToXFE/JQ/E
# ABgfZXLWU0ziTN6R3ygQBHMUBaB5bdrPbF6MRYs03h4obEMnxYOX8VBRKe1uNnzQ
# VTeLni2nHkX/QqvXnNb+YkDFkxUGtMTaiLR9wjxUxu2hECZpqyU1d0IbX6Wq8/gV
# utDojBIFeRlqAcuEVT0cKsb+zJNEsuEB7O7/cuvTQasnM9AWcIQfVjnzrvwiCZ85
# EE8LUkqRhoS3Y50OHgaY7T/lwd6UArb+BOVAkg2oOvol/DJgddJ35XTxfUlQ+8Hg
# gt8l2Yv7roancJIFcbojBcxlRcGG0LIhp6GvReQGgMgYxQbV1S3CrWqZzBt1R9xJ
# gKf47CdxVRd/ndUlQ05oxYy2zRWVFjF7mcr4C34Mj3ocCVccAvlKV9jEnstrniLv
# UxxVZE/rptb7IRE2lskKPIJgbaP5t2nGj/ULLi49xTcBZU8atufk+EMF/cWuiC7P
# OGT75qaL6vdCvHlshtjdNXOCIUjsarfNZzCCByEwggULoAMCAQICEhEhSVgE4ecG
# aV3R0SmX+u9mUzALBgkqhkiG9w0BAQswUzELMAkGA1UEBhMCRVUxKTAnBgNVBAoM
# IEVVUk9QRUFOIFNZU1RFTSBPRiBDRU5UUkFMIEJBTktTMRkwFwYDVQQDDBBFU0NC
# LVBLSSBST09UIENBMB4XDTIzMDYwODE1MDcwMFoXDTM4MDYwODE1MDcwMFowWjEL
# MAkGA1UEBhMCRVUxKTAnBgNVBAoMIEV1cm9wZWFuIFN5c3RlbSBvZiBDZW50cmFs
# IEJhbmtzMSAwHgYDVQQDDBdFU0NCLVBLSSBPTkxJTkUgQ0EgVjEuMjCCAiIwDQYJ
# KoZIhvcNAQEBBQADggIPADCCAgoCggIBAJXgofrPjb7zq7sFa3QMduMfNBBSPNvg
# 9dKHtufCMxRE42UkiUxMv/dgK7OL52XXiVr8Ha5aMN8XHUt8O+Qjxq5uRGKFe5DL
# FcWwgIOZkCxXm316+LVRqVaLYML7UPPUNfgrX2IqXRrk2RFmbMMP98GE69T/ctn6
# pd2QFGMxB52T5Li1QueVheyCp4l9b97STOgKTf7KYdKT/BlPW76SezJxn4vKg7gF
# EtD9Cy/z3LbGyKx7psp9DIWRhviUXS6V2+uaK+49+0JV7eEk+nE6NqeDKe3sChOq
# MC0YabsICe0bfLiE07t1E7hOfvqfwMMZqLW2KR2eEU4jgPOdXYvL3b5H9W+cV/OL
# p/+7g+xFn2Q6shAZ2uddR2Pf8V4fPn8B+y4Uqr4Juuzjo9MGB4MjkMSVA49iGdmG
# BVXrfog4g3HFor6uIeC39RLbZUeAJIuPIYUDgGM+0AwRYMKUZrY4wKiRy9CNYTHk
# Mc97UVuBR4lrXR540POhcmSvjM2lP830lf7O7ijvo4N/i98BhbD0QX+jneeauhwb
# /WBX3djbaytb7XPKBQnaRApJfamOIxmgb+bkYypScdGTSDByeJGGBdgXW8V/QCVr
# KSm1EYMa1C0Ig5ImQlI1Z5qKxZJXhlnu/dghm5/qFxTyyEfseQrsswjaIwdW3k4b
# fWuyhc6uwwSrAgMBAAGjggHqMIIB5jAOBgNVHQ8BAf8EBAMCAQYwEQYDVR0gBAow
# CDAGBgRVHSAAMBIGA1UdEwEB/wQIMAYBAf8CAQAwggEqBgNVHR8EggEhMIIBHTAo
# oCagJIYiaHR0cDovL3BraS5lc2NiLmV1L2NybHMvcm9vdENBLmNybDCBnKCBmaCB
# loaBk2xkYXA6Ly9sZGFwLXBraS5lc2NiLmV1L0NOPUVTQ0IlMjBQS0klMjBSb290
# JTIwQ0EsT1U9UEtJLE9VPUVTQ0IlMjBQS0ksTz1FU0NCLEM9RVU/Y2VydGlmaWNh
# dGVSZXZvY2F0aW9uTGlzdD9iYXNlP29iamVjdGNsYXNzPWNSTERpc3RyaWJ1dGlv
# blBvaW50KTAsoCqgKIYmaHR0cDovL2lhbS1jcmwuZXNjYi5ldS9lc2NiL3Jvb3RD
# QS5jcmwwJKAioCCGHmh0dHA6Ly9lc2NicGtpL2NybHMvcm9vdENBLmNybDA/Bggr
# BgEFBQcBAQQzMDEwLwYIKwYBBQUHMAKGI2h0dHA6Ly9wa2kuZXNjYi5ldS9jZXJ0
# cy9yb290Q0EuY3J0MB0GA1UdDgQWBBQ9baO9BVBraqyB7qPwRztiW0we6TAfBgNV
# HSMEGDAWgBTVhR1pY5coyVnm0WcHzVS83AJ+6jALBgkqhkiG9w0BAQsDggIBAG5A
# z3yiI+Q0w+Qb5YFvYWySpSgwyxUyVivr4L5N04crntbzf1lnMo2onRakzcz5D0bM
# nmjTQvywbMI0GF+IjRAkRoK0AvQ7ZtNI/4Wc/coySUpHGmNnaAUU8Hnc53EGj4Ai
# xF+a2+hkQar4JySkIbNNHNWTQ39tjsAEgq7QDj60sz84RqGjyv/5iRvUjNDiUdX/
# QAir8NGTBs80eq6pEcnsJe2BT5NqZhwAPg7EmhtHyiZFNje9GrLvs0hXqhNft+Ii
# IKrLnoE6B5/3sSXxovmvnr/SA3A05xJFb5k4/0q9pcOMXrGj7F8r06xQo1eMFFuE
# hEBbGI36LIUbVi2etdQ4IGjFRbVSWh8bqEA0hmEvzdE+YsVKZ3a11k/88k4wsWdC
# Ojzfaf0F/PqDhSAztRMNRxPqfjQn0QhEB6tEpcGNp4E+pt5p4rYKExKJWxO+5j0x
# Hnkg05txf6APPgTv8ROq947uW9KasUV1AYVD6WpfrUZksvVy6g93pQ+lIsFjlzWR
# PFNPMvjwdbKtm/95U6n+hzAo0pddcXfET0UN/Ox0Xul7iK/eKWp9woYRlFLH1Au3
# XhSGdi+An7WtLOkhYTumm0BgU99SzXQamvur7gZq41sswKPtCtgM8RF/3LVAohzd
# 7a2leUEFNgk6nj2L2u+clvmN804to3PlSJoHosH6MIIHvTCCBaWgAwIBAgIUK1QH
# rlez/xH8zRHtzmxsCF3vHWUwDQYJKoZIhvcNAQELBQAwWjELMAkGA1UEBhMCRVUx
# KTAnBgNVBAoMIEV1cm9wZWFuIFN5c3RlbSBvZiBDZW50cmFsIEJhbmtzMSAwHgYD
# VQQDDBdFU0NCLVBLSSBPTkxJTkUgQ0EgVjEuMjAeFw0yNTEwMTYxMzE2MTdaFw0y
# ODEwMTYxMzE2MTdaMHQxCzAJBgNVBAYTAkJFMSkwJwYDVQQKDCBFVVJPUEVBTiBT
# WVNURU0gT0YgQ0VOVFJBTCBCQU5LUzEmMCQGA1UECwwdTmF0aW9uYWwgQmFuayBv
# ZiBCZWxnaXVtIChCRSkxEjAQBgNVBAMMCVBhY2thZ2luZzCCASIwDQYJKoZIhvcN
# AQEBBQADggEPADCCAQoCggEBAKbx+h54PozE9PMwFXC+9fdaBOOizuBLxXFuQI9g
# mgu8NmTGqd/4cJiAndiS1FBH3+Q4S4WdLrIu8KtwUpskYw7sFwOHLaOYyJfuBjTM
# 6T1YRVEOuX5Mq6QnvhzbQ7Rmj+wu8/JwJBNg6IGlqjfLufZxMh2sB4b32bgQB/sA
# +Udsg1Lc7TnXwyMkroCAoDF1WEHToy+13mmMofZPTNSYBuH+YY490dGTehsgx1ca
# nGMa5XPLnl86NfVTaXjrfOq9BXr4NzSNAFprIWU160f+nX4161T5vFla3uf7SlPk
# mvXbEj5XB+gubunjX/pqalWAvTbWK+RKxyAM9gkGowqcEW8CAwEAAaOCA18wggNb
# MB0GA1UdDgQWBBT7njRPGdfnaKr1+6YXud75X1H6FzAfBgNVHSMEGDAWgBQ9baO9
# BVBraqyB7qPwRztiW0we6TAMBgNVHRMBAf8EAjAAMA4GA1UdDwEB/wQEAwIHgDAT
# BgNVHSUEDDAKBggrBgEFBQcDAzCCASUGA1UdHwSCARwwggEYMIIBFKCCARCgggEM
# hiBodHRwOi8vZXNjYnBraS9jcmxzL3N1YkNBdjEyLmNybIYkaHR0cDovL3BraS5l
# c2NiLmV1L2NybHMvc3ViQ0F2MTIuY3JshoGXbGRhcDovL2xkYXAtcGtpLmVzY2Iu
# ZXUvQ049RVNDQi1QS0klMjBPTkxJTkUlMjBDQSUyMFYxLjIsT1U9UEtJLE9VPUVT
# Q0ItUEtJLE89RVNDQixDPUVVP2NlcnRpZmljYXRlUmV2b2NhdGlvbkxpc3Q/YmFz
# ZT9vYmplY3RjbGFzcz1jUkxEaXN0cmlidXRpb25Qb2ludIYoaHR0cDovL2lhbS1j
# cmwuZXNjYi5ldS9lc2NiL3N1YkNBdjEyLmNybDAkBgNVHREEHTAbgRlzb2Z0d2Fy
# ZS5wYWNrYWdpbmdAbmJiLmJlMHoGA1UdIARzMHEwNwYJBAB/AAoBAgQDMCowKAYI
# KwYBBQUHAgEWHGh0dHBzOi8vcGtpLmVzY2IuZXUvcG9saWNpZXMwNgYIBAB/AAoB
# AgEwKjAoBggrBgEFBQcCARYcaHR0cHM6Ly9wa2kuZXNjYi5ldS9wb2xpY2llczAc
# BggEAH8ACgEDAgQQQkFOQ08gREUgRVNQQcORQTAbBggEAH8ACgEDAwQPVkFURVMt
# UTI4MDI0NzJHMIHfBggrBgEFBQcBAQSB0jCBzzAvBggrBgEFBQcwAoYjaHR0cDov
# L3BraS5lc2NiLmV1L2NlcnRzL3Jvb3RDQS5jcnQwMQYIKwYBBQUHMAKGJWh0dHA6
# Ly9wa2kuZXNjYi5ldS9jZXJ0cy9zdWJDQXYxMi5jcnQwHwYIKwYBBQUHMAGGE2h0
# dHA6Ly9vY3NwLWVzY2Jwa2kwIwYIKwYBBQUHMAGGF2h0dHA6Ly9vY3NwLXBraS5l
# c2NiLmV1MCMGCCsGAQUFBzABhhdodHRwOi8vaWFtLW9jc3AuZXNjYi5ldTANBgkq
# hkiG9w0BAQsFAAOCAgEAGs7Q0cESAohNtd3FYblDTGcMrug66mvRw+/mrr2ltHj2
# 3EEWh0IunkvjJ6XeWEX62SBStBaKb60RXGt5smRog25s6EQx1uYPO9cwxCMAjdQK
# eklMH0gl78nb9LZdUcuy6W/buKvZ6KuM50g3rnmUTedoC0LeHygz8kLzORg5NkWb
# UNyRdqKH70LjXgW1YKdP1bxm4p+fiLm1mrA0v/kZrajzVng/CndT3z+lyRtam7AI
# d7CYwE7wP78eRvEZS1rsL25swWZqYYYtAARwKpsGqcgHE/biyr3W83iyA8ZcL14H
# 9idNc7tLXRU6vL4ZuDa6misbHetvJ2cwMFKyrCKDGiSBSgs70Ygl2jL2dFeumm9v
# oUGlPuzu1k596X9XnBjuyGzPlMmFf46LvqnXN/AqoCbPcxQyZDiR9iGTa44KPkdz
# CnN29ojwrxJYCuwOTgqjJmolBLyp2iqefgLKxDFTXgI/vO1nsQMg4h3rOHKbfKNp
# SiPDkKsqO6hJmoCUvE6wPUvOrFeu28Z/5pzK67dgN12ghKyOqDCBIDtmPXhiXke7
# KUSCWc0PJJgYoSYMG7xITfPJUiYdWerMV7u3/FKEHVoV441itXn/XvhF1c/O398z
# nGg/3vWYLh9EgjTFmvCEBA0+NlX3dENphN/0xeQ4EQfjsN1ARqz+HWTPG/xV5swx
# ggVOMIIFSgIBATByMFoxCzAJBgNVBAYTAkVVMSkwJwYDVQQKDCBFdXJvcGVhbiBT
# eXN0ZW0gb2YgQ2VudHJhbCBCYW5rczEgMB4GA1UEAwwXRVNDQi1QS0kgT05MSU5F
# IENBIFYxLjICFCtUB65Xs/8R/M0R7c5sbAhd7x1lMA0GCWCGSAFlAwQCAQUAoIGE
# MBgGCisGAQQBgjcCAQwxCjAIoAKAAKECgAAwGQYJKoZIhvcNAQkDMQwGCisGAQQB
# gjcCAQQwHAYKKwYBBAGCNwIBCzEOMAwGCisGAQQBgjcCARUwLwYJKoZIhvcNAQkE
# MSIEIPuPqk2K3LhZR0J3/p4bF3LZh71TeGw9rlcnxPf8DRSzMA0GCSqGSIb3DQEB
# AQUABIIBAHMfrDMklasPxf7AyrjSruHSOu6tNBq9hRYx2bzKKDVKaf8E2v/SN3c7
# yvey7PKPKVc8Nvl1oRnhjnjJybIzAmqEBu2SvRDa+ThpM0Us2pSrfQ3fUQ+sfRTH
# Y5xmNmQQLRFoMj5UeINkkkKKO80QwSB3wbufxf/5XKRbx0+ed1uz/45YM+xJYn1O
# SxWLRP0GtumX2v4jgNOz0/Vf07XrSIGbnrHwuYafrrTYO6thaM70+WTbW/F7YVXH
# 3KHYq5QqwKeZTncSzEVSgw3zf9cZIENJ6ZIhDyNwgNW2mCH22kHeJSh8nnB+0nFW
# MGClZ/uFOurHOGODeZMWBiTQSaUkLxGhggMmMIIDIgYJKoZIhvcNAQkGMYIDEzCC
# Aw8CAQEwfTBpMQswCQYDVQQGEwJVUzEXMBUGA1UEChMORGlnaUNlcnQsIEluYy4x
# QTA/BgNVBAMTOERpZ2lDZXJ0IFRydXN0ZWQgRzQgVGltZVN0YW1waW5nIFJTQTQw
# OTYgU0hBMjU2IDIwMjUgQ0ExAhAKgO8YS43xBYLRxHanlXRoMA0GCWCGSAFlAwQC
# AQUAoGkwGAYJKoZIhvcNAQkDMQsGCSqGSIb3DQEHATAcBgkqhkiG9w0BCQUxDxcN
# MjYwNDA3MTgxNzE2WjAvBgkqhkiG9w0BCQQxIgQgB9vtD3QD1iqYkaFQIkpjGhsC
# Z4ojy5rO5/prm55GAOgwDQYJKoZIhvcNAQEBBQAEggIApASUlwaVGsTU9hPweHuc
# W3nwikw0x181hvREAExgECgm3Dhfsj21INWd1kdiRnDKpVOttJbt78O5inCbouaV
# TCnbI00p+mOJk4If3vkOR9LBoxHschtSv3QYmlarrRUfL6d4LkYZ003JuSSk6807
# EMbbcwM45dwtnkdapg1Zk35VXljLgHEHxjyPemV5rP0uASdrgp5S+QEKGFU4E4+y
# LGvha/76BXIgpaH2kQYWGCzV4FTHWgKRZPEZU99r7oYg+IWqLHWKBwlc2i0Ebiyf
# +ontfpmmu45HE1rjsbWzfCsNadO8vyGy+UiFmQTjYtOBTv79QuGZwpszGkZWhVeZ
# sBpT049ljGIilicQCOlAKozdHRp9/NpYwI8eduFx06yi+oClFPnHCMvoASGAOYpL
# USQx6QHyA64eGETExtj1yuFK5Ir7ipS07BVhzl38oNb1630T1yFeSQLbY9UGPkpQ
# CZt80SjvVxymNebSBosohwHwdTiJpWNedav8U4Uhc9wS8F8jEMzLNyO9unbnpI5k
# gIfKXrjnSkPPXXHn8A1PwFIE4fjNT1oN1OYOI6p/rjX2DT1Dhmdv93gHNf5i8G5j
# 7S2v+mZp+w2RN4U/jGilQ6X5DBNH+wl+BJxKkACgu9mRmugfeaP3VjZU74AgzAbG
# V6atAytgzRmruSBQrHKO38k=
# SIG # End signature block

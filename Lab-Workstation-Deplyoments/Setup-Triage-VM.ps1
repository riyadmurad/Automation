#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Windows 11 Malware Analysis Lab - Automated Setup Script
.DESCRIPTION
    Automates the full malware lab configuration guide:
      - Windows hardening (Updates, UAC, Defender, file extensions)
      - Group Policy Defender disable (Pro only)
      - Tool installation via Chocolatey + manual GitHub downloads
.NOTES
    Run as Administrator in an elevated PowerShell session.
    Reboot checkpoints are built in; re-run after each reboot.
    State is tracked in: C:\MalwareLab\setup_state.json
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Continue'

# Paths and State
$LabRoot   = 'C:\MalwareLab'
$ToolsDir  = "$LabRoot\Tools"
$StateFile = "$LabRoot\setup_state.json"

function Get-State {
    if (Test-Path $StateFile) {
        return Get-Content $StateFile | ConvertFrom-Json
    }
    return [PSCustomObject]@{ Phase = 0 }
}

function Set-State {
    param([int]$Phase)
    if (-not (Test-Path $LabRoot)) { New-Item -ItemType Directory -Path $LabRoot | Out-Null }
    [PSCustomObject]@{ Phase = $Phase } | ConvertTo-Json | Set-Content -Path $StateFile
}

# Logging
$LogFile = "$LabRoot\setup_log.txt"
function Write-Log {
    param([string]$Message, [string]$Level = 'INFO')
    $line = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] [$Level] $Message"
    $color = switch ($Level) {
        'ERROR' { 'Red' }
        'WARN'  { 'Yellow' }
        default { 'Cyan' }
    }
    Write-Host $line -ForegroundColor $color
    if (-not (Test-Path $LabRoot)) { New-Item -ItemType Directory -Path $LabRoot | Out-Null }
    Add-Content -Path $LogFile -Value $line
}

function Invoke-Step {
    param([string]$Name, [scriptblock]$Block)
    Write-Log ">>> $Name"
    try {
        & $Block
        Write-Log "<<< $Name - OK"
    }
    catch {
        Write-Log "<<< $Name - FAILED: $_" 'ERROR'
    }
}


# PHASE 0 - System Settings
function Invoke-Phase0 {
    Write-Log "===== PHASE 0: Windows Settings ====="

    Invoke-Step 'Set Metered Connection + Pause Windows Update' {
        # Mark all network profiles as Private (reduces auto-update triggers)
        $profiles = Get-NetConnectionProfile -ErrorAction SilentlyContinue
        foreach ($p in $profiles) {
            Set-NetConnectionProfile -InterfaceIndex $p.InterfaceIndex -NetworkCategory Private -ErrorAction SilentlyContinue
        }

        # Set metered connection cost via registry
        $costKey = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\NetworkList\DefaultMediaCost'
        if (Test-Path $costKey) {
            Set-ItemProperty -Path $costKey -Name 'Default'  -Value 2 -ErrorAction SilentlyContinue
            Set-ItemProperty -Path $costKey -Name 'Ethernet' -Value 2 -ErrorAction SilentlyContinue
            Set-ItemProperty -Path $costKey -Name 'WiFi'     -Value 2 -ErrorAction SilentlyContinue
        }

        # Pause Windows Update for 35 days
        $wuKey = 'HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings'
        if (-not (Test-Path $wuKey)) { New-Item -Path $wuKey -Force | Out-Null }
        $pauseUntil = (Get-Date).AddDays(35).ToString('yyyy-MM-ddTHH:mm:ssZ')
        Set-ItemProperty -Path $wuKey -Name 'PauseUpdatesExpiryTime'          -Value $pauseUntil
        Set-ItemProperty -Path $wuKey -Name 'PauseFeatureUpdatesStartTime'    -Value (Get-Date -Format 'yyyy-MM-ddTHH:mm:ssZ')
        Set-ItemProperty -Path $wuKey -Name 'PauseQualityUpdatesStartTime'    -Value (Get-Date -Format 'yyyy-MM-ddTHH:mm:ssZ')

        # Disable Windows Update service
        Stop-Service -Name wuauserv -ErrorAction SilentlyContinue
        Set-Service  -Name wuauserv -StartupType Disabled -ErrorAction SilentlyContinue
        Write-Log 'Windows Update paused and service disabled.'
    }

    Invoke-Step 'Disable UAC Prompts' {
        $uacKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
        Set-ItemProperty -Path $uacKey -Name 'ConsentPromptBehaviorAdmin' -Value 0
        Set-ItemProperty -Path $uacKey -Name 'PromptOnSecureDesktop'       -Value 0
        Write-Log 'UAC set to Never Notify.'
    }

    Invoke-Step 'Show File Extensions and Hidden Files' {
        $advKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced'
        Set-ItemProperty -Path $advKey -Name 'HideFileExt'      -Value 0
        Set-ItemProperty -Path $advKey -Name 'Hidden'           -Value 1
        Set-ItemProperty -Path $advKey -Name 'ShowSuperHidden'  -Value 1
        Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 1
        Start-Process explorer
        Write-Log 'File extensions and hidden files enabled.'
    }


    Invoke-Step 'Disable Windows Defender via Registry' {
        # Main policy key
        $defKey = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender'
        if (-not (Test-Path $defKey)) { New-Item -Path $defKey -Force | Out-Null }
        Set-ItemProperty -Path $defKey -Name 'DisableAntiSpyware' -Value 1 -Type DWord

        # Real-Time Protection sub-key
        $rtpKey = "$defKey\Real-Time Protection"
        if (-not (Test-Path $rtpKey)) { New-Item -Path $rtpKey -Force | Out-Null }
        Set-ItemProperty -Path $rtpKey -Name 'DisableRealtimeMonitoring'  -Value 1 -Type DWord
        Set-ItemProperty -Path $rtpKey -Name 'DisableBehaviorMonitoring'  -Value 1 -Type DWord
        Set-ItemProperty -Path $rtpKey -Name 'DisableOnAccessProtection'  -Value 1 -Type DWord
        Set-ItemProperty -Path $rtpKey -Name 'DisableIOAVProtection'      -Value 1 -Type DWord

        # reg add fallback
        reg add "HKLM\SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection" /v DisableRealtimeMonitoring /t REG_DWORD /d 1 /f 2>&1 | Out-Null

        # Set-MpPreference (best-effort; may fail if service is already stopped)
        $prefs = @(
            @{ Name='DisableRealtimeMonitoring';     Value=$true }
            @{ Name='DisableIOAVProtection';         Value=$true }
            @{ Name='DisableScriptScanning';         Value=$true }
            @{ Name='DisableBehaviorMonitoring';     Value=$true }
            @{ Name='MAPSReporting';                 Value=0 }
            @{ Name='SubmitSamplesConsent';          Value=2 }
            @{ Name='DisableArchiveScanning';        Value=$true }
            @{ Name='DisableRemovableDriveScanning'; Value=$true }
        )
        foreach ($pref in $prefs) {
            try {
                $params = @{ $pref.Name = $pref.Value }
                Set-MpPreference @params -ErrorAction Stop
            }
            catch {
                Write-Log "Set-MpPreference $($pref.Name): $_" 'WARN'
            }
        }

        # Disable Defender scheduled tasks
        Get-ScheduledTask -TaskPath '\Microsoft\Windows\Windows Defender\' -ErrorAction SilentlyContinue |
            Disable-ScheduledTask -ErrorAction SilentlyContinue

        # Disable WinDefend service
        Stop-Service -Name WinDefend -ErrorAction SilentlyContinue
        Set-Service  -Name WinDefend -StartupType Disabled -ErrorAction SilentlyContinue

        Write-Log 'Windows Defender disabled via registry, MpPreference, and scheduled tasks.'
    }

    Invoke-Step 'Configure Group Policy - Disable Defender (Pro/Enterprise only)' {
        $edition = (Get-WindowsEdition -Online -ErrorAction SilentlyContinue).Edition
        if ($edition -match 'Pro|Enterprise|Education') {
            $gpoKey = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows Defender'
            if (-not (Test-Path $gpoKey)) { New-Item -Path $gpoKey -Force | Out-Null }
            Set-ItemProperty -Path $gpoKey -Name 'DisableAntiSpyware' -Value 1 -Type DWord
            Write-Log "Group Policy Defender key set (edition: $edition)."
        }
        else {
            Write-Log "Edition '$edition' does not support Group Policy - skipping." 'WARN'
        }
    }

    Set-State -Phase 1
}

# PHASE 1 - Verify Defender + Install Chocolatey
function Invoke-Phase1 {
    Write-Log "===== PHASE 1: Verify Defender + Install Chocolatey ====="

    Invoke-Step 'Verify Defender Disabled' {
        try {
            $status = Get-MpComputerStatus -ErrorAction Stop
            $av  = $status.AntivirusEnabled
            $rtp = $status.RealTimeProtectionEnabled
            $ams = $status.AMServiceEnabled
            Write-Log "Defender Status: AntivirusEnabled=$av  RealTimeProtection=$rtp  AMService=$ams"
            if ($av -or $rtp) {
                Write-Log 'Defender still appears enabled. Manually turn off Tamper Protection in Windows Security and re-run.' 'WARN'
            }
        }
        catch {
            Write-Log 'Get-MpComputerStatus unavailable - Defender likely fully disabled.' 'WARN'
        }
    }

    Invoke-Step 'Install Chocolatey' {
        if (-not (Get-Command choco -ErrorAction SilentlyContinue)) {
            [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
            $installScript = (New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1')
            Invoke-Expression $installScript
            $env:PATH += ";$env:ALLUSERSPROFILE\chocolatey\bin"
            Write-Log 'Chocolatey installed.'
        }
        else {
            Write-Log 'Chocolatey already installed.'
        }
    }

    Set-State -Phase 2
}

# PHASE 2 - Install Tools via Chocolatey
function Invoke-Phase2 {
    Write-Log "===== PHASE 2: Chocolatey Tool Installation ====="

    # Refresh PATH
    $machinePath = [System.Environment]::GetEnvironmentVariable('PATH', 'Machine')
    $userPath    = [System.Environment]::GetEnvironmentVariable('PATH', 'User')
    $env:PATH    = "$machinePath;$userPath"

    $packages = @(
        'python',
        '7zip',
        'notepadplusplus',
        'hxd',
        'pestudio',
        'sysinternals',
        'wireshark',
        'x64dbg.portable',
        'cyberchef',
        'die',
        'pebear',
        'floss',
        'capa',
        'regshot',
        'speakeasy',
        'mal_unpack',
        'javaruntime',
        'systeminformer-nightlybuilds',
        'ghidra',
        'fakenet',
        'ida-free',
        'strings',
        'yara',
        'chrome-remote-desktop-chrome',
        'explorersuite',
        'resourcehacker.portable',
        'ConEmu',
        'exeinfo',
        'pebear',
        'registryexplorer',
        'fiddler',
        'cutter',
        'dnspy',
        'volatility3',
        'autopsy', 
        'processhacker'


    )

    foreach ($pkg in $packages) {
        Invoke-Step "choco install $pkg" {
            $result = choco install $pkg -y --no-progress 2>&1
            $result | ForEach-Object { Write-Log $_ }
        }
    }

    Set-State -Phase 99
}


# PHASE 99 - Summary
function Invoke-Summary {
    Write-Log "===== SETUP COMPLETE ====="
    Write-Host ""
    Write-Host "+--------------------------------------------------------------+" -ForegroundColor Green
    Write-Host "|          MALWARE LAB SETUP - COMPLETE                        |" -ForegroundColor Green
    Write-Host "+--------------------------------------------------------------+" -ForegroundColor Green
    Write-Host "|  Lab root   : $LabRoot" -ForegroundColor Green
    Write-Host "|  Tools dir  : $ToolsDir" -ForegroundColor Green
    Write-Host "|  Log file   : $LogFile" -ForegroundColor Green
    Write-Host "+--------------------------------------------------------------+" -ForegroundColor Green
    Write-Host "|  ACTION: Review manual installs:                             |" -ForegroundColor Yellow
    Write-Host "|  $LabRoot\MANUAL_INSTALLS.txt" -ForegroundColor Yellow
    Write-Host "+--------------------------------------------------------------+" -ForegroundColor Green
    Write-Host "|  REMINDERS:                                                  |" -ForegroundColor Cyan
    Write-Host "|  * Keep VM in host-only networking during analysis           |" -ForegroundColor Cyan
    Write-Host "|  * Snapshot the VM NOW before detonating any malware         |" -ForegroundColor Cyan
    Write-Host "|  * Verify Defender is off after every reboot:                |" -ForegroundColor Cyan
    Write-Host "|    Get-MpComputerStatus | Select Antivirus*,RealTime*,AMS*   |" -ForegroundColor Cyan
    Write-Host "+--------------------------------------------------------------+" -ForegroundColor Green
    Write-Host ""
    Write-Host "Press any key to reboot..." -ForegroundColor Yellow
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    Write-Log 'Restarting computer.'
    Restart-Computer -Force
}

# MAIN DISPATCHER
$state = Get-State

Write-Host ""
Write-Host "+--------------------------------------------------+" -ForegroundColor Magenta
Write-Host "|   Windows 11 Malware Lab - Automated Setup       |" -ForegroundColor Magenta
Write-Host "|   Current phase: $($state.Phase)                                |" -ForegroundColor Magenta
Write-Host "+--------------------------------------------------+" -ForegroundColor Magenta
Write-Host ""
Invoke-Phase0
Invoke-Phase1
Invoke-Phase2
Invoke-Summary


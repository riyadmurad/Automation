# Hyper-V VM Deployment Script from CSV
# Usage: .\Deploy-HyperVVMs.ps1 -CSVFilePath "C:\path\to\your\file.csv" [-DryRun]

param(
    [Parameter(Mandatory=$true)]
    [string]$CSVFilePath
    
)

# Set error action preference for better visibility
$ErrorActionPreference = "Stop"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Hyper-V VM Deployment Script" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

# Function to get VHDX template path based on VM type
function Get-TemplatesPath {
    param(
        [string]$VMType
    )
    
    switch ($VMType.ToLower()) {
        "ubuntu" { return "D:\VM-Templates\ubunutu-24-4-lts.vhdx" }
        "win_client" { return "D:\VM-Templates\win11_template.vhdx" }
        "win_server" { return "D:\VM-Templates\win_srv.vhdx" }
        default { 
            Write-Host "[ERROR] Unknown VM type: $VMType" -ForegroundColor Red
            return $null 
        }
    }
}

# Function to validate Hyper-V module availability
function Test-HyperVAvailability {
    try {
        Get-Module -ListAvailable -Name Hyper-V | Out-Null
        Write-Host "[INFO] Hyper-V module found" -ForegroundColor Green
        return $true
    } catch {
        Write-Host "[ERROR] Hyper-V module not available. Please run as Administrator with Hyper-V installed." -ForegroundColor Red
        return $false
    }
}

# Import CSV file
Write-Host "Reading CSV file: $CSVFilePath" -ForegroundColor Yellow
try {
    $VMs = Import-Csv -Path $CSVFilePath -ErrorAction Stop
    Write-Host "[INFO] Successfully imported $($VMs.Count) VM configurations from CSV" -ForegroundColor Green
} catch {
    Write-Host "[ERROR] Failed to import CSV file: $_" -ForegroundColor Red
    exit 1
}

# Check if Hyper-V is available
if (-not (Test-HyperVAvailability)) {
    exit 1
}

# Process each VM from the CSV
foreach ($VM in $VMs) {
    Write-Host ""
    Write-Host "----------------------------------------" -ForegroundColor Yellow
    Write-Host "Processing: $($VM.VM_NAME)" -ForegroundColor Yellow
    Write-Host "----------------------------------------" -ForegroundColor Yellow
    
    # Get VHDX template path based on VM type
    $VHDXTemplatePath = Get-TemplatesPath -VMType $VM.VM_TYPE
    
    if (-not $VHDXTemplatePath) {
        Write-Host "[SKIP] Skipping $($VM.VM_NAME) due to invalid VM type" -ForegroundColor Red
        continue
    }
    
    # Validate VHDX template exists
    if (-not (Test-Path $VHDXTemplatePath)) {
        Write-Host "[ERROR] Template not found: $VHDXTemplatePath" -ForegroundColor Red
        continue
    }
    <# 
    # Check if VM already exists
    try {
        Get-VM -Name $VM.VM_NAME -ErrorAction SilentlyContinue | Out-Null
        Write-Host "[WARN] VM '$($VM.VM_NAME)' already exists. Skipping..." -ForegroundColor Yellow
        continue
    } catch {
        # Continue processing
    }
  #>
    # Create default folders:
    New-Item -ItemType Directory -Path "d:\vms\$($VM.VM_NAME)\Virtual Hard Disks" -Force
    New-Item -ItemType Directory -Path "d:\vms\$($VM.VM_NAME)\Snapshots" -Force
    New-Item -ItemType Directory -Path "d:\vms\$($VM.VM_NAME)\Virtual Machines" -Force
    # Copy the master disk to a new instance
    Copy-Item -Path $VHDXTemplatePath -Destination "d:\vms\$($VM.VM_NAME)\Virtual Hard Disks\$($VM.VM_NAME).vhdx" -Force
    Write-Host "  VM Name: $($VM.VM_NAME)" -ForegroundColor White
    Write-Host "  RAM: $($VM.VM_RAM) GB" -ForegroundColor White
    Write-Host "  CPUs: $([int]($VM.VM_CPU)) cores" -ForegroundColor White
    Write-Host "  Type: $($VM.VM_TYPE)" -ForegroundColor White
    Write-Host "  Network: $($VM.VM_NET)" -ForegroundColor White
    Write-Host "  Location: d:\vms\$($VM.VM_NAME)" -ForegroundColor White
    # Create the VM from template VHDX
    Write-Host "[INFO] Creating VM $($VM.VM_NAME)..." -ForegroundColor Blue
    try {
        # Create New-VM command parameters
        $NewVMParams = @{
            Name = $VM.VM_NAME
            MemoryStartupBytes = [int]($VM.VM_RAM) * 1GB
            Path = "D:\VMs"
            VHDPath  = "d:\vms\$($VM.VM_NAME)\Virtual Hard Disks\$($VM.VM_NAME).vhdx"
            SwitchName = $VM.VM_NET
            Generation = 2

        }
        Import-Module Hyper-V -ErrorAction SilentlyContinue
        New-VM @NewVMParams -ErrorAction Stop | Out-Null
        Set-VM $VM.VM_NAME -ProcessorCount ($VM.VM_CPU) -AutomaticCheckpointsEnabled $false
        Set-VMMemory $VM.VM_NAME -DynamicMemoryEnabled $false
        # Generate automatic checkpoint for the VM (for Ubuntu, may need additional config)
        if ($VM.VM_TYPE.ToLower() -eq "ubuntu") {
           Set-VMFirmware -VMName $VM.VM_NAME -SecureBootTemplate "MicrosoftUEFICertificateAuthority"
        }
        if ($VM.VM_TYPE.ToLower() -eq "win_client") {
             Set-VMFirmware -VMName $VM.VM_NAME -SecureBootTemplate "MicrosoftWindows"  
            }        
        
        Write-Host "[SUCCESS] VM '$($VM.VM_NAME)' created successfully!" -ForegroundColor Green
        
    } catch {
        Write-Host "[ERROR] Failed to create VM '$($VM.VM_NAME)': $_" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Deployment Complete!" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan



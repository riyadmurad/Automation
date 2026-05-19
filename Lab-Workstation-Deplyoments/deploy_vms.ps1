# Hyper-V VM Deployment Script from CSV or single VM

[CmdletBinding(DefaultParameterSetName = 'Bulk')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Bulk')]
    [Alias('Bulk')]
    [string]$CSVFilePath,

    [Parameter(Mandatory = $true, ParameterSetName = 'Single')]
    [Alias('Name')]
    [string]$VMName,

    [Parameter(Mandatory = $true, ParameterSetName = 'Single')]
    [Alias('Type')]
    [string]$VMType,

    [Parameter(Mandatory = $true, ParameterSetName = 'Single')]
    [Alias('CPUs')]
    [int]$VMCpu,

    [Parameter(Mandatory = $true, ParameterSetName = 'Single')]
    [Alias('RAM')]
    [int]$VMRam,

    [Parameter(Mandatory = $false, ParameterSetName = 'Single')]
    [Alias('Network')]
    [string]$VMNet = "Default Switch"
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
        "win_server" { return "D:\VM-Templates\srv_template.vhdx" }
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
    }
    catch {
        Write-Host "[ERROR] Hyper-V module not available. Please run as Administrator with Hyper-V installed." -ForegroundColor Red
        return $false
    }
}

# Function to copy large files with progress output
function Copy-FileWithProgress {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Source,
        [Parameter(Mandatory = $true)]
        [string]$Destination,
        [int]$BufferSize = 4MB
    )

    $sourceFile = Get-Item -Path $Source -ErrorAction Stop
    $totalBytes = $sourceFile.Length
    $bytesCopied = 0
    $destinationDirectory = Split-Path -Path $Destination -Parent

    if (-not (Test-Path -Path $destinationDirectory)) {
        New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
    }

    Write-Host "[INFO] Copying $Source to $Destination" -ForegroundColor Blue

    $sourceStream = [System.IO.File]::Open($Source, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
    try {
        $destinationStream = [System.IO.File]::Open($Destination, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
        try {
            $buffer = New-Object byte[] $BufferSize
            while ($true) {
                $read = $sourceStream.Read($buffer, 0, $buffer.Length)
                if ($read -le 0) { break }

                $destinationStream.Write($buffer, 0, $read)
                $bytesCopied += $read
                $percentComplete = [math]::Round(($bytesCopied / $totalBytes) * 100, 0)

                Write-Progress -Activity "Copying VHDX" -Status "$percentComplete% complete" -PercentComplete $percentComplete
            }

            $destinationStream.Flush()
            Write-Progress -Activity "Copying VHDX" -Completed
            Write-Host "[INFO] Copy complete: $Destination" -ForegroundColor Green
        }
        finally {
            $destinationStream.Dispose()
        }
    }
    finally {
        $sourceStream.Dispose()
    }
}

# Import CSV file or build single VM payload
if ($PSCmdlet.ParameterSetName -eq 'Bulk') {
    Write-Host "Reading CSV file: $CSVFilePath" -ForegroundColor Yellow
    try {
        $VMs = Import-Csv -Path $CSVFilePath -ErrorAction Stop
        foreach ($VM in $VMs) {
            if (-not $VM.PSObject.Properties.Match('VM_NET')) {
                $VM | Add-Member -NotePropertyName VM_NET -NotePropertyValue "Default Switch" -Force
            }
            elseif (-not $VM.VM_NET) {
                $VM.VM_NET = "Default Switch"
            }
        }
        Write-Host "[INFO] Successfully imported $($VMs.Count) VM configurations from CSV" -ForegroundColor Green
    }
    catch {
        Write-Host "[ERROR] Failed to import CSV file: $_" -ForegroundColor Red
        exit 1
    }
}
else {
    Write-Host "[INFO] Deploying single VM: $VMName" -ForegroundColor Yellow
    $VMs = @(
        [PSCustomObject]@{
            VM_NAME = $VMName
            VM_TYPE = $VMType
            VM_CPU  = $VMCpu
            VM_RAM  = $VMRam
            VM_NET  = $VMNet
        }
    )
}

# Track deployment progress
$TotalVMs = $VMs.Count
$CurrentVMIndex = 0

# Check if Hyper-V is available
if (-not (Test-HyperVAvailability)) {
    exit 1
}

# Process each VM from the CSV
foreach ($VM in $VMs) {
    $CurrentVMIndex++
    $PercentComplete = [math]::Round(($CurrentVMIndex / $TotalVMs) * 100, 0)
    Write-Host ""
    Write-Host "----------------------------------------" -ForegroundColor Yellow
    Write-Host "Processing: $($VM.VM_NAME) ($CurrentVMIndex of $TotalVMs, $PercentComplete% complete)" -ForegroundColor Yellow
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

    # Check if VM already exists
    $ExistingVM = Get-VM -Name $VM.VM_NAME -ErrorAction SilentlyContinue
    if ($ExistingVM) {
        Write-Host "[WARN] VM '$($VM.VM_NAME)' already exists. Skipping to prevent accidental override." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "==========================================" -ForegroundColor Yellow
        Write-Host "[WARN] Deployment skipped for VM: '$($VM.VM_NAME)'" -ForegroundColor Yellow
        Write-Host "==========================================" -ForegroundColor Yellow
        continue
    }

    # Destination paths for this VM
    $VMRootPath = "d:\vms\$($VM.VM_NAME)"
    $VHDXFolderPath = "$VMRootPath\Virtual Hard Disks"
    $DestinationVHDXPath = "$VHDXFolderPath\$($VM.VM_NAME).vhdx"

    # Skip if a destination folder or VHDX already exists for this VM name
    if ((Test-Path -Path $VMRootPath -PathType Container) -or (Test-Path -Path $DestinationVHDXPath -PathType Leaf)) {
        Write-Host "[WARN] Destination path for VM '$($VM.VM_NAME)' already exists. Skipping copy and VM creation to avoid conflict." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "==========================================" -ForegroundColor Yellow
        Write-Host "[WARN] Deployment skipped for VM: '$($VM.VM_NAME)'" -ForegroundColor Yellow
        Write-Host "==========================================" -ForegroundColor Yellow
        continue
    }

    # Create default folders:
    New-Item -ItemType Directory -Path $VHDXFolderPath -Force | Out-Null
    New-Item -ItemType Directory -Path "$VMRootPath\Snapshots" -Force | Out-Null
    New-Item -ItemType Directory -Path "$VMRootPath\Virtual Machines" -Force | Out-Null

    # Copy the master disk to a new instance with progress reporting
    Copy-FileWithProgress -Source $VHDXTemplatePath -Destination $DestinationVHDXPath
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
            Name               = $VM.VM_NAME
            MemoryStartupBytes = [int]($VM.VM_RAM) * 1GB
            Path               = "D:\VMs"
            VHDPath            = "d:\vms\$($VM.VM_NAME)\Virtual Hard Disks\$($VM.VM_NAME).vhdx"
            SwitchName         = $VM.VM_NET
            Generation         = 2

        }
        Import-Module Hyper-V -ErrorAction SilentlyContinue
        New-VM @NewVMParams -ErrorAction Stop | Out-Null
        Set-VM $VM.VM_NAME -ProcessorCount ($VM.VM_CPU) -AutomaticCheckpointsEnabled $false
        Set-VMMemory $VM.VM_NAME -DynamicMemoryEnabled $false
        Set-VMProcessor -VMName $VM.VM_NAME -ExposeVirtualizationExtensions $true
        if ($VM.VM_TYPE.ToLower() -eq "ubuntu") {
            Set-VMFirmware -VMName $VM.VM_NAME -SecureBootTemplate "MicrosoftUEFICertificateAuthority"
        }
        if ($VM.VM_TYPE.ToLower() -eq "win_client") {
            Set-VMFirmware -VMName $VM.VM_NAME -SecureBootTemplate "MicrosoftWindows"  
        }        
        
        Write-Host "[SUCCESS] VM '$($VM.VM_NAME)' created successfully!" -ForegroundColor Green
        Write-Host ""
        Write-Host "==========================================" -ForegroundColor Cyan
        Write-Host "Deployment Complete!" -ForegroundColor Cyan
        Write-Host "==========================================" -ForegroundColor Cyan

        
    }
    catch {
        Write-Host "[ERROR] Failed to create VM '$($VM.VM_NAME)': $_" -ForegroundColor Red
    }
}




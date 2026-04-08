# Lab Workstation Deployments
This folder contains PowerShell scripts for automating Hyper-V VM deployments and malware analysis lab setup.

## Scripts Overview

### 1. deploy_vms.ps1
Deploys multiple VMs from a CSV configuration file using the Hyper-V PowerShell module.

**Description:**
- Reads VM configurations from a CSV file
- Creates VMs with specified hardware resources (RAM, CPU)
- Copies template VHDX files and instantiates new VMs
- Configures VM settings including memory, processor count, and firmware
- Supports Ubuntu, Windows Client, and Windows Server VM types

**Prerequisites:**
- Hyper-V role installed on the host machine
- Administrative privileges to run the script
- Template VHDX files available in `D:\VM-Templates\`
- Virtual switch configured (e.g., LabSwitch)
- Powershell execution policy set to allow script execution: `Set-ExecutionPolicy Unrestricted`
- Disable Windows Defender AV Real-Time Protection.

**Usage:**
```powershell
.\deploy_vms.ps1 -CSVFilePath "vms.csv"
```

**Optional Parameters:**
- `-DryRun` - Preview VMs to be created without actually deploying them

**Template VHDX Locations:**
- Ubuntu: `D:\VM-Templates\ubunutu-24-4-lts.vhdx`
- Windows Client: `D:\VM-Templates\win11_template.vhdx`
- Windows Server: `D:\VM-Templates\win_srv.vhdx`

**Output Structure:**
Each VM is deployed to `d:\vms\<VM_NAME>\` with the following subdirectories:
- `Virtual Hard Disks\` - Contains the VM's VHDX file
- `Snapshots\` - For VM snapshots
- `Virtual Machines\` - VM configuration files

**CSV File Format:**
The CSV file must have the following columns:
| Column  | Description                              | Example          |
| ------- | ---------------------------------------- | ---------------- |
| VM_NAME | Unique name for the VM                   | `test_deploy_vm` |
| VM_RAM  | RAM in GB                                | `4`              |
| VM_CPU  | Number of CPU cores                      | `4`              |
| VM_TYPE | VM type (ubuntu, win_client, win_server) | `win_client`     |
| VM_NET  | Virtual switch name                      | `LabSwitch`      |

**Example CSV:**
```csv
VM_NAME,VM_RAM,VM_CPU,VM_TYPE,VM_NET
test_deploy_vm,4,4,win_client,LabSwitch
```

**Features:**
- Automatic template VHDX copying
- Generation 2 VM creation (UEFI support)
- Dynamic memory disabled for better performance
- Secure boot configuration based on VM type
- Error handling with detailed logging
- Skips VMs that already exist (commented out by default)

---

### 2. Setup-Triage-VM.ps1
Automated setup script for creating a Windows malware analysis lab environment.

**Description:**
- Configures Windows 11 for malware analysis testing
- Disables Windows Defender and Windows Update
- Hardens system settings for lab environment
- Installs required tools via Chocolatey
- Tracks setup progress in `C:\MalwareLab\setup_state.json`

**Warning:**
This script makes significant system changes. Ensure you have backups or snapshots before running.

---

## Quick Start

1. **Deploy VMs:**
   ```powershell
   # Edit vms.csv with your VM configurations
   .\deploy_vms.ps1 -CSVFilePath ".\vms.csv"
   ```

2. **Setup Malware Lab (on a Windows VM):**
   ```powershell
   # Run as Administrator
   .\Setup-Triage-VM.ps1
   ```

## Directory Structure
```
Lab-Workstation-Deplyoments/
├── README.md                    # This file
├── deploy_vms.ps1              # VM deployment script
├── Setup-Triage-VM.ps1        # Malware lab setup script
├── vms.csv                     # VM configuration template
└── README.md                   # Additional documentation
```

## Troubleshooting

**VM Deployment Issues:**
- Ensure Hyper-V module is available: `Get-Module -ListAvailable -Name Hyper-V`
- Check template VHDX files exist in `D:\VM-Templates\`
- Verify virtual switch is configured: `Get-VMSwitch`
- Run PowerShell as Administrator

**Malware Lab Setup Issues:**
- Check if running as Administrator
- Review logs in `C:\MalwareLab\setup_log.txt`
- Some features may require Windows Pro/Enterprise edition

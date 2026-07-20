# Lab Workstation Deployments
This folder contains PowerShell scripts for automating Hyper-V VM deployments, malware analysis lab setup, and Active Directory lab creation.

## Scripts Overview

### 1. deploy_vms.ps1
Deploys multiple VMs from a CSV configuration file or a single VM using the Hyper-V PowerShell module.

**Description:**
- Reads VM configurations from a CSV file or accepts single VM parameters
- Creates Generation 2 VMs with specified hardware resources (RAM, CPU)
- Copies template VHDX files and instantiates new VMs
- Configures VM settings including memory, processor count, and firmware
- Supports Ubuntu, Windows Client, and Windows Server VM types
- Progress tracking with file copy progress indicator
- Skips existing VMs to prevent accidental overrides

**Prerequisites:**
- Hyper-V role installed on the host machine
- Administrative privileges to run the script
- Template VHDX files available in `D:\VM-Templates\`
- Virtual switch configured with internet access (e.g., LabSwitch)
- PowerShell execution policy set to allow script execution: `Set-ExecutionPolicy Unrestricted`

**Usage:**
```powershell
# Deploy multiple VMs from CSV
.\deploy_vms.ps1 -CSVFilePath ".\vms.csv"

# Deploy a single VM
.\deploy_vms.ps1 -VMName "test_vm" -VMType "win_client" -VMCpu 4 -VMRam 8 -VMNet "LabSwitch"
```

**Template VHDX Locations:**
- Ubuntu: `D:\VM-Templates\ubunutu-24-4-lts.vhdx`
- Windows Client: `D:\VM-Templates\win11_template.vhdx`
- Windows Server: `D:\VM-Templates\srv_template.vhdx`

**Output Structure:**
Each VM is deployed to `d:\vms\<VM_NAME>\` with the following subdirectories:
- `Virtual Hard Disks\` - Contains the VM's VHDX file
- `Snapshots\` - For VM snapshots
- `Virtual Machines\` - VM configuration files

**CSV File Format:**
| Column   | Description                              | Example          |
| -------- | ---------------------------------------- | ---------------- |
| VM_NAME  | Unique name for the VM                    | `test_deploy_vm` |
| VM_RAM   | RAM in GB                                | `4`              |
| VM_CPU   | Number of CPU cores                      | `4`              |
| VM_TYPE  | VM type (ubuntu, win_client, win_server) | `win_client`     |
| VM_NET   | Virtual switch name                      | `LabSwitch`      |

**Example CSV:**
```csv
VM_NAME,VM_RAM,VM_CPU,VM_TYPE,VM_NET
test_deploy_vm,4,4,win_client,LabSwitch
ubuntu_vm,8,4,ubuntu,LabSwitch
```

**Features:**
- Automatic template VHDX copying with progress indicator
- Generation 2 VM creation (UEFI support)
- Dynamic memory disabled for better performance
- Secure boot configuration based on VM type
- Virtualization extensions exposed to VMs
- Error handling with detailed logging
- Skips VMs that already exist to prevent accidental override

---

### 2. Setup-Triage-VM.ps1
Automated setup script for creating a Windows malware analysis lab environment.

**Description:**
- Configures Windows 11 for malware analysis testing
- Disables Windows Defender, Windows Update, and UAC
- Hardens system settings for lab environment
- Installs 30+ security and malware analysis tools via Chocolatey
- Phase-based execution with state persistence in `C:\MalwareLab\setup_state.json`
- Detailed logging to `C:\MalwareLab\setup_log.txt`
- Automatic reboots between phases

**Warning:**
This script makes significant system changes. Ensure you have backups or snapshots before running. Disable Windows Defender Tamper Protection before running.

**Usage:**
```powershell
# Run as Administrator on a deployed Windows VM
.\Setup-Triage-VM.ps1
```

**Execution Phases:**
| Phase | Description |
| ----- | ----------- |
| 0     | Windows settings (UAC, Defender, Windows Update, file extensions) |
| 1     | Verify Defender disabled + install Chocolatey |
| 2     | Install all tools via Chocolatey |
| 99    | Summary and reboot |

**Tools Installed via Chocolatey:**
| Category | Tools |
| -------- | ----- |
| Disassemblers/Debuggers | x64dbg, IDA Free, Ghidra, Cutter, dnSpy |
| Malware Analysis | PEbear, Detect It Easy (die), FLOSS, CAPA, Mal_Unpack, Speakeasy, Yara, Volatility3, AutoPsy |
| Network Analysis | Wireshark, Fiddler, FakeNet |
| System Tools | Sysinternals, Process Hacker, System Informer, RegShot |
| Utilities | 7zip, Notepad++, HxD, PE Studio, Chrome, ConEmu, Resource Hacker, NASM, Python, Java Runtime |
| Debugging/RE | Explorer Suite, EXE Info, Strings |

**State File:** `C:\MalwareLab\setup_state.json`
**Log File:** `C:\MalwareLab\setup_log.txt`

---

### 3. Lab-Creation.ps1
Creates Active Directory users, groups, and Organizational Units for testing environments.

**Description:**
- Creates multiple OUs based on real department names
- Generates random users with realistic names and job titles
- Creates security groups with realistic naming conventions (DL/SG prefixes)
- Assigns random group memberships to users
- Exports lab data to JSON for reference
- Supports cleanup of existing lab environments with `-CleanExisting` switch

**Default Departments:**
Information Technology, Human Resources, Finance, Marketing, Sales, Engineering, Research and Development, Operations, Customer Service, Legal, Compliance, Product Management, Quality Assurance, Supply Chain, Facilities

**Prerequisites:**
- Active Directory module for PowerShell (`Import-Module ActiveDirectory`)
- Domain admin credentials with permission to create OUs, users, and groups
- Target domain must be accessible

**Usage:**
```powershell
# Create lab environment with default settings (1000 users, 100 groups)
.\Lab-Creation.ps1 -DomainName "contoso.com" -AdminUsername "Administrator" -AdminPassword "P@ssw0rd!" -RootOu "OU=Lab,DC=contoso,DC=com"

# Custom user/group counts with cleanup
.\Lab-Creation.ps1 -DomainName "contoso.com" -AdminUsername "Administrator" -AdminPassword "P@ssw0rd!" -RootOu "OU=Lab,DC=contoso,DC=com" -UserCount 500 -GroupCount 50 -CleanExisting

# Specific membership ranges
.\Lab-Creation.ps1 -DomainName "contoso.com" -AdminUsername "Administrator" -AdminPassword "P@ssw0rd!" -RootOu "OU=Lab,DC=contoso,DC=com" -MinGroupMembers 5 -MaxGroupMembers 15
```

**Parameters:**
| Parameter         | Description                          | Default |
| ----------------- | ------------------------------------ | ------- |
| DomainName        | AD domain name (e.g., contoso.com)   | Required |
| AdminUsername     | Domain admin username                 | Required |
| AdminPassword     | Domain admin password                 | Required |
| RootOu            | Root OU path for lab resources       | Required |
| UserCount         | Number of users to create            | 1000    |
| GroupCount        | Number of groups to create           | 100     |
| MinGroupMembers    | Minimum members per group            | 3       |
| MaxGroupMembers    | Maximum members per group            | 20      |
| Departments       | Array of department names            | (15 default depts) |
| CleanExisting     | Remove existing lab before creating   | Switch  |
| UseSsl            | Use LDAPS for secure connections       | Switch  |

**Output:**
- Lab data exported to `LabCreation_<timestamp>.json`
- Summary printed to console with user/group counts

---

## Quick Start

### 1. Deploy VMs
```powershell
# Edit vms.csv with your VM configurations
.\deploy_vms.ps1 -CSVFilePath ".\vms.csv"
```

### 2. Setup Malware Lab (on a Windows VM)
```powershell
# Run as Administrator
.\Setup-Triage-VM.ps1
```

### 3. Create Active Directory Lab Environment
```powershell
# Create test users and groups in AD
.\Lab-Creation.ps1 -DomainName "contoso.com" -AdminUsername "Administrator" -AdminPassword "P@ssw0rd!" -RootOu "OU=Lab,DC=contoso,DC=com"
```

## Directory Structure
```
Lab-Workstation-Deplyoments/
├── README.md              # This file
├── deploy_vms.ps1        # VM deployment script
├── Setup-Triage-VM.ps1   # Malware lab setup script
├── Lab-Creation.ps1      # AD lab user/group creation script
└── vms.csv               # VM configuration template
```

## Troubleshooting

**VM Deployment Issues:**
- Ensure Hyper-V module is available: `Get-Module -ListAvailable -Name Hyper-V`
- Check template VHDX files exist in `D:\VM-Templates\`
- Verify virtual switch is configured: `Get-VMSwitch`
- Run PowerShell as Administrator
- Check if VM already exists (script skips existing VMs)

**Malware Lab Setup Issues:**
- Ensure running as Administrator
- Disable Windows Defender Tamper Protection in Windows Security before running
- Review logs in `C:\MalwareLab\setup_log.txt`
- Check current phase in `C:\MalwareLab\setup_state.json` and re-run to continue
- Some features may require Windows Pro/Enterprise edition (Group Policy)

**Lab Creation Issues:**
- Verify Active Directory module is available: `Get-Module -ListAvailable -Name ActiveDirectory`
- Check credentials have domain admin permissions
- Ensure target OU parent path exists
- Use `-CleanExisting` to remove partial/corrupt lab environments
- Review console output for specific error messages

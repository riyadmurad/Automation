# Automation

A collection of PowerShell scripts for automating lab environments, malware analysis setup, and knowledge management.

---

## Quick Navigation

| Category | Description | Documentation |
| -------- | ----------- | ------------- |
| [Lab Workstation Deployments](./Lab-Workstation-Deplyoments/) | Hyper-V VMs, malware analysis labs, AD environments | [View README](./Lab-Workstation-Deplyoments/README.md) |
| [Obsidian Vault Initialization](./Obsidian-Vault-Initialization/) | Structured knowledge base setup | [View README](./Obsidian-Vault-Initialization/README.md) |

---

## Lab Workstation Deployments

Automated provisioning and configuration for security research environments.

**Location:** `Lab-Workstation-Deplyoments/`

### What it includes

| Script | Purpose |
| ------ | ------- |
| `deploy_vms.ps1` | Deploy Hyper-V VMs from CSV or command line |
| `Setup-Triage-VM.ps1` | Configure a Windows malware analysis lab |
| `Lab-Creation.ps1` | Create AD users, groups, and OUs for testing |
| `vms.csv` | VM configuration template |

### Key features

- **VM Deployment** - Spin up Ubuntu, Windows 11, or Windows Server VMs from templates
- **Malware Analysis Lab** - Disable Defender/UAC, install 30+ security tools
- **Active Directory Lab** - Generate 1000+ test users with group memberships

➡ **[Full documentation](./Lab-Workstation-Deplyoments/README.md)**

---

## Obsidian Vault Initialization

Automated setup for a Zettelkasten-style knowledge management system.

**Location:** `Obsidian-Vault-Initialization/`

### What it includes

| Script | Purpose |
| ------ | ------- |
| `initialize_obsidian_vault.ps1` | Create a structured Obsidian vault |

### Folder structure created

```
00_Inbox/        → Raw, unprocessed notes
10_Daily/        → Daily logs and timestamps
20_Projects/     → Active projects
30_Areas/       → Knowledge domains (Reverse Engineering, Programming, etc.)
40_Resources/    → Cheat sheets, payloads, research papers
50_Completed/    → Archived projects
90_System/       → Templates and attachments
```

➡ **[Full documentation](./Obsidian-Vault-Initialization/README.md)**

---

## Getting Started

### Deploy a Malware Analysis Lab

```powershell
# 1. Deploy VMs from CSV
.\Lab-Workstation-Deplyoments\deploy_vms.ps1 -CSVFilePath ".\Lab-Workstation-Deplyoments\vms.csv"

# 2. On a Windows VM, run as Administrator to configure the lab
.\Lab-Workstation-Deplyoments\Setup-Triage-VM.ps1
```

### Create an AD Test Environment

```powershell
.\Lab-Workstation-Deplyoments\Lab-Creation.ps1 `
    -DomainName "contoso.com" `
    -AdminUsername "Administrator" `
    -AdminPassword "P@ssw0rd!" `
    -RootOu "OU=Lab,DC=contoso,DC=com"
```

### Initialize an Obsidian Vault

```powershell
.\Obsidian-Vault-Initialization\initialize_obsidian_vault.ps1 `
    -VaultName "ResearchVault" `
    -RootPath "C:\Users\Public\Documents"
```

---

## Repository Structure

```
Automation/
├── README.md                              # This file
├── Lab-Workstation-Deplyoments/
│   ├── README.md                          # Detailed lab deployment docs
│   ├── deploy_vms.ps1                     # VM deployment
│   ├── Setup-Triage-VM.ps1                # Malware lab setup
│   ├── Lab-Creation.ps1                   # AD lab creation
│   └── vms.csv                            # VM config template
└── Obsidian-Vault-Initialization/
    ├── README.md                          # Detailed vault docs
    └── initialize_obsidian_vault.ps1      # Vault setup
```

---

## Requirements

- **Lab Workstation Scripts:** Windows 10/11 Pro or Windows Server with Hyper-V
- **Lab-Creation.ps1:** Active Directory module, domain admin credentials
- **Obsidian Script:** PowerShell 5.1+, Obsidian app (optional)

# Automation

A Microsoft 365 and Azure security assessment platform, alongside PowerShell automation for lab environments, malware analysis setup, and knowledge management.

---

## Quick Navigation

| Category | Description | Documentation |
| -------- | ----------- | ------------- |
| M365 and Azure Security Assessment | Next.js/.NET foundation with a local synthetic assessment console | [Setup and status](./m365-azure-security-assessment-platform/README.md) |
| [Lab Workstation Deployments](./Lab-Workstation-Deplyoments/) | Hyper-V VMs, malware analysis labs, AD environments | [View README](./Lab-Workstation-Deplyoments/README.md) |
| [Obsidian Vault Initialization](./Obsidian-Vault-Initialization/) | Structured knowledge base setup | [View README](./Obsidian-Vault-Initialization/README.md) |

---

## M365 and Azure Security Assessment

The platform lives in [m365-azure-security-assessment-platform](./m365-azure-security-assessment-platform/README.md). It targets MSP multi-customer and standalone assessment, detailed evidence reports, and controlled remediation.

**Current implementation:** a local synthetic Next.js console, .NET API/domain contracts, 13 unverified control definitions, two fixture customers, inactive workers, and unit/API/browser tests. This is not a production assessment service. Microsoft sign-in, customer connections, persistent evidence/audit storage, and live remediation are not implemented. Synthetic examples are blocked in production.

Run the local preview from the repository root:

```powershell
npm ci --prefix ./m365-azure-security-assessment-platform/web
$env:ENABLE_SYNTHETIC_PREVIEW = 'true'
npm run dev --prefix ./m365-azure-security-assessment-platform/web
```

Open http://127.0.0.1:3000. No Microsoft credentials are needed and no customer services are contacted. See the [platform README](./m365-azure-security-assessment-platform/README.md) for backend commands, testing, dependency policy, and remaining gates.

Planning and implementation acceptance are tracked in [OpenSpec tasks](./openspec/changes/m365-azure-security-baseline-platform/tasks.md). The Microsoft Zero Trust Assessment repository has been researched but is not integrated.

**Paused milestone:** Application Foundation tasks 1.1-1.5 are accepted. Start later work from the [resume checkpoint](./m365-azure-security-assessment-platform/RESUME_CHECKPOINT.md); later-group draft files are not completed production features.

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
- **Security Assessment Platform:** Node.js and .NET versions pinned in the platform; Docker is required for future container-backed integration tests, not for the synthetic preview.

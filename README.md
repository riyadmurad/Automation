# Automation
Scripts for automating Hyper-V VM deployments and malware analysis lab setup.

## Lab Workstation Deployments
- **deploy_vms.ps1**: Deploys multiple Hyper-V VMs from a CSV configuration file
- **Setup-Triage-VM.ps1**: Configures Windows 11 for malware analysis testing
- **vms.csv**: VM configuration template

## Quick Start
1. Edit `vms.csv` with your VM configurations
2. Run `.\deploy_vms.ps1 -CSVFilePath ".\Lab-Workstation-Deplyoments\vms.csv"`
3. On a deployed Windows VM, run `.\Lab-Workstation-Deplyoments\Setup-Triage-VM.ps1` as Administrator


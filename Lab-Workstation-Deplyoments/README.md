# Lab Workstation Deployments
This script is used to deploy bulk VMs for a home lab environment using Hyper-V. It reads VM configuration from a CSV file and creates the VMs accordingly.


# deploy_vms.ps1
This script is used to deploy the VMs for the lab. It uses the Hyper-V module to create and configure the VMs using a specified CSV file.

## Prerequisites
- Hyper-V role installed on the host machine.
- You must have administrative privileges to run the script and create VMs.
- VHDX files for the VM templates should be available in the specified location.

### CSV File Format
The CSV file should have the following columns:
* VM_NAME: The name of the VM to be created.
* VM_RAM: The amount of RAM to be allocated to the VM (in GB).
* VM_CPU: The number of CPU cores to be allocated to the VM.
* VM_TYPE: 
    - ubuntu.
    - win_client.
    - win_server.
* VM_NET: The name of assigned virtual switch for the VM.

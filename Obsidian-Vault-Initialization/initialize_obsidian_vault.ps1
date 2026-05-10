<#
.SYNOPSIS
    Obsidian Vault Initializer (v2.3) - FINAL STABLE VERSION
.DESCRIPTION
    Automates the creation of a structured Obsidian Knowledge Base.
    Uses defensive programming to ensure all directories exist before file creation.
#>

param (
    [Parameter(Mandatory=$true)]
    [string]$VaultName ,
    
    [Parameter(Mandatory=$true)]
    [string]$RootPath
)

# If RootPath is not provided, construct it from VaultName
if (-not $RootPath) {
    $RootPath = Join-Path $Home "Documents\$VaultName"
}

# The folder structure (using forward slashes for easy reading, script handles conversion)
$Structure = @(
    "00_Inbox",
    "10_Daily",
    "20_Projects",
    "30_Areas/Reverse_Engineering",
    "30_Areas/Programming",
    "30_Areas/MasterStudy/Software_Protection",
    "30_Areas/Mobile_Security",
    "40_Resources/Cheat_Sheets",
    "40_Resources/Payloads",
    "40_Resources/Research_Papers",
    "40_Resources/Scripts",
    "50_Archive",
    "90_System/Templates",
    "90_System/Attachments"
)

# Function to write files safely by ensuring the parent directory exists
function Write-FileSafely {
    param (
        [Parameter(Mandatory=$true)] [string]$FilePath,
        [Parameter(Mandatory=$true)] [string]$Content
    )
    
    # Get the directory portion of the path
    $ParentDir = Split-Path -Path $FilePath -Parent
    
    # If the directory doesn't exist, create it
    if (-not (Test-Path $ParentDir)) {
        Write-Host "[!] Creating missing directory: $ParentDir" -ForegroundColor Yellow
        New-Item -Path $ParentDir -ItemType Directory -Force | Out-Null
    }

    # Write the content using UTF8 encoding
    Set-Content -Path $FilePath -Value $Content -Encoding UTF8
    Write-Host "[+] Successfully created: $FilePath" -ForegroundColor Green
}

function Initialize-Vault {
    param ([string]$Path)

    Write-Host "[+] Initializing Sentinel Vault at: $TemplatePath" -ForegroundColor Cyan

    # 1. Create the Root Directory
    if (-not (Test-Path $Path)) {
        New-Item -Path $Path -ItemType Directory | Out-Null
    }

    # 2. Create the Folder Structure
    foreach ($Folder in $Structure) {
        # Convert forward slashes to Windows backslashes for reliability
        $TargetDir = Join-Path $Path $Folder.Replace("/", "\")
        if (-not (Test-Path $TargetDir)) {
            New-Item -Path $TargetDir -ItemType Directory -Force | Out-Null
            Write-Host "[+] Created: $TargetDir" -ForegroundColor Green
        }
    }

    # 3. Define the README Content
    $ReadmePath = Join-Path $Path "README.md"
    $ReadmeContent = @"
# 🛡️ SENTINEL KNOWLEDGE BASE: OPERATIONAL SOP


## ⚙️ MANDATORY CONFIGURATION (DO THIS FIRST)
To prevent "Vault Rot" (clutter), you **must** configure these settings immediately after opening this vault.

### 1. File & Link Settings (The "No-Clutter" Rule)
*   **Path:** `Settings` $\rightarrow$ `Files & Links`
*   **Default location for new attachments:** Set to `In the folder specified below`.
*   **New link format:** `Relative path to file`.
*   **Folder for new attachments:** Set to `90_System/Attachments`.
*   **New file location:** Set to `Same folder as current file` (This keeps Project artifacts inside their project folders).

### 2. Daily Notes Configuration
*   **Path:** `Settings` $\rightarrow$ `Daily Notes`
*   **New file location:** `10_Daily`.
*   **Template file location:** `90_System/Templates/Daily_Note_Template.md`.

### 3. Templater Plugin (The Automation Engine)
*   **Path:** `Settings` $\rightarrow$ `Templater`
*   **Template folder location:** `90_System/Templates`.
*   **Folder Templates (CRITICAL):** Enable this and map your folders:
    *   `20_Projects` $\rightarrow$ `Project_Template.md`
    *   `30_Areas` $\rightarrow$ `Atomic_Note_Template.md`
    *   `10_Daily` $\rightarrow$ `Daily_Note_Template.md`

### 4. Recommended Hotkeys
*   **Create New Note:** `Ctrl + N`
*   **Insert Template:** `Alt + E` (Configure this in `Settings` $\rightarrow$ `Hotkeys`)
*   **Search Everything:** `Ctrl + O`

---
## 🧠 THE GOLDEN RULE
**Folders define the LIFECYCLE. Links define the TOPIC.**
Do not use folders to categorize subjects; use `[[WikiLinks]]` for that. Use folders to manage how long information is "active."

---

## 📂 FOLDER DIRECTORY & USAGE


### `00_Inbox` | **The Landing Zone**
* **Definition:** The landing zone for raw, unverified data.
* **Rule:** Do not store permanent knowledge here. Process this folder daily.
* **Example:** You find a suspicious IP on a forum. Paste it here immediately as a quick note.

### `10_Daily`| **The Chronological Log**
* **Definition:** A chronological log of your professional activity.
* **Rule:** Use this for timestamps, meeting notes, study notes and "What I did today."
* **Example:** `2023-10-27.md` $\rightarrow$ "Investigated alert from EDR; linked to `[[APT28_Hunt]]`."

### `20_Projects`| **The Active Missions**
* **Definition:** Active, time-bound engagements or specific investigations.
* **Rule:** Use **subfolders** for each project. When finished, move the folder to `50_Archive`.
* **Example:** `20_Projects/APT28_Hunt_2023/` containing `Analysis_Log.md` and `Malware_Samples/`.

### `30_Areas`| **The Knowledge Domains**
* **Definition:** Permanent domains of expertise (Evergreen).
* **Rule:** Use a **flat structure**. Use **MOCs (Maps of Content)** to organize notes.
* **Example:** `30_Areas/Programming/C_Pointers.md`. Do not create a `C` folder.

### `40_Resources`| **The Library**
* **Definition:** The Library. Reusable, static technical intelligence and other addtional tools and scripts.
* **Rule:** If it's a tool, a payload, or a cheat sheet used across multiple projects, it lives here.
*  **Example:** `40_Resources/Cheat_Sheets/Linux_PrivEsc_Commands.md`.

### `50_Archive` | **The Graveyard**
* **Definition:** The graveyard of completed work.
* **Rule:** Move entire project folders here once the engagement is closed/completed.
* **Example:** Moving `20_Projects/APT2_Hunt/` to `50_Archive/APT2_Hunt/`.

### `90_System`| **Templates** and **Attachments**
* **Definition:** The engine and infrastructure of the vault.
* **Rule:** Contains templates, scripts, and attachments.
* **Example:** `90_System/Templates/Incident_Report_Template.md`.

---

## 🔗 MULTI-DOMAIN LINKING EXAMPLES

### 🧪 Scenario 1: The Researcher (Area $\rightarrow$ Resource)
**Context:** You are deep-diving into low-level internals.
1. **The Area:** You are reading your note `[[Understanding_Assembly]]` in `30_Areas/Reverse_Engineering`.
2. **The Action:** You encounter a complex instruction. You don't create a new note; you link to your existing reference: *"The behavior of this opcode is documented in `[[x86_64_Instruction_Set]]`"*.
3. **The Connection:** You jump from a high-level concept to a low-level technical cheat sheet in `40_Resources`.

### 🛠️ Scenario 2: The Developer (Project $\rightarrow$ Area $\rightarrow$ Resource)
**Context:** You are working on a specific professional task.
1. **The Project:** You are working in `20_Projects/App_Secrets_Expiry_Logic/`.
2. **The Action:** You are implementing the logic. You document the implementation strategy: *"Applying the rotation pattern learned in `[[Security_Best_Practices]]`"*.
3. **The Connection:** Within that Area note, you further link to a technical implementation guide: *"Refer to `[[JWT_Implementation_Guide]]` for token structure."*
4. **The Result:** A single project note connects your **Work** to your **Expertise** to your **Reference Library**.

### 🎓 Scenario 3: The Scholar (Daily $\rightarrow$ Area)
**Context:** You are performing academic study alongside work.
1. **The Daily Log:** In your `10_Daily/2023-10-27.md`, you log your study session.
2. **The Action:** *"Spent 2 hours studying `[[Software_Protection]]` concepts, specifically focusing on anti-debugging techniques."*
3. **The Connection:** By linking the `[[Software_Protection]]` Area note, you can later use the **Obsat-side Graph View** to see exactly which days you worked on this specific academic subject.

---
*End of SOP*
"@

    # 4. Define the Template Content
    $TemplatePath = Join-Path $Path "90_System\Templates\Atomic_Note_Template.md"
    $TemplateContent = @"
---
type: concept
tags: []
status: research
date: $(Get-Date -Format "yyyy-MM-dd")
---
# $'\n'
## Summary
$'\n'
## Technical Details
$'\n'
## References
$'\n'
"@

    # 5. Execute Safe Writes (This handles all directory checks internally)
    Write-FileSafely -FilePath $ReadmePath -Content $ReadmeContent
    Write-FileSafely -FilePath $TemplatePath -Content $TemplateContent

    Write-Host "`n[!] Vault Initialization Complete. Ready for Intel Collection." -ForegroundColor Cyan

$ProjectTemplatePath = Join-Path $Path "90_System\Templates\Project_Template.md"
$ProjectTemplateContent = @"
---
type: project
status: active
date: $(Get-Date -Format "yyyy-MM-dd")
tags: [active]
---
# Project: 

## 🎯 Objective
$'\n'
## 🔍 Progress Log
- $(Get-Date -Format "yyyy-MM-dd"): Project Initiated.
## 🛠 Tools & Artifacts
$'\n'
## 📂 Related Areas
[[ ]]
"@

    Write-FileSafely -FilePath $ProjectTemplatePath -Content $ProjectTemplateContent

}

# Main Execution Block
try {
    Initialize-Vault -Path (Join-Path $RootPath $VaultName)
}
catch {
    Write-Error "Critical Failure during Vault Initialization: $_"
}

<#
.SYNOPSIS
    Lab Creation Script - Generates users, groups, and OUs for testing environments.

.DESCRIPTION
    Creates multiple OUs with users and groups based on real department names,
    then assigns random users to random groups. Designed for lab/testing environments.

.PARAMETER DomainName
    The Active Directory domain name (e.g., "contoso.com" or "CONTOSO")

.PARAMETER AdminUsername
    Username of a domain admin account (e.g., "Administrator" or "CONTOSO\Administrator")

.PARAMETER AdminPassword
    Password for the domain admin account

.PARAMETER RootOu
    The root OU path where all lab OUs will be created (e.g., "OU=Lab,DC=contoso,DC=com")

.PARAMETER UserCount
    Number of users to create (default: 1000)

.PARAMETER GroupCount
    Number of groups to create (default: 100)

.PARAMETER MinGroupMembers
    Minimum members per group (default: 3)

.PARAMETER MaxGroupMembers
    Maximum members per group (default: 20)

.PARAMETER Departments
    Array of department names to create OUs for (default: common enterprise departments)

.EXAMPLE
    .\Lab-Creation.ps1 -DomainName "contoso.com" -AdminUsername "Administrator" -AdminPassword "P@ssw0rd!" -RootOu "OU=Lab,DC=contoso,DC=com"
#>

param(
    [Parameter(Mandatory = $true, HelpMessage = "Active Directory domain name (e.g., contoso.com or CONTOSO)")]
    [ValidateNotNullOrEmpty()]
    [string]$DomainName,

    [Parameter(Mandatory = $true, HelpMessage = "Domain admin username")]
    [ValidateNotNullOrEmpty()]
    [string]$AdminUsername,

    [Parameter(Mandatory = $true, HelpMessage = "Domain admin password")]
    [ValidateNotNullOrEmpty()]
    [string]$AdminPassword,

    [Parameter(Mandatory = $true, HelpMessage = "Root OU path for all lab resources (e.g., OU=Lab,DC=contoso,DC=com)")]
    [ValidateNotNullOrEmpty()]
    [string]$RootOu,

    [Parameter(Mandatory = $false)]
    [ValidateRange(1, 10000)]
    [int]$UserCount = 1000,

    [Parameter(Mandatory = $false)]
    [ValidateRange(1, 1000)]
    [int]$GroupCount = 100,

    [Parameter(Mandatory = $false)]
    [ValidateRange(1, 100)]
    [int]$MinGroupMembers = 3,

    [Parameter(Mandatory = $false)]
    [ValidateRange(1, 100)]
    [int]$MaxGroupMembers = 20,

    [Parameter(Mandatory = $false)]
    [string[]]$Departments = @(
        "Information Technology",
        "Human Resources",
        "Finance",
        "Marketing",
        "Sales",
        "Engineering",
        "Research and Development",
        "Operations",
        "Customer Service",
        "Legal",
        "Compliance",
        "Product Management",
        "Quality Assurance",
        "Supply Chain",
        "Facilities"
    ),

    [Parameter(Mandatory = $false)]
    [switch]$CleanExisting,

    [Parameter(Mandatory = $false)]
    [switch]$UseSsl
)

#Requires -Module ActiveDirectory

$ErrorActionPreference = "Stop"

# ============================================================
# FUNCTIONS
# ============================================================

function Write-Log {
    param(
        [string]$Message,
        [ValidateSet("INFO", "WARNING", "ERROR", "SUCCESS")]
        [string]$Level = "INFO"
    )
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $color = switch ($Level) {
        "INFO" { "Cyan" }
        "WARNING" { "Yellow" }
        "ERROR" { "Red" }
        "SUCCESS" { "Green" }
    }
    Write-Host "[$timestamp] [$Level] $Message" -ForegroundColor $color
}

function Get-RandomPassword {
    param([int]$Length = 16)

    $specialChars = '!@#$%^&*'
    $uppercase = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ'
    $lowercase = 'abcdefghijklmnopqrstuvwxyz'
    $numbers = '0123456789'

    $password = ""
    $password += $specialChars[(Get-Random -Maximum $specialChars.Length)]
    $password += $uppercase[(Get-Random -Maximum $uppercase.Length)]
    $password += $lowercase[(Get-Random -Maximum $lowercase.Length)]
    $password += $numbers[(Get-Random -Maximum $numbers.Length)]

    $allChars = $specialChars + $uppercase + $lowercase + $numbers
    for ($i = 4; $i -lt $Length; $i++) {
        $password += $allChars[(Get-Random -Maximum $allChars.Length)]
    }

    return ($password.ToCharArray() | Get-Random -Count $Length) -join ''
}

function New-LabOu {
    param(
        [string]$Name,
        [string]$ParentPath
    )

    $ouPath = "OU=$Name,$ParentPath"

    # First verify parent exists
    try {
        $null = Get-ADOrganizationalUnit -Identity $ParentPath -Credential $script:Credential -ErrorAction Stop
    }
    catch {
        Write-Log "Parent OU does not exist or access denied: $ParentPath" -Level "ERROR"
        return $null
    }

    # Check if OU already exists using try/catch
    try {
        $existingOu = Get-ADOrganizationalUnit -Identity $ouPath -Credential $script:Credential -ErrorAction Stop
        if ($existingOu) {
            Write-Log "OU already exists: $Name" -Level "WARNING"
            return $ouPath
        }
    }
    catch {
        # OU doesn't exist, that's what we want - proceed with creation
        if ($_.Exception.Message -notmatch "not found" -and $_.Exception.Message -notmatch "ADIdentityNotFoundException") {
            Write-Log "Error checking OU '$Name': $($_.Exception.Message)" -Level "ERROR"
            return $null
        }
    }

    # Create the OU
    try {
        New-ADOrganizationalUnit -Name $Name -Path $ParentPath -ProtectedFromAccidentalDeletion $false -Credential $script:Credential -ErrorAction Stop
        Write-Log "Created OU: $Name" -Level "SUCCESS"
        return $ouPath
    }
    catch {
        if ($_.Exception.Message -match "Access is denied") {
            Write-Log "Access denied creating OU '$Name' - check admin permissions" -Level "ERROR"
        }
        elseif ($_.Exception.Message -match "already exists") {
            Write-Log "OU already exists: $Name" -Level "WARNING"
            return $ouPath
        }
        else {
            Write-Log "Failed to create OU '$Name': $($_.Exception.Message)" -Level "ERROR"
        }
        return $null
    }
}

function New-LabUser {
    param(
        [string]$FirstName,
        [string]$LastName,
        [string]$Username,
        [string]$OuPath,
        [string]$Password
    )

    try {
        $existingUser = Get-ADUser -Identity $Username -Credential $script:Credential -ErrorAction Stop
        if ($existingUser) {
            return @{ Status = "Exists"; Username = $Username }
        }
    }
    catch {
        # Only warn if it's a real error, not "not found"
        $errorMsg = $_.Exception.Message
        if ($errorMsg -notmatch "Cannot find" -and $errorMsg -notmatch "does not exist" -and $errorMsg -notmatch "ADIdentityNotFoundException") {
            Write-Log "Error checking user '$Username': $errorMsg" -Level "WARNING"
        }
    }

    try {
        New-ADUser `
            -Name $Username `
            -GivenName $FirstName `
            -Surname $LastName `
            -SamAccountName $Username `
            -UserPrincipalName "$Username@$DomainName" `
            -Path $OuPath `
            -AccountPassword (ConvertTo-SecureString $Password -AsPlainText -Force) `
            -Enabled $true `
            -PasswordNeverExpires $true `
            -CannotChangePassword $false `
            -ChangePasswordAtLogon $false `
            -Description "Lab user - $FirstName $LastName" `
            -Credential $script:Credential `
            -ErrorAction Stop
        return @{ Status = "Created"; Username = $Username }
    }
    catch {
        if ($_.Exception.Message -match "already exists") {
            return @{ Status = "Exists"; Username = $Username }
        }
        Write-Log "Failed to create user '$Username': $($_.Exception.Message)" -Level "ERROR"
        return @{ Status = "Failed"; Username = $Username; Error = $_.Exception.Message }
    }
}

function New-LabGroup {
    param(
        [string]$GroupName,
        [string]$OuPath,
        [string]$Description = ""
    )

    try {
        $existingGroup = Get-ADGroup -Identity $GroupName -Credential $script:Credential -ErrorAction Stop
        if ($existingGroup) {
            return @{ Status = "Exists"; GroupName = $GroupName }
        }
    }
    catch {
        # Only warn if it's a real error, not "not found"
        $errorMsg = $_.Exception.Message
        if ($errorMsg -notmatch "Cannot find" -and $errorMsg -notmatch "does not exist" -and $errorMsg -notmatch "ADIdentityNotFoundException") {
            Write-Log "Error checking group '$GroupName': $errorMsg" -Level "WARNING"
        }
    }

    try {
        New-ADGroup `
            -Name $GroupName `
            -SamAccountName $GroupName `
            -GroupCategory Security `
            -GroupScope Global `
            -Path $OuPath `
            -Description $Description `
            -Credential $script:Credential `
            -ErrorAction Stop
        return @{ Status = "Created"; GroupName = $GroupName }
    }
    catch {
        if ($_.Exception.Message -match "already exists") {
            return @{ Status = "Exists"; GroupName = $GroupName }
        }
        Write-Log "Failed to create group '$GroupName': $($_.Exception.Message)" -Level "ERROR"
        return @{ Status = "Failed"; GroupName = $GroupName; Error = $_.Exception.Message }
    }
}

function Add-RandomGroupMembership {
    param(
        [string]$Username,
        [string[]]$AvailableGroups,
        [int]$MinMembers,
        [int]$MaxMembers
    )

    $membershipCount = Get-Random -Minimum $MinMembers -Maximum ($MaxMembers + 1)
    $selectedGroups = Get-Random -InputObject $AvailableGroups -Count ([Math]::Min($membershipCount, $AvailableGroups.Count))

    $added = 0
    foreach ($group in $selectedGroups) {
        try {
            Add-ADGroupMember -Identity $group -Members $Username -Credential $script:Credential -ErrorAction Stop
            $added++
        }
        catch {
            Write-Log "Failed to add $Username to group $group $($_.Exception.Message)" -Level "WARNING"
        }
    }
    return $added
}

function Remove-LabEnvironment {
    param(
        [string]$BasePath
    )

    Write-Log "Cleaning existing lab environment under: $BasePath" -Level "WARNING"

    try {
        if (Get-ADOrganizationalUnit -Identity $BasePath -Credential $script:Credential -ErrorAction SilentlyContinue) {
            Set-ADOrganizationalUnit -Identity $BasePath -ProtectedFromAccidentalDeletion $false -Credential $script:Credential
            Get-ADOrganizationalUnit -Identity $BasePath -Credential $script:Credential | Remove-ADOrganizationalUnit -Recursive -Confirm:$false -Credential $script:Credential -ErrorAction Stop
            Write-Log "Removed root OU and all child OUs" -Level "SUCCESS"
        }
        else {
            Write-Log "Root OU does not exist, nothing to clean" -Level "INFO"
        }
    }
    catch {
        Write-Log "Failed to clean lab environment: $($_.Exception.Message)" -Level "ERROR"
    }
}

function Test-OuPath {
    param([string]$OuPath)

    try {
        $null = Get-ADOrganizationalUnit -Identity $OuPath -Credential $script:Credential -ErrorAction Stop
        return $true
    }
    catch {
        return $false
    }
}

function Sanitize-OuName {
    param([string]$Name)

    # Remove invalid OU characters and replace spaces with dashes
    $sanitized = $Name -replace '[\\/"\[\]\+|;<>=,*?]', '' -replace '\s+', '-'
    return $sanitized
}

# ============================================================
# SCRIPT BODY
# ============================================================

Write-Host ""
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "     LAB CREATION SCRIPT" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host ""

# Validate Admin credentials
Write-Log "Validating admin credentials..."
$securePassword = ConvertTo-SecureString $AdminPassword -AsPlainText -Force
$script:Credential = New-Object System.Management.Automation.PSCredential("${DomainName}\${AdminUsername}", $securePassword)

try {
    $null = Get-ADDomain -Credential $script:Credential -ErrorAction Stop
    Write-Log "Credentials validated successfully" -Level "SUCCESS"
}
catch {
    Write-Log "Invalid credentials or unable to connect to domain: $($_.Exception.Message)" -Level "ERROR"
    exit 1
}

# Get domain info
try {
    $domainInfo = Get-ADDomain -Credential $script:Credential
    $domainDn = $domainInfo.DistinguishedName
    Write-Log "Connected to domain: $domainDn" -Level "INFO"
}
catch {
    Write-Log "Failed to get domain info: $($_.Exception.Message)" -Level "ERROR"
    exit 1
}

# Validate Root OU format
Write-Log "Validating root OU path..."
if (-not (Test-OuPath -OuPath $RootOu)) {
    Write-Log "Root OU does not exist. Creating: $RootOu"
    try {
        # Extract parent path and OU name
        $parts = $RootOu -split '(?<!\\),'
        $ouName = ($RootOu -replace 'OU=', '' -replace ',.*', '').Trim()
        $parentPath = ($RootOu -replace '^[^,]+,', '').Trim()

        # Check if parent exists or create it
        if (-not (Test-OuPath -OuPath $parentPath)) {
            Write-Log "Parent path does not exist: $parentPath" -Level "ERROR"
            Write-Log "Please ensure the parent path exists before running this script." -Level "ERROR"
            exit 1
        }

        New-ADOrganizationalUnit -Name $ouName -Path $parentPath -ProtectedFromAccidentalDeletion $false -Credential $script:Credential -ErrorAction Stop
        Write-Log "Created root OU: $RootOu" -Level "SUCCESS"
    }
    catch {
        Write-Log "Failed to create root OU: $($_.Exception.Message)" -Level "ERROR"
        exit 1
    }
}
else {
    Write-Log "Root OU validated: $RootOu" -Level "SUCCESS"
}

# Clean existing if requested
if ($CleanExisting) {
    Remove-LabEnvironment -BasePath $RootOu
}

# Create container OUs under Root OU
Write-Log "Creating container OUs..."
$labUsersOu = New-LabOu -Name "Users" -ParentPath $RootOu
$labGroupsOu = New-LabOu -Name "Groups" -ParentPath $RootOu

if (-not $labUsersOu -or -not $labGroupsOu) {
    Write-Log "Failed to create container OUs" -Level "ERROR"
    exit 1
}

Write-Log "Created container OUs: Users, Groups" -Level "SUCCESS"

# ============================================================
# CREATE DEPARTMENT OUs AND DISTRIBUTE USERS/GROUPS
# ============================================================
Write-Log "Creating department OUs based on real organization departments..."

$userOuPaths = @()
$groupOuPaths = @()
$departmentMappings = @{}

foreach ($department in $Departments) {
    $sanitizedDept = Sanitize-OuName -Name $department

    # Create Users sub-OU for this department
    $userOuPath = New-LabOu -Name "Users-$sanitizedDept" -ParentPath $labUsersOu
    # Create Groups sub-OU for this department
    $groupOuPath = New-LabOu -Name "Groups-$sanitizedDept" -ParentPath $labGroupsOu

    if ($userOuPath) {
        $userOuPaths += $userOuPath
        $departmentMappings[$department] = @{ UserOu = $userOuPath; GroupOu = $groupOuPath }
    }
    if ($groupOuPath) {
        $groupOuPaths += $groupOuPath
    }
}

Write-Log "Created $($userOuPaths.Count) department user OUs and $($groupOuPaths.Count) department group OUs" -Level "SUCCESS"

# ============================================================
# CREATE GROUPS
# ============================================================
Write-Log "Creating $GroupCount groups across departments..."

$createdGroups = @()
$groupBatchSize = 50

# Group type prefixes for realistic naming
$groupPrefixes = @(
    "DL",   # Distribution List
    "SG"    # Security Group
)

for ($i = 1; $i -le $GroupCount; $i++) {
    $ouPath = $groupOuPaths | Get-Random

    # Extract department name from OU path - get text between "OU=Groups-" and first ","
    if ($ouPath -match 'OU=Groups-([^,]+)') {
        $deptName = $matches[1] -replace '-', ' '
    }
    else {
        $deptName = "General"
    }

    # Sanitize department name - remove any LDAP path characters
    $deptName = $deptName -replace '[,/=+<>]', ''

    $prefix = $groupPrefixes | Get-Random

    # Generate realistic group names
    $groupSuffix = switch (Get-Random -Maximum 6) {
        0 { "Management" }
        1 { "Team" }
        2 { "Project" }
        3 { "Committee" }
        4 { "Council" }
        5 { "WorkingGroup" }
    }

    # Create group name without spaces or special characters
    $groupName = "${prefix}-${deptName}-${groupSuffix}-$(Get-Random -Maximum 9999)"
    $groupName = $groupName -replace '\s+', ''  # Remove all spaces

    $result = New-LabGroup -GroupName $groupName -OuPath $ouPath -Description "Lab created $deptName $groupSuffix"
    if ($result.Status -eq "Created") {
        $createdGroups += $groupName
    }

    if ($i % $groupBatchSize -eq 0) {
        Write-Log "Processed $i / $GroupCount groups (created so far: $($createdGroups.Count))..."
    }
}

Write-Log "Successfully created $($createdGroups.Count) groups" -Level "SUCCESS"

# ============================================================
# CREATE USERS
# ============================================================
Write-Log "Creating $UserCount users across departments..."

$firstNames = @(
    "James", "Mary", "John", "Patricia", "Robert", "Jennifer", "Michael", "Linda",
    "William", "Elizabeth", "David", "Barbara", "Richard", "Susan", "Joseph", "Jessica",
    "Thomas", "Sarah", "Charles", "Karen", "Christopher", "Nancy", "Daniel", "Lisa",
    "Matthew", "Betty", "Anthony", "Margaret", "Mark", "Sandra", "Donald", "Ashley",
    "Steven", "Kimberly", "Paul", "Emily", "Andrew", "Donna", "Joshua", "Michelle",
    "Kenneth", "Dorothy", "Kevin", "Carol", "Brian", "Amanda", "George", "Melissa",
    "Timothy", "Deborah", "Ronald", "Stephanie", "Edward", "Rebecca", "Jason", "Sharon",
    "Jeffrey", "Laura", "Ryan", "Cynthia", "Jacob", "Kathleen", "Gary", "Amy",
    "Nicholas", "Angela", "Eric", "Shirley", "Jonathan", "Anna", "Stephen", "Brenda",
    "Larry", "Pamela", "Justin", "Emma", "Scott", "Nicole", "Brandon", "Helen",
    "Benjamin", "Samantha", "Samuel", "Katherine", "Raymond", "Christine", "Gregory", "Debra",
    "Frank", "Rachel", "Alexander", "Carolyn", "Patrick", "Janet", "Raymond", "Catherine"
)

$lastNames = @(
    "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis",
    "Rodriguez", "Martinez", "Hernandez", "Lopez", "Gonzalez", "Wilson", "Anderson", "Thomas",
    "Taylor", "Moore", "Jackson", "Martin", "Lee", "Perez", "Thompson", "White",
    "Harris", "Sanchez", "Clark", "Ramirez", "Lewis", "Robinson", "Walker", "Young",
    "Allen", "King", "Wright", "Scott", "Torres", "Nguyen", "Hill", "Flores",
    "Green", "Adams", "Nelson", "Baker", "Hall", "Rivera", "Campbell", "Mitchell",
    "Carter", "Roberts", "Gomez", "Phillips", "Evans", "Turner", "Diaz", "Parker",
    "Cruz", "Edwards", "Collins", "Reyes", "Stewart", "Morris", "Morales", "Murphy"
)

# Job titles for realistic user descriptions
$titles = @(
    "Manager", "Director", "Analyst", "Specialist", "Coordinator", "Administrator",
    "Engineer", "Technician", "Consultant", "Associate", "Senior", "Junior",
    "Lead", "Assistant", "Executive", "Officer", "Supervisor", "Representative"
)

$userBatchSize = 100
$allUsers = @()
$usersByDepartment = @{}

# Initialize department user tracking
foreach ($department in $Departments) {
    $usersByDepartment[$department] = @()
}

for ($i = 1; $i -le $UserCount; $i++) {
    # Select random department and OU
    $department = $Departments | Get-Random
    $ouPath = $userOuPaths | Get-Random

    # Generate user details
    $firstName = $firstNames | Get-Random
    $lastName = $lastNames | Get-Random
    $title = $titles | Get-Random
    $username = "$($firstName.Substring(0,1))$($lastName)$(Get-Random -Maximum 999)"
    $password = Get-RandomPassword -Length 16

    $result = New-LabUser -FirstName $firstName -LastName $lastName -Username $username -OuPath $ouPath -Password $password

    if ($result.Status -eq "Created") {
        $allUsers += $username
        # Track user for their primary department
        $usersByDepartment[$department] += $username
    }

    if ($i % $userBatchSize -eq 0) {
        Write-Log "Processed $i / $UserCount (created so far: $($allUsers.Count))..."
    }
}

Write-Log "Successfully created $($allUsers.Count) users" -Level "SUCCESS"

# ============================================================
# ASSIGN RANDOM GROUP MEMBERSHIPS
# ============================================================
if ($createdGroups.Count -eq 0) {
    Write-Log "No groups created - skipping group membership assignment" -Level "WARNING"
}
elseif ($allUsers.Count -eq 0) {
    Write-Log "No users created - skipping group membership assignment" -Level "WARNING"
}
else {
    Write-Log "Assigning random group memberships..."
    $memberBatchSize = 50
    $processedUsers = 0

    foreach ($username in $allUsers) {
        $added = Add-RandomGroupMembership -Username $username -AvailableGroups $createdGroups -MinMembers $MinGroupMembers -MaxMembers $MaxGroupMembers
        $processedUsers++

        if ($processedUsers % $memberBatchSize -eq 0) {
            Write-Log "Assigned memberships to $processedUsers / $($allUsers.Count) users..."
        }
    }

    Write-Log "Assigned memberships to all users" -Level "SUCCESS"
}

# ============================================================
# SUMMARY REPORT
# ============================================================
Write-Host ""
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "     LAB CREATION COMPLETE" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host ""
Write-Log "Summary:" -Level "INFO"
Write-Log "  Domain:              $DomainName" -Level "INFO"
Write-Log "  Root OU:             $RootOu" -Level "INFO"
Write-Log "  Departments:         $($Departments.Count)" -Level "INFO"
Write-Log "  Users Created:       $($allUsers.Count)" -Level "INFO"
Write-Log "  Groups Created:      $($createdGroups.Count)" -Level "INFO"
Write-Log "  Membership Range:   $MinGroupMembers - $MaxGroupMembers per user" -Level "INFO"
Write-Host ""
Write-Host "  Department OUs:" -ForegroundColor White
foreach ($dept in $Departments) {
    $sanitized = Sanitize-OuName -Name $dept
    Write-Host "    - $dept" -ForegroundColor Gray
}
Write-Host ""
Write-Log "All users have been enabled with randomly generated passwords" -Level "WARNING"
Write-Log "Recommend changing passwords before production use." -Level "WARNING"
Write-Host ""

# Export user/group list to JSON for reference
$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$exportPath = ".\LabCreation_$timestamp.json"

$exportData = @{
    CreatedAt   = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Domain      = $DomainName
    RootOu      = $RootOu
    Departments = $Departments
    UserCount   = $allUsers.Count
    GroupCount  = $createdGroups.Count
    Users       = $allUsers
    Groups      = $createdGroups
    UserOUs     = $userOuPaths
    GroupOUs    = $groupOuPaths
}

try {
    $exportData | ConvertTo-Json -Depth 10 | Out-File -FilePath $exportPath -Encoding UTF8
    Write-Log "Lab data exported to: $exportPath" -Level "SUCCESS"
}
catch {
    Write-Log "Failed to export lab data: $($_.Exception.Message)" -Level "WARNING"
}

Write-Log "Lab creation completed successfully!" -Level "SUCCESS"

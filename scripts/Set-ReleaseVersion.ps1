param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$AssemblyVersion
)

$ErrorActionPreference = 'Stop'

function Update-XmlNodeValue {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$XPath,

        [Parameter(Mandatory = $true)]
        [string]$Value,

        [hashtable]$NamespaceMap = @{}
    )

    [xml]$document = Get-Content -Path $Path -Raw
    $navigator = $document.CreateNavigator()
    $namespaceManager = New-Object System.Xml.XmlNamespaceManager($navigator.NameTable)
    foreach ($entry in $NamespaceMap.GetEnumerator()) {
        $namespaceManager.AddNamespace($entry.Key, $entry.Value)
    }

    $node = $document.SelectSingleNode($XPath, $namespaceManager)
    if ($null -eq $node) {
        throw "Node '$XPath' was not found in '$Path'."
    }

    $node.InnerText = $Value
    $document.Save($Path)
}

$projectFiles = @(
    'Directory.Build.props',
    'LanMountainDesktop/LanMountainDesktop.csproj',
    'LanMountainDesktop.Launcher/LanMountainDesktop.Launcher.csproj',
    'LanMountainDesktop.Shared.Contracts/LanMountainDesktop.Shared.Contracts.csproj'
)

foreach ($projectFile in $projectFiles) {
    Update-XmlNodeValue -Path $projectFile -XPath '/Project/PropertyGroup/Version' -Value $Version
}

$manifestNamespace = @{ asm = 'urn:schemas-microsoft-com:asm.v1' }
Update-XmlNodeValue -Path 'LanMountainDesktop/app.manifest' -XPath '/asm:assembly/asm:assemblyIdentity/@version' -Value $AssemblyVersion -NamespaceMap $manifestNamespace
Update-XmlNodeValue -Path 'LanMountainDesktop.Launcher/app.manifest' -XPath '/asm:assembly/asm:assemblyIdentity/@version' -Value $AssemblyVersion -NamespaceMap $manifestNamespace

Write-Host "Stamped release version metadata. Version=$Version AssemblyVersion=$AssemblyVersion"

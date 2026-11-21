<#
.SYNOPSIS
  Gives DeckIsland.exe a package identity so it can mirror Windows notifications. NOT RUN OR TESTED BY THE AUTHOR OF THIS CHANGE.

.DESCRIPTION
  Run once, in an elevated PowerShell, from a folder that holds DeckIsland.exe:
      .\register.ps1 -AppFolder "C:\path\to\folder\with\DeckIsland.exe"
  It (1) makes a self-signed certificate "CN=DeckIsland Dev", (2) trusts it for this machine (Trusted People store: this is a
  security change to your PC, read the script first), (3) packs and signs the sparse package in AppxManifest.xml, and
  (4) registers it with AppFolder as its external location. Needs the Windows SDK (makeappx.exe, signtool.exe).
  Restart DeckIsland afterwards and allow notification access if Windows asks.
  Undo with:  Get-AppxPackage DeckIsland.Notifications | Remove-AppxPackage
#>
param([Parameter(Mandatory)][string]$AppFolder)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path (Join-Path $AppFolder 'DeckIsland.exe'))) { throw "DeckIsland.exe not found in $AppFolder" }
$tool = { param($name) Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\$name" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName }
$makeappx = & $tool 'makeappx.exe'; $signtool = & $tool 'signtool.exe'
if (-not $makeappx -or -not $signtool) { throw 'Install the Windows SDK (makeappx.exe and signtool.exe were not found).' }

$work = Join-Path $env:TEMP 'deckisland-sparse'
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory $work | Out-Null
Copy-Item (Join-Path $PSScriptRoot 'AppxManifest.xml') $work

# The manifest's logo, drawn from the program's own icon.
Add-Type -AssemblyName System.Drawing
$icon = [System.Drawing.Icon]::ExtractAssociatedIcon((Join-Path $AppFolder 'DeckIsland.exe'))
$bitmap = New-Object System.Drawing.Bitmap 150, 150
$graphics = [System.Drawing.Graphics]::FromImage($bitmap); $graphics.DrawIcon($icon, (New-Object System.Drawing.Rectangle 0, 0, 150, 150)); $graphics.Dispose()
$bitmap.Save((Join-Path $work 'logo.png'), [System.Drawing.Imaging.ImageFormat]::Png); $bitmap.Dispose()

$cert = New-SelfSignedCertificate -Type Custom -Subject 'CN=DeckIsland Dev' -KeyUsage DigitalSignature -FriendlyName 'DeckIsland Dev' `
  -CertStoreLocation 'Cert:\CurrentUser\My' -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
$cer = Join-Path $work 'deckisland.cer'
Export-Certificate -Cert $cert -FilePath $cer | Out-Null
Import-Certificate -FilePath $cer -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null

$package = Join-Path $work 'DeckIsland.Notifications.msix'
& $makeappx pack /d $work /p $package /nv | Out-Null
& $signtool sign /fd SHA256 /sha1 $cert.Thumbprint $package | Out-Null
Add-AppxPackage -Path $package -ExternalLocation $AppFolder
Write-Host 'Registered. Restart DeckIsland.'

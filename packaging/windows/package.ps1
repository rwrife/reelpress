[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string] $OutputDirectory = "artifacts",

    [Parameter(Mandatory = $false)]
    [ValidatePattern('^\d+\.\d+\.\d+\.\d+$')]
    [string] $PackageVersion = "0.1.0.0"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
if (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $RepoRoot $OutputDirectory
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$FfmpegRelease = "https://github.com/eugeneware/ffmpeg-static/releases/download/b6.1.1"
$ExpectedHashes = @{
    "ffmpeg.exe" = "04e1307997530f9cf2fe35cba2ca7e8875ca91da02f89d6c7243df819c94ad00"
    "ffprobe.exe" = "3a7e2dc003dc2cd1472827e4c7c4f056ae1ae0ae7c5bbc580c99b49827351ba4"
    "FFmpeg-GPLv3.txt" = "8ceb4b9ee5adedde47b31e975c1d90c73ad27b6b165a1dcd80c7c545eb65b903"
}

function Get-VerifiedFile {
    param(
        [Parameter(Mandatory = $true)][string] $Url,
        [Parameter(Mandatory = $true)][string] $Destination,
        [Parameter(Mandatory = $true)][string] $ExpectedSha256
    )

    & curl.exe --fail --location --retry 3 $Url --output $Destination
    if ($LASTEXITCODE -ne 0) {
        throw "Download failed: $Url"
    }

    $actual = (Get-FileHash -Path $Destination -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $ExpectedSha256) {
        throw "Checksum mismatch for $Destination. Expected $ExpectedSha256, got $actual."
    }
}

function Find-WindowsSdkTool {
    param([Parameter(Mandatory = $true)][string] $Name)

    $sdkRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits/10/bin"
    $candidate = Get-ChildItem -Path $sdkRoot -Filter $Name -File -Recurse |
        Where-Object { $_.DirectoryName -like "*\x64" } |
        Sort-Object -Property FullName -Descending |
        Select-Object -First 1

    if ($null -eq $candidate) {
        throw "Unable to locate $Name under $sdkRoot."
    }

    return $candidate.FullName
}

$TempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("reelpress-package-" + [Guid]::NewGuid().ToString("N"))
$PublishRoot = Join-Path $TempRoot "publish"
$LicenseRoot = Join-Path $PublishRoot "licenses"
New-Item -ItemType Directory -Force -Path $PublishRoot, $LicenseRoot | Out-Null

try {
    Push-Location $RepoRoot
    try {
        dotnet publish "src/ReelPress.Desktop/ReelPress.Desktop.csproj" `
            --configuration Release `
            --runtime win-x64 `
            --self-contained true `
            --output $PublishRoot `
            -p:DebugType=None `
            -p:DebugSymbols=false
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed."
        }
    }
    finally {
        Pop-Location
    }

    $NativeRoot = Join-Path $PublishRoot "runtimes/win-x64/native"
    New-Item -ItemType Directory -Force -Path $NativeRoot | Out-Null

    Get-VerifiedFile `
        -Url "$FfmpegRelease/ffmpeg-win32-x64" `
        -Destination (Join-Path $NativeRoot "ffmpeg.exe") `
        -ExpectedSha256 $ExpectedHashes["ffmpeg.exe"]
    Get-VerifiedFile `
        -Url "$FfmpegRelease/ffprobe-win32-x64" `
        -Destination (Join-Path $NativeRoot "ffprobe.exe") `
        -ExpectedSha256 $ExpectedHashes["ffprobe.exe"]
    Get-VerifiedFile `
        -Url "$FfmpegRelease/win32-x64.LICENSE" `
        -Destination (Join-Path $LicenseRoot "FFmpeg-GPLv3.txt") `
        -ExpectedSha256 $ExpectedHashes["FFmpeg-GPLv3.txt"]

    Copy-Item (Join-Path $RepoRoot "LICENSE") (Join-Path $PublishRoot "LICENSE.txt")
    Copy-Item (Join-Path $RepoRoot "packaging/THIRD-PARTY-NOTICES.md") (Join-Path $PublishRoot "THIRD-PARTY-NOTICES.md")

    & (Join-Path $NativeRoot "ffmpeg.exe") -version | Select-Object -First 1
    if ($LASTEXITCODE -ne 0) { throw "Bundled ffmpeg smoke test failed." }
    & (Join-Path $NativeRoot "ffprobe.exe") -version | Select-Object -First 1
    if ($LASTEXITCODE -ne 0) { throw "Bundled ffprobe smoke test failed." }

    $ZipPath = Join-Path $OutputDirectory "reelpress-win-x64.zip"
    if (Test-Path $ZipPath) { Remove-Item -Force $ZipPath }
    Compress-Archive -Path (Join-Path $PublishRoot "*") -DestinationPath $ZipPath -CompressionLevel Optimal

    $ZipSmokeRoot = Join-Path $TempRoot "zip-smoke"
    Expand-Archive -Path $ZipPath -DestinationPath $ZipSmokeRoot
    $DesktopExecutable = Join-Path $ZipSmokeRoot "ReelPress.Desktop.exe"
    $AppProcess = Start-Process -FilePath $DesktopExecutable -PassThru
    Start-Sleep -Seconds 5
    if ($AppProcess.HasExited) {
        if ($AppProcess.ExitCode -ne 0) {
            throw "Portable application smoke test exited with code $($AppProcess.ExitCode)."
        }
    }
    else {
        Stop-Process -Id $AppProcess.Id -Force
        $AppProcess.WaitForExit()
    }

    Copy-Item (Join-Path $RepoRoot "packaging/windows/AppxManifest.xml") (Join-Path $PublishRoot "AppxManifest.xml")
    [xml] $manifest = Get-Content -Raw (Join-Path $PublishRoot "AppxManifest.xml")
    $manifest.Package.Identity.Version = $PackageVersion
    $manifest.Save((Join-Path $PublishRoot "AppxManifest.xml"))

    & python (Join-Path $RepoRoot "packaging/generate_brand_assets.py") --output (Join-Path $PublishRoot "Assets")
    if ($LASTEXITCODE -ne 0) { throw "MSIX asset generation failed." }

    $MakeAppx = Find-WindowsSdkTool "makeappx.exe"
    $SignTool = Find-WindowsSdkTool "signtool.exe"
    $MsixPath = Join-Path $OutputDirectory "ReelPress_${PackageVersion}_x64.msix"
    if (Test-Path $MsixPath) { Remove-Item -Force $MsixPath }
    & $MakeAppx pack /o /d $PublishRoot /p $MsixPath
    if ($LASTEXITCODE -ne 0) { throw "makeappx failed." }

    $MsixSmokeRoot = Join-Path $TempRoot "msix-smoke"
    & $MakeAppx unpack /o /p $MsixPath /d $MsixSmokeRoot
    if ($LASTEXITCODE -ne 0) { throw "MSIX unpack verification failed." }
    if (-not (Test-Path (Join-Path $MsixSmokeRoot "ReelPress.Desktop.exe"))) {
        throw "MSIX verification did not find ReelPress.Desktop.exe."
    }
    if (-not (Test-Path (Join-Path $MsixSmokeRoot "runtimes/win-x64/native/ffmpeg.exe"))) {
        throw "MSIX verification did not find the bundled ffmpeg.exe."
    }

    $Certificate = New-SelfSignedCertificate `
        -Type Custom `
        -Subject "CN=ReelPress" `
        -FriendlyName "ReelPress CI test signing certificate" `
        -KeyUsage DigitalSignature `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3") `
        -NotAfter (Get-Date).AddYears(2)

    $PasswordText = [Guid]::NewGuid().ToString("N")
    $Password = ConvertTo-SecureString -String $PasswordText -Force -AsPlainText
    $PfxPath = Join-Path $TempRoot "ReelPress-test.pfx"
    $CerPath = Join-Path $OutputDirectory "ReelPress-test.cer"
    Export-PfxCertificate -Cert $Certificate -FilePath $PfxPath -Password $Password | Out-Null
    Export-Certificate -Cert $Certificate -FilePath $CerPath -Type CERT | Out-Null
    try {
        & $SignTool sign /fd SHA256 /f $PfxPath /p $PasswordText $MsixPath
        if ($LASTEXITCODE -ne 0) { throw "signtool failed." }

        # Signature/hash verification succeeds before SignTool evaluates the
        # trust chain. The only accepted nonzero result is this expected error
        # for the intentionally self-signed, untrusted CI certificate.
        $PreviousNativeErrorPreference = $PSNativeCommandUseErrorActionPreference
        try {
            $PSNativeCommandUseErrorActionPreference = $false
            $VerifyOutput = (& $SignTool verify /pa /v $MsixPath 2>&1) -join [Environment]::NewLine
            $VerifyExitCode = $LASTEXITCODE
        }
        finally {
            $PSNativeCommandUseErrorActionPreference = $PreviousNativeErrorPreference
        }
        if ($VerifyExitCode -ne 0 `
            -and $VerifyOutput -notmatch "root\s+certificate\s+which\s+is\s+not\s+trusted\s+by\s+the\s+trust\s+provider") {
            throw "MSIX signature/hash verification failed: $VerifyOutput"
        }

        $Signature = Get-AuthenticodeSignature -FilePath $MsixPath
        if ($null -eq $Signature.SignerCertificate `
            -or $Signature.SignerCertificate.Thumbprint -ne $Certificate.Thumbprint) {
            throw "MSIX signer certificate verification failed."
        }
    }
    finally {
        Remove-Item -Path ("Cert:\CurrentUser\My\" + $Certificate.Thumbprint) -Force -ErrorAction SilentlyContinue
    }

    Write-Host "Created $ZipPath"
    Write-Host "Created $MsixPath"
    Write-Host "Created $CerPath"
}
finally {
    Remove-Item -Path $TempRoot -Recurse -Force -ErrorAction SilentlyContinue
}

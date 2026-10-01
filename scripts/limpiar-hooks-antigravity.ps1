<#
.SYNOPSIS
    Limpia los hooks heredados de openDynamic en la configuración de Antigravity.

.DESCRIPTION
    Busca los archivos hooks.json de Antigravity y elimina de forma no destructiva
    únicamente las claves 'openDynamic-approvals', 'openDynamic-status' y 'openDynamic-events',
    preservando intactas todas las demás herramientas y configuraciones del usuario.
    Genera un respaldo automático .bak con marca de tiempo antes de realizar cualquier cambio.
    Aborta de forma segura sin modificar el archivo si el JSON es inválido.

.PARAMETER Path
    Ruta opcional a un archivo hooks.json específico. Si no se indica, busca en las ubicaciones estándar:
    - $env:USERPROFILE\.gemini\config\hooks.json
    - $env:USERPROFILE\.gemini\antigravity\hooks.json

.EXAMPLE
    .\scripts\limpiar-hooks-antigravity.ps1 -WhatIf
    Muestra qué cambios se realizarían sin modificar los archivos.

.EXAMPLE
    .\scripts\limpiar-hooks-antigravity.ps1
    Ejecuta la limpieza con respaldo automático.
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param (
    [Parameter(Mandatory = $false)]
    [string]$Path
)

$targetFiles = @()

if ($Path) {
    if (Test-Path -Path $Path) {
        $targetFiles += (Resolve-Path -Path $Path).Path
    } else {
        Write-Warning "La ruta especificada no existe: $Path"
    }
} else {
    $standardPaths = @(
        (Join-Path $env:USERPROFILE ".gemini\config\hooks.json"),
        (Join-Path $env:USERPROFILE ".gemini\antigravity\hooks.json")
    )

    foreach ($p in $standardPaths) {
        if (Test-Path -Path $p) {
            $targetFiles += $p
        }
    }
}

if ($targetFiles.Count -eq 0) {
    Write-Host "No se encontraron archivos hooks.json en las ubicaciones estándar." -ForegroundColor Yellow
    return
}

$keysToRemove = @("openDynamic-approvals", "openDynamic-status", "openDynamic-events")

foreach ($file in $targetFiles) {
    Write-Host "Examinando: $file" -ForegroundColor Cyan

    $rawContent = Get-Content -Path $file -Raw -Encoding UTF8 -ErrorAction Stop

    $json = $null
    try {
        $json = $rawContent | ConvertFrom-Json -ErrorAction Stop
    } catch {
        Write-Error "El archivo '$file' contiene JSON inválido. Operación abortada sin tocar el archivo."
        continue
    }

    if ($null -eq $json) {
        Write-Host "El archivo '$file' está vacío o no contiene un objeto JSON válido." -ForegroundColor Yellow
        continue
    }

    $existingPropertyNames = @($json.psobject.properties.Name)
    $foundKeys = @()
    foreach ($k in $keysToRemove) {
        if ($existingPropertyNames -contains $k) {
            $foundKeys += $k
        }
    }

    if ($foundKeys.Count -eq 0) {
        Write-Host "No se encontraron claves de openDynamic en '$file'. No se requieren cambios." -ForegroundColor Green
        continue
    }

    Write-Host "Claves a retirar encontradas: $($foundKeys -join ', ')" -ForegroundColor Yellow

    if ($PSCmdlet.ShouldProcess($file, "Retirar claves [$($foundKeys -join ', ')] y generar respaldo")) {
        $timestamp = (Get-Date).ToString("yyyyMMdd-HHmmss")
        $bakPath = "$file.openDynamic-$timestamp.bak"

        Copy-Item -Path $file -Destination $bakPath -Force
        Write-Host "Respaldo creado en: $bakPath" -ForegroundColor Green

        foreach ($k in $foundKeys) {
            $json.psobject.properties.Remove($k)
        }

        $newJson = $json | ConvertTo-Json -Depth 20
        [System.IO.File]::WriteAllText($file, $newJson, [System.Text.Encoding]::UTF8)

        Write-Host "Limpieza completada exitosamente en '$file'." -ForegroundColor Green
    }
}

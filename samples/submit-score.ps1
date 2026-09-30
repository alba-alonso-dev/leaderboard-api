#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Submits a score as a game server, signing the request with HMAC-SHA256 (PowerShell 7+).

.EXAMPLE
    ./samples/submit-score.ps1 -GameId <id> -KeyId lbk_... -Secret lbs_... -PlayerId <id> -Value 4200
    ./samples/submit-score.ps1 ... -Metadata '{"level":3}' -Nonce 0123456789abcdef0123456789abcdef

.NOTES
    canonical = METHOD \n PATH \n TIMESTAMP \n NONCE \n hex(SHA256(body))
    signature = Base64(HMAC-SHA256(secret, canonical))
#>
param(
    [Parameter(Mandatory)] [string] $GameId,
    [Parameter(Mandatory)] [string] $KeyId,
    [Parameter(Mandatory)] [string] $Secret,
    [Parameter(Mandatory)] [string] $PlayerId,
    [Parameter(Mandatory)] [long] $Value,
    [string] $Metadata,
    [string] $Nonce,
    [string] $BaseUrl = ($env:BASE_URL ?? 'http://localhost:8080')
)

$ErrorActionPreference = 'Stop'
$utf8 = [System.Text.Encoding]::UTF8
$path = "/api/v1/games/$GameId/scores"

$body = if ($Metadata) { "{`"playerId`":`"$PlayerId`",`"value`":$Value,`"metadata`":$Metadata}" }
        else { "{`"playerId`":`"$PlayerId`",`"value`":$Value}" }
$bodyBytes = $utf8.GetBytes($body)

$timestamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString()
if (-not $Nonce) { $Nonce = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(16)).ToLowerInvariant() }
$bodyHash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bodyBytes)).ToLowerInvariant()
$canonical = "POST`n$path`n$timestamp`n$Nonce`n$bodyHash"
$signature = [Convert]::ToBase64String(
    [System.Security.Cryptography.HMACSHA256]::HashData($utf8.GetBytes($Secret), $utf8.GetBytes($canonical)))

$response = Invoke-WebRequest -Method Post -Uri "$BaseUrl$path" -SkipHttpErrorCheck `
    -ContentType 'application/json' -Body $bodyBytes `
    -Headers @{ 'X-Api-Key' = $KeyId; 'X-Timestamp' = $timestamp; 'X-Nonce' = $Nonce; 'X-Signature' = $signature }

$response.Content
"HTTP $($response.StatusCode)"

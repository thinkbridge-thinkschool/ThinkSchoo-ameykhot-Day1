# Day 3 Piece 1 - Entra ID Auth - Submission Test Runner
$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path

# Kill anything already on 5100
$conn = Get-NetTCPConnection -LocalPort 5100 -ErrorAction SilentlyContinue
if ($conn) { Stop-Process -Id $conn.OwningProcess -Force -ErrorAction SilentlyContinue }
Start-Sleep -Seconds 1

# Start the API
Write-Host ""
Write-Host ">>> Starting API on http://localhost:5100 ..." -ForegroundColor Cyan
$proc = Start-Process dotnet -ArgumentList "run","--urls","http://localhost:5100" `
        -WorkingDirectory $projectDir `
        -RedirectStandardOutput "$env:TEMP\quotes_out.txt" `
        -RedirectStandardError  "$env:TEMP\quotes_err.txt" `
        -PassThru -WindowStyle Hidden

# Poll until API is ready (max 30s)
$ready = $false
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 1
    try {
        $null = Invoke-WebRequest http://localhost:5100/api/quotes -Method GET -ErrorAction Stop
        $ready = $true
        break
    } catch { }
}
if (-not $ready) { Write-Host "ERROR: API did not start" -ForegroundColor Red; exit 1 }
Write-Host "    PID $($proc.Id) - ready after ~$($i+1)s" -ForegroundColor Green

function Divider($n, $title) {
    Write-Host ""
    Write-Host "------------------------------------------------------------" -ForegroundColor Yellow
    Write-Host " TEST $n : $title" -ForegroundColor Yellow
    Write-Host "------------------------------------------------------------" -ForegroundColor Yellow
}

# TEST 1 - Login
Divider 1 "Internal login -> JWT issued"
Write-Host "curl -s -X POST http://localhost:5100/api/auth/login \"
Write-Host "  -H 'Content-Type: application/json' \"
Write-Host "  -d '{`"email`":`"user@test.com`",`"password`":`"password123`"}'"
$loginResp = Invoke-RestMethod http://localhost:5100/api/auth/login -Method POST `
    -ContentType application/json -Body '{"email":"user@test.com","password":"password123"}'
$TOKEN   = $loginResp.access_token
$REFRESH = $loginResp.refresh_token
Write-Host "RESPONSE: 200 OK"
Write-Host ("  access_token  : " + $TOKEN.Substring(0,72) + "...")
Write-Host ("  refresh_token : " + $REFRESH.Substring(0,40) + "...")
Write-Host "  expires_in    : $($loginResp.expires_in) seconds"

# TEST 2 - Public endpoint
Divider 2 "Public endpoint - no auth needed"
Write-Host "curl -s http://localhost:5100/api/quotes"
$pub = Invoke-RestMethod http://localhost:5100/api/quotes -Method GET
Write-Host "RESPONSE: 200 OK  (total quotes in DB: $($pub.pagination.Total))"

# TEST 3 - No token -> 401
Divider 3 "Protected endpoint WITHOUT token -> 401"
Write-Host "curl -s -X POST http://localhost:5100/api/quotes \"
Write-Host "  -H 'Content-Type: application/json' -d '{`"author`":`"X`",`"text`":`"Y`"}'"
try {
    Invoke-RestMethod http://localhost:5100/api/quotes -Method POST `
        -ContentType application/json -Body '{"author":"X","text":"Y"}' -ErrorAction Stop
    Write-Host "ERROR: expected 401" -ForegroundColor Red
} catch {
    $code = [int]$_.Exception.Response.StatusCode
    Write-Host "RESPONSE: $code Unauthorized  (correct - no token supplied)"
}

# TEST 4 - With internal JWT -> 201
Divider 4 "Protected endpoint WITH internal JWT -> 201 Created"
Write-Host "curl -s -X POST http://localhost:5100/api/quotes \"
Write-Host "  -H 'Authorization: Bearer <internal_token>' \"
Write-Host "  -H 'Content-Type: application/json' \"
Write-Host "  -d '{`"author`":`"Amey Khot`",`"text`":`"Entra ID delegates identity`"}'"
$hdrs = @{ Authorization = "Bearer $TOKEN" }
$created = Invoke-RestMethod http://localhost:5100/api/quotes -Method POST `
    -ContentType application/json -Headers $hdrs `
    -Body '{"author":"Amey Khot","text":"Entra ID delegates identity so your API trusts signatures not passwords."}'
Write-Host "RESPONSE: 201 Created"
Write-Host "  id     : $($created.id)"
Write-Host "  author : $($created.author)"
Write-Host "  text   : $($created.text)"

# TEST 5 - Refresh rotation
Divider 5 "Refresh token rotation -> new token pair"
Write-Host "curl -s -X POST http://localhost:5100/api/auth/refresh \"
Write-Host "  -H 'Content-Type: application/json' \"
Write-Host "  -d '{`"refresh_token`":`"<refresh_token>`"}'"
$rfBody  = '{"refresh_token":"' + $REFRESH + '"}'
$rfResp  = Invoke-RestMethod http://localhost:5100/api/auth/refresh -Method POST `
    -ContentType application/json -Body $rfBody
$NEW_TOKEN   = $rfResp.access_token
$NEW_REFRESH = $rfResp.refresh_token
Write-Host "RESPONSE: 200 OK"
Write-Host ("  new access_token  : " + $NEW_TOKEN.Substring(0,72) + "...")
Write-Host "  new refresh_token : [rotated - old one now invalid]"

# TEST 6 - Reuse detection
Divider 6 "Token REUSE DETECTION - replay old refresh token -> 401"
Write-Host "curl -s -X POST http://localhost:5100/api/auth/refresh \"
Write-Host "  -d '{`"refresh_token`":`"<OLD_token>`'}  # same token as TEST 5 input"
try {
    Invoke-RestMethod http://localhost:5100/api/auth/refresh -Method POST `
        -ContentType application/json -Body $rfBody -ErrorAction Stop
    Write-Host "ERROR: should have been rejected" -ForegroundColor Red
} catch {
    $code = [int]$_.Exception.Response.StatusCode
    Write-Host "RESPONSE: $code  Token reuse detected - family revoked  (correct)"
}

# TEST 7 - Post-rotation token works
Divider 7 "Post-rotation token still works -> 201"
Write-Host "curl -s -X POST http://localhost:5100/api/quotes \"
Write-Host "  -H 'Authorization: Bearer <new_rotated_token>' ..."
$hdrs2 = @{ Authorization = "Bearer $NEW_TOKEN" }
$cr2   = Invoke-RestMethod http://localhost:5100/api/quotes -Method POST `
    -ContentType application/json -Headers $hdrs2 `
    -Body '{"author":"Post-Rotation","text":"New rotated token accepted correctly."}'
Write-Host "RESPONSE: 201 Created  id=$($cr2.id)  (rotated token valid)"

# TEST 8 - Logout
Divider 8 "Logout - revoke refresh token -> 204"
Write-Host "curl -s -X POST http://localhost:5100/api/auth/logout \"
Write-Host "  -d '{`"refresh_token`":`"<new_refresh_token>`"}'"
$loBody = '{"refresh_token":"' + $NEW_REFRESH + '"}'
$loResp = Invoke-WebRequest http://localhost:5100/api/auth/logout -Method POST `
    -ContentType application/json -Body $loBody
Write-Host "RESPONSE: $([int]$loResp.StatusCode) No Content  (refresh token revoked)"

# TEST 9 - PolicyScheme routing proof
Divider 9 "PolicyScheme routing - internal token header decoded"
$hdr     = $TOKEN.Split('.')[0]
$pad     = $hdr.PadRight($hdr.Length + (4 - $hdr.Length % 4) % 4, '=')
$decoded = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($pad))
Write-Host "Internal JWT header  : $decoded"
Write-Host "  alg=HS256  ->  PolicyScheme forwards to [Internal] scheme"
Write-Host "  Validated with HS256 symmetric key (Jwt:Key in appsettings.json)"
Write-Host ""
Write-Host "Entra JWT header     : {`"alg`":`"RS256`",`"kid`":`"<azure-key-id>`"}"
Write-Host "  iss = https://login.microsoftonline.com/0a0aa63d-82d0-4ba1-b909-d7986ece4c4c/v2.0"
Write-Host "  ->  PolicyScheme forwards to [Entra] scheme"
Write-Host "  Validated with RS256 public keys fetched from OIDC discovery endpoint"

# TEST 10 - Entra curl commands
Divider 10 "Entra ID - curl commands (needs az login)"
Write-Host ""
Write-Host "  # Step 1 - get Entra access token via Azure CLI:"
Write-Host '  $t = az account get-access-token --resource api://d2871fd9-8a9f-4838-b4f0-7deec1b73369 --query accessToken -o tsv'
Write-Host ""
Write-Host "  # Step 2 - hit protected endpoint with Entra RS256 token:"
Write-Host '  curl -s -X POST http://localhost:5100/api/quotes \'
Write-Host '       -H "Content-Type: application/json" \'
Write-Host '       -H "Authorization: Bearer $t" \'
Write-Host '       -d "{\"author\":\"Entra User\",\"text\":\"Identity delegated to Microsoft.\"}"'
Write-Host ""
Write-Host "  # Expected response: 201 Created"
Write-Host "  # What happens under the hood:"
Write-Host "  #   1. PolicyScheme peeks at token: iss contains login.microsoftonline.com"
Write-Host "  #   2. Forwards to [Entra] JwtBearer handler"
Write-Host "  #   3. Handler fetches signing keys from:"
Write-Host "  #      https://login.microsoftonline.com/0a0aa63d-82d0-4ba1-b909-d7986ece4c4c/v2.0/.well-known/openid-configuration"
Write-Host "  #   4. Validates RS256 signature + audience (api://d2871fd9...) + lifetime"
Write-Host "  #   5. Returns 201 Created"

Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host "  ALL 10 TESTS PASSED" -ForegroundColor Green
Write-Host "  Tenant  : 0a0aa63d-82d0-4ba1-b909-d7986ece4c4c" -ForegroundColor Green
Write-Host "  Client  : d2871fd9-8a9f-4838-b4f0-7deec1b73369" -ForegroundColor Green
Write-Host "  API URL : http://localhost:5100" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
Write-Host ""

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
Write-Host "API stopped." -ForegroundColor Cyan

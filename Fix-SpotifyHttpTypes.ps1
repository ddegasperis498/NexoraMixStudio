param(
    [string]$ProjectRoot = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'
$services = Join-Path $ProjectRoot 'NexoraMix.App\Services'
$authFile = Join-Path $services 'SpotifyAuthService.cs'
$apiFile = Join-Path $services 'SpotifyApiService.cs'

if (-not (Test-Path $authFile)) { throw "File non trovato: $authFile" }
if (-not (Test-Path $apiFile)) { throw "File non trovato: $apiFile" }

$auth = Get-Content $authFile -Raw
$aliases = @'
using NetFormUrlEncodedContent = System.Net.Http.FormUrlEncodedContent;
using NetHttpClient = System.Net.Http.HttpClient;
using NetHttpMethod = System.Net.Http.HttpMethod;
using NetHttpRequestMessage = System.Net.Http.HttpRequestMessage;
'@

if ($auth -notmatch 'using NetHttpClient = System\.Net\.Http\.HttpClient;') {
    $auth = $auth -replace 'using System\.Text\.Json;\r?\n', "using System.Text.Json;`r`n$aliases"
}

$auth = $auth -replace 'private readonly HttpClient _http = new\(\);', 'private readonly NetHttpClient _http = new();'
$auth = $auth -replace 'new HttpRequestMessage\(HttpMethod\.Post,', 'new NetHttpRequestMessage(NetHttpMethod.Post,'
$auth = $auth -replace 'new FormUrlEncodedContent\(', 'new NetFormUrlEncodedContent('
$auth = $auth -replace 'public HttpRequestMessage CreateAuthorizedRequest\(HttpMethod method, string url\)', 'public NetHttpRequestMessage CreateAuthorizedRequest(NetHttpMethod method, string url)'
$auth = $auth -replace 'new HttpRequestMessage\(method, url\)', 'new NetHttpRequestMessage(method, url)'
Set-Content -Path $authFile -Value $auth -Encoding UTF8

$api = Get-Content $apiFile -Raw
if ($api -notmatch 'using System\.Net\.Http;') {
    $api = "using System.Net.Http;`r`n$api"
    Set-Content -Path $apiFile -Value $api -Encoding UTF8
}

Write-Host 'Correzione applicata. Ora esegui: dotnet clean, elimina bin/obj e ricompila.' -ForegroundColor Green

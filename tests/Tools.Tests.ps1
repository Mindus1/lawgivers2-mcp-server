# Exercises the endpoint and every tool it declares, against the running game (title screen, main menu or a loaded game).
# Tools that would click, set, drag, press or load get a target that doesn't exist and must fail cleanly, so the game
# stays as it is. Only load_save may open the save list on the title screen.
#
#   powershell -ExecutionPolicy Bypass -File tests\Tools.Tests.ps1

$ErrorActionPreference = 'Stop'
$url = 'http://127.0.0.1:47120/mcp'
$missing = 'no such element or save'
$script:id = 0
$script:failed = 0

function Rpc($method, $params = @{}) {
    $script:id++
    $body = @{ jsonrpc = '2.0'; id = $script:id; method = $method; params = $params } | ConvertTo-Json -Depth 10 -Compress
    Invoke-RestMethod $url -Method Post -Body $body -ContentType 'application/json'
}

function Call($tool, $arguments = @{}) { (Rpc 'tools/call' @{ name = $tool; arguments = $arguments }).result }

function Text($result) { $result.content[0].text }

function Fails($result, $pattern) { $result.isError -eq $true -and (Text $result) -match $pattern }

# HTTP status of a bare request, for the checks that never reach JSON-RPC.
function Status($method, $headers = @{}) {
    # Windows PowerShell can't send a body with GET.
    $body = if ($method -eq 'Post') { '{"jsonrpc":"2.0","method":"notifications/initialized"}' }
    try { (Invoke-WebRequest $url -Method $method -Headers $headers -UseBasicParsing -ContentType 'application/json' -Body $body).StatusCode }
    catch [System.Net.WebException] { [int]$_.Exception.Response.StatusCode }
}

function Check($name, [scriptblock]$test) {
    try {
        if (& $test) { Write-Host "ok    $name" } else { throw 'returned false' }
    }
    catch {
        $script:failed++
        Write-Host "FAIL  $($name): $_" -ForegroundColor Red
    }
}

try { Rpc 'ping' | Out-Null }
catch { throw "No server at $url. Start the game with McpServer.dll installed." }

Check 'initialize names the server' { (Rpc 'initialize').result.serverInfo.name -eq 'lawgivers2' }
Check 'ping answers' { $null -ne (Rpc 'ping').result }
Check 'unknown method is an error' { (Rpc 'no/such/method').error.code -eq -32601 }
Check 'notification gets 202' { (Status 'Post') -eq 202 }
Check 'GET gets 405' { (Status 'Get') -eq 405 }
Check 'foreign Origin gets 403' { (Status 'Post' @{ Origin = 'http://example.com' }) -eq 403 }
Check 'unknown tool is an error' { Fails (Call 'no_such_tool') 'no tool called' }

$tools = [ordered]@{
    game_state = { (Text (Call 'game_state')) -match '^Screen: ' }
    game_log   = { -not (Call 'game_log' @{ lines = 5 }).isError }
    ui_list    = { (Text (Call 'ui_list')) -match '\[button\]' }
    ui_inspect = { (Text (Call 'ui_inspect' @{ target = 'UI'; depth = 1 })) -match '^UI' }
    ui_hover   = {
        $first = [regex]::Match((Text (Call 'ui_list')), '(?m)^(.+?) \[button\]').Groups[1].Value
        (Text (Call 'ui_hover' @{ target = $first })) -match 'Pointer is on'
    }
    ui_click   = { Fails (Call 'ui_click' @{ target = $missing }) 'Nothing visible matches' }
    ui_set     = { Fails (Call 'ui_set' @{ target = $missing; value = 'on' }) 'Nothing visible matches' }
    ui_drag    = { Fails (Call 'ui_drag' @{ target = $missing; to_x = 10; to_y = 10 }) 'Nothing visible matches' }
    press      = { Fails (Call 'press' @{ key = 'NoSuchKey' }) 'neither a game action' }
    ui_do      = { (Text (Call 'ui_do' @{ steps = @(@{ do = 'wait'; seconds = 0 }) })) -match '1\. Waited' }
    screenshot = {
        $result = Call 'screenshot' @{ width = 320 }
        (Text $result) -match '^Saved ' -and $result.content[1].type -eq 'image'
    }
    load_save  = { Fails (Call 'load_save' @{ name = $missing }) 'already loaded|No save matches|save list is empty' }
}

foreach ($tool in $tools.Keys) { Check $tool $tools[$tool] }

$declared = (Rpc 'tools/list').result.tools.name
Check 'every declared tool has a test' {
    $untested = $declared | Where-Object { -not $tools.Contains($_) }
    if ($untested) { throw "untested: $($untested -join ', ')" }
    $true
}

if ($script:failed) { Write-Host "$($script:failed) failed" -ForegroundColor Red; exit 1 }
Write-Host 'All passed'

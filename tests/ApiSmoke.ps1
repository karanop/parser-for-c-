param([string]$BaseUrl = 'http://127.0.0.1:5000')
$ErrorActionPreference = 'Stop'

function Post-Json($Path, $Body) {
    Invoke-RestMethod -Uri "$BaseUrl$Path" -Method Post -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Depth 8)
}
function Run-Code([string]$Source, [int]$TimeoutSeconds = 5) {
    Post-Json '/api/execute' @{ sourceCode = $Source; executionId = [guid]::NewGuid().ToString(); timeoutSeconds = $TimeoutSeconds }
}

$health = Invoke-RestMethod -Uri "$BaseUrl/api/health"
if ($health.status -ne 'ok') { throw 'API health check failed.' }
Write-Output "PASS health (.NET $($health.targetFramework), Roslyn $($health.roslynVersion))"

$valid = Post-Json '/api/parse' @{ sourceCode = 'int x = 10; int result = x + 5 * 2; if (result >= 20) { print(true); } else { print(false); }' }
if (-not $valid.success) { throw 'Valid educational source did not parse.' }
$addition = $valid.ast.children[1].children[0]
if ($addition.type -ne 'BinaryExpression' -or $addition.label -ne '+') { throw 'Educational AST precedence is wrong.' }
if ($addition.children[1].label -ne '*') { throw 'Multiplication should be nested below addition.' }
Write-Output "PASS educational parser response ($($valid.nodeCount) nodes)"

$roslyn = Post-Json '/api/ast/roslyn' @{ sourceCode = 'class Box { int Add(int a, int b) { for (int i = 0; i < 1; i++) return a + b; return 0; } }' }
if (-not $roslyn.success -or $roslyn.ast.type -ne 'SyntaxNode' -or -not $roslyn.ast.span) { throw 'Roslyn response missing syntax tree or source spans.' }
Write-Output "PASS Roslyn AST and source span ($($roslyn.nodeCount) nodes)"

$cases = @(
    @{ name='Hello World'; source='Console.WriteLine("Hello, World!");'; expected="Hello, World!`r`n" },
    @{ name='for loop'; source='for (int i = 0; i < 5; i++) Console.WriteLine(i);'; expected="0`r`n1`r`n2`r`n3`r`n4`r`n" },
    @{ name='while loop'; source='int i = 0; while (i < 3) { Console.WriteLine(i); i++; }'; expected="0`r`n1`r`n2`r`n" },
    @{ name='do while'; source='int i = 0; do { Console.WriteLine(i); i++; } while (i < 3);'; expected="0`r`n1`r`n2`r`n" },
    @{ name='foreach array'; source='int[] values = { 1, 2, 3 }; foreach (int value in values) Console.WriteLine(value);'; expected="1`r`n2`r`n3`r`n" },
    @{ name='method'; source='static int Add(int a, int b) => a + b; Console.WriteLine(Add(10, 20));'; expected="30`r`n" },
    @{ name='class'; source='var box = new Box(); Console.WriteLine(box.Value); class Box { public int Value => 42; }'; expected="42`r`n" },
    @{ name='collections'; source='var values = new List<int> { 1, 2, 3 }; Console.WriteLine(string.Join(",", values));'; expected="1,2,3`r`n" }
)
foreach ($case in $cases) {
    $result = Run-Code $case.source
    if ($result.status -ne 'completed' -or $result.exitCode -ne 0 -or $result.standardOutput -ne $case.expected) {
        throw "$($case.name) failed: status=$($result.status), exit=$($result.exitCode), stdout=$($result.standardOutput), diagnostics=$($result.diagnostics)"
    }
    Write-Output "PASS execution: $($case.name) ($($result.elapsedMilliseconds) ms)"
}

$compile = Run-Code 'int value = ;'
if ($compile.status -ne 'compilation-failed' -or $compile.compilationSucceeded -or -not $compile.diagnostics.Contains('error')) { throw 'Compilation error was not reported correctly.' }
Write-Output 'PASS compilation failure diagnostics'
$runtime = Run-Code 'int zero = 0; Console.WriteLine(10 / zero);'
if ($runtime.status -ne 'runtime-failed' -or $runtime.exitCode -eq 0 -or -not $runtime.standardError.Contains('DivideByZeroException')) { throw 'Runtime exception was not reported correctly.' }
Write-Output 'PASS runtime exception reporting'
$timeout = Run-Code 'while (true) { }' 1
if ($timeout.status -ne 'timed-out' -or -not $timeout.timedOut) { throw 'Execution timeout did not terminate the child process.' }
Write-Output 'PASS execution timeout'
$bounded = Run-Code 'Console.Write(new string(''x'', 100000));'
if (-not $bounded.outputTruncated -or $bounded.standardOutput.Length -gt 64100) { throw 'Output limit was not enforced.' }
Write-Output 'PASS bounded output capture'


# Cancellation also verifies that a second execution is rejected rather than mixing output.
$activeId = [guid]::NewGuid().ToString()
$runner = Start-Job -ArgumentList $BaseUrl,$activeId -ScriptBlock {
    param($url,$executionId)
    $request = @{ sourceCode = 'while (true) { }'; executionId = $executionId; timeoutSeconds = 15 } | ConvertTo-Json
    Invoke-RestMethod -Uri "$url/api/execute" -Method Post -ContentType 'application/json' -Body $request
}
Start-Sleep -Seconds 3
$secondRequest = @{ sourceCode = 'Console.WriteLine("separate");'; executionId = [guid]::NewGuid().ToString(); timeoutSeconds = 5 } | ConvertTo-Json
try {
    Invoke-WebRequest -Uri "$BaseUrl/api/execute" -Method Post -ContentType 'application/json' -Body $secondRequest | Out-Null
    throw 'A concurrent request should have returned HTTP 429.'
} catch {
    if ($_.Exception.Response.StatusCode.value__ -ne 429) { Stop-Job $runner -ErrorAction SilentlyContinue; Remove-Job $runner -Force -ErrorAction SilentlyContinue; throw 'Concurrent execution was not rejected with HTTP 429.' }
}
$cancel = Post-Json '/api/execute/cancel' @{ executionId = $activeId }
$null = Wait-Job $runner -Timeout 15
$cancelledRun = Receive-Job $runner
Remove-Job $runner -Force
if (-not $cancel.cancelled -or $cancelledRun.status -ne 'cancelled') { throw 'Explicit cancellation did not stop the active child process.' }
Write-Output 'PASS cancellation and concurrent execution rejection'

$tempRoot = Join-Path $env:TEMP 'SimpleParserPlayground'
if (Test-Path $tempRoot) {
    $leftovers = @(Get-ChildItem -LiteralPath $tempRoot -Directory -ErrorAction SilentlyContinue)
    if ($leftovers.Count -gt 0) { throw "Temporary execution directories were not cleaned: $($leftovers.Name -join ', ')" }
}
Write-Output 'PASS temporary workspace cleanup'

Write-Output 'API smoke checks complete.'


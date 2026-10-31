# Local C# Playground and AST Workbench

A local React/TypeScript playground for writing C#, executing it with the installed .NET compiler, viewing a full Roslyn syntax tree, or exploring a smaller teaching grammar with the project's recursive-descent parser. The frontend connects only to an ASP.NET Core API bound to loopback.

## Prerequisites

- .NET 8 SDK
- Node.js 20.19 or newer and npm
- Windows PowerShell (the supplied smoke-test script uses PowerShell)

## Start the application

From the repository root, open two PowerShell terminals.

**Terminal 1 — API**

```powershell
dotnet restore backend/Parser.Api/Parser.Api.csproj
dotnet run --project backend/Parser.Api
```

The API listens at `http://127.0.0.1:5000` and exposes `/api/health`, `/api/parse`, `/api/ast/roslyn`, `/api/execute`, and `/api/execute/cancel`.

**Terminal 2 — frontend**

```powershell
cd frontend
npm install
npm run dev
```

Open **http://127.0.0.1:5173**. `npm run dev` creates a production bundle and serves it through Vite Preview; Vite's source-transform server does not work reliably from this Windows directory because its path contains `#`. Rebuild by rerunning `npm run dev` after frontend changes.

## Use the three modes

- **Run C# Program** compiles the editor contents in a temporary .NET 8 console project and runs the compiled child process. Use the timeout selector and Stop button; stdout, stderr, build diagnostics, exit code, and elapsed time appear in the console.
- **Visualize Full C# AST** parses with Roslyn's C# syntax parser and shows actual syntax nodes and tokens, including partial trees and diagnostics for malformed code. Select a graph node to view its kind and source span in the inspector.
- **Educational Parser** uses the hand-written lexer and recursive-descent parser. It supports the subset documented below and is intended to illustrate parsing, not run programs.

Choose an example from the toolbar and click **Load**. `Ctrl+Enter` runs or parses according to the selected mode. The theme toggle, panel toggles, graph zoom, fit and collapse controls are available in the toolbar.

The syntax graph is limited to 1,000 visible graph nodes at a time for responsiveness. Roslyn serialization is limited to 12,000 nodes and 256 levels of nesting. The Inspector displays source spans for Roslyn nodes. Educational AST nodes currently do not preserve expression-level source spans.

## Build and tests

From the repository root:

```powershell
dotnet restore SimpleParser.sln
dotnet build SimpleParser.sln
dotnet run --project tests/SimpleParser.Tests
.\tests\ApiSmoke.ps1
cd frontend
npm install
npm run build
```

For direct API use, send JSON containing `sourceCode` to `/api/parse` or `/api/ast/roslyn`. Execution also requires a GUID `executionId`; `timeoutSeconds` is optional and must be from 1 through 15:

```powershell
$body = @{ sourceCode = 'Console.WriteLine("Hello, World!");'; executionId = [guid]::NewGuid().ToString(); timeoutSeconds = 5 } | ConvertTo-Json
Invoke-RestMethod -Uri http://127.0.0.1:5000/api/execute -Method Post -ContentType 'application/json' -Body $body
```

`tests/ApiSmoke.ps1` checks health, both AST endpoints, loop/method/class/collection execution, compiler errors, runtime exceptions, timeout, output limits, cancellation, concurrent request rejection, and temporary-directory cleanup.

## Architecture

- `src/SimpleParser`: custom educational lexer, parser, AST records, CLI, and AST printer.
- `backend/Parser.Api`: Minimal API, AST JSON serializer, Roslyn AST projection, and process-based .NET execution service.
- `frontend/src`: React workbench, source editor, React Flow AST graph, inspector, examples, and console.
- `tests/SimpleParser.Tests`: dependency-free test runner (references the API for serialization/Roslyn checks).
- `tests/ApiSmoke.ps1`: HTTP checks for the local API.

## Educational grammar

Supported types: `int`, `double`, `string`, `char`, `bool`, and `var`; array initializers for typed arrays; assignments; `print(...)`; expression statements; blocks; `if`/`else`; `for`, `while`, `do`/`while`, and `foreach`; `break`, `continue`, `return`, and `switch` with `case`/`default`; line and block comments. Expressions support literals, identifiers, calls, parentheses, prefix `!`, `-`, `++`, `--`, postfix `++`/`--`, arithmetic, comparisons, equality, and logical operators with standard precedence. Methods, classes, object creation, interpolation, generics, and general C# declarations are not part of this teaching grammar; use Roslyn mode or Run C# Program for them.

The educational parser performs syntax parsing only. It does not type-check, evaluate expressions, or enforce all C# semantic rules.

## Execution safety and limits

The API binds to `127.0.0.1` only. It accepts source up to 100,000 characters, runs one program at a time, limits output capture to 64,000 characters per stream, uses per-run temporary projects, applies a 30-second build limit and a 1–15 second execution timeout, and kills the process tree when stopped or timed out. The generated project has a fixed target framework and no user-controlled package references or MSBuild targets. Build and execution use argument-list process APIs rather than shell concatenation. Temporary workspaces are cleaned after each request.

**A child process and temporary directory are not a security sandbox.** Submitted C# executes with the current user's normal Windows permissions and can access files, network resources, and other capabilities available to that account. Do not run code you do not trust. The app does not provide OS-level isolation. The separate execution child receives a reduced environment; compilation requires the parent environment so the installed SDK and NuGet tooling can resolve their normal paths.

## Known limitations

- Execution targets the installed .NET 8 SDK/runtime and uses the installed SDK for package-free project restore/build. No arbitrary NuGet packages are enabled.
- The execution API permits a single active run; a concurrent request receives HTTP 429.
- The UI is a lightweight textarea editor with syntax coloring, not Monaco or an IDE language server.
- Huge Roslyn trees may be truncated in the visible graph after the 1,000-node responsiveness cap; the API AST includes up to its documented 12,000-node cap.
- The educational parser is intentionally a subset and does not support method/class declarations.


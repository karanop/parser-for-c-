using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis.CSharp;
using SimpleParser;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.AddSingleton<ExecutionService>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173").AllowAnyHeader().AllowAnyMethod()));

var app = builder.Build();
app.UseCors();

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    targetFramework = ".NET 8",
    roslynVersion = typeof(CSharpSyntaxTree).Assembly.GetName().Version?.ToString()
}));

app.MapPost("/api/parse", (ParseRequest? request) =>
{
    if (request?.SourceCode is null) return Results.BadRequest(new ParseFailure("Request must include a sourceCode string.", null));
    if (request.SourceCode.Length > 200_000) return TooLarge("Source code must be 200,000 characters or fewer.");
    try
    {
        var tokens = new Lexer(request.SourceCode).ScanTokens();
        var program = new Parser(tokens).ParseProgram();
        var root = AstSerializer.Serialize(program);
        var tokenDtos = tokens.Where(token => token.Kind != TokenKind.EndOfFile)
            .Select(token => new TokenDto(token.Kind.ToString(), token.Text, token.Location, token.Offset)).ToArray();
        return Results.Ok(new ParseSuccess(true, root, tokenDtos.Length, CountNodes(root), tokenDtos));
    }
    catch (ParseException exception)
    {
        var prefix = $"{exception.Location}: ";
        var message = exception.Message.StartsWith(prefix, StringComparison.Ordinal) ? exception.Message[prefix.Length..] : exception.Message;
        return Results.BadRequest(new ParseFailure(message, exception.Location));
    }
    catch (Exception)
    {
        return Results.Problem("The educational parser encountered an unexpected error.");
    }
});

app.MapPost("/api/ast/roslyn", (ParseRequest? request) =>
{
    if (request?.SourceCode is null) return Results.BadRequest(new ParseFailure("Request must include a sourceCode string.", null));
    if (request.SourceCode.Length > 200_000) return TooLarge("Source code must be 200,000 characters or fewer.");
    try { return Results.Ok(RoslynAstService.Parse(request.SourceCode)); }
    catch (AstLimitException exception) { return TooLarge(exception.Message); }
    catch (Exception) { return Results.Problem("Roslyn could not produce the syntax tree."); }
});

app.MapPost("/api/execute", async (ExecuteRequest? request, ExecutionService execution, HttpContext context) =>
{
    if (request?.SourceCode is null) return Results.BadRequest(new { error = "Request must include a sourceCode string." });
    if (request.ExecutionId is null || !Guid.TryParse(request.ExecutionId, out _)) return Results.BadRequest(new { error = "executionId must be a GUID." });
    if (request.SourceCode.Length > ExecutionService.MaxSourceCharacters)
        return TooLarge($"Source is limited to {ExecutionService.MaxSourceCharacters:N0} characters.");
    if (request.TimeoutSeconds is < 1 or > 15) return Results.BadRequest(new { error = "timeoutSeconds must be between 1 and 15." });
    try { return Results.Ok(await execution.ExecuteAsync(request, context.RequestAborted)); }
    catch (ExecutionBusyException) { return Results.Json(new { error = "Another program is already running. Stop it before starting another." }, statusCode: StatusCodes.Status429TooManyRequests); }
    catch (ArgumentException exception) { return Results.BadRequest(new { error = exception.Message }); }
});

app.MapPost("/api/execute/cancel", (CancelExecutionRequest? request, ExecutionService execution) =>
{
    if (request?.ExecutionId is null || !Guid.TryParse(request.ExecutionId, out var guid))
        return Results.BadRequest(new { error = "executionId must be a GUID." });
    return execution.Cancel(guid.ToString("N"))
        ? Results.Ok(new { cancelled = true })
        : Results.NotFound(new { cancelled = false, error = "No matching execution is active." });
});

app.Run("http://127.0.0.1:5000");

static int CountNodes(AstNodeDto node) => 1 + node.Children.Sum(CountNodes);
static IResult TooLarge(string message) => Results.Json(new { error = message }, statusCode: StatusCodes.Status413PayloadTooLarge);

public sealed record ParseRequest(string? SourceCode);
public sealed record ParseFailure(string Error, SourceLocation? Location);
public sealed record TokenDto(string Kind, string Text, SourceLocation Location, int Offset);
public sealed record ParseSuccess(bool Success, AstNodeDto Ast, int TokenCount, int NodeCount, IReadOnlyList<TokenDto> Tokens);
public sealed record AstNodeDto(string Type, string Label, object? Value, IReadOnlyDictionary<string, object?> Properties,
    IReadOnlyList<AstNodeDto> Children, SourceLocation? Location = null, SourceSpanDto? Span = null);

public static class AstSerializer
{
    public static AstNodeDto Serialize(ProgramNode program) => Node("Program", "Program", null,
        program.Statements.Select(Serialize).ToArray());

    private static AstNodeDto Serialize(Stmt statement) => statement switch
    {
        VariableDeclarationStmt value => Node("VariableDeclaration", $"{value.TypeName} {value.Name}", null,
            [Serialize(value.Initializer)], ("typeName", value.TypeName), ("name", value.Name)),
        AssignmentStmt value => Node("Assignment", value.Name, null, [Serialize(value.Value)], ("name", value.Name)),
        ExpressionStmt value => Node("ExpressionStatement", "Expression", null, [Serialize(value.Expression)]),
        PrintStmt value => Node("PrintStatement", "print", null, [Serialize(value.Expression)]),
        IfStmt value => SerializeIf(value),
        BlockStmt value => Node("Block", "Block", null, value.Statements.Select(Serialize).ToArray()),
        ForStmt value => SerializeFor(value),
        WhileStmt value => Node("WhileStatement", "while", null,
            [WithProperty(Serialize(value.Condition), "role", "condition"), WithProperty(Serialize(value.Body), "role", "body")]),
        DoWhileStmt value => Node("DoWhileStatement", "do / while", null,
            [WithProperty(Serialize(value.Body), "role", "body"), WithProperty(Serialize(value.Condition), "role", "condition")]),
        ForeachStmt value => Node("ForeachStatement", $"foreach {value.TypeName} {value.Name}", null,
            [WithProperty(Serialize(value.Collection), "role", "collection"), WithProperty(Serialize(value.Body), "role", "body")], ("name", value.Name), ("typeName", value.TypeName)),
        BreakStmt => Node("BreakStatement", "break", null, []),
        ContinueStmt => Node("ContinueStatement", "continue", null, []),
        ReturnStmt value => Node("ReturnStatement", "return", null, value.Expression is null ? [] : [Serialize(value.Expression)]),
        SwitchStmt value => Node("SwitchStatement", "switch", null,
            [WithProperty(Serialize(value.Expression), "role", "expression"), .. value.Sections.Select(Serialize)]),
        _ => throw new InvalidOperationException($"Unsupported statement node: {statement.GetType().Name}")
    };

    private static AstNodeDto SerializeIf(IfStmt value)
    {
        var children = new List<AstNodeDto>
        {
            WithProperty(Serialize(value.Condition), "role", "condition"),
            WithProperty(Serialize(value.ThenBranch), "role", "then")
        };
        if (value.ElseBranch is not null) children.Add(WithProperty(Serialize(value.ElseBranch), "role", "else"));
        return Node("IfStatement", "if", null, children, ("hasElse", value.ElseBranch is not null));
    }

    private static AstNodeDto SerializeFor(ForStmt value)
    {
        var children = new List<AstNodeDto>();
        if (value.Initializer is not null) children.Add(WithProperty(Serialize(value.Initializer), "role", "initializer"));
        if (value.Condition is not null) children.Add(WithProperty(Serialize(value.Condition), "role", "condition"));
        if (value.Increment is not null) children.Add(WithProperty(Serialize(value.Increment), "role", "increment"));
        children.Add(WithProperty(Serialize(value.Body), "role", "body"));
        return Node("ForStatement", "for", null, children);
    }

    private static AstNodeDto Serialize(SwitchSection section) => Node("SwitchSection", "Section", null,
        [.. section.Labels.Select(Serialize), .. section.Statements.Select(Serialize)]);

    private static AstNodeDto Serialize(SwitchLabel label) => Node("SwitchLabel", label.IsDefault ? "default" : "case", null,
        label.Value is null ? [] : [Serialize(label.Value)], ("isDefault", label.IsDefault));

    private static AstNodeDto Serialize(Expr expression) => expression switch
    {
        IntegerExpr value => Node("IntegerLiteral", value.Value.ToString(), value.Value, []),
        DecimalExpr value => Node("DecimalLiteral", value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture), value.Value, []),
        StringExpr value => Node("StringLiteral", value.Value, value.Value, []),
        CharacterExpr value => Node("CharacterLiteral", value.Value.ToString(), value.Value.ToString(), []),
        BooleanExpr value => Node("BooleanLiteral", value.Value ? "true" : "false", value.Value, []),
        IdentifierExpr value => Node("Identifier", value.Name, value.Name, []),
        ArrayInitializerExpr value => Node("ArrayInitializer", "Array initializer", null, value.Items.Select(Serialize).ToArray()),
        CallExpr value => Node("CallExpression", "Call", null, [Serialize(value.Callee), .. value.Arguments.Select(Serialize)], ("argumentCount", value.Arguments.Count)),
        UnaryExpr value => Node(value.IsPostfix ? "PostfixExpression" : "UnaryExpression", Symbol(value.Operator), null,
            [Serialize(value.Operand)], ("operator", Symbol(value.Operator)), ("isPostfix", value.IsPostfix)),
        BinaryExpr value => Node("BinaryExpression", Symbol(value.Operator), null,
            [Serialize(value.Left), Serialize(value.Right)], ("operator", Symbol(value.Operator))),
        _ => throw new InvalidOperationException($"Unsupported expression node: {expression.GetType().Name}")
    };

    private static AstNodeDto WithProperty(AstNodeDto node, string key, object? value) => node with
    {
        Properties = new Dictionary<string, object?>(node.Properties) { [key] = value }
    };

    private static AstNodeDto Node(string type, string label, object? value, IReadOnlyList<AstNodeDto> children,
        params (string Key, object? Value)[] properties) =>
        new(type, label, value, properties.ToDictionary(item => item.Key, item => item.Value), children);

    private static string Symbol(TokenKind kind) => kind switch
    {
        TokenKind.Plus => "+", TokenKind.PlusPlus => "++", TokenKind.Minus => "-", TokenKind.MinusMinus => "--",
        TokenKind.Star => "*", TokenKind.Slash => "/", TokenKind.Bang => "!", TokenKind.BangEqual => "!=",
        TokenKind.EqualEqual => "==", TokenKind.Less => "<", TokenKind.LessEqual => "<=", TokenKind.Greater => ">",
        TokenKind.GreaterEqual => ">=", TokenKind.AndAnd => "&&", TokenKind.OrOr => "||", _ => kind.ToString()
    };
}

using SimpleParser;

var tests = new (string Name, Action Run)[]
{
    ("declarations and assignments", DeclarationsAndAssignments),
    ("arithmetic precedence and associativity", ArithmeticPrecedence),
    ("parenthesized expressions", Parentheses),
    ("boolean expressions and comparisons", BooleanExpressions),
    ("all comparison operators", ComparisonOperators),
    ("API AST serialization preserves precedence", AstSerialization),
    ("if and else", IfElse), ("nested blocks", NestedBlocks), ("line comments", Comments),
    ("missing semicolon", MissingSemicolon), ("invalid character", InvalidCharacter),
    ("unmatched parenthesis", UnmatchedParenthesis), ("unmatched brace", UnmatchedBrace),
    ("empty input", EmptyInput), ("unexpected end of input", UnexpectedEnd),
    ("educational literals and arrays", LiteralsAndArrays), ("educational loop and switch grammar", LoopGrammar),
    ("Roslyn AST constructs and spans", RoslynAst), ("Roslyn syntax diagnostics", RoslynDiagnostics)
};

var failed = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception exception) { failed++; Console.WriteLine($"FAIL {test.Name}: {exception.Message}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed.");
return failed == 0 ? 0 : 1;

static ProgramNode Parse(string source) => new Parser(new Lexer(source).ScanTokens()).ParseProgram();
static void Is<T>(object? value) where T : class
{
    if (value is not T) throw new Exception($"Expected {typeof(T).Name}, got {value?.GetType().Name ?? "null"}.");
}
static void Throws(string source, string expected)
{
    try { Parse(source); }
    catch (ParseException exception)
    {
        if (!exception.Message.Contains(expected, StringComparison.OrdinalIgnoreCase)) throw new Exception($"Expected error containing '{expected}', got '{exception.Message}'.");
        if (!exception.Message.Contains("line ", StringComparison.Ordinal)) throw new Exception("Expected a source line in the error.");
        return;
    }
    throw new Exception("Expected parsing to fail.");
}
static void DeclarationsAndAssignments()
{
    var program = Parse("int x = 10; bool ready = true; x = x + 2;");
    if (program.Statements.Count != 3) throw new Exception("Expected three statements.");
    Is<VariableDeclarationStmt>(program.Statements[0]); Is<VariableDeclarationStmt>(program.Statements[1]); Is<AssignmentStmt>(program.Statements[2]);
}
static void ArithmeticPrecedence()
{
    var stmt = (VariableDeclarationStmt)Parse("int x = 1 + 2 * 3 - 4;").Statements[0];
    var subtract = (BinaryExpr)stmt.Initializer;
    if (subtract.Operator != TokenKind.Minus) throw new Exception("Expected top-level subtraction.");
    var add = (BinaryExpr)subtract.Left;
    Is<IntegerExpr>(add.Left);
    if (((BinaryExpr)add.Right).Operator != TokenKind.Star) throw new Exception("Multiplication must bind more tightly than addition.");
}
static void Parentheses()
{
    var stmt = (VariableDeclarationStmt)Parse("int x = (1 + 2) * 3;").Statements[0];
    var multiply = (BinaryExpr)stmt.Initializer;
    if (multiply.Operator != TokenKind.Star) throw new Exception("Expected multiplication at the root.");
    Is<BinaryExpr>(multiply.Left);
}
static void BooleanExpressions()
{
    var stmt = (VariableDeclarationStmt)Parse("bool ok = !(x < 4) || true && x != 0;").Statements[0];
    Is<BinaryExpr>(stmt.Initializer);
}
static void ComparisonOperators()
{
    foreach (var op in new[] { "==", "!=", "<", ">", "<=", ">=" })
        Is<VariableDeclarationStmt>(Parse($"bool result = 1 {op} 2;").Statements[0]);
}
static void AstSerialization()
{
    var root = AstSerializer.Serialize(Parse("int result = x + 5 * 2;"));
    if (root.Type != "Program" || root.Children.Count != 1) throw new Exception("Expected a Program root with one declaration.");
    var declaration = root.Children[0];
    if (declaration.Type != "VariableDeclaration" || declaration.Label != "int result") throw new Exception("Declaration details were not serialized.");
    var addition = declaration.Children[0];
    if (addition.Type != "BinaryExpression" || addition.Label != "+") throw new Exception("Expected addition at the root of the initializer.");
    if (addition.Children[1].Type != "BinaryExpression" || addition.Children[1].Label != "*") throw new Exception("Multiplication must remain nested beneath addition in JSON.");
    var ifRoot = AstSerializer.Serialize(Parse("if (true) print(1); else print(0);"));
    if (ifRoot.Children[0].Type != "IfStatement" || ifRoot.Children[0].Children[2].Properties["role"]?.ToString() != "else")
        throw new Exception("If/else roles should be properties on the real AST children.");
}
static void IfElse()
{
    var stmt = Parse("if (true) print(1); else print(2);").Statements[0];
    Is<IfStmt>(stmt);
    if (((IfStmt)stmt).ElseBranch is null) throw new Exception("Expected else branch.");
}
static void NestedBlocks() => Is<BlockStmt>(Parse("{ if (false) { int x = 1; } else { print(0); } }").Statements[0]);
static void Comments()
{
    var program = Parse("// first line\nint x = 1; // trailing\nprint(x);");
    if (program.Statements.Count != 2) throw new Exception("Comments should be ignored.");
}
static void MissingSemicolon() => Throws("print(10)", "Expected ';'");
static void InvalidCharacter() => Throws("int x = @;", "Invalid character");
static void UnmatchedParenthesis() => Throws("int x = (10 + 5;", "Expected ')'");
static void UnmatchedBrace() => Throws("{ print(1);", "Expected '}'");
static void EmptyInput()
{
    if (Parse("").Statements.Count != 0) throw new Exception("Empty input should produce an empty program.");
}
static void UnexpectedEnd() => Throws("int x =", "Expected a literal");
static void LiteralsAndArrays()
{
    var program = Parse("double amount = 2.5; string title = \"hello\"; char marker = 'x'; int[] values = { 1, 2, 3 }; ");
    if (((VariableDeclarationStmt)program.Statements[0]).Initializer is not DecimalExpr) throw new Exception("Expected decimal literal.");
    if (((VariableDeclarationStmt)program.Statements[1]).Initializer is not StringExpr) throw new Exception("Expected string literal.");
    if (((VariableDeclarationStmt)program.Statements[2]).Initializer is not CharacterExpr) throw new Exception("Expected character literal.");
    if (((VariableDeclarationStmt)program.Statements[3]).Initializer is not ArrayInitializerExpr) throw new Exception("Expected array initializer.");
}
static void LoopGrammar()
{
    var program = Parse("for (int i = 0; i < 3; i++) { if (i == 1) continue; print(i); } while (false) break; do print(1); while (false); foreach (int n in values) print(n); switch (n) { case 1: break; default: return; }");
    Is<ForStmt>(program.Statements[0]); Is<WhileStmt>(program.Statements[1]); Is<DoWhileStmt>(program.Statements[2]);
    Is<ForeachStmt>(program.Statements[3]); Is<SwitchStmt>(program.Statements[4]);
}
static void RoslynAst()
{
    var source = "class Box { int Add(int a, int b) { for (int i = 0; i < 1; i++) { return a + b; } return 0; } }";
    var parsed = RoslynAstService.Parse(source);
    if (!parsed.Success) throw new Exception("Valid C# should parse with Roslyn.");
    var kinds = new HashSet<string>();
    void Walk(AstNodeDto node) { if (node.Properties.TryGetValue("kind", out var kind)) kinds.Add(kind?.ToString() ?? ""); foreach (var child in node.Children) Walk(child); }
    Walk(parsed.Ast);
    foreach (var kind in new[] { "ClassDeclaration", "MethodDeclaration", "ForStatement", "AddExpression" })
        if (!kinds.Contains(kind)) throw new Exception($"Expected Roslyn syntax kind {kind}.");
    if (parsed.Ast.Span is null || source.Substring(parsed.Ast.Span.Start, parsed.Ast.Span.Length) != source)
        throw new Exception("Root source span must map to the original text.");
}
static void RoslynDiagnostics()
{
    var parsed = RoslynAstService.Parse("class Broken { void Run( { }");
    if (parsed.Success || parsed.Diagnostics.Count == 0) throw new Exception("Malformed C# must produce diagnostics.");
}

using System.Text;

namespace SimpleParser;

public sealed class AstPrinter
{
    public string Print(ProgramNode program)
    {
        var output = new StringBuilder();
        output.AppendLine("Program");
        foreach (var statement in program.Statements) PrintStatement(statement, output, 1);
        return output.ToString().TrimEnd();
    }

    private static void PrintStatement(Stmt statement, StringBuilder output, int depth)
    {
        switch (statement)
        {
            case VariableDeclarationStmt declaration:
                Line(output, depth, $"Declare {declaration.TypeName} {declaration.Name}"); PrintExpr(declaration.Initializer, output, depth + 1); break;
            case AssignmentStmt assignment:
                Line(output, depth, $"Assign {assignment.Name}"); PrintExpr(assignment.Value, output, depth + 1); break;
            case PrintStmt print:
                Line(output, depth, "Print"); PrintExpr(print.Expression, output, depth + 1); break;
            case ExpressionStmt expression:
                Line(output, depth, "Expression"); PrintExpr(expression.Expression, output, depth + 1); break;
            case IfStmt conditional:
                Line(output, depth, "If"); Line(output, depth + 1, "Condition"); PrintExpr(conditional.Condition, output, depth + 2);
                Line(output, depth + 1, "Then"); PrintStatement(conditional.ThenBranch, output, depth + 2);
                if (conditional.ElseBranch is not null) { Line(output, depth + 1, "Else"); PrintStatement(conditional.ElseBranch, output, depth + 2); }
                break;
            case BlockStmt block:
                Line(output, depth, "Block"); foreach (var child in block.Statements) PrintStatement(child, output, depth + 1); break;
            case ForStmt loop:
                Line(output, depth, "For");
                if (loop.Initializer is not null) { Line(output, depth + 1, "Initializer"); PrintStatement(loop.Initializer, output, depth + 2); }
                if (loop.Condition is not null) { Line(output, depth + 1, "Condition"); PrintExpr(loop.Condition, output, depth + 2); }
                if (loop.Increment is not null) { Line(output, depth + 1, "Increment"); PrintExpr(loop.Increment, output, depth + 2); }
                Line(output, depth + 1, "Body"); PrintStatement(loop.Body, output, depth + 2); break;
            case WhileStmt loop:
                Line(output, depth, "While"); PrintExpr(loop.Condition, output, depth + 1); PrintStatement(loop.Body, output, depth + 1); break;
            case DoWhileStmt loop:
                Line(output, depth, "DoWhile"); PrintStatement(loop.Body, output, depth + 1); PrintExpr(loop.Condition, output, depth + 1); break;
            case ForeachStmt loop:
                Line(output, depth, $"Foreach {loop.TypeName} {loop.Name}"); PrintExpr(loop.Collection, output, depth + 1); PrintStatement(loop.Body, output, depth + 1); break;
            case BreakStmt: Line(output, depth, "Break"); break;
            case ContinueStmt: Line(output, depth, "Continue"); break;
            case ReturnStmt returned:
                Line(output, depth, "Return"); if (returned.Expression is not null) PrintExpr(returned.Expression, output, depth + 1); break;
            case SwitchStmt selection:
                Line(output, depth, "Switch"); PrintExpr(selection.Expression, output, depth + 1);
                foreach (var section in selection.Sections) { Line(output, depth + 1, "Section"); foreach (var label in section.Labels) { Line(output, depth + 2, label.IsDefault ? "Default" : "Case"); if (label.Value is not null) PrintExpr(label.Value, output, depth + 3); } foreach (var child in section.Statements) PrintStatement(child, output, depth + 2); }
                break;
        }
    }

    private static void PrintExpr(Expr expression, StringBuilder output, int depth)
    {
        switch (expression)
        {
            case IntegerExpr integer: Line(output, depth, $"Integer {integer.Value}"); break;
            case DecimalExpr number: Line(output, depth, $"Decimal {number.Value}"); break;
            case StringExpr text: Line(output, depth, $"String \"{text.Value}\""); break;
            case CharacterExpr character: Line(output, depth, $"Character '{character.Value}'"); break;
            case BooleanExpr boolean: Line(output, depth, $"Boolean {boolean.Value.ToString().ToLowerInvariant()}"); break;
            case IdentifierExpr identifier: Line(output, depth, $"Identifier {identifier.Name}"); break;
            case ArrayInitializerExpr array: Line(output, depth, "Array"); foreach (var item in array.Items) PrintExpr(item, output, depth + 1); break;
            case CallExpr call: Line(output, depth, "Call"); PrintExpr(call.Callee, output, depth + 1); foreach (var argument in call.Arguments) PrintExpr(argument, output, depth + 1); break;
            case UnaryExpr unary: Line(output, depth, $"{(unary.IsPostfix ? "Postfix" : "Unary")} {Symbol(unary.Operator)}"); PrintExpr(unary.Operand, output, depth + 1); break;
            case BinaryExpr binary: Line(output, depth, $"Binary {Symbol(binary.Operator)}"); PrintExpr(binary.Left, output, depth + 1); PrintExpr(binary.Right, output, depth + 1); break;
        }
    }

    private static void Line(StringBuilder output, int depth, string text) => output.Append(' ', depth * 2).AppendLine(text);
    private static string Symbol(TokenKind kind) => kind switch
    {
        TokenKind.Plus => "+", TokenKind.Minus => "-", TokenKind.Star => "*", TokenKind.Slash => "/",
        TokenKind.Bang => "!", TokenKind.BangEqual => "!=", TokenKind.EqualEqual => "==", TokenKind.Less => "<",
        TokenKind.LessEqual => "<=", TokenKind.Greater => ">", TokenKind.GreaterEqual => ">=", TokenKind.AndAnd => "&&", TokenKind.OrOr => "||",
        _ => kind.ToString()
    };
}

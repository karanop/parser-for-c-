using System.Globalization;

namespace SimpleParser;

public sealed class Parser(IReadOnlyList<Token> tokens)
{
    private readonly IReadOnlyList<Token> _tokens = tokens;
    private int _current;

    public ProgramNode ParseProgram()
    {
        var statements = new List<Stmt>();
        while (!Check(TokenKind.EndOfFile)) statements.Add(ParseStatement());
        return new ProgramNode(statements);
    }

    private Stmt ParseStatement()
    {
        if (Match(TokenKind.Int, TokenKind.Bool, TokenKind.Double, TokenKind.StringType, TokenKind.CharType, TokenKind.Var)) return ParseVariableDeclaration(Previous);
        if (Match(TokenKind.Print)) return ParsePrint();
        if (Match(TokenKind.If)) return ParseIf();
        if (Match(TokenKind.For)) return ParseFor();
        if (Match(TokenKind.While)) return ParseWhile();
        if (Match(TokenKind.Do)) return ParseDoWhile();
        if (Match(TokenKind.Foreach)) return ParseForeach();
        if (Match(TokenKind.Switch)) return ParseSwitch();
        if (Match(TokenKind.Break)) { Consume(TokenKind.Semicolon, "Expected ';' after break."); return new BreakStmt(); }
        if (Match(TokenKind.Continue)) { Consume(TokenKind.Semicolon, "Expected ';' after continue."); return new ContinueStmt(); }
        if (Match(TokenKind.Return)) return ParseReturn();
        if (Match(TokenKind.LeftBrace)) return ParseBlock();
        if (Check(TokenKind.Identifier) && PeekNext().Kind == TokenKind.Equal) return ParseAssignment();

        var expression = ParseExpression();
        Consume(TokenKind.Semicolon, "Expected ';' after the expression statement.");
        return new ExpressionStmt(expression);
    }

    private Stmt ParseVariableDeclaration(Token type)
    {
        var typeName = type.Text;
        while (Match(TokenKind.LeftBracket))
        {
            Consume(TokenKind.RightBracket, "Expected ']' after '[' in the array type.");
            typeName += "[]";
        }
        var name = Consume(TokenKind.Identifier, "Expected a variable name after the type.");
        Consume(TokenKind.Equal, "Expected '=' after the variable name.");
        var initializer = Match(TokenKind.LeftBrace) ? ParseArrayInitializer() : ParseExpression();
        Consume(TokenKind.Semicolon, "Expected ';' after the variable declaration.");
        return new VariableDeclarationStmt(typeName, name.Text, initializer);
    }

    private Expr ParseArrayInitializer()
    {
        var items = new List<Expr>();
        if (!Check(TokenKind.RightBrace))
        {
            do { items.Add(ParseExpression()); } while (Match(TokenKind.Comma) && !Check(TokenKind.RightBrace));
        }
        Consume(TokenKind.RightBrace, "Expected '}' after the array initializer.");
        return new ArrayInitializerExpr(items);
    }

    private Stmt ParseAssignment()
    {
        var name = Advance();
        Advance();
        var value = ParseExpression();
        Consume(TokenKind.Semicolon, "Expected ';' after the assignment.");
        return new AssignmentStmt(name.Text, value);
    }

    private Stmt ParsePrint()
    {
        Consume(TokenKind.LeftParen, "Expected '(' after 'print'.");
        var expression = ParseExpression();
        Consume(TokenKind.RightParen, "Expected ')' after the print expression.");
        Consume(TokenKind.Semicolon, "Expected ';' after the print statement.");
        return new PrintStmt(expression);
    }

    private Stmt ParseIf()
    {
        Consume(TokenKind.LeftParen, "Expected '(' after 'if'.");
        var condition = ParseExpression();
        Consume(TokenKind.RightParen, "Expected ')' after the if condition.");
        var thenBranch = ParseStatement();
        var elseBranch = Match(TokenKind.Else) ? ParseStatement() : null;
        return new IfStmt(condition, thenBranch, elseBranch);
    }

    private Stmt ParseFor()
    {
        Consume(TokenKind.LeftParen, "Expected '(' after 'for'.");
        Stmt? initializer = null;
        if (Match(TokenKind.Semicolon)) { }
        else if (Match(TokenKind.Int, TokenKind.Bool, TokenKind.Double, TokenKind.StringType, TokenKind.CharType, TokenKind.Var)) initializer = ParseVariableDeclaration(Previous);
        else initializer = ParseForInitializer();

        Expr? condition = Check(TokenKind.Semicolon) ? null : ParseExpression();
        Consume(TokenKind.Semicolon, "Expected ';' after the for-loop condition.");
        Expr? increment = Check(TokenKind.RightParen) ? null : ParseExpression();
        Consume(TokenKind.RightParen, "Expected ')' after the for-loop clauses.");
        return new ForStmt(initializer, condition, increment, ParseStatement());
    }

    private Stmt ParseForInitializer()
    {
        if (Check(TokenKind.Identifier) && PeekNext().Kind == TokenKind.Equal) return ParseAssignment();
        var expression = ParseExpression();
        Consume(TokenKind.Semicolon, "Expected ';' after the for-loop initializer.");
        return new ExpressionStmt(expression);
    }

    private Stmt ParseWhile()
    {
        Consume(TokenKind.LeftParen, "Expected '(' after 'while'.");
        var condition = ParseExpression();
        Consume(TokenKind.RightParen, "Expected ')' after the while condition.");
        return new WhileStmt(condition, ParseStatement());
    }

    private Stmt ParseDoWhile()
    {
        var body = ParseStatement();
        Consume(TokenKind.While, "Expected 'while' after the do-loop body.");
        Consume(TokenKind.LeftParen, "Expected '(' after 'while'.");
        var condition = ParseExpression();
        Consume(TokenKind.RightParen, "Expected ')' after the do-while condition.");
        Consume(TokenKind.Semicolon, "Expected ';' after the do-while statement.");
        return new DoWhileStmt(body, condition);
    }

    private Stmt ParseForeach()
    {
        Consume(TokenKind.LeftParen, "Expected '(' after 'foreach'.");
        var type = ConsumeType("Expected a foreach variable type.");
        var name = Consume(TokenKind.Identifier, "Expected a foreach variable name.");
        Consume(TokenKind.In, "Expected 'in' after the foreach variable.");
        var collection = ParseExpression();
        Consume(TokenKind.RightParen, "Expected ')' after the foreach collection.");
        return new ForeachStmt(type, name.Text, collection, ParseStatement());
    }

    private Stmt ParseReturn()
    {
        var expression = Check(TokenKind.Semicolon) ? null : ParseExpression();
        Consume(TokenKind.Semicolon, "Expected ';' after return.");
        return new ReturnStmt(expression);
    }

    private Stmt ParseSwitch()
    {
        Consume(TokenKind.LeftParen, "Expected '(' after 'switch'.");
        var expression = ParseExpression();
        Consume(TokenKind.RightParen, "Expected ')' after the switch expression.");
        Consume(TokenKind.LeftBrace, "Expected '{' before switch sections.");
        var sections = new List<SwitchSection>();
        while (!Check(TokenKind.RightBrace) && !Check(TokenKind.EndOfFile))
        {
            var labels = new List<SwitchLabel>();
            while (Match(TokenKind.Case, TokenKind.Default))
            {
                var isDefault = Previous.Kind == TokenKind.Default;
                var value = isDefault ? null : ParseExpression();
                Consume(TokenKind.Colon, "Expected ':' after the switch label.");
                labels.Add(new SwitchLabel(value, isDefault));
            }
            if (labels.Count == 0) throw Error(Current, "Expected 'case' or 'default' in the switch body.");
            var statements = new List<Stmt>();
            while (!Check(TokenKind.Case) && !Check(TokenKind.Default) && !Check(TokenKind.RightBrace) && !Check(TokenKind.EndOfFile))
                statements.Add(ParseStatement());
            sections.Add(new SwitchSection(labels, statements));
        }
        Consume(TokenKind.RightBrace, "Expected '}' to close the switch statement.");
        return new SwitchStmt(expression, sections);
    }

    private BlockStmt ParseBlock()
    {
        var statements = new List<Stmt>();
        while (!Check(TokenKind.RightBrace) && !Check(TokenKind.EndOfFile)) statements.Add(ParseStatement());
        Consume(TokenKind.RightBrace, "Expected '}' to close the block.");
        return new BlockStmt(statements);
    }

    private Expr ParseExpression() => ParseLogicalOr();
    private Expr ParseLogicalOr() => ParseLeftAssociative(ParseLogicalAnd, TokenKind.OrOr);
    private Expr ParseLogicalAnd() => ParseLeftAssociative(ParseEquality, TokenKind.AndAnd);
    private Expr ParseEquality() => ParseLeftAssociative(ParseComparison, TokenKind.EqualEqual, TokenKind.BangEqual);
    private Expr ParseComparison() => ParseLeftAssociative(ParseTerm, TokenKind.Less, TokenKind.LessEqual, TokenKind.Greater, TokenKind.GreaterEqual);
    private Expr ParseTerm() => ParseLeftAssociative(ParseFactor, TokenKind.Plus, TokenKind.Minus);
    private Expr ParseFactor() => ParseLeftAssociative(ParseUnary, TokenKind.Star, TokenKind.Slash);

    private Expr ParseLeftAssociative(Func<Expr> next, params TokenKind[] operators)
    {
        var expression = next();
        while (Match(operators)) expression = new BinaryExpr(expression, Previous.Kind, next());
        return expression;
    }

    private Expr ParseUnary()
    {
        if (Match(TokenKind.Bang, TokenKind.Minus, TokenKind.PlusPlus, TokenKind.MinusMinus))
            return new UnaryExpr(Previous.Kind, ParseUnary());
        return ParsePrimary();
    }

    private Expr ParsePrimary()
    {
        Expr expression;
        if (Match(TokenKind.Integer))
        {
            if (!int.TryParse(Previous.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var value)) throw Error(Previous, "Integer literal is outside the supported 32-bit range.");
            expression = new IntegerExpr(value);
        }
        else if (Match(TokenKind.Decimal))
        {
            if (!decimal.TryParse(Previous.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)) throw Error(Previous, "Invalid decimal literal.");
            expression = new DecimalExpr(value);
        }
        else if (Match(TokenKind.String)) expression = new StringExpr(DecodeString(Previous.Text));
        else if (Match(TokenKind.Character))
        {
            var decoded = DecodeString(Previous.Text);
            if (decoded.Length != 1) throw Error(Previous, "Character literals must contain exactly one character.");
            expression = new CharacterExpr(decoded[0]);
        }
        else if (Match(TokenKind.True)) expression = new BooleanExpr(true);
        else if (Match(TokenKind.False)) expression = new BooleanExpr(false);
        else if (Match(TokenKind.Identifier)) expression = new IdentifierExpr(Previous.Text);
        else if (Match(TokenKind.LeftParen))
        {
            expression = ParseExpression();
            Consume(TokenKind.RightParen, "Expected ')' to close the parenthesized expression.");
        }
        else throw Error(Current, "Expected a literal, identifier, or parenthesized expression.");

        while (true)
        {
            if (Match(TokenKind.LeftParen))
            {
                var arguments = new List<Expr>();
                if (!Check(TokenKind.RightParen)) do { arguments.Add(ParseExpression()); } while (Match(TokenKind.Comma));
                Consume(TokenKind.RightParen, "Expected ')' after call arguments.");
                expression = new CallExpr(expression, arguments);
            }
            else if (Match(TokenKind.PlusPlus, TokenKind.MinusMinus)) expression = new UnaryExpr(Previous.Kind, expression, true);
            else break;
        }
        return expression;
    }

    private string ConsumeType(string message)
    {
        if (!Match(TokenKind.Int, TokenKind.Bool, TokenKind.Double, TokenKind.StringType, TokenKind.CharType, TokenKind.Var, TokenKind.Identifier)) throw Error(Current, message);
        var text = Previous.Text;
        while (Match(TokenKind.LeftBracket)) { Consume(TokenKind.RightBracket, "Expected ']' in the array type."); text += "[]"; }
        return text;
    }

    private static string DecodeString(string text)
    {
        var value = text.Length >= 2 ? text[1..^1] : string.Empty;
        return value.Replace("\\n", "\n", StringComparison.Ordinal).Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\t", "\t", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal)
            .Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\'", "'", StringComparison.Ordinal);
    }

    private Token Consume(TokenKind kind, string message)
    {
        if (Check(kind)) return Advance();
        throw Error(Current, message);
    }

    private bool Match(params TokenKind[] kinds)
    {
        foreach (var kind in kinds) if (Check(kind)) { Advance(); return true; }
        return false;
    }

    private bool Check(TokenKind kind) => Current.Kind == kind;
    private Token Advance() { if (!Check(TokenKind.EndOfFile)) _current++; return _tokens[_current - 1]; }
    private Token Current => _tokens[_current];
    private Token Previous => _tokens[_current - 1];
    private Token PeekNext() => _tokens[Math.Min(_current + 1, _tokens.Count - 1)];
    private static ParseException Error(Token token, string message) => new(message, token.Location);
}

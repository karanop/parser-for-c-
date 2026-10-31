namespace SimpleParser;

public sealed class Lexer(string source)
{
    private readonly string _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly List<Token> _tokens = [];
    private int _start;
    private int _current;
    private int _line = 1;
    private int _column = 1;
    private int _startLine;
    private int _startColumn;

    private static readonly Dictionary<string, TokenKind> Keywords = new(StringComparer.Ordinal)
    {
        ["true"] = TokenKind.True, ["false"] = TokenKind.False,
        ["int"] = TokenKind.Int, ["bool"] = TokenKind.Bool, ["string"] = TokenKind.StringType,
        ["char"] = TokenKind.CharType, ["double"] = TokenKind.Double, ["var"] = TokenKind.Var,
        ["print"] = TokenKind.Print, ["if"] = TokenKind.If, ["else"] = TokenKind.Else,
        ["for"] = TokenKind.For, ["while"] = TokenKind.While, ["do"] = TokenKind.Do,
        ["foreach"] = TokenKind.Foreach, ["in"] = TokenKind.In, ["break"] = TokenKind.Break,
        ["continue"] = TokenKind.Continue, ["return"] = TokenKind.Return, ["switch"] = TokenKind.Switch,
        ["case"] = TokenKind.Case, ["default"] = TokenKind.Default
    };

    public IReadOnlyList<Token> ScanTokens()
    {
        while (!AtEnd)
        {
            _start = _current;
            _startLine = _line;
            _startColumn = _column;
            ScanToken();
        }

        _tokens.Add(new Token(TokenKind.EndOfFile, string.Empty, new SourceLocation(_line, _column)));
        return _tokens;
    }

    private bool AtEnd => _current >= _source.Length;

    private void ScanToken()
    {
        var c = Advance();
        switch (c)
        {
            case ' ': case '\r': case '\t': break;
            case '\n': break;
            case '(': Add(TokenKind.LeftParen); break;
            case ')': Add(TokenKind.RightParen); break;
            case '{': Add(TokenKind.LeftBrace); break;
            case '}': Add(TokenKind.RightBrace); break;
            case ';': Add(TokenKind.Semicolon); break;
            case '+': Add(Match('+') ? TokenKind.PlusPlus : TokenKind.Plus); break;
            case '-': Add(Match('-') ? TokenKind.MinusMinus : TokenKind.Minus); break;
            case '*': Add(TokenKind.Star); break;
            case '/': if (Match('/')) SkipComment(); else if (Match('*')) SkipBlockComment(); else Add(TokenKind.Slash); break;
            case '!': Add(Match('=') ? TokenKind.BangEqual : TokenKind.Bang); break;
            case '=': Add(Match('=') ? TokenKind.EqualEqual : TokenKind.Equal); break;
            case '<': Add(Match('=') ? TokenKind.LessEqual : TokenKind.Less); break;
            case '>': Add(Match('=') ? TokenKind.GreaterEqual : TokenKind.Greater); break;
            case '&': if (Match('&')) Add(TokenKind.AndAnd); else Invalid(c); break;
            case '|': if (Match('|')) Add(TokenKind.OrOr); else Invalid(c); break;
            default:
                if (c == '[') Add(TokenKind.LeftBracket);
                else if (c == ']') Add(TokenKind.RightBracket);
                else if (c == ',') Add(TokenKind.Comma);
                else if (c == ':') Add(TokenKind.Colon);
                else if (c == '.') Add(TokenKind.Dot);
                else if (c == '"') ScanQuoted('"', TokenKind.String);
                else if (c == '\'') ScanQuoted('\'', TokenKind.Character);
                else if (char.IsAsciiDigit(c)) ScanNumber();
                else if (char.IsLetter(c) || c == '_') ScanIdentifier();
                else Invalid(c);
                break;
        }
    }

    private void ScanNumber()
    {
        while (!AtEnd && char.IsAsciiDigit(Peek())) Advance();
        if (!AtEnd && Peek() == '.' && char.IsAsciiDigit(PeekNext()))
        {
            Advance();
            while (!AtEnd && char.IsAsciiDigit(Peek())) Advance();
            Add(TokenKind.Decimal);
        }
        else Add(TokenKind.Integer);
    }

    private void ScanQuoted(char quote, TokenKind kind)
    {
        while (!AtEnd && Peek() != quote)
        {
            if (Peek() == '\n') Invalid("Unterminated literal");
            if (Peek() == '\\')
            {
                Advance();
                if (AtEnd) Invalid("Unterminated literal");
            }
            Advance();
        }
        if (AtEnd) Invalid("Unterminated literal");
        Advance();
        Add(kind);
    }

    private void ScanIdentifier()
    {
        while (!AtEnd && (char.IsLetterOrDigit(Peek()) || Peek() == '_')) Advance();
        var text = _source[_start.._current];
        Add(Keywords.TryGetValue(text, out var kind) ? kind : TokenKind.Identifier);
    }

    private void SkipComment()
    {
        while (!AtEnd && Peek() != '\n') Advance();
    }

    private void SkipBlockComment()
    {
        while (!AtEnd)
        {
            if (Peek() == '*' && PeekNext() == '/') { Advance(); Advance(); return; }
            Advance();
        }
        Invalid("Unterminated block comment");
    }

    private char Advance()
    {
        var c = _source[_current++];
        if (c == '\n') { _line++; _column = 1; }
        else _column++;
        return c;
    }

    private bool Match(char expected)
    {
        if (AtEnd || Peek() != expected) return false;
        Advance();
        return true;
    }

    private char Peek() => AtEnd ? '\0' : _source[_current];
    private char PeekNext() => _current + 1 < _source.Length ? _source[_current + 1] : '\0';

    private void Add(TokenKind kind) => _tokens.Add(new Token(kind, _source[_start.._current], new SourceLocation(_startLine, _startColumn), _start));

    private void Invalid(object c) => throw new ParseException(c is char character ? $"Invalid character '{character}'." : c.ToString()!, new SourceLocation(_startLine, _startColumn));
}

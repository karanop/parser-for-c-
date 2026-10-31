namespace SimpleParser;

public enum TokenKind
{
    EndOfFile, Identifier, Integer, Decimal, String, Character, True, False,
    Int, Bool, StringType, CharType, Double, Var, Print, If, Else,
    For, While, Do, Foreach, In, Break, Continue, Return, Switch, Case, Default,
    Plus, PlusPlus, Minus, MinusMinus, Star, Slash, Bang, BangEqual, Equal, EqualEqual,
    Less, LessEqual, Greater, GreaterEqual, AndAnd, OrOr,
    LeftParen, RightParen, LeftBrace, RightBrace, LeftBracket, RightBracket,
    Semicolon, Comma, Colon, Dot
}

public readonly record struct SourceLocation(int Line, int Column)
{
    public override string ToString() => $"line {Line}, column {Column}";
}

public sealed record Token(TokenKind Kind, string Text, SourceLocation Location, int Offset = 0)
{
    public override string ToString() => $"{Kind,-12} {Text,-12} ({Location.Line}:{Location.Column})";
}

public sealed class ParseException(string message, SourceLocation location)
    : Exception($"{location}: {message}")
{
    public SourceLocation Location { get; } = location;
}

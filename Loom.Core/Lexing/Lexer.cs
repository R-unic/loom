using Loom.Core.Diagnostics;
using Loom.Core.Text;

namespace Loom.Core.Lexing;

public sealed class Lexer(SourceFile file)
{
    private static readonly Dictionary<string, SyntaxKind>.AlternateLookup<ReadOnlySpan<char>> _keywordLookup = SyntaxFacts.KeywordMap
        .GetAlternateLookup<ReadOnlySpan<char>>();

    private readonly DiagnosticBag _diagnostics = new();
    private readonly Stack<InterpolationFrame> _interpolationStack = new();
    private readonly int _sourceLength = file.SourceText.Length;
    private int _position;

    public LexerResult Tokenize()
    {
        var allTokens = new List<Token>();
        var significantTokens = new List<Token>();
        foreach (var token in GetTokens())
        {
            allTokens.Add(token);

            if (SyntaxFacts.IsTrivia(token.Kind)) continue;
            significantTokens.Add(token);
        }

        return new LexerResult(significantTokens, allTokens, _diagnostics);
    }

    private bool AtDanglingInterpolationText() => _interpolationStack.Count > 0 && _interpolationStack.Peek().Mode == InterpolationMode.Text;

    private IEnumerable<Token> GetTokens()
    {
        while (!IsEof() || AtDanglingInterpolationText())
        {
            if (AtDanglingInterpolationText())
            {
                foreach (var token in LexInterpolationText())
                    yield return token;

                continue;
            }

            var start = _position;
            var current = Current();
            if (current == '$' && !IsEof(1) && Peek(1) is '"' or '\'')
            {
                yield return LexInterpolatedStringStart(start);
                continue;
            }

            if (char.IsLetter(current) || current == '_')
            {
                yield return LexIdentifier(start);
                continue;
            }

            if (char.IsWhiteSpace(current))
            {
                yield return LexWhitespace(start);
                continue;
            }

            if (current is '"' or '\'')
            {
                yield return LexString(start, current);
                continue;
            }

            if (AtNumber())
            {
                yield return LexNumber(start);
                continue;
            }

            if (OperatorTrie.TryMatch(file.SourceText, _position, out var operatorKind, out var operatorLength))
            {
                Advance(operatorLength);
                var token = CreateToken(operatorKind, start);
                TrackInterpolationBraces(operatorKind);
                yield return token;
                continue;
            }

            if (TryLexRegexRule(start, out var regexToken))
            {
                yield return regexToken;
                continue;
            }

            RecoverUnexpectedCharacterDiagnostic(start);
            break;
        }

        yield return CreateToken(SyntaxKind.Eof, _position);
    }

    private void TrackInterpolationBraces(SyntaxKind operatorKind)
    {
        if (_interpolationStack.Count == 0 || _interpolationStack.Peek().Mode != InterpolationMode.Hole) return;

        if (operatorKind == SyntaxKind.LBrace)
        {
            var frame = _interpolationStack.Pop();
            _interpolationStack.Push(frame with { BraceDepth = frame.BraceDepth + 1 });
        }
        else if (operatorKind == SyntaxKind.RBrace)
        {
            var frame = _interpolationStack.Pop();
            _interpolationStack.Push(
                frame.BraceDepth == 0
                    ? frame with { Mode = InterpolationMode.Text }
                    : frame with { BraceDepth = frame.BraceDepth - 1 }
            );
        }
    }

    private Token LexInterpolatedStringStart(int start)
    {
        Advance(); // '$'
        var terminator = Current();
        Advance(); // opening quote
        _interpolationStack.Push(new InterpolationFrame(terminator, InterpolationMode.Text, 0));
        return CreateToken(SyntaxKind.InterpolatedStringStart, start);
    }

    private IEnumerable<Token> LexInterpolationText()
    {
        var frame = _interpolationStack.Peek();
        var start = _position;
        while (!IsEof() && Current() != '\n' && Current() != frame.Terminator && Current() != '{')
        {
            if (Current() == '\\' && !IsEof(1))
                Advance();

            Advance();
        }

        if (_position > start)
            yield return CreateToken(SyntaxKind.InterpolatedStringText, start);

        if (IsEof() || Current() == '\n')
        {
            var quotedQuote = frame.Terminator == '"' ? "'\"'" : "\"'\"";
            _diagnostics.Error(GetSpan(start), InternalCodes.UnterminatedString, $"Unterminated string literal: expected closing {quotedQuote}.");
            _interpolationStack.Pop();
            yield return CreateToken(SyntaxKind.InterpolatedStringEnd, _position);
            yield break;
        }

        if (Current() == '{')
        {
            var braceStart = _position;
            Advance();
            yield return CreateToken(SyntaxKind.LBrace, braceStart);
            _interpolationStack.Pop();
            _interpolationStack.Push(frame with { Mode = InterpolationMode.Hole, BraceDepth = 0 });
            yield break;
        }

        var endStart = _position;
        Advance();
        yield return CreateToken(SyntaxKind.InterpolatedStringEnd, endStart);
        _interpolationStack.Pop();
    }

    private Token LexString(int start, char terminator)
    {
        Advance();
        while (!IsEof() && Current() != terminator && Current() != '\n')
        {
            if (Current() == '\\' && !IsEof(1))
                Advance();

            Advance();
        }

        if (IsEof() || Current() == '\n')
        {
            var quotedQuote = terminator == '"' ? "'\"'" : "\"'\"";
            _diagnostics.Error(GetSpan(start), InternalCodes.UnterminatedString, $"Unterminated string literal: expected closing {quotedQuote}.");
        }
        else
        {
            Advance();
        }

        return CreateToken(SyntaxKind.StringLiteral, start);
    }

    private bool TryLexRegexRule(int start, out Token token)
    {
        if (LexerRules.RegexRulesByFirstCharacter.TryGetValue(Current(), out var candidates))
            foreach (var (rule, regex) in candidates)
            {
                var match = regex.Match(file.SourceText, _position);
                if (!match.Success || match.Index != _position || match.Length == 0) continue;

                Advance(match.Length);
                token = CreateToken(rule.Syntax, start);
                return true;
            }

        token = null!;
        return false;
    }

    private Token LexWhitespace(int start)
    {
        AdvanceWhile(char.IsWhiteSpace);
        return CreateToken(SyntaxKind.Whitespace, start);
    }

    private Token LexIdentifier(int start)
    {
        Advance();
        AdvanceWhile(ch => char.IsLetterOrDigit(ch) || ch == '_');
        var identifierText = file.SourceText.AsSpan(start, _position - start);
        var kind = _keywordLookup.TryGetValue(identifierText, out var keywordKind) ? keywordKind : SyntaxKind.Identifier;
        return CreateToken(kind, start);
    }

    private Token LexNumber(int start)
    {
        if (AtRadixLiteral())
            return LexRadixNumber(start);

        if (LexDecimal(start) is { } decimalError)
            return decimalError;

        if (TryLexExponent(start) is { } exponentError)
            return exponentError;

        LexUnitSuffix();
        return CreateToken(SyntaxKind.NumberLiteral, start);
    }

    private Token LexRadixNumber(int start)
    {
        Advance();
        switch (char.ToLowerInvariant(Current()))
        {
            case 'x':
                Advance();
                if (!ReadHexDigits())
                    return ReportMalformedHex(start);

                break;

            case 'b':
                Advance();
                if (!ReadBinaryDigits())
                    return ReportMalformedBinary(start);

                break;

            case 'o':
                Advance();
                if (!ReadOctalDigits())
                    return ReportMalformedOctal(start);

                break;
        }

        LexUnitSuffix();
        return CreateToken(SyntaxKind.NumberLiteral, start);
    }

    private void LexUnitSuffix()
    {
        if (IsEof()) return;
        switch (char.ToLowerInvariant(Current()))
        {
            case 'h':
                Advance();
                Match('z');
                break;

            case 'm':
                Advance();
                Match('s');
                break;

            case 's':
                Advance();
                break;
        }
    }

    private Token? LexDecimal(int start)
    {
        if (Current() == '.')
        {
            Advance();
            ReadDigits();
            return null;
        }

        ReadDigits();
        return LexFraction(start);
    }

    private Token? LexFraction(int start)
    {
        if (IsEof() || Current() != '.')
            return null;

        if (!IsEof(1) && Peek(1) == '.')
            return null;

        Advance();
        if (!IsEof() && char.IsDigit(Current()))
        {
            ReadDigits();
            return null;
        }

        var span = GetSpan(start);
        _diagnostics.Error(
            span,
            InternalCodes.MalformedNumber,
            $"Malformed float literal '{span.GetText()}': expected one or more digits after the decimal point."
        );

        return CreateToken(SyntaxKind.NumberLiteral, start);
    }

    private Token? TryLexExponent(int start)
    {
        if (IsEof() || Current() is not ('e' or 'E'))
            return null;

        Advance();

        if (!IsEof() && Current() == '-')
            Advance();

        if (!IsEof() && char.IsDigit(Current()))
        {
            ReadDigits();
            return null;
        }

        var span = GetSpan(start);
        _diagnostics.Error(
            span,
            InternalCodes.MalformedNumber,
            $"Malformed scientific notation '{span.GetText()}': expected one or more digits after the exponent."
        );

        return CreateToken(SyntaxKind.NumberLiteral, start);
    }

    private bool ReadHexDigits() => AdvanceWhile(static c => char.IsDigit(c) || (uint)(char.ToLowerInvariant(c) - 'a') <= 5 || c == '_');
    private bool ReadBinaryDigits() => AdvanceWhile(static c => c is '0' or '1' or '_');
    private bool ReadOctalDigits() => AdvanceWhile(static c => (uint)(c - '0') <= 7 || c == '_');
    private void ReadDigits() => AdvanceWhile(static c => char.IsDigit(c) || c == '_');
    private bool AtNumber() => char.IsDigit(Current()) || Current() == '.' && !IsEof(1) && char.IsDigit(Peek(1));
    private bool AtRadixLiteral() => Current() == '0' && !IsEof(1) && char.ToLowerInvariant(Peek(1)) is 'x' or 'b' or 'o';

    private Token ReportMalformedHex(int start)
    {
        var span = GetSpan(start);
        _diagnostics.Error(
            span,
            InternalCodes.MalformedNumber,
            $"Malformed hexadecimal literal '{span.GetText()}': expected at least one hex digit after '0x'."
        );

        return CreateToken(SyntaxKind.NumberLiteral, start);
    }

    private Token ReportMalformedBinary(int start)
    {
        var span = GetSpan(start);
        _diagnostics.Error(
            span,
            InternalCodes.MalformedNumber,
            $"Malformed binary literal '{span.GetText()}': expected at least one binary digit after '0b'."
        );

        return CreateToken(SyntaxKind.NumberLiteral, start);
    }

    private Token ReportMalformedOctal(int start)
    {
        var span = GetSpan(start);
        _diagnostics.Error(
            span,
            InternalCodes.MalformedNumber,
            $"Malformed octal literal '{span.GetText()}': expected at least one octal digit after '0o'."
        );

        return CreateToken(SyntaxKind.NumberLiteral, start);
    }

    private void RecoverUnexpectedCharacterDiagnostic(int start)
    {
        if (TryDiagnosticRule(start, LexerRules.Diagnostic)) return;

        var character = Current();
        var display = char.IsControl(character) ? $"U+{(int)character:X4}" : $"'{character}'";
        _diagnostics.Error(GetSpan(start), InternalCodes.UnexpectedCharacter, $"Unexpected character {display}.");
    }

    private bool TryDiagnosticRule(int start, IReadOnlyList<LexerDiagnosticRule> rules)
    {
        foreach (var rule in rules)
        {
            var match = rule.Pattern.Match(file.SourceText, _position);
            if (!match.Success || match.Index != _position) continue;

            Advance(match.Length);
            _diagnostics.Report(
                GetSpan(start),
                rule.Severity,
                rule.DiagnosticCode,
                rule.MessageFactory(match.Value),
                rule.Hint
            );

            return true;
        }

        return false;
    }

    private void Match(char c)
    {
        if (IsEof() || char.ToLowerInvariant(Current()) != c) return;
        Advance();
    }

    private bool AdvanceWhile(Func<char, bool> predicate)
    {
        var any = false;
        while (!IsEof() && predicate(Current()))
        {
            any = true;
            Advance();
        }

        return any;
    }

    private Token CreateToken(SyntaxKind kind, int start) => new(kind, file, new TextSpan(start, _position - start));
    private void Advance(int offset = 1) => _position += offset;
    private char Current() => Peek(0);
    private char Peek(int offset) => file.SourceText[_position + offset];
    private bool IsEof(int offset = 0) => _position + offset >= _sourceLength;
    private LocationSpan GetSpan(int start) => new(new Location(file, start), _position - start);

    private enum InterpolationMode { Text, Hole }
    private readonly record struct InterpolationFrame(char Terminator, InterpolationMode Mode, int BraceDepth);
}
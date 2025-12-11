

using System.Text.RegularExpressions;

namespace migrator.Engine;

public static class SqlTableExtractor
{
    public static HashSet<string> ExtractTableNames(string sql)
    {
        var tokenizer = new SqlTokenizer(sql);
        var tokens = tokenizer.Tokenize().ToList();

        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];

            switch (t.ValueUpper)
            {
                case "FROM":
                case "JOIN":
                case "INTO":
                case "UPDATE":
                case "MERGE":
                    TryReadTable(tokens, ref i, results);
                    break;

                case "CREATE":
                case "ALTER":
                case "DROP":
                    TryReadDDL(tokens, ref i, results);
                    break;

                case "ON":
                    // For CREATE INDEX ... ON table
                    TryReadTable(tokens, ref i, results);
                    break;
            }
        }

        return results;
    }

    private static void TryReadDDL(List<SqlToken> tokens, ref int i, HashSet<string> output)
    {
        if (i + 2 < tokens.Count && tokens[i + 1].ValueUpper == "TABLE")
        {
            TryReadTable(tokens, ref i, output, offset: 2);
        }
    }

    private static void TryReadTable(List<SqlToken> tokens, ref int index, HashSet<string> output, int offset = 1)
    {
        int i = index + offset;

        // Skip optional keywords
        while (i < tokens.Count && tokens[i].ValueUpper is "ONLY" or "IF" or "(")
            i++;

        // Read identifier (supports schema.table)
        var name = ReadCompoundIdentifier(tokens, ref i);
        if (name != null)
            output.Add(name);
    }

    private static string? ReadCompoundIdentifier(List<SqlToken> tokens, ref int i)
    {
        if (i >= tokens.Count)
            return null;

        if (!tokens[i].IsIdentifier)
            return null;

        var parts = new List<string> { tokens[i].Value };
        i++;

        while (i + 1 < tokens.Count &&
               tokens[i].Value == "." &&
               tokens[i + 1].IsIdentifier)
        {
            parts.Add(tokens[i + 1].Value);
            i += 2;
        }

        return string.Join(".", parts);
    }
}

/// -------- Tokenizer --------

public class SqlTokenizer
{
    private readonly string _s;
    private int _i;

    public SqlTokenizer(string sql) => _s = sql;

    public IEnumerable<SqlToken> Tokenize()
    {
        while (_i < _s.Length)
        {
            char c = _s[_i];

            if (char.IsWhiteSpace(c))
            {
                _i++;
                continue;
            }

            if (c == '-' && Peek(1) == '-')
                SkipLineComment();
            else if (c == '/' && Peek(1) == '*')
                SkipBlockComment();
            else if (c == '\'' || c == '"')
                yield return ReadStringOrQuotedIdentifier();
            else if (c == '[')
                yield return ReadBracketIdentifier();
            else if (IsIdentifierStart(c))
                yield return ReadIdentifier();
            else
                yield return new SqlToken(_s[_i++].ToString());
        }

        yield return SqlToken.EOF;
    }

    private SqlToken ReadIdentifier()
    {
        int start = _i;
        while (_i < _s.Length && IsIdentifierPart(_s[_i])) _i++;
        var text = _s[start.._i];
        return new SqlToken(text);
    }

    private SqlToken ReadStringOrQuotedIdentifier()
    {
        char quote = _s[_i++];
        int start = _i;
        while (_i < _s.Length && _s[_i] != quote) _i++;
        var text = _s[start.._i];
        _i++;
        return new SqlToken(text, IsString: true);
    }

    private SqlToken ReadBracketIdentifier()
    {
        _i++;
        int start = _i;
        while (_i < _s.Length && _s[_i] != ']') _i++;
        var text = _s[start.._i];
        _i++;
        return new SqlToken(text, isIdentifier: true);
    }

    private void SkipLineComment()
    {
        _i += 2;
        while (_i < _s.Length && _s[_i] != '\n') _i++;
    }

    private void SkipBlockComment()
    {
        _i += 2;
        while (_i + 1 < _s.Length && !(_s[_i] == '*' && _s[_i + 1] == '/')) _i++;
        _i += 2;
    }

    private char Peek(int offset) =>
        _i + offset < _s.Length ? _s[_i + offset] : '\0';

    private static bool IsIdentifierStart(char c) =>
        char.IsLetter(c) || c == '_' || c == '$';

    private static bool IsIdentifierPart(char c) =>
        char.IsLetterOrDigit(c) || c is '_' or '$' or '#';
}

public record SqlToken(string Value, bool IsString = false, bool? isIdentifier = null)
{
    public string ValueUpper => Value.ToUpperInvariant();
    public bool IsIdentifier => isIdentifier ?? (!IsString && IdentifierRegex.IsMatch(Value));

    private static readonly Regex IdentifierRegex = new(@"^[A-Za-z_][A-Za-z0-9_]*$");
    public static SqlToken EOF => new("", false);
}


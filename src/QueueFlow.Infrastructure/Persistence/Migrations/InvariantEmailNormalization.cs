using System.Text;

namespace QueueFlow.Infrastructure.Persistence.Migrations;

internal static class InvariantEmailNormalization
{
    // Materialize the .NET invariant Unicode mapping instead of relying on the database collation.
    public static string CreateFunctionSql()
    {
        var upper = new StringBuilder();
        var lower = new StringBuilder();
        for (var value = 1; value <= 0x10ffff; value++)
        {
            if (!Rune.IsValid(value)) continue;
            var rune = new Rune(value);
            var normalized = Rune.ToLowerInvariant(rune);
            if (rune == normalized) continue;
            upper.Append(rune); lower.Append(normalized);
        }
        var whitespace = string.Concat(Enumerable.Range(1, char.MaxValue).Where(value => char.IsWhiteSpace((char)value)).Select(value => $"\\{value:x4}"));
        return $"""
            CREATE OR REPLACE FUNCTION queueflow_normalize_email(value text) RETURNS text
            LANGUAGE sql IMMUTABLE STRICT PARALLEL SAFE AS $normalize$
                SELECT translate(btrim(value, U&'{whitespace}'), '{upper}', '{lower}') COLLATE "C";
            $normalize$;
            """;
    }
}

namespace ZipMp3Player;

internal static class TagTextNormalization
{
    public static string ToHalfWidthAlphaNumeric(string value)
    {
        // Deliberately not NFKC: kana, symbols, circled numbers and Roman
        // numerals stay unchanged. Only U+3000 is mapped to an ASCII space;
        // preserve whitespace counts and all other whitespace characters.
        char[]? converted = null;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character is not (>= 'Ａ' and <= 'Ｚ' or >= 'ａ' and <= 'ｚ' or >= '０' and <= '９' or '\u3000')) continue;
            converted ??= value.ToCharArray();
            converted[index] = character == '\u3000' ? ' ' : (char)(character - 0xfee0);
        }
        return converted is null ? value : new string(converted);
    }
}

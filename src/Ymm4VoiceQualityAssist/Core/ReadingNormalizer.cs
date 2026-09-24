using System.Text;

namespace Ymm4VoiceQualityAssist.Core;

public static class ReadingNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var source = value.Normalize(NormalizationForm.FormKC);
        var sb = new StringBuilder(source.Length);

        foreach (var ch in source)
        {
            if (ShouldIgnore(ch))
                continue;

            if (ch is >= '\u3041' and <= '\u3096')
            {
                sb.Append((char)(ch + 0x60));
                continue;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    static bool ShouldIgnore(char ch) =>
        char.IsWhiteSpace(ch)
        || ch is '\''
            or '’'
            or '、'
            or '。'
            or '，'
            or ','
            or '/'
            or '？'
            or '?'
            or '！'
            or '!'
            or '_';
}

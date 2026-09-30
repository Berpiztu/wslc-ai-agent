using System.Text;

namespace WslcAgent.ApiClient;

/// <summary>
/// Splits and joins command lines the way a POSIX shell reads words: single
/// quotes literal, double quotes with backslash escapes, and outside quotes a
/// backslash escapes only a space or a quote, so Windows paths (<c>c:\IA</c>,
/// <c>\\server</c>) in a pasted run line survive. Used for a container's
/// command and for pasted run lines.
/// </summary>
public static class ShellWords
{
    public static IReadOnlyList<string> Split(string text)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        var inWord = false;
        var quote = '\0';
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote == '\'')
            {
                if (c == '\'')
                {
                    quote = '\0';
                }
                else
                {
                    word.Append(c);
                }

                continue;
            }

            if (quote == '"')
            {
                if (c == '"')
                {
                    quote = '\0';
                }
                else if (c == '\\' && i + 1 < text.Length && text[i + 1] is '"' or '\\' or '$' or '`')
                {
                    word.Append(text[++i]);
                }
                else
                {
                    word.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '\'' or '"':
                    quote = c;
                    inWord = true;
                    break;
                case '\\' when i + 1 < text.Length && text[i + 1] is ' ' or '\t' or '"' or '\'':
                    word.Append(text[++i]);
                    inWord = true;
                    break;
                case ' ' or '\t' or '\n' or '\r':
                    if (inWord)
                    {
                        words.Add(word.ToString());
                        word.Clear();
                        inWord = false;
                    }

                    break;
                default:
                    word.Append(c);
                    inWord = true;
                    break;
            }
        }

        if (inWord)
        {
            words.Add(word.ToString());
        }

        return words;
    }

    /// <summary>Words back to one line, quoting only what needs it.</summary>
    public static string Join(IEnumerable<string> words) => string.Join(' ', words.Select(Quote));

    public static string Quote(string word)
    {
        if (word.Length > 0 && word.All(c => char.IsLetterOrDigit(c) || "-_./:=@%+,".Contains(c)))
        {
            return word;
        }

        return "'" + word.Replace("'", "'\\''") + "'";
    }
}

using System.Text;

namespace TaskFlow.Domain.Workspaces;

public static class WorkspaceSlug
{
    private const int MaxBaseLength = 40;

    // Tabla explícita en vez de string.Normalize(FormD): con InvariantGlobalization no hay ICU
    // y Normalize no descompone "é" en "e" + tilde. Esto no depende de la configuración del runtime.
    private static readonly Dictionary<char, char> Transliterations = new()
    {
        ['á'] = 'a', ['à'] = 'a', ['ä'] = 'a', ['â'] = 'a', ['ã'] = 'a',
        ['é'] = 'e', ['è'] = 'e', ['ë'] = 'e', ['ê'] = 'e',
        ['í'] = 'i', ['ì'] = 'i', ['ï'] = 'i', ['î'] = 'i',
        ['ó'] = 'o', ['ò'] = 'o', ['ö'] = 'o', ['ô'] = 'o', ['õ'] = 'o',
        ['ú'] = 'u', ['ù'] = 'u', ['ü'] = 'u', ['û'] = 'u',
        ['ñ'] = 'n', ['ç'] = 'c',
    };

    /// <summary>"José.Núñez+test" → "jose-nunez-test". Siempre devuelve algo que cumple el formato de slug.</summary>
    public static string FromText(string text)
    {
        var sb = new StringBuilder();
        foreach (var raw in text.ToLowerInvariant())
        {
            var c = Transliterations.GetValueOrDefault(raw, raw);
            if (char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c))
                sb.Append(c);
            else if (sb.Length > 0 && sb[^1] != '-')
                sb.Append('-'); // cualquier otro carácter es separador
        }

        var slug = sb.ToString().Trim('-');
        if (slug.Length > MaxBaseLength)
            slug = slug[..MaxBaseLength].TrimEnd('-');
        return slug.Length == 0 ? "workspace" : slug;
    }
}

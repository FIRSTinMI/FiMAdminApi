using System.Text.Encodings.Web;

namespace FiMAdminApi.Clients.Extensions;

internal static class QueryString
{
    private static readonly UrlEncoder UrlEncoder = UrlEncoder.Default;
    public static string Create(ICollection<KeyValuePair<string, string>> pairs)
    {
        if (pairs.Count == 0) return "";
        return '?' + string.Join('&', pairs.Select(p => 
            UrlEncoder.Encode(p.Key) 
            + "=" 
            + UrlEncoder.Encode(p.Value)));
    }
}
namespace FiMAdminApi.Extensions;

public static class FormattableStringExtensions
{
    public static string EncodeString(this FormattableString str, Func<string, string> func)
    {
        var invariantParameters = str.GetArguments()
            .Select(a => FormattableString.Invariant($"{a}"));
        var escapedParameters = invariantParameters
            .Select(func)
            .Cast<object>()
            .ToArray();

        return string.Format(str.Format, escapedParameters);
    }
}
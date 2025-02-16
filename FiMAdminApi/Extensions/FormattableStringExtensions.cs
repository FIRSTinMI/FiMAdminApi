namespace FiMAdminApi.Extensions;

public static class FormattableStringExtensions
{
    /// <summary>
    /// Encode all <c>$"{parameters}"</c> in a string. For example, this can be used to create a
    /// safe URL path which contains user-provided values
    /// </summary>
    /// <param name="str">Formattable string to encode</param>
    /// <param name="func">Function to run to convert an unencoded parameter to encoded text</param>
    /// <returns>Encoded string</returns>
    public static string EncodeString(this FormattableString str, Func<string, string> func)
    {
        var invariantParameters = str.GetArguments()
            .Select(a => FormattableString.Invariant($"{a}"));
        var escapedParameters = invariantParameters
            .Select(func)
            .Cast<object>() // string.Format wants an object[] for parameters, but they'll always be strings
            .ToArray();

        return string.Format(str.Format, escapedParameters);
    }
}
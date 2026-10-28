using System.Globalization;

namespace DuAnCode.Web.Services
{
    public static class LocaleHelper
    {
        public static bool IsEnglish => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("en", System.StringComparison.OrdinalIgnoreCase);

        public static string T(string viText, string enText)
        {
            return IsEnglish ? enText : viText;
        }
    }
}

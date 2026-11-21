// Windows 2000 / .NET 2.0 build only: System.Net.WebUtility arrived with .NET 4.0; .NET 2.0 has the
// same decoder in System.Web.HttpUtility.

namespace System.Net
{
    /// <summary>HTML entity decoding (.NET 4.0 WebUtility), backed by .NET 2.0's HttpUtility.</summary>
    public static class WebUtility
    {
        /// <summary>Replaces HTML entities such as &amp;amp; or &amp;#252; by their characters.</summary>
        public static string HtmlDecode(string value) => System.Web.HttpUtility.HtmlDecode(value);
    }
}

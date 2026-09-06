using RabbitMQ.Services.Interfaces;
using System.Text.RegularExpressions;

namespace RabbitMQ.Services.Implementations
{
    public sealed partial class UriMasker : IUriMasker
    {
        private const string MaskValue = "***";

        public string Mask(string? uri)
        {
            if (string.IsNullOrEmpty(uri))
            {
                return string.Empty;
            }

            var match = UserInfoRegex().Match(uri);
            if (!match.Success)
            {
                return uri;
            }

            var prefix = match.Groups["prefix"].Value;
            var userInfo = match.Groups["password"].Success ? $"{MaskValue}:{MaskValue}" : MaskValue;

            return string.Concat(prefix, userInfo, "@", uri.AsSpan(match.Length));
        }

        [GeneratedRegex(@"^(?<prefix>[A-Za-z][A-Za-z0-9+.\-]*://)(?<user>[^:@/?#]*)(:(?<password>[^@/?#]*))?@")]
        private static partial Regex UserInfoRegex();
    }
}

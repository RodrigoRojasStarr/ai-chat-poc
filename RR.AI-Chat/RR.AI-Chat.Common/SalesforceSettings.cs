namespace RR.AI_Chat.Common
{
    public class SalesforceSettings
    {
        public string ClientId { get; init; } = null!;

        public string ClientSecret { get; init; } = null!;

        public string UserName { get; init; } = null!;

        public string Password { get; init; } = null!;

        public string SecurityToken { get; init; } = null!;

        public Uri Endpoint { get; init; } = null!;
    }
}

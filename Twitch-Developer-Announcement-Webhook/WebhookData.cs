namespace TwitchDeveloperAnnouncementWebhook
{
    using System.Text.Json.Serialization;

    public class WebhookData
    {
        [JsonPropertyName("username")]
        public string? Username { get; set; }
        [JsonPropertyName("avatar_url")]
        public string? AvatarUrl { get; set; }
        [JsonPropertyName("allowed_mentions")]
        public Dictionary<string, string[]>? AllowedMentions { get; set; }
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }
}

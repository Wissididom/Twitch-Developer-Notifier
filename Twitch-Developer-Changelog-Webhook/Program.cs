using System.Xml;
using System.Text;
using System.Text.Json;
using TwitchDeveloperCommon;

namespace TwitchDeveloperChangelogWebhook
{
    public static class Program
    {
        private const string WebhookUsername = "Twitch Developer Changelog";
        private const string WebhookAvatarUrl = "https://dev.twitch.tv/marketing-assets/images/TwitchDev.png";
        private const string ContentFormatString = "New Developer Changelog Entry (Updated: <t:{0}:F>):\n**Title:** ``{1}``\n**Link:** <{2}>\n**Description:**\n{3}";

        public static async Task Main(string[] args)
        {
            DotEnv.Load(Path.Combine(Directory.GetCurrentDirectory(), ".env"));
            using var client = new HttpClient();
            var doc = new XmlDocument();
            doc.LoadXml(await client.GetStringAsync("https://dev.twitch.tv/docs/rss/change-log.xml").ConfigureAwait(false));
            var channel = doc.GetElementsByTagName("channel")[0];
            if (channel is null) return;
            string? lastBuildDateStr = (from node in channel.ChildNodes.OfType<XmlNode>() where node.Name == "lastBuildDate" select node.InnerText).FirstOrDefault();
            //DateTime? lastBuildDate = lastBuildDateStr is null ? null : DateTime.Parse(lastBuildDateStr);
            List<XmlNode> changelogEntries = [];
            changelogEntries.AddRange(channel.ChildNodes.OfType<XmlNode>().Where(node => node.Name == "item"));
            var fileNeedsUpdate = true;
            if (File.Exists("lastUpdatedValue"))
            {
                var lastUpdatedValue = await File.ReadAllTextAsync("lastUpdatedValue").ConfigureAwait(false);
                if (lastBuildDateStr is not null && lastUpdatedValue.Trim().Equals(lastBuildDateStr.Trim()))
                {
                    Console.WriteLine("Already latest version");
                    fileNeedsUpdate = false;
                }
                else
                {
                    Console.WriteLine("Needs update");
                    if (changelogEntries.Count > 0)
                    {
                        var entry = changelogEntries[0];
                        var title = string.Empty;
                        var link = string.Empty;
                        var description = string.Empty;
                        long pubDateUnix = 0;
                        foreach (XmlNode? node in entry.ChildNodes)
                        {
                            if (node is null) continue;
                            switch (node.Name)
                            {
                                case "title":
                                    title = node.InnerText;
                                    break;
                                case "link":
                                    link = node.InnerText;
                                    break;
                                case "description":
                                    description = ConvertHtml(node.InnerText);
                                    break;
                                case "pubDate":
                                    if (DateTime.TryParse(node.InnerText, out var pubDate))
                                    {
                                        pubDateUnix = ((DateTimeOffset)pubDate).ToUnixTimeSeconds();
                                    }
                                    break;
                            }
                        }
                        if (string.IsNullOrWhiteSpace(title) ||
                            string.IsNullOrWhiteSpace(link) ||
                            string.IsNullOrWhiteSpace(description) ||
                            pubDateUnix < 1) return;
                        Console.WriteLine((await PostDiscordMessage(client, title, link, pubDateUnix, description)).StatusCode);
                    }
                }
            }
            else
            {
                Console.WriteLine("File does not exist");
            }
            if (lastBuildDateStr is not null && fileNeedsUpdate) await File.WriteAllTextAsync("lastUpdatedValue", lastBuildDateStr);
        }

        private static string ConvertHtml(string html)
        {
            var config = new ReverseMarkdown.Config
            {
                UnknownTags = ReverseMarkdown.Config.UnknownTagsOption.Bypass,
                GithubFlavored = false,
                RemoveComments = true,
                OutputLineEnding = "\n",
                SmartHrefHandling = true
            };
            var converter = new ReverseMarkdown.Converter(config);
            return converter.Convert(html);
        }

        private static async Task<HttpResponseMessage> PostDiscordMessage(HttpClient client, string title, string link, long pubDateUnix, string description)
        {
            var url = $"{Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL")}?wait=true";
            //private const string ContentFormatString = "New Developer Changelog Entry (Updated: <t:{0}:F>):\nTitle: ``{1}``\nLink: <{2}>\nDescription:\n{3}";
            var formattedContent = string.Format(ContentFormatString, pubDateUnix, title, link, description);
            Console.WriteLine(formattedContent);
            var webhookData = new WebhookData
            {
                Username = WebhookUsername,
                AvatarUrl = WebhookAvatarUrl,
                AllowedMentions = new Dictionary<string, string[]>{
                    { "parse", [] }
                },
                Content = formattedContent
            };
            var webhookJson = JsonSerializer.Serialize(webhookData);
            var content = new StringContent(webhookJson, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);
            Console.WriteLine(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            return response;
            //return await client.PostAsync(url, content);
        }
    }
}

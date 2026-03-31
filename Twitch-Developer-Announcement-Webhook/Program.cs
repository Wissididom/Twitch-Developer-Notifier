using System.Xml;
using System.Text;
using System.Text.Json;
using TwitchDeveloperCommon;

namespace TwitchDeveloperAnnouncementWebhook
{
    public static class Program
    {
        private const string WebhookUsername = "Twitch Developer Announcement";
        private const string WebhookAvatarUrl = "https://d2opxh93rbxzdn.cloudfront.net/optimized/2X/0/05909729875cce3b53e59d5561c4d23bb14948fc_2_180x180.png";
        private const string ContentFormatString = "New Developer Announcement (Updated: <t:{0}:F>):\nTitle: ``{1}``\nCreator: ``{2}``\nLink: <{3}>";

        public static async Task Main(string[] args)
        {
            DotEnv.Load(Path.Combine(Directory.GetCurrentDirectory(), ".env"));
            using var client = new HttpClient();
            var doc = new XmlDocument();
            doc.LoadXml(await client.GetStringAsync("https://discuss.dev.twitch.com/c/announcements/13.rss").ConfigureAwait(false));
            var channel = doc.GetElementsByTagName("channel")[0];
            if (channel is null) return;
            string? lastBuildDateStr = (from node in channel.ChildNodes.OfType<XmlNode>() where node.Name == "lastBuildDate" select node.InnerText).FirstOrDefault();
            //DateTime? lastBuildDate = lastBuildDateStr is null ? null : DateTime.Parse(lastBuildDateStr);
            List<XmlNode> announcements = [];
            announcements.AddRange(channel.ChildNodes.OfType<XmlNode>().Where(node => node.Name == "item"));
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
                    if (announcements.Count > 0)
                    {
                        var announcement = announcements[0];
                        var creator = string.Empty;
                        var title = string.Empty;
                        var link = string.Empty;
                        long pubDateUnix = 0;
                        foreach (XmlNode? node in announcement.ChildNodes)
                        {
                            if (node is null) continue;
                            switch (node.Name)
                            {
                                case "dc:creator":
                                    creator = StripCData(node.InnerText);
                                    break;
                                case "title":
                                    title = node.InnerText;
                                    break;
                                case "link":
                                    link = node.InnerText;
                                    break;
                                case "pubDate":
                                    if (DateTime.TryParse(node.InnerText, out var pubDate))
                                    {
                                        pubDateUnix = ((DateTimeOffset)pubDate).ToUnixTimeSeconds();
                                    }
                                    break;
                            }
                        }
                        if (string.IsNullOrWhiteSpace(creator) ||
                            string.IsNullOrWhiteSpace(title) ||
                            string.IsNullOrWhiteSpace(link) ||
                            pubDateUnix < 1) return;
                        Console.WriteLine((await PostDiscordMessage(client, creator, title, link, pubDateUnix)).StatusCode);
                    }
                }
            }
            else
            {
                Console.WriteLine("File does not exist");
            }
            if (lastBuildDateStr is not null && fileNeedsUpdate) await File.WriteAllTextAsync("lastUpdatedValue", lastBuildDateStr);
        }

        private static string StripCData(string input)
        {
            return input.Replace("<![CDATA[", "").Replace("]]>", "").Trim();
        }

        private static async Task<HttpResponseMessage> PostDiscordMessage(HttpClient client, string creator, string title, string link, long pubDateUnix)
        {
            var url = $"{Environment.GetEnvironmentVariable("DISCORD_WEBHOOK_URL")}?wait=true";
            var formattedContent = string.Format(ContentFormatString, pubDateUnix, title, creator, link);
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

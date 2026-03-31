using DnsClient;
using System.Xml;
using MailKit.Net.Smtp;
using MimeKit;
using MimeKit.Cryptography;
using TwitchDeveloperCommon;

namespace TwitchDeveloperChangelogEmail
{
    public static class Program
    {
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
                        var isoPubDate = string.Empty;
                        var displayPubDate = string.Empty;
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
                                    description = node.InnerText;
                                    break;
                                case "pubDate":
                                    if (DateTime.TryParse(node.InnerText, out var pubDate))
                                    {
                                        isoPubDate = pubDate.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                                        displayPubDate = pubDate.ToUniversalTime().ToString("F") + " UTC";
                                    }
                                    break;
                            }
                        }
                        if (string.IsNullOrWhiteSpace(title) ||
                            string.IsNullOrWhiteSpace(link) ||
                            string.IsNullOrWhiteSpace(description) ||
                            string.IsNullOrWhiteSpace(isoPubDate) ||
                            string.IsNullOrWhiteSpace(displayPubDate)) return;
                        var plainBody = $"New Developer Changelog Entry (Updated: {displayPubDate})\nTitle: {title}\nLink: {link}\nDescription:\n{description}";
                        var htmlBody = $"New Developer Changelog Entry (Updated: <time datetime=\"{isoPubDate}\">{displayPubDate}</time>)<br />\nTitle: {title}<br />\nLink: <a href=\"{link}\">{link}</a><br />\nDescription:<br />\n{description}";
                        var senderEmail = Environment.GetEnvironmentVariable("SENDER_EMAIL");
                        var receiverEmail = Environment.GetEnvironmentVariable("RECEIVER_EMAIL");
                        if (senderEmail is null || receiverEmail is null) return;
                        var lookup = new LookupClient();
                        var result = await lookup.QueryAsync(receiverEmail[(receiverEmail.LastIndexOf('@') + 1)..], QueryType.MX);
                        var mxRecords = result.Answers.MxRecords().OrderBy(r => r.Preference).Select(r => r.Exchange.Value).ToList();
                        Console.WriteLine((await SendEmail(senderEmail, receiverEmail, $"New Developer Changelog Entry (Updated: {displayPubDate})", plainBody, htmlBody, mxRecords[0], 25)));
                    }
                }
            }
            else
            {
                Console.WriteLine("File does not exist");
            }
            if (lastBuildDateStr is not null && fileNeedsUpdate) await File.WriteAllTextAsync("lastUpdatedValue", lastBuildDateStr);
        }

        private static async Task<bool> SendEmail(string fromEmail, string toEmail, string subject, string plainBody, string htmlBody, string smtpHost, int smtpPort, string? username = null, string? password = null, string? dkimPrivateKeyPath = null, string? dkimDomain = null, string? dkimSelector = null)
        {
            // dkim private key is a .pem file and the selector can be "default"
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(fromEmail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;
            var bodyBuilder = new BodyBuilder
            {
                TextBody = plainBody,
                HtmlBody = htmlBody
            };
            message.Body = bodyBuilder.ToMessageBody();
            if (!string.IsNullOrWhiteSpace(dkimPrivateKeyPath) &&
                File.Exists(dkimPrivateKeyPath) &&
                !string.IsNullOrWhiteSpace(dkimDomain) &&
                !string.IsNullOrWhiteSpace(dkimSelector))
            {
                var privateKey = await File.ReadAllTextAsync(dkimPrivateKeyPath);
                var signer = new DkimSigner(privateKey, dkimDomain, dkimSelector)
                {
                    HeaderCanonicalizationAlgorithm = DkimCanonicalizationAlgorithm.Relaxed,
                    BodyCanonicalizationAlgorithm = DkimCanonicalizationAlgorithm.Relaxed
                };
                message.Prepare(EncodingConstraint.SevenBit); // ensure headers are clean
                signer.Sign(message, ["From", "To", "Subject", "Date", "Message-ID"]);
                Console.WriteLine("DKIM signing applied.");
            }
            else
            {
                Console.WriteLine("No DKIM key found, sending unsigned.");
            }
            try
            {
                using var client = new SmtpClient();
        
                // Optional: for debugging
                client.MessageSent += (_, _) => Console.WriteLine("Message sent");
                client.ServerCertificateValidationCallback = (_, _, _, _) => true; // skip TLS validation for local testing
        
                await client.ConnectAsync(smtpHost, smtpPort);
        
                // Authenticate if credentials are provided
                if (!string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password))
                    await client.AuthenticateAsync(username, password);
        
                await client.SendAsync(message);
                await client.DisconnectAsync(true);
        
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                return false;
            }
        }
    }
}

using Community.Microsoft.Extensions.AI.CoreML;
using Microsoft.Extensions.AI;

IChatClient client = new AppleIntelligenceChatClient();

Console.WriteLine(await client.GetResponseAsync("What is AI?"));
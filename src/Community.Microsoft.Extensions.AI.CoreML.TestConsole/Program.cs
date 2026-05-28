using System.ComponentModel;
using Community.Microsoft.Extensions.AI.CoreML;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

IChatClient client = new AppleIntelligenceChatClient();

Console.WriteLine("\n==== AI Agent with tools ====");

[Description("Get the weather for a given location.")]
static string GetWeather([Description("The location to get the weather for.")] string location)
    => $"The weather in {location} is cloudy with a high of 15°C.";

AIAgent agent = client.AsAIAgent(
    name: "WeatherAgent",
    instructions: """
        You are a weather assistant. All answers must be given like you are a ninja.
        """,
    tools: [AIFunctionFactory.Create(GetWeather, "GetWeather", "Get the weather for a given location.")]);

Console.WriteLine(await agent.RunAsync("What's the weather in New York?"));
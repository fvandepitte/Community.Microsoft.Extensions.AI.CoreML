using System.ComponentModel;
using Community.Microsoft.Extensions.AI.CoreML;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

IChatClient client = new AppleIntelligenceChatClient();

Console.WriteLine("==== Simple chat ====");

var response = await client.GetResponseAsync("What is the average airspeed of a laden swallow?");
Console.WriteLine(response);

Console.WriteLine("\n==== AI Agent with tools ====");

[Description("Get the weather for a given location.")]
static string GetWeather([Description("The location to get the weather for.")] string location)
    => $"The weather in {location} is cloudy with a high of 15°C.";

AIAgent agent = client.AsAIAgent(
    name: "WeatherAgent",
    instructions: "You are a weather assistant.",
    tools: [AIFunctionFactory.Create(GetWeather)]);

Console.WriteLine(await agent.RunAsync("What's the weather in New York?"));
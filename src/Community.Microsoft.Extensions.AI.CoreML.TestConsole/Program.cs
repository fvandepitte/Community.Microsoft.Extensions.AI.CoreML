using System.ComponentModel;
using Community.Microsoft.Extensions.AI.CoreML;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

IChatClient client = new AppleIntelligenceChatClient();

Console.WriteLine("==== Simple chat ====");

var response = await client.GetResponseAsync("What is the average airspeed of a laden swallow?");
Console.WriteLine(response);

Console.WriteLine("\n==== AI Agent with tools ====");

string GetWeather(string location) => $"It is cloudy in {location} with a high of 15°C.";

var agent = new AppleIntelligenceChatClient()
    .AsAIAgent(
        name: "WeatherAgent",
        instructions: "You are a helpful, but talkative weather assistant.",
        tools: [AIFunctionFactory.Create(GetWeather, "GetWeather", "Get the weather for a given location.")]);

Console.WriteLine(await agent.RunAsync("What's the weather in New York?"));
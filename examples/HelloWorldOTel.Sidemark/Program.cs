using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Sidemark;

[assembly: Sidemark(typeof(OTelConfig))]

using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource("HelloWorldOTel.Sidemark")
    .AddConsoleExporter()
    .Build();

var greeter = new Greeter();
await greeter.GreetAsync("World");
await greeter.GreetAsync("Comments");

public static class OTelConfig
{
    public static readonly ActivitySource ActivitySource = new("HelloWorldOTel.Sidemark", "1.0.0");

    // Optional pattern overrides (defaults shown, kept here as a demo - remove these to demo defaults)
    // public const string ActivityPattern = "//?";
    // public const string TagPattern = "//?";
    // public const string EventPattern = "//!";
}

public class Greeter
{
    public async Task GreetAsync(string name) //? Greet
    {
        var greeting = $"Hello, {name}!"; //?
        var greetingLength = greeting.Length; //? greeting.length

        await EmitAsync(greeting); //! AboutToWrite
    }

    // No //? on the signature: just having //? on a local in the body still creates a child
    // activity named after the method (EmitAsync), which becomes a child of the outer Greet.
    private async Task EmitAsync(string line)
    {
        var emittedAt = DateTime.UtcNow; //? emitted.at

        await Task.Delay(25);
        Console.WriteLine(line);
    }
}

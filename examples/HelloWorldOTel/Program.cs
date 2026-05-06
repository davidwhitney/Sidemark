using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

// Define an ActivitySource for distributed tracing
var activitySource = new ActivitySource("HelloWorldOTel");

// Define a Meter for metrics
var meter = new Meter("HelloWorldOTel");
var greetingCounter = meter.CreateCounter<long>("greetings.count", description: "Counts the number of greetings");
var greetingDuration = meter.CreateHistogram<double>("greetings.duration_ms", unit: "ms", description: "Duration of greeting operations");

// Configure OpenTelemetry with tracing and metrics, exporting to the console
using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource("HelloWorldOTel")
    .AddConsoleExporter()
    .Build();

using var meterProvider = Sdk.CreateMeterProviderBuilder()
    .AddMeter("HelloWorldOTel")
    .AddConsoleExporter()
    .Build();

Console.WriteLine("Hello, World!");

// Add a baggage entry (propagated across service boundaries)
Baggage.SetBaggage("user.name", "otel-demo-user");
Baggage.SetBaggage("environment", "development");

var stopwatch = Stopwatch.StartNew();

// Start a trace activity with a tag
using (var activity = activitySource.StartActivity("SayHello"))
{
    activity?.SetTag("greeting.language", "en");
    activity?.SetTag("greeting.message", "Hello, World!");

    Console.WriteLine($"Baggage[user.name]  = {Baggage.GetBaggage("user.name")}");
    Console.WriteLine($"Baggage[environment] = {Baggage.GetBaggage("environment")}");

    // Simulate some work
    await Task.Delay(50);

    activity?.SetStatus(ActivityStatusCode.Ok);
}

stopwatch.Stop();

// Record metrics
greetingCounter.Add(1, new TagList { { "greeting.language", "en" } });
greetingDuration.Record(stopwatch.Elapsed.TotalMilliseconds, new TagList { { "greeting.language", "en" } });

Console.WriteLine("OpenTelemetry data exported to console above.");

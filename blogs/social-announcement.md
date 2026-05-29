# Introducing Sidemark - Active Telemetry Comments for C\#

OpenTelemetry is table stakes now - but instrumenting real code buries its intent under a wall of `SetTag` / `AddEvent` / `StartActivity` bookkeeping.

So I built **Sidemark**: telemetry that rides along in your comments. Annotate a line with `//?` and it becomes the equivalent `Activity` call at build time - the code you read stays the code that does the work.

```csharp
var orderId = order.Id; //?   →  SetTag("orderId", orderId)
```

It's a Roslyn rewriter wired into MSBuild (your source files are never touched; the IL is identical to hand-written). One NuGet package ships the attributes, an analyzer, and the build task - no runtime dependencies added to your app.

It's a bit of a heresy to make comments load-bearing, but sometimes we need to rethink "normal form" to find something better.

Code: https://github.com/davidwhitney/Sidemark · NuGet: https://www.nuget.org/packages/Sidemark

# Sidemark

OpenTelemetry instrumentation — `ActivitySource.StartActivity`, `activity?.SetTag`, `activity?.AddEvent`, `activity?.SetStatus(ActivityStatusCode.Error, ex.Message)` — has a habit of taking over a file. A method that does one obvious thing turns into a wall of bookkeeping, and in review you spend half your time separating *what the code does* from *what we report about what the code does*. Intent gets buried in instrumentation; the lines that matter and the lines that observe them sit at the same visual weight, and the file reads heavier than it is.

Sidemark is an experiment in moving that bookkeeping into a layer the language already gives you and mostly ignores: comments. A small set of comment syntaxes (`//?`, `//!`, `//?!`) become **ride-along annotations** — information that travels next to the code, gets read at build time, and turns into the equivalent `Activity` calls in the compiled output. The code you read stays the code that does the work. The telemetry rides along, no longer competing with logic for your attention.

The framing is loosely inspired by Wallaby.js's *Live Annotations* — that feature treats comments as a surface for runtime debugging information, projecting variable values inline next to the code that produces them. Sidemark takes the same instinct in the other direction: comments as a *write* surface for instrumentation rather than a *read* surface for debug values. The shared idea is that comments are an under-used channel for information *about* code that isn't itself code, and that surfacing it there keeps the underlying logic legible.

In practice: drop a `//?` next to a variable to tag the current span with it. Drop a `//!` next to a statement to fire an event. Add a `//?` to a method signature when the method should *be* a span, or to a `catch` clause when failure should be recorded. Everything else is sugar on top.

### Tags

```csharp
// before
var orderId = order.Id;
var totalCents = order.Total * 100;
Activity.Current?.SetTag("orderId", orderId);
Activity.Current?.SetTag("order.total_cents", totalCents);

// after
var orderId    = order.Id;             //?
var totalCents = order.Total * 100;    //? order.total_cents
```

### Events

```csharp
// before
Activity.Current?.AddEvent(new ActivityEvent("ApiCalled"));
await CallExternalApi();

// after
await CallExternalApi(); //! ApiCalled
```

### Activities (extended)

```csharp
// before
public async Task<Order> Checkout(Cart cart)
{
    using var activity = MyConfig.ActivitySource.StartActivity("Checkout");
    // ...
}

// after
public async Task<Order> Checkout(Cart cart) //?
{
    // ...
}
```

### Exception handling (extended)

```csharp
// before
catch (Exception ex)
{
    Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
    throw;
}

// after
catch (Exception ex) //?
{
    throw;
}
```

It all happens at build time via a Roslyn rewriter wired into MSBuild. The compiler sees the rewritten source; runtime IL is identical to what you would have written by hand.

---

## Installation

Two packages, both targeting any .NET project that ships C#:

```bash
dotnet add package Sidemark
dotnet add package Sidemark.Analyzer
```

`Sidemark` brings in the attributes, the MSBuild task that runs before `CoreCompile`, and a tiny build-time Roslyn dependency. `Sidemark.Analyzer` adds IDE diagnostics that flag misused markers.

---

## Setup

### 1. Add a configuration class and assembly attribute

```csharp
using System.Diagnostics;
using Sidemark;

[assembly: Sidemark(typeof(OTelConfig))]

public static class OTelConfig
{
    public static readonly ActivitySource ActivitySource = new("MyApp", "1.0.0");
}
```

The configuration class needs to expose a `static` field or property named **exactly** `ActivitySource`. The rewriter generates calls against `OTelConfig.ActivitySource.StartActivity(...)` based on the type you point at — the `ActivitySource` member name is by convention.

### 2. Hook the source up to your tracer provider as you would normally

```csharp
using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource("MyApp")
    .AddConsoleExporter()
    .Build();
```

### 3. Annotate methods

That's it. Your `//?` and `//!` comments take effect on the next build.

---

## Syntax

Sidemark understands two markers, each of which behaves slightly differently depending on where it's attached.

### Summary

| Where | Marker | Effect | Payload |
| --- | --- | --- | --- |
| Method / local-function signature | `//?` | Wraps the body in `using var __sidemarkScope = source.StartActivity(name)` (a child of `Activity.Current`). | Activity name. Defaults to the method name. |
| Method / local-function signature | `//!` | `Activity.Current?.AddEvent(...)` at body entry. | Event name. Defaults to the method name. |
| Method / local-function signature | `//?!` | Both: creates a new activity *and* emits an entry event inside it. | Event name. Activity name is always the method name. Defaults to the method name. |
| Local variable declaration | `//?` | `Activity.Current?.SetTag(key, variable)` after the declaration. | Tag key. Defaults to the variable name. |
| Statement (leading or trailing trivia) | `//!` | `Activity.Current?.AddEvent(...)` immediately before the statement. | Event name. Required. |
| `catch` clause | `//?` | `Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message)` at catch entry (or without `.Message` if no exception variable is declared). | None. |

A method or local function with no signature `//?` is **not** wrapped in a new activity — body directives still expand, but they target whatever `Activity.Current` is at the call site. If there is no ambient activity, the chained calls are no-ops.

### `//?` — annotation

| Position | Effect | Payload |
| --- | --- | --- |
| Method signature | Wraps the body in `using var __sidemarkScope = source.StartActivity(name)` — a new child of `Activity.Current`. | Activity name. Defaults to the method name. |
| Local variable declaration | Emits `Activity.Current?.SetTag(key, variable)` immediately after the declaration. | Tag key. Defaults to the variable name. |
| `catch` clause | Emits `Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message)` at the top of the catch block. | None — the exception variable name is read from the catch declaration. |

```csharp
public void Process() //?                  // activity named "Process"
public void Process() //? order.processed  // activity named "order.processed"

var customerId = LookupCustomer(); //?               // tag "customerId"
var spend      = orderTotal;       //? customer.ltv  // tag "customer.ltv"

try { /* ... */ }
catch (Exception ex) //?                              // SetStatus(Error, ex.Message) at catch entry
{
    throw;
}
```

If the catch declaration has no variable (`catch (Exception)` or just `catch`), the rewriter emits `SetStatus(Error)` without a message argument.

### `//!` — event

| Position | Effect | Payload |
| --- | --- | --- |
| Method signature | Emits `Activity.Current?.AddEvent(...)` at the top of the method body. | Event name. Defaults to the method name. |
| Statement (leading or trailing trivia) | Emits `Activity.Current?.AddEvent(...)` immediately before the statement. | Event name. Required. |

```csharp
public void OnRequest() //! RequestReceived  // event added at body entry

await CallApi();    //! ApiCalled            // event before the await
//! BeforeWork
DoExpensiveThing();                          // event before DoExpensiveThing()
```

### `//?!` — compound (activity + entry event)

For the common case of "create an activity *and* emit an event when this method starts", `//?!` is shorthand for both:

```csharp
public void Handle() //?! Started
{
    // creates an activity called "Handle" and adds an event "Started" at body entry
}
```

The payload is the **event name**. The activity always uses the method name. If the payload is omitted, the event name also falls back to the method name:

```csharp
public void Handle() //?!
{
    // activity "Handle" + event "Handle"
}
```

If you need both names to be different, fall back to the two-marker form:

```csharp
public void Handle() //? OrderHandled
                     //! Started
{
    // activity "OrderHandled" + event "Started"
}
```

### What happens to a method with no signature `//?`

A method without `//?` on its signature is **not** wrapped in a new activity — it just chains onto whatever `Activity.Current` is when it runs. Body directives still fire; they target the ambient activity.

```csharp
public void EnrichOrder()           // no //? - no new span
{
    var orderId = order.Id; //?     // tag added to the caller's ambient span
}
```

If there is no ambient activity (no parent has called `StartActivity`), the `Activity.Current?.SetTag(...)` calls are simply no-ops. This makes "tag-only" helper methods composable without proliferating tiny spans.

---

## Configuration model

The assembly attribute points at a configuration class. The class can also opt into custom marker patterns by declaring string-literal members of the right names. All of them are optional.

```csharp
[assembly: Sidemark(typeof(OTelConfig))]

public static class OTelConfig
{
    public static readonly ActivitySource ActivitySource = new("MyApp", "1.0.0");

    // All four of these are optional. Defaults shown.
    public const string ActivityPattern      = "//?";
    public const string TagPattern           = "//?";
    public const string EventPattern         = "//!";
    public const string ActivityEventPattern = "//?!";
}
```

The pattern members are read **syntactically at build time** — they need to be string literal `const`, `static readonly`, or property initializers. If a pattern member isn't declared, the default is used.

This is useful if `//?` or `//!` clash with an existing convention in your codebase, or if you'd like the markers to be more self-explanatory:

```csharp
public static class OTelConfig
{
    public static readonly ActivitySource ActivitySource = new("MyApp", "1.0.0");

    public const string ActivityPattern      = "//span";
    public const string TagPattern           = "//tag";
    public const string EventPattern         = "//evt";
    public const string ActivityEventPattern = "//span!";
}

// then:
public async Task Checkout() //span
{
    var orderId = order.Id;   //tag
    await Process(order);     //evt OrderProcessed
}
```

`ActivityPattern` and `TagPattern` may share a value (the default does) — context decides which one is meant. `ActivityEventPattern` is matched eagerly: when a piece of trivia matches the compound pattern, the rewriter does **not** also try to interpret it as a plain `ActivityPattern` or `EventPattern`, even if those would prefix-match too.

### Per-class / per-method `ActivitySource` overrides

If a particular class or method should report against a different `ActivitySource` than the assembly default, attach an `SidemarkActivitySource` attribute:

```csharp
[SidemarkActivitySource(typeof(BillingTelemetry), nameof(BillingTelemetry.ActivitySource))]
public class BillingService
{
    public void RefundOrder() //?  // creates an activity on BillingTelemetry.ActivitySource
    {
        ...
    }
}
```

The lookup order for the source is: method-level attribute, then class-level, then assembly-level `Sidemark` config, then a final fallback that the MSBuild task can override via the `$(SidemarkActivitySource)` MSBuild property.

---

## Disabling the rewriter

There are two ways to turn it off without ripping out the package reference:

```csharp
// In any C# file in the project:
[assembly: DisableSidemark]
```

```xml
<!-- Or in the .csproj: -->
<PropertyGroup>
    <SidemarkDisable>true</SidemarkDisable>
</PropertyGroup>
```

When disabled, the rewriter is a no-op and your `//?` / `//!` comments are passed through to the compiler unchanged (they're just plain comments at that point).

---

## Analyzer

`Sidemark.Analyzer` adds diagnostics so misused markers light up in your IDE and your CI logs:

| ID | When it fires |
| --- | --- |
| `SDM001` | `//?` is attached to a statement that is **not** a local variable declaration — there's nothing for `SetTag` to bind to. |
| `SDM002` | `//!` on a body statement is missing a payload — events on statements need an explicit name. (Empty `//!` on a method signature is fine; it falls back to the method name.) |
| `SDM003` | A directive sits on a member the rewriter does not process: an expression-bodied method, a constructor, an accessor, an operator. The directive is silently ignored at build time. |
| `SDM004` | `//?!` (the compound marker) is used outside a method or local-function signature. It does nothing in any other position. |
| `SDM005` | Two `//?` directives in the same method resolve to the same tag key. `Activity.SetTag(key, …)` is last-write-wins, so the earlier value is dropped. |
| `SDM006` | A `catch (Exception ex) //?` carries a payload after `//?`. The catch annotation doesn't take one — the payload is silently discarded. |

All rules are warnings, so they don't fail the build by default. Suppress per-occurrence with `#pragma warning disable SDM005` (or whichever rule) if you have a reason to keep something the analyzer doesn't like.

---

## How it works

Sidemark runs as an MSBuild task that hooks into `BeforeTargets="CoreCompile"`. For each `@(Compile)` item it parses with Roslyn, walks the syntax tree, and writes a rewritten copy to `obj/$(Configuration)/$(TargetFramework)/sidemark/`. The `@(Compile)` list is then swapped to point at the rewritten copies. Your source files on disk are never touched.

A few practical consequences:

- **The compiler sees only the rewritten files.** Stack traces and debugger line numbers point at the rewritten code in `obj/`, not at your annotated source. If this matters for your debugging flow, the rewritten files are human-readable and live next to the rest of the build artefacts.
- **Comments are stripped from IL anyway.** The whole point of doing this in MSBuild rather than at runtime is that the comments need to be visible to Roslyn — they're trivia in the syntax tree. Once compiled, the IL is identical to what you'd have written by hand.
- **The analyzer runs against your *original* source in your IDE** (because the rewriter hasn't fired yet), and against the rewritten source during `dotnet build`. The directive comments are deliberately preserved in the rewritten output so the analyzer fires consistently in both surfaces.

---

## A worked example

```csharp
using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Sidemark;

[assembly: Sidemark(typeof(OTelConfig))]

using var tracerProvider = Sdk.CreateTracerProviderBuilder()
    .AddSource("HelloWorldOTel")
    .AddConsoleExporter()
    .Build();

var greeter = new Greeter();
await greeter.GreetAsync("World");

public static class OTelConfig
{
    public static readonly ActivitySource ActivitySource = new("HelloWorldOTel", "1.0.0");
}

public class Greeter
{
    public async Task GreetAsync(string name) //? Greet
    {
        var greeting       = $"Hello, {name}!"; //?
        var greetingLength = greeting.Length;   //? greeting.length

        await EmitAsync(greeting); //! AboutToWrite
    }

    private async Task EmitAsync(string line)
    {
        var emittedAt = DateTime.UtcNow; //? emitted.at  // tag added to the parent Greet span

        await Task.Delay(25);
        Console.WriteLine(line);
    }
}
```

Running this produces a single `Greet` span per call, with `greeting`, `greeting.length`, and `emitted.at` tags, plus an `AboutToWrite` event — the equivalent of about thirty lines of explicit OTel code, written in five.

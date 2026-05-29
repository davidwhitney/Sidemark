# Sidemark: Active Telemetry Comments for C#

OpenTelemetry has quietly become table stakes. That's a good thing, but if you've instrumented a real codebase, you know the tax. A method that does one obvious thing slowly fills up with `StartActivity`, `SetTag`, `AddEvent`, `SetStatus`. The *bookkeeping* of telemetry starts to drown out the *intent* of the code, and in review you spend half your time mentally separating "what this code does" from "what we report about what it does."

It's easy to think "oh, but the framework takes care of this with auto-instrumentation", but if you talk to the experts in OTel, they'll go to great lengths to explain that auto-instrumentation is a floor, not a ceiling. Most of the value in telemetry comes from the custom instrumentation you add to your code that adds business context to your traces. And that custom instrumentation is the stuff that clutters up your code.

Here's the kind of thing I mean:

```csharp
// before - ugly, obtuse, who put that there
var orderId = order.Id;
Activity.Current?.SetTag("orderId", orderId);
```

Sidemark is my answer to that code-obfuscation problem: **non-invasive instrumentation** via what I'm calling **Active Comments**.

```csharp
// after - glorious, beautiful, basking in the light of the sun, closer to god, happy, satiated
var orderId = order.Id; //?
```

## The idea

A small set of comment syntaxes - `//?`, `//!`, `//?!` - become **ride-along annotations**. They travel next to the code, get read at build time, and turn into the equivalent `Activity` calls in the compiled output. The code you read stays the code that does the work. The telemetry rides along instead of competing with your logic for attention.

The framing is loosely inspired by Wallaby.js's *Live Annotations*, which project runtime values inline next to the code that produced them. Sidemark takes the same instinct in the other direction: comments as a *write* surface for instrumentation, rather than a *read* surface for debug values. Comments are an under-used channel for information *about* code that isn't itself code - and surfacing it there keeps the underlying logic legible.

Yes, this means making comments **load-bearing**, which is a bit of a heresy. Comments have a reputation for bit-rot and lies. But used this way they move back toward their original purpose: a place for the things a programmer needs to understand the code - reimagined for an era where a lot of that understanding happens while staring at production traces.

## What it looks like

**Tags** - drop `//?` on a local and it becomes a `SetTag`:

```csharp
var orderId    = order.Id;          //?
var totalCents = order.Total * 100; //? order.total_cents
```

**Events** - `//!` emits an `AddEvent`, before a statement or at method entry:

```csharp
await CallExternalApi(); //! ApiCalled
```

**Activities** - `//?` on a method signature wraps the body in a span:

```csharp
public async Task<Order> Checkout(Cart cart) //?
{
    // ...
}
```

**Exceptions** - `//?` on a `catch` records the error status:

```csharp
catch (Exception ex) //?
{
    throw;
}
```

A worked example with a couple of tags, a span and an event comes out to roughly thirty lines of hand-written OpenTelemetry - written in five.

## How it works

There's no runtime magic. Sidemark runs as an MSBuild task that hooks in just before `CoreCompile`. For each source file it parses the syntax tree with Roslyn, rewrites the annotated bits into the equivalent `Activity` calls, and writes the rewritten copy into `obj/`. The compiler only ever sees the rewritten files - **your source files on disk are never touched**, and the emitted IL is identical to what you'd have written by hand. Comments are trivia in the syntax tree, which is exactly why this has to happen at build time rather than at runtime.

A nice consequence: the markers are *just comments*. Turn Sidemark off with `[assembly: DisableSidemark]` or `<SidemarkDisable>true</SidemarkDisable>` and they pass straight through to the compiler as the plain comments they always were.

## What's in the box

It's a single NuGet package - `dotnet add package Sidemark` - that ships three things:

- **The runtime attributes** you use to point Sidemark at your `ActivitySource` (and to disable it).
- **A Roslyn analyzer** that flags misused markers (`SDM001`–`SDM006`) right in your IDE and your CI logs. It's wired in automatically by the package - nothing to switch on.
- **The MSBuild rewriter** that does the actual work at build time.

The package is deliberately lean: it borrows the Roslyn that already ships with your .NET SDK instead of bundling its own copy, so it adds no transitive NuGet dependencies to your app - just the marker attributes and some build-time tooling.

Requirements are modest: a **.NET SDK of 8.0.200 or newer** to build, and any target framework compatible with `netstandard2.0` - so modern .NET, .NET Framework 4.6.1+, Mono and Unity are all fine.

Setup is one assembly attribute pointing at a config class:

```csharp
[assembly: Sidemark(typeof(OTelConfig))]

public static class OTelConfig
{
    public static readonly ActivitySource ActivitySource = new("MyApp", "1.0.0");
}
```

…and then your `//?` and `//!` comments take effect on the next build.

## Is this a good idea?

If the idea makes you recoil slightly, I understand. Try it on a service or two and see whether your code reads lighter afterwards. That's the point - the lines that matter and the lines that observe them should not sit at the same visual weight, but appear as legitimate annotations scoped to individual lines of code. Comments are the most natural way to do that.

- **Source:** <https://github.com/davidwhitney/Sidemark>
- **NuGet:** <https://www.nuget.org/packages/Sidemark>

// Six intentionally-broken comments live below to demo the Sidemark analyzer.
// Open this file in an IDE: each misused //? or //! gets a squiggle.
// During `dotnet build` they appear as warnings SDM001-SDM006.

using System;

namespace HelloWorldOTel.Sidemark.Demo;

public class AnalyzerDemoBadUsage
{
    // SDM001 — //? must be attached to a local declaration, not a call
    public void TagOnNonDeclaration() //?
    {
        DoStuff(); //?
    }

    // SDM002 — //! event directive on a body statement needs an explicit name
    public void EmptyEvent() //?
    {
        DoStuff(); //!
    }

    // SDM003 — directives on members the rewriter doesn't process (expression-bodied here)
    public int Value() //?
        => 42;

    // SDM004 — //?! is only meaningful on a method/local-function signature
    public void CompoundOffSignature() //?
    {
        var x = 1; //?!
    }

    // SDM005 — duplicate tag key in the same method
    public void DuplicateTagKey() //?
    {
        var oldId = 1; //? order.id
        var newId = 2; //? order.id
    }

    // SDM006 — catch //? does not take a payload
    public void CatchPayloadIgnored() //?
    {
        try { DoStuff(); }
        catch (Exception ex) //? errored
        {
            throw;
        }
    }

    private void DoStuff() { }
}

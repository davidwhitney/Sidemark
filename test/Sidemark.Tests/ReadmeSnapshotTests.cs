namespace Sidemark.Tests;

public class ReadmeSnapshotTests : RewriterTestBase
{
    [Fact]
    public void RewritingTheReadmeSampleProducesTheExpectedSource()
    {
        var samplePath = Path.Combine(AppContext.BaseDirectory, "Samples", "MyService.input.cs");
        var input = File.ReadAllText(samplePath);

        var options = new SidemarkOptions
        {
            ActivitySourceExpression = "OTelConfig.MyActivitySource"
        };

        var output = Rewrite(input, options);

        const string expected = """
            using System.Diagnostics;

            public static class OTelConfig
            {
                public static readonly ActivitySource MyActivitySource = new("MyCompany.MyProduct.MyLibrary", "1.0.0");
            }

            public class MyService
            {
                public async Task DoWork()
                {
                    using var __sidemarkScope = OTelConfig.MyActivitySource.StartActivity("Optional Custom Activity Name");
                    var operationId = 123;
                    System.Diagnostics.Activity.Current?.SetTag("operationId", operationId);
                    var somethingWithANameOveride = 456;
                    System.Diagnostics.Activity.Current?.SetTag("friendly.Name", somethingWithANameOveride);

                    try
                    {
                        System.Diagnostics.Activity.Current?.AddEvent(new System.Diagnostics.ActivityEvent("Working"));
                        await Task.Delay(100);
                    }
                    catch (Exception ex)
                    {
                        Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
                        throw;
                    }
                }
            }
            """;

        AssertCSharpEquivalent(expected, output);
    }
}

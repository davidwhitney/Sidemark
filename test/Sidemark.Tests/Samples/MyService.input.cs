using System.Diagnostics;

public static class OTelConfig
{
    public static readonly ActivitySource MyActivitySource = new("MyCompany.MyProduct.MyLibrary", "1.0.0");
}

public class MyService
{
    public async Task DoWork() //? Optional Custom Activity Name
    {
        var operationId = 123; //?
        var somethingWithANameOveride = 456; //? friendly.Name

        try
        {
            await Task.Delay(100); //! Working
        }
        catch (Exception ex)
        {
            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}

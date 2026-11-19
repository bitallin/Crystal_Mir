namespace Crystal.Web.Gateway;

internal static class Program
{
    public static async Task Main(string[] args)
    {
        WebApplication app = GatewayApp.Build(args);
        await app.RunAsync();
    }
}

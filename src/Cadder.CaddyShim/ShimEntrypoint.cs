using Cadder.Contracts;

namespace Cadder.CaddyShim;

public static class ShimEntrypoint
{
    public static int Run(string[] args)
    {
        if (args.Contains("--cadder-shim-info", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"{CadderRoles.CaddyShimEntrypoint}: PATH-facing caddy.exe shim");
            return 0;
        }

        Console.Error.WriteLine("Cadder caddy.exe shim scaffold. Registration flow is implemented in TASK-1.3.");
        return 2;
    }
}

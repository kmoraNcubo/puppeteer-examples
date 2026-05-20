using System;
using Puppeteer;
using Welcome;

namespace HostInMemory;

internal static class Program
{
    private const string ActorName = "host";

    private static int Main()
    {
        var domainAssembly = typeof(WelcomeDomain).Assembly;

        // --- host: opens, runs the verb three times, then closes. ---
        Console.WriteLine("--- host: open ---");
        var host = new ActorV2(ActorName, domainAssembly);
        host.ConfigureStorage(DatabaseType.IN_MEMORY, "InMemory");

        // The 'upgrade' DSL primitive runs its body the first time the actor
        // sees this script, and is recognized as already-applied on every
        // subsequent rehydration. It is the idiomatic way to seed actor state
        // without the host having to ask "is this a fresh actor?".
        host.Using("upgrade('seed') { count = 0; g = Greeter(); }").PerformCommand();

        for (int i = 0; i < 3; i++)
        {
            string json = host.Using(@"
                {
                    msg = g.Greet('Puppeteer');
                    count = count + 1;
                    print msg greeting, count visits;
                }
            ").PerformCommand();
            Console.WriteLine(json);
        }

        Console.WriteLine("--- host: close ---");
        host.GracefulExit();

        // --- concierge: same name, brand-new reference, takes the next shift. ---
        // Re-runs the same seed script. Within a single process IN_MEMORY
        // storage keeps the 'seed' marker, so the upgrade body is recognized
        // as already-applied and 'count' is NOT reset to 0.
        Console.WriteLine("--- concierge: open with same name, re-apply seed, read state ---");
        var concierge = new ActorV2(ActorName, domainAssembly);
        concierge.ConfigureStorage(DatabaseType.IN_MEMORY, "InMemory");
        concierge.Using("upgrade('seed') { count = 0; g = Greeter(); }").PerformCommand();

        string view = concierge.Using(@"
            {
                msg = g.Greet('Puppeteer');
                print msg greeting, count visits;
            }
        ").PerformQuery();
        Console.WriteLine($"Concierge sees: {view}");
        concierge.GracefulExit();

        // Note (see README): the seed survives across the close/reopen above
        // because IN_MEMORY is keyed by actor name *inside the process*. Across
        // process exits the table is gone — re-run this program and the host
        // opens again at count = 0.
        return 0;
    }
}

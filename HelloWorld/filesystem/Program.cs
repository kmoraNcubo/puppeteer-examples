using System;
using System.IO;
using Puppeteer;
using Welcome;

namespace HostFileSystem;

internal static class Program
{
    private const string ActorName = "host";

    private static int Main()
    {
        string journalDir = Path.Combine(AppContext.BaseDirectory, "journal");
        Directory.CreateDirectory(journalDir);
        string connectionString = $"path={journalDir};maxFileSize=4194304";
        Console.WriteLine($"Journal directory: {journalDir}");

        var domainAssembly = typeof(WelcomeDomain).Assembly;

        // --- host: opens, runs the verb three times, then closes. ---
        // On first run the journal is empty; on every subsequent run the
        // framework rehydrates the actor by replaying the recorded scripts
        // before any new work begins.
        Console.WriteLine("--- host: open ---");
        var host = new ActorV2(ActorName, domainAssembly);
        host.ConfigureStorage(DatabaseType.FileSystem, connectionString);

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
        // Re-runs the same seed script. The 'seed' marker lives in the
        // journal on disk, so the upgrade body is recognized as
        // already-applied and 'count' is NOT reset to 0.
        Console.WriteLine("--- concierge: open with same name, re-apply seed, read state ---");
        var concierge = new ActorV2(ActorName, domainAssembly);
        concierge.ConfigureStorage(DatabaseType.FileSystem, connectionString);
        concierge.Using("upgrade('seed') { count = 0; g = Greeter(); }").PerformCommand();

        string view = concierge.Using(@"
            {
                msg = g.Greet('Puppeteer');
                print msg greeting, count visits;
            }
        ").PerformQuery();
        Console.WriteLine($"Concierge sees: {view}");
        concierge.GracefulExit();

        // Re-running this program is the visible contrast with the InMemory
        // variant: on the second run the journal on disk is replayed and the
        // host opens at the count the previous run left behind.
        return 0;
    }
}

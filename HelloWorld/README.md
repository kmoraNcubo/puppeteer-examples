# Hello World

A minimal Puppeteer V2 actor with one domain library and two programs that
configure the actor against different storage backends. The solution is
[`HelloWorld.sln`](HelloWorld.sln).

## Layout

```
HelloWorld/
├── HelloWorld.sln
├── domain/        WelcomeDomain library  (no Puppeteer reference)
├── inmemory/      HostInMemory program   (IN_MEMORY storage)
└── filesystem/    HostFileSystem program (FileSystem storage)
```

[`domain/`](domain/) holds the shared domain library. Its only public type
is `WelcomeDomain`, an empty anchor that lets a program pass the assembly
as `typeof(WelcomeDomain).Assembly`. The verb-bearing class — `Greeter`,
with one `internal` method `Greet(name)` — is itself `internal`: nothing
outside the domain assembly instantiates or calls it directly. The
framework discovers and invokes it through reflection.

| Program | Storage | What it shows |
|---|---|---|
| [`inmemory/`](inmemory/) | In-memory | State survives across actor references inside a process; lost on process exit. |
| [`filesystem/`](filesystem/) | File system (local path) | State is replayed from the journal on every process start; accumulates across runs. |

Each program is a single .NET host: an actor opens under the variable
`host`, runs the verb a few times, then closes. A second actor reference
under the variable `concierge`, with the same name, opens and re-applies
the same `upgrade('seed') { ... }` script — recognized as already-applied
— before querying the state. Run each program twice to see the contrast.

## What you should see

```
$ dotnet run --project HelloWorld/inmemory/HostInMemory.csproj
--- host: open ---
Starting Puppeteer.EventSourcing.ActorHandler's Actor
{"greeting":"Hello, Puppeteer!","visits":1}
{"greeting":"Hello, Puppeteer!","visits":2}
{"greeting":"Hello, Puppeteer!","visits":3}
--- host: close ---
--- concierge: open with same name, re-apply seed, read state ---
Starting Puppeteer.EventSourcing.ActorHandler's Actor
1%2%3%4%Concierge sees: {"greeting":"Hello, Puppeteer!","visits":3}
```

A second `dotnet run` of `HostInMemory` produces the same output — the
process boundary erased the in-memory journal, so the host opens again at
`count = 0`.

```
$ dotnet run --project HelloWorld/filesystem/HostFileSystem.csproj
Journal directory: .../HelloWorld/filesystem/bin/Debug/net9.0/journal
--- host: open ---
Starting Puppeteer.EventSourcing.ActorHandler's Actor
{"greeting":"Hello, Puppeteer!","visits":1}
{"greeting":"Hello, Puppeteer!","visits":2}
{"greeting":"Hello, Puppeteer!","visits":3}
--- host: close ---
--- concierge: open with same name, re-apply seed, read state ---
Starting Puppeteer.EventSourcing.ActorHandler's Actor
1%2%3%4%Concierge sees: {"greeting":"Hello, Puppeteer!","visits":3}
```

A second `dotnet run` of `HostFileSystem` rehydrates the actor from the
journal on disk before any new work happens. The host opens at `count =
3`, runs the verb three more times, and the concierge sees `count = 6`:

```
$ dotnet run --project HelloWorld/filesystem/HostFileSystem.csproj
Journal directory: .../HelloWorld/filesystem/bin/Debug/net9.0/journal
--- host: open ---
Starting Puppeteer.EventSourcing.ActorHandler's Actor
1%2%3%4%5%{"greeting":"Hello, Puppeteer!","visits":4}
{"greeting":"Hello, Puppeteer!","visits":5}
{"greeting":"Hello, Puppeteer!","visits":6}
--- host: close ---
--- concierge: open with same name, re-apply seed, read state ---
Starting Puppeteer.EventSourcing.ActorHandler's Actor
1%2%3%4%5%6%7%8%9%Concierge sees: {"greeting":"Hello, Puppeteer!","visits":6}
```

Lines like `1%2%3%4%` are the framework's journal-replay progress
markers; `Concierge sees:` is the program's own label around the query
result.

## Conceptual entry point

The design conditions these examples illustrate — clean domain, journal as
the persistent substrate, rehydration by script replay, `upgrade` as a
once-applied DSL primitive — are developed in the companion papers
repository
[`alvaroNCubo/puppeteer-papers`](https://github.com/alvaroNCubo/puppeteer-papers).
Paper 1 (*Anti-porosity*) is the entry point.

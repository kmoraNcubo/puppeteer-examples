# Host — FileSystem (local)

One actor, one verb, one piece of state (`count`). The journal is written
to disk and replayed on every restart.

## How to run

```
dotnet run --project HelloWorld/filesystem/HostFileSystem.csproj
```

Run it twice. The second run picks up where the first one left off
(`count = 3` becomes `4 → 5 → 6`).

## What happens

The program hands `typeof(WelcomeDomain).Assembly` to a new actor and
configures `FileSystem` storage at `bin/Debug/net9.0/journal/`. The DSL
`upgrade('seed') { count = 0; g = Greeter(); }` is the idiomatic seed: it
runs the first time the actor sees it and is recognized as already-applied
on every subsequent invocation — the program does not have to ask "is this
a fresh actor?".

On the first run the journal directory is empty. The actor opens under
the variable `host`, the seed runs and writes `count = 0`, the verb runs
three times (`count` goes 1 → 2 → 3), and `host` closes. A second actor
reference under the variable `concierge`, with the same name, opens and
re-applies the same seed script — the `seed` marker is in the journal on
disk, so the upgrade body is recognized as already-applied and `count` is
*not* reset to 0. `concierge` reads `count` and sees `3`.

On the second run the framework replays the journal before any new work
happens. `host` opens at `count = 3`, the verb runs three more times (4 →
5 → 6), `host` closes; `concierge` re-applies the seed (still
already-applied, body skipped) and sees `count = 6`.

Compare with the `inmemory/` variant, where the same code starts at
`count = 0` on every invocation of the process.

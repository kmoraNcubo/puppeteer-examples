# Host — InMemory

One actor, one verb, one piece of state (`count`). The journal lives in
memory only.

## How to run

```
dotnet run --project HelloWorld/inmemory/HostInMemory.csproj
```

Run it twice. Both invocations begin at `count = 0`.

## What happens

The program hands `typeof(WelcomeDomain).Assembly` to a new actor and
configures `IN_MEMORY` storage. The DSL `upgrade('seed') { count = 0; g =
Greeter(); }` is the idiomatic seed: it runs the first time the actor sees
it and is recognized as already-applied on every subsequent invocation —
the program does not have to ask "is this a fresh actor?".

The actor opens under the variable `host`, runs the verb three times
(`count` goes 1 → 2 → 3), then closes. A second actor reference under the
variable `concierge`, with the same name, opens and **re-applies the same
seed script**. Within a single process the `seed` marker survives the
close-and-reopen (`IN_MEMORY` storage is keyed by actor name in a
process-wide table), so the upgrade body is recognized as already-applied
and `count` is *not* reset to 0. `concierge` then reads `count` and sees
`3`.

The ephemerality of `IN_MEMORY` is at the **process** boundary: re-run
this program and the table is gone, the host opens at `count = 0`, and the
sequence repeats identically. To observe state surviving process exit, see
the `filesystem/` variant.

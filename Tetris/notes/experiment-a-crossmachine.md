# Experiment A — cross-machine staging (Increment C2)

**Claim closed:** Paper 9 ("Identity Precedes Staging") Experiment A previously
ran the StageManager "duo" **co-hosted in one process**. This increment runs the
same `Well` domain across **three separate processes in three Docker containers**,
joined over **real Kestrel TLS** on a compose bridge network. Three containers =
three machines (Paper 7 §5.2 fixes the peer count at THREE — the minimum at which
the no-privileged-node claim is unambiguous). The distributed staging is now real,
not written-around.

**The measurement is a ZERO, not an adjective.** The new capability is a new host
plus docker files; the domain and the actor are byte-for-byte unchanged:

```
$ git diff -- Tetris/domain     # (empty)
$ git diff -- Tetris/actor       # (empty)
```

Both diffs are empty. See [§4](#4-acceptance-test-the-measurements).

---

## 1. What C2 is (and how it differs from C1)

| | C1 (`sm-duo-tls`, shipped) | **C2 (`sm-cluster`, this increment)** |
|---|---|---|
| Nodes | 2 `StageV2` | 3 `StageV2` |
| Processes | **1** (both in-proc) | **3** (one per container) |
| Transport | Kestrel HTTPS on **loopback** ports | Kestrel HTTPS across the **compose bridge**, dialled by service name |
| Rendezvous | shared object reference (same process) | **out-of-band** invitation + fingerprint exchange (shared volume) |
| Machines | 1 | 3 containers |

`sm-duo-tls/Program.cs` named this step in its own header — *"de-risk the network
transport before C2 (Docker cross-machine)."* This is that C2.

The domain is a parameter, exactly as in every other Tetris host:

```csharp
StageFactory.Create<StageV2>(PerformerId.New(), session, typeof(TetrisDomain).Assembly)
```

The `Well`, `TetrisActor`, and the StageManager machinery are all unchanged; only
`Tetris/sm-cluster/` and `Tetris/docker/` are new.

## 2. Files added (purely additive)

```
Tetris/sm-cluster/Program.cs                 # TetrisStageCluster — 1 StageV2/process, env-driven
Tetris/sm-cluster/TetrisStageCluster.csproj  # references ..\actor (→ engine transitively)
Tetris/docker/Dockerfile                     # aspnet:9.0, COPY build/host, EXPOSE 5443
Tetris/docker/docker-compose.yml             # tetris-a/b/c on tetris-net; 5443 internal
Tetris/docker/run-demo.sh                    # publish → compose up → wait for convergence
Tetris/docker/.gitignore                     # ignores build/ (published artifacts)
Tetris/notes/experiment-a-crossmachine.md    # this note
Tetris/notes/experiment-a-crossmachine.log   # full captured run log (appendix)
Tetris/Tetris.sln                            # + the sm-cluster project registration (additive)
```

## 3. The rendezvous — verified against the framework, not fabricated

The one piece that genuinely differs from the in-proc duo is **cross-container
rendezvous**: the in-proc duo hands `b` the invitation object `a` created by shared
reference; across containers that reference cannot cross. I read the framework
before wiring anything:

- **`ConnectionInvitation`** (`Choreography/Transport/ConnectionInvitation.cs`) is a
  plain serialisable value: `(PerformerId InviterId, ChannelPurpose Purpose, string
  Address)`.
- **`HttpsTransport.CreateInvitationAsync`** builds the address as
  `"{advertiseUrl}|{localId}|{purpose}|{guid}"` — the **advertise URL is embedded**,
  so an accepter in another container dials the right service name.
- **`AcceptInvitationAsync`** POSTs to `{remoteAdvertiseUrl}/connect` and sends back
  its **own** advertise URL, so channels are bidirectional. Both endpoints therefore
  dial each other → **fingerprint pinning must be symmetric**
  (`Stage.TrustPeerHttpsFingerprint`, keyed by advertise URL).
- The transport has **no discovery server**. So the invitation `Address`, the
  inviter's `PerformerId`, and each node's self-signed TLS fingerprint must cross
  **out of band**.

**Chosen mechanism — Paper 7's shared-volume bootstrap** (the proven 3-Docker
harness `Puppeteer-Pacifico-paper7/docker/`, which carries exactly these values over
a shared `/bootstrap` volume — its analog to the out-of-band Usher/QR hop). We reuse
that pattern. We do **not** need Paper 7's Usher: the Usher only *assigns an
identity*, and a Stage's identity is just its `PerformerId`, so `PerformerId.New()`
per process suffices — exactly as `sm-duo` / `sm-duo-tls` already do.

> **What actually crosses the network vs. the volume.** The peer traffic that
> *moves the Well* — coordination (`DirectorAnnounce`/heartbeats), replication
> (`CueEvent`/`CueAck`), and command forwarding — all crosses **container-to-container
> over real TLS** on port 5443 (never exposed to the host). Only the *initial
> rendezvous bootstrap* (TLS fingerprints + invitation addresses) uses the shared
> volume. This is the same honesty Paper 7 applies to its Usher hop.

**Topology** — a fixed Director star (rotation is Paper 7's concern, deliberately
out of scope here): `tetris-a` promotes and plays; `tetris-b`/`-c` are casts that
replicate the Well live over TLS and each render their own frame.

## 4. Acceptance test — the measurements

### 4a. The `Well` plays across 3 containers over TLS; peers converge

All three nodes reach the **same journal entry (13) with byte-identical Well state**:

```
tetris-a  | [tetris-a] convergence checkpoint reached: role=DIRECTOR entry=13 snapshot=type=- cleared=0 awaiting=True over=False cells=8
tetris-b  | [tetris-b] convergence checkpoint reached: role=cast     entry=13 snapshot=type=- cleared=0 awaiting=True over=False cells=8
tetris-c  | [tetris-c] convergence checkpoint reached: role=cast     entry=13 snapshot=type=- cleared=0 awaiting=True over=False cells=8
```

The cross-container TLS handshake (director view; heartbeat noise removed):

```
tetris-a | [tetris] Stage started; local TLS fingerprint cf39405438508d77…
tetris-a | [tetris] pinned peer https://tetris-b:5443/ → fp adea71497ecb9f36…
tetris-a | [tetris] pinned peer https://tetris-c:5443/ → fp 488c4cd2725c5305…
tetris-a | [tetris] coordination up with tetris-b
tetris-a | [tetris] coordination up with tetris-c
tetris-a | [tetris] promoted to Director (IsDirector=True) over real TLS
tetris-a | [tetris] data star up with tetris-b (replication+command)
tetris-a | [tetris] data star up with tetris-c (replication+command)
tetris-a | [tetris] scripted sequence done; final journal entry = 13
tetris-a | [tetris] catch-up sent to 2 cast(s) up to entry 13
```

Cast `tetris-c` (self-signed cert distinct per container; pins both peers;
replicates over TLS):

```
tetris-c | [tetris] Stage started; local TLS fingerprint 488c4cd2725c5305…
tetris-c | [tetris] pinned peer https://tetris-a:5443/ → fp cf39405438508d77…
tetris-c | [tetris] coordination up with director
tetris-c | [Stage …] DirectorAnnounce from 593a207917ff4dd7… (peerMax=0)
tetris-c | [tetris] data star up with director (replication+command); director announced.
tetris-c | [tetris] caught up to entry 13 (target 13)
tetris-c | [tetris]   cast sees: type=- cleared=0 awaiting=True over=False cells=8   <- REPLICATED over TLS
```

**Each node writes its frame** (per-node `/data` volume). The rendered board grid is
**byte-identical across all three** (only the one-line header, which names the node,
differs):

```
$ for f in a b c; do docker compose exec -T tetris-$f cat /data/tetris-$f.frame \
      | tail -n +3 | md5sum; done
49ba6f3c0a1b9322f82c4d54ce00cd9e  -   # tetris-a grid
49ba6f3c0a1b9322f82c4d54ce00cd9e  -   # tetris-b grid
49ba6f3c0a1b9322f82c4d54ce00cd9e  -   # tetris-c grid
```

The converged board (the two dropped pieces, 8 cells):

```
tetris-c (cast) entry=13
Lines cleared: 0

|                    |
   … (empty rows) …
|      []            |
|      [][][][]      |
|        [][][]      |
+====================+
```

Full run log: [`experiment-a-crossmachine.log`](experiment-a-crossmachine.log).

### 4b. Purely additive — domain and actor unchanged

Base commit: **`485b766`** (`Tetris: point engine reference at Pacifico master`).

```
$ git diff -- Tetris/domain      →  (empty)   exit 0
$ git diff -- Tetris/actor        →  (empty)   exit 0
$ git status --short
  ?? Tetris/docker/
  ?? Tetris/sm-cluster/
  (Tetris/Tetris.sln: additive — new project registration only)
$ git diff --stat -- Tetris/sm-duo Tetris/sm-duo-tls Tetris/sm-server \
                     Tetris/console Tetris/web Tetris/web-rest
  (empty — existing hosts untouched)
```

Confirmed again across the whole additive commit range **`485b766..HEAD`** (the
single commit that is this increment; base `485b766` is the prior tip):

```
$ git diff 485b766..HEAD -- Tetris/domain   →  (empty)   exit 0
$ git diff 485b766..HEAD -- Tetris/actor      →  (empty)   exit 0
$ git diff 485b766..HEAD --stat
  Tetris/Tetris.sln                           |  19 +-   (project registration; the 1 "deletion" is a UTF-8 BOM)
  Tetris/docker/.gitattributes                |   3 +
  Tetris/docker/.gitignore                    |   2 +
  Tetris/docker/Dockerfile                    |  30 ++
  Tetris/docker/docker-compose.yml            |  87 ++
  Tetris/docker/run-demo.sh                   | 103 ++
  Tetris/notes/experiment-a-crossmachine.log  | 127 ++
  Tetris/notes/experiment-a-crossmachine.md   | 249 ++
  Tetris/sm-cluster/Program.cs                | 439 ++
  Tetris/sm-cluster/TetrisStageCluster.csproj |  19 ++
  10 files changed, 1077 insertions(+), 1 deletion(-)
```

Every changed path is new (a host, docker files, notes) or the additive solution
registration — no domain, no actor, no existing host. (Commit is LOCAL to the
worktree; not pushed.)

## 5. How to reproduce

From `Tetris/` (Docker Desktop running, .NET SDK on PATH):

```bash
docker/run-demo.sh          # publish → build image → up 3 containers → wait for convergence
docker/run-demo.sh --down   # tear down (docker compose down -v)
```

`run-demo.sh` publishes `sm-cluster` to `docker/build/host` (framework-dependent;
the engine lives on the host by project path and is not compiled inside Docker),
then `docker compose up --build`, then waits for three `convergence checkpoint
reached` lines.

## 6. Honest caveats — what is and is not reached

1. **Frame *push sink* is currently inert for every Stage host (pre-existing drift,
   not a C2 regression).** The engine's reaction matcher on current master observes
   *ActorV2 Actions (Define+Invocation)*; `TetrisActor` issues *literal-script*
   commands, so `[Reaction 'Frame_*'] skipped a literal ScriptEvent`. The
   `FrameFileSink` push channel therefore never fires — **this also happens in the
   shipped `sm-duo-tls`** (verified: it writes no `.frame` files either; its
   "REPLICATED over TLS" is likewise shown via `Snapshot()`). Because the actor and
   engine are off-limits, this host wires the sink identically **and** additionally
   renders each node's frame **directly from the `WellSnapshot` it holds** (reusing
   the existing `BoardRenderer`). So "each writes its frame" holds — via the host we
   own, from replicated state, independent of the drifted push path. Fixing the push
   path proper would require migrating `TetrisActor` to parametrized V2 commands
   (an actor change, out of scope).

2. **Rendezvous bootstrap uses a shared Docker volume**, not the network. This is
   deliberate and mirrors Paper 7. The *peer data plane* (coordination, replication,
   command forwarding — everything that moves the Well) is genuine
   container-to-container TLS; only the initial fingerprint + invitation exchange is
   out-of-band over the volume. A fully-networked bootstrap (Paper 7's Usher on
   :6443) was **not** wired — it adds an identity-assignment authority C2 does not
   need. See [§3](#3-the-rendezvous--verified-against-the-framework-not-fabricated).

3. **Live replication has a connect-readiness race the framework does not
   auto-recover from.** `ListenReplication` drops out-of-order `CueEvent`s and never
   requests catch-up, so a cast that misses one live entry (entry 1 in an early run,
   here) stalls forever. The fix is the framework's own `SendCatchUpAsync`, which
   the Director issues to each cast after play (paced 10 ms/entry → in order). This
   is the framework-idiomatic repair, not a workaround bolted outside it — but it is
   worth recording that gap-free live delivery is **not** guaranteed by the
   handshake alone.

4. **Fixed Director; no rotation.** `tetris-a` is the Director for the whole run.
   Director rotation across peers is Paper 7's F1/F2 territory and is intentionally
   out of scope for the C2 "distributed staging exists" claim.

5. **TLS trust is TOFU (unpinned CA), fingerprints exchanged over the volume.** Each
   container generates a fresh self-signed cert per start (note the fingerprints
   differ run-to-run); peers pin by SHA-256 fingerprint. Production pinning against a
   real CA is not exercised (the same posture as `sm-duo-tls`).

6. **Engine-reference junction (local build-env only).** This work was done in a git
   worktree nested under `Tetris/.claude/worktrees/`, where the actor's relative
   engine reference (`..\..\..\Puppeteer Pacifico`) does not resolve. A directory
   **junction** outside the repo tree (`.claude/worktrees/Puppeteer Pacifico` →
   `C:\Users\alvar\source\repos\Puppeteer Pacifico`) bridges it. This is a local
   environment fix, invisible to git; from the main checkout the relative path
   resolves natively and no junction is needed.

7. **Commits are LOCAL to the worktree; nothing is pushed** (publication pass
   deferred, per the task).

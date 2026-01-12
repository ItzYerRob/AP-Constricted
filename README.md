# AP-Constricted
A "Very Spooky" (TM) game where the player(s) have to out-maneuver an enemy AI, working their way upwards until they can finally escape the building. The player(s) will unlock powers that let them manipulate objects, which they can use to confuse, misdirect, or slow down the enemy.

## How to Use
Starting the game, you will be immediately placed on the Lobby, where one can begin playing alone or with multiple players (Noting that internet connection is currently always required). The Lobby presents the following buttons:
- Host: Clicking it will create a session and share the displayed join code.
- Join: After pressing Host, a code will be provided on the adjacent text field. This code can be shared to other players, who can press Join to join the hosts's session.
- Copy: Helpful button that just copies the code given to the session, for easier sharing of it
- Play: After a session is created, the Host can click this button to load the game scene for all clients.
- Singleplayer: Not properly implemented yet :(
  
After starting the actual game scene, the player(s) will be loaded onto the first floor, where they can control the player as so:
- WASD: Controls movement
- Spacebar: jumps
- E: Interact (When something is interactible)
- Right Mouse Button: Manipulation power to pick up and throw objects (Once unlocked)

#Usage Hints:
- Note that there's actually currently no gameover during this build for ease of testing (Health can go into the negatives), so feel free to hug the enemy.
- Open the door to the right of spawn and pick up the crystal to unlock power
- Break through the barricades to the left of spawn, and go left again, to go up the stairs to the next floor.
- A key chain can be randomly found among shelves throughout the floors. Picking up this key chain lets one enter the car at the last floor, ending the game.

# Core Systems
Here is a resumed explanation of this project's most complex features (Namedly the AI and Networking).

## Enemy AI State Machine
- Key files: EnemyAI.cs, AIPatrolState.cs, AIPursueTargetState.cs, AIInvestigateNoiseState.cs, AIStunState.cs, EnemyHearing.cs, EnemyNavmeshMotor.cs.
- States
  - Patrol: Follows navmesh patrol points, clears targets, checks PlayerTarget.AllPlayers via `CanAggroTarget()`.
  - Pursue: Chases current target, continuously refreshes motor target, `ShouldDeaggro()` uses distance/FOV/LOS with a grace timer before falling back to patrol.
  - InvestigateNoise: Moves toward `noisePosition`, times out after `investigateDuration`, or clears once within `investigateReachRadius`, any valid sighting of a player jumps to Pursue.
  - Stun: Locks movement and applies a one-time knockback, if a target was present when stunned it tries to resume chase, otherwise it emits a high-bias investigation cue along the stun direction to probe the source.
- Detection/aggro: View-cone gating (`useViewCone`), and gives up after `lostConfirmSeconds` of no contact, chooses the closest detected valid player transform, `HasLineOfSightTo` raycasts from eye position.
- Noise arbitration: `NotifyHeardNoise(pos, suspicion, bias)` keeps only the highest `suspicion * bias` score, accepted noises update `noisePosition`/`noiseScore` and trigger Investigate when patrolling. All noises are ignored during pursuit.

## Noise & Suspicion System
- `NoiseSystem`: Singleton that handles emitting the eventss to listeners. Server emitters call `EmitNoise(NoiseEvent)` to alert registered `INoiseListener`s (Currently just the Enemy).
- `NoiseOnImpact`: Runs on server: derives loudness and radius from collision speed (min/max clamps + cooldown) and emits a noise event tagged with the emitting source NetworkObject.
- `EnemyHearing`: Listens on server: scales hearing by `hearingMultiplier` and `maxHearingRadius`, computes suspicion as `loudness * (1 - dist/effectiveRadius)`, rejects below `minSuspicionToInvestigate`, and ignores noise while already pursuing.
- Investigation lifecycle: EnemyAI caches `hasNoiseToInvestigate`, `noisePosition`, `noiseSuspicion`, and a weighted `noiseScore`, InvestigateState walks to the cached point, cancels after `investigateDuration` or when within `investigateReachRadius`. StunState can inject a biased investigate hint via `stunDirectionBias`.

## Multiplayer / Networking
- Services bootstrap: `MultiplayerBootstrap` auto-initializes Unity Services (on the Production env) and signs in anonymously, `UgsReady.EnsureAsync()` prevents multiple init calls, avoiding shennanigans.
- Lobby/session flow (`LobbyMenuController`): UI buttons create or join UGS Multiplayer sessions with Relay networking (`SessionOptions.WithRelayNetwork()`), join codes are copied/displayed, state guards prevent overlapping operations, callbacks are carried onto the main thread via a queue, host-only Play triggers Netcode scene load to the actual game scene.
- Runtime glue: `NetworkRuntime` keeps the NetworkManager alive across scenes and logs transport failures, `PlayerTarget` maintains a static list of player transforms for AI sensing.

Spawning & authority:
- `PerPlayerSpawner`: Runs on server to spawn an extra prefab per connected client with ownership.
- `AuthoritativeNetworkRB`: Replicates rigidbody state from the current owner via NetworkVariable and interpolates followers, with an RPC path to request temporary authority.
- `NetGrabbableRB`: Hands off ownership on grab requests and validates release position/velocity before returning authority to server. 
- `RBManipulator`: (client-side) handles aiming, grab requests, local spring-hold, and throw force.
- `ServerPushProxy`: Is a server-only kinematic capsule mirroring the player to apply push forces to other physics bodies, but is currently to-be-deprecated (Since logic is transitioning over to Authoritative RBs).

## Known Issues
-Singleplayer isn't implemented yet and the Singleplayer button is decorative only.
-Cannot join other player's sessions in aggressively guarded internet connections (Such as certain work/university connections, etc)
-Severe packet losses cause attempts to grab objects to fail entirely :(
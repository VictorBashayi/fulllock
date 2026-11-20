# PB page (replaces the Car page) — design

## Intent
First real test showed PBs were saved but never shown: F1 2021 reports Time Trial as session type **13** (seen in the user's pbs.json), not 12. The user also wants the phone's second centre page (tyres/fuel/ERS "Car" view) removed and replaced by a personal-best page: current track's Time Trial dry/wet PB with sector times, plus every other track they have a TT PB on.

## Goals
- Time Trial constant -> 13 (fixes the timing-page PB row too).
- Store S1/S2/S3 with each PB.
- New phone page in place of the Car view (page index 1):
  - top: current track, DRY and WET lap time + S1 S2 S3
  - below: scrollable list of every track with a TT PB, DRY and WET lap times; current track highlighted
- Old PBs saved without sectors show `—` for sectors until beaten.

## Non-goals
Top-N laps per track, other session types on the page (still recorded silently), deleting PBs, sector PBs independent of the lap.

## Approaches
1. **New PB-list packet from PC (chosen):** PC owns the store; sends the whole TT list. One new packet type.
2. Phone keeps its own copy from race-state packets: only sees the current track, loses everything else. Rejected.
3. Squeeze the list into the 10 Hz race packet: wastes bandwidth 10x/s and exceeds the phone's 256-byte buffer. Rejected.

## Design
- **Sectors:** `RaceTracker.OnPlayerLapCompleted` passes `(lapMs, s1Ms, s2Ms)` (the car's S1/S2 already required > 0). `PbStore` entry becomes `{Ms, S1, S2, Date}`; S3 = Ms − S1 − S2. Old JSON entries load with S1 = S2 = 0.
- **Packet** (PC -> phone, HMAC like the others), marker `0xFFFFFFFB`: `[marker:u32][count:u8]` then per track `[trackId:i8][dryMs:u32][dryS1:u16][dryS2:u16][wetMs:u32][wetS1:u16][wetS2:u16]` (17 bytes; 0 = none). 30 tracks max ≈ 515 bytes. Sent every 2 s while a phone is connected and immediately after a new PB (UDP can drop one).
- **Phone:** receive buffer 256 -> 1024 bytes. New `PbView` replaces `CarView` at `centerPage == 1`; `CarView` code deleted. Track names from `TrackNames`. Old phones ignore the unknown marker.
- **Engineer** keeps using tyre/fuel data on the PC; only the phone page goes.

## Constraints
F1 2021 only. Tests: sectors stored/loaded (and old-format load), list packet round-trip on both sides, 30-track size fits, TT = 13.

## Left to the builder
Exact styling, list sort order (proposal: track id order, current track pinned at top).

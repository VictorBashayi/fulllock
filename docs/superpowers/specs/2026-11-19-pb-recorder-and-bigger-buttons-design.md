# PB recorder + bigger face buttons (design)

## Intent
User plays F1 2021 offline, so no leaderboard stores their times. They want their best lap per track kept, mainly **time trial** laps (always the fastest), split **dry / wet**, and shown on the phone dashboard. Also: bigger D-pad and X/Y/A/B buttons.

## Goals
- Save best valid lap per (track, session type, dry|wet) on the PC; survives restarts.
- Phone dashboard shows the Time Trial dry and wet PB for the current track (also while in race/practice/quali, as the target to beat).
- Bigger D-pad and face buttons.

## Non-goals
Sector PBs, history/graphs, engineer "new PB" call, other game versions' session codes, cloud sync, editing/deleting PBs from the app.

## Approaches considered
1. **PC does it all, phone displays (chosen):** telemetry lands on the PC; one new store + 3 packet fields.
2. Phone computes/stores from `bestLapMs`: loses laps if the phone app is closed, `bestLapMs` is per-session only, no track ID on the phone. Rejected.
3. Only record time trial: simpler, but the user wanted nothing lost; keying by session type costs one extra key field. Rejected.

## Design
- **Parse** F1 2021 session packet (id 1): body offsets weather 0, trackLength 4 (u16), sessionType 6 (u8), trackId 7 (i8) after the 24-byte header. Receiver supports F1 23+ headers too; only 2021 session-code mapping is guaranteed (Time Trial = 12 per the 2021 spec - **verify against a real session**). Unknown/other game: record nothing for TT, no crash.
- **Lap completion:** hook `RaceTracker`'s lap-complete branch, only for a clean `lapNum == previous+1` crossing of a valid lap from a flying/on-track driverStatus (out-laps and lap-number drops must not record). Condition = wet if visual compound == 8 (full wet) else dry.
- **Store:** `PbStore` (windows-receiver), JSON in the app data dir via `AppPaths`; key `trackId/sessionType/condition` -> `{ms, date}`. Write only on improvement; atomic write (temp + move); corrupt file -> start empty, keep a `.bad` copy.
- **Wire:** append to the race packet: trackId (i8), TT dry PB ms (u32), TT wet PB ms (u32); 0 = none. New fields are appended after byte 53, so old phones keep working with a new PC; the PB row needs the new phone app.
- **Phone:** under BEST in `DashboardViews.kt` show `PB DRY m:ss.mmm   WET m:ss.mmm` and a short track name; highlight when the lap just set a PB. Track-name table for F1 2021 IDs.
- **Buttons:** face buttons: divisor 2.8 -> 2.5 (`ControllerScreen.kt:471`); D-pad: its keys already touch at the corners, so widen its column (weights) taking from the pedals. Verify on a device; don't shrink the 240dp timing card's column.

## Constraints
Unauthenticated-input rules unchanged (packet stays HMAC'd). Tests: PbStore (improve/ignore/corrupt/restart), wet vs dry classification, session packet parse, packet round-trip on both sides.

## Left to the builder
Exact new divisor values, track-name table, highlight styling, protocol version number.

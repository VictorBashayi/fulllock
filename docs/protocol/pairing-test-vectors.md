# Pairing test vectors

Generated with Python hashlib.pbkdf2_hmac / hmac; C# and Kotlin suites must match.

| Name | Value (hex unless noted) |
|---|---|
| code | `K7QD9MXP2A` (input variants: `k7qd-9mxp-2a`, ` K7QD 9MXP 2A `) |
| key | `60a20da146d5592c8a6ad61091a0551ecf63753c657085b111d631a5dbdc99d9` |
| ctx | `0102030405060708` ‖ `1112131415161718` (pcSession ‖ phoneNonce) |
| V1 control payload (seq 1, steer 0x4000, throttle 0x80, brake 0, buttons 0x1000) | `010000000000000000000000004080000010` |
| V1 tag (dir 01, ctx, ctr 7) | `f363ac24a73c7d68` |
| V2 ping payload (seq 1, ts 0, level 2, nonce 1112…18) | `ffffffff010000000000000000000000021112131415161718` |
| V2 tag (dir 01, ctx empty, ctr 1) | `5826a84a01af4962` |
| V3 game-state payload | `feffffff033200000000000000000000` |
| V3 tag (dir 02, ctx, ctr 5) | `1338ee307bedb447` |

Sealed datagram = payload ‖ ctr (u32 LE) ‖ tag. HMAC input = dir ‖ ctx ‖ payload ‖ ctr (LE).

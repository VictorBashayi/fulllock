package com.f1controller.network

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test
import java.nio.ByteBuffer
import java.nio.ByteOrder

class PongVerifierTest {
    private val auth = PacketAuth(PacketAuth.keyFromRaw(ByteArray(32) { it.toByte() }))
    private val nonce = ByteArray(8) { (0x11 + it).toByte() }
    private val sessionA = ByteArray(8) { (1 + it).toByte() }
    private val sessionB = ByteArray(8) { (0x21 + it).toByte() }
    private val gameState = "feffffff033200000000000000000000".chunked(2).map { it.toInt(16).toByte() }.toByteArray()
    private val any: (Int) -> Boolean = { true }

    // Mirrors Session.BuildPong / SealForActive on the PC.
    private fun pong(seq: Int, pcSession: ByteArray, ctr: Int, forNonce: ByteArray = nonce): ByteArray {
        val p = ByteBuffer.allocate(24).order(ByteOrder.LITTLE_ENDIAN)
            .putInt(-1).putInt(seq).putLong(1234L).put(pcSession).array()
        return seal(p, pcSession + forNonce, ctr)
    }
    private fun data(payload: ByteArray, pcSession: ByteArray, ctr: Int) = seal(payload, pcSession + nonce, ctr)
    private fun seal(p: ByteArray, ctx: ByteArray, ctr: Int): ByteArray {
        val out = ByteArray(p.size + PacketAuth.TRAILER_SIZE)
        auth.seal(PacketAuth.DIR_PC_TO_PHONE, ctx, p, p.size, ctr, out)
        return out
    }
    private fun PongVerifier.pong(d: ByteArray, isOut: (Int) -> Boolean = any) = acceptPong(d, d.size, isOut)
    private fun PongVerifier.data(d: ByteArray) = acceptData(d, d.size)

    @Test fun acceptsValidPongAndLearnsPcSession() {
        val v = PongVerifier(auth, nonce)
        assertNull(v.pcSession)
        assertEquals(24, v.pong(pong(7, sessionA, 1)) { it == 7 })
        assertArrayEquals(sessionA, v.pcSession)
    }

    @Test fun rejectsPongForOtherNonce() {
        val v = PongVerifier(auth, nonce)
        assertNull(v.pong(pong(1, sessionA, 1, forNonce = ByteArray(8))))
        assertNull(v.pcSession)
    }

    @Test fun rejectsPongNotOutstanding() {
        val v = PongVerifier(auth, nonce)
        assertNull(v.pong(pong(9, sessionA, 1)) { it == 7 })
        assertNull(v.pcSession)
    }

    @Test fun rejectsDataBeforePcSessionKnown() {
        assertNull(PongVerifier(auth, nonce).data(data(gameState, sessionA, 2)))
    }

    @Test fun acceptsSealedGameStateAfterPong() {
        val v = PongVerifier(auth, nonce)
        v.pong(pong(1, sessionA, 1))
        assertEquals(16, v.data(data(gameState, sessionA, 5)))
    }

    @Test fun rejectsReplayedData() {
        val v = PongVerifier(auth, nonce)
        v.pong(pong(1, sessionA, 1))
        val d = data(gameState, sessionA, 5)
        assertEquals(16, v.data(d))
        assertNull(v.data(d))
        assertNull(v.data(data(gameState, sessionA, 4)))
        assertNull("pong ctr must also advance", v.pong(pong(2, sessionA, 3)))
    }

    @Test fun PongWithNewPcSessionResetsCounter() {
        val v = PongVerifier(auth, nonce)
        v.pong(pong(1, sessionA, 1))
        assertEquals(16, v.data(data(gameState, sessionA, 50)))
        assertEquals(24, v.pong(pong(2, sessionB, 1)))
        assertArrayEquals(sessionB, v.pcSession)
        assertEquals(16, v.data(data(gameState, sessionB, 2)))
        assertNull("old session data dropped", v.data(data(gameState, sessionA, 51)))
    }

    @Test fun rejectsUnsealedV1Datagrams() {
        val v = PongVerifier(auth, nonce)
        val rawPong = ByteBuffer.allocate(16).order(ByteOrder.LITTLE_ENDIAN).putInt(-1).putInt(1).putLong(0).array()
        assertNull(v.pong(rawPong))
        v.pong(pong(1, sessionA, 1))
        assertNull(v.data(gameState.copyOf()))
    }
}

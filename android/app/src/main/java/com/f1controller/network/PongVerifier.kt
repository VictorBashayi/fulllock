package com.f1controller.network

/** Pure: decides whether a datagram is a valid pong for our nonce and tracks the PC ctr (spec §3.2–3.3). */
class PongVerifier(private val auth: PacketAuth, private val phoneNonce: ByteArray) {
    /** Learned from the first valid pong; read by the send thread. */
    @Volatile var pcSession: ByteArray? = null
        private set
    private var lastPcCtr = 0L // PC ctr starts at 1

    /** Returns the pong payload length if it's a valid pong (marker FFFFFFFF, seq outstanding), else null. */
    fun acceptPong(data: ByteArray, len: Int, isOutstanding: (seq: Int) -> Boolean): Int? {
        if (len != PONG_SIZE + PacketAuth.TRAILER_SIZE || data.size < len || readInt(data, 0) != -1) return null
        val session = data.copyOfRange(16, PONG_SIZE)
        val o = auth.open(PacketAuth.DIR_PC_TO_PHONE, session + phoneNonce, data, len) ?: return null
        val same = session.contentEquals(pcSession)
        if (same && o.ctr <= lastPcCtr) return null
        // Checked only after the tag so forged packets can't consume outstanding seqs
        if (!isOutstanding(readInt(data, 4))) return null
        pcSession = session // a new pcSession means the PC restarted: its ctr starts over
        lastPcCtr = o.ctr
        return o.payloadLen
    }

    /** For non-pong datagrams: valid under current session and ctr > last. */
    fun acceptData(data: ByteArray, len: Int): Int? {
        val session = pcSession ?: return null
        val o = auth.open(PacketAuth.DIR_PC_TO_PHONE, session + phoneNonce, data, len) ?: return null
        if (o.ctr <= lastPcCtr) return null
        lastPcCtr = o.ctr
        return o.payloadLen
    }

    private fun readInt(d: ByteArray, at: Int) =
        (d[at].toInt() and 0xFF) or ((d[at + 1].toInt() and 0xFF) shl 8) or
            ((d[at + 2].toInt() and 0xFF) shl 16) or (d[at + 3].toInt() shl 24)

    companion object {
        const val PONG_SIZE = 24
    }
}

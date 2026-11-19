package com.f1controller.network

/** F1 2021 track ids (session packet) as short names for the dashboard. */
object TrackNames {
    private val names = listOf(
        "Melbourne", "Paul Ricard", "Shanghai", "Sakhir", "Catalunya", "Monaco", "Montreal",
        "Silverstone", "Hockenheim", "Hungaroring", "Spa", "Monza", "Singapore", "Suzuka",
        "Abu Dhabi", "Austin", "Interlagos", "Austria", "Sochi", "Mexico", "Baku",
        "Sakhir Short", "Silverstone Short", "Austin Short", "Suzuka Short", "Hanoi",
        "Zandvoort", "Imola", "Portimao", "Jeddah",
    )

    fun short(id: Int): String = when {
        id < 0 -> ""
        id < names.size -> names[id]
        else -> "Track $id"
    }
}

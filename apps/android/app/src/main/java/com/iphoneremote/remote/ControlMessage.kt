package com.iphoneremote.remote

import org.json.JSONObject

object ControlMessage {
    const val VERSION = 1

    fun tap(x: Float, y: Float, button: Int = 0): String {
        val nx = x.coerceIn(0f, 1f)
        val ny = y.coerceIn(0f, 1f)
        return """{"v":$VERSION,"t":"down","x":$nx,"y":$ny,"b":$button}""" +
               "\n" +
               """{"v":$VERSION,"t":"up","x":$nx,"y":$ny,"b":$button}"""
    }

    fun move(x: Float, y: Float): String {
        val nx = x.coerceIn(0f, 1f)
        val ny = y.coerceIn(0f, 1f)
        return """{"v":$VERSION,"t":"move","x":$nx,"y":$ny}"""
    }

    fun down(x: Float, y: Float, button: Int = 0): String {
        val nx = x.coerceIn(0f, 1f)
        val ny = y.coerceIn(0f, 1f)
        return """{"v":$VERSION,"t":"down","x":$nx,"y":$ny,"b":$button}"""
    }

    fun up(x: Float, y: Float, button: Int = 0): String {
        val nx = x.coerceIn(0f, 1f)
        val ny = y.coerceIn(0f, 1f)
        return """{"v":$VERSION,"t":"up","x":$nx,"y":$ny,"b":$button}"""
    }

    fun wheel(dx: Int, dy: Int): String {
        return """{"v":$VERSION,"t":"wheel","dx":$dx,"dy":$dy}"""
    }

    fun key(vk: Int, down: Boolean): String {
        return """{"v":$VERSION,"t":"key","k":$vk,"down":$down}"""
    }

    fun text(s: String): String {
        return """{"v":$VERSION,"t":"text","s":${JSONObject.quote(s)}}"""
    }

    fun parse(json: String): ParsedMessage? {
        return try {
            val obj = JSONObject(json)
            if (obj.optInt("v") != VERSION) null
            else ParsedMessage(
                version = obj.getInt("v"),
                type = obj.getString("t"),
                x = obj.optDouble("x")?.toFloat(),
                y = obj.optDouble("y")?.toFloat(),
                button = obj.optInt("b", 0),
                dx = obj.optInt("dx", 0),
                dy = obj.optInt("dy", 0),
                keyCode = obj.optInt("k", -1),
                keyDown = obj.optBoolean("down", true),
                text = obj.optString("s", null),
            )
        } catch (_: Exception) {
            null
        }
    }
}

data class ParsedMessage(
    val version: Int,
    val type: String,
    val x: Float?,
    val y: Float?,
    val button: Int,
    val dx: Int,
    val dy: Int,
    val keyCode: Int,
    val keyDown: Boolean,
    val text: String?,
)
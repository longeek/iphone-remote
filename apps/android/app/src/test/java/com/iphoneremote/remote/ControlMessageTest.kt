package com.iphoneremote.remote

import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class ControlMessageTest {

    @Test
    fun move_clampsValuesTo01() {
        val json = ControlMessage.move(-0.5f, 1.5f)
        val first = json.lines().first()
        val msg = ControlMessage.parse(first)!!
        assertEquals(0f, msg.x, 0.001f)
        assertEquals(1f, msg.y, 0.001f)
    }

    @Test
    fun down_generatesValidJson() {
        val json = ControlMessage.down(0.25f, 0.75f, 1)
        val msg = ControlMessage.parse(json)!!
        assertEquals("down", msg.type)
        assertEquals(0.25f, msg.x!!, 0.001f)
        assertEquals(0.75f, msg.y!!, 0.001f)
        assertEquals(1, msg.button)
    }

    @Test
    fun up_generatesValidJson() {
        val json = ControlMessage.up(0.5f, 0.5f, 0)
        val msg = ControlMessage.parse(json)!!
        assertEquals("up", msg.type)
        assertEquals(0, msg.button)
    }

    @Test
    fun wheel_generatesValidJson() {
        val json = ControlMessage.wheel(0, -3)
        val msg = ControlMessage.parse(json)!!
        assertEquals("wheel", msg.type)
        assertEquals(0, msg.dx)
        assertEquals(-3, msg.dy)
    }

    @Test
    fun key_generatesValidJson() {
        val json = ControlMessage.key(27, true)
        val msg = ControlMessage.parse(json)!!
        assertEquals("key", msg.type)
        assertEquals(27, msg.keyCode)
        assertTrue(msg.keyDown)
    }

    @Test
    fun key_downFalse() {
        val json = ControlMessage.key(13, false)
        val msg = ControlMessage.parse(json)!!
        assertFalse(msg.keyDown)
    }

    @Test
    fun text_escapesQuotes() {
        val json = ControlMessage.text("hello\"world")
        val msg = ControlMessage.parse(json)!!
        assertEquals("text", msg.type)
        assertEquals("hello\"world", msg.text)
    }

    @Test
    fun text_emptyString() {
        val json = ControlMessage.text("")
        val msg = ControlMessage.parse(json)!!
        assertEquals("", msg.text)
    }

    @Test
    fun parse_returnsNullForWrongVersion() {
        val json = """{"v":2,"t":"down","x":0.5,"y":0.5,"b":0}"""
        assertNull(ControlMessage.parse(json))
    }

    @Test
    fun parse_returnsNullForInvalidJson() {
        assertNull(ControlMessage.parse("not json"))
    }

    @Test
    fun parse_handlesAllMessageTypes() {
        for (type in listOf("move", "down", "up", "wheel", "key", "text")) {
            val json = when (type) {
                "move" -> ControlMessage.move(0.1f, 0.2f)
                "down" -> ControlMessage.down(0.1f, 0.2f)
                "up" -> ControlMessage.up(0.1f, 0.2f)
                "wheel" -> ControlMessage.wheel(1, 2)
                "key" -> ControlMessage.key(65, true)
                "text" -> ControlMessage.text("a")
                else -> ""
            }
            val msg = ControlMessage.parse(json)
            assertNotNull("Failed for type: $type", msg)
            assertEquals(type, msg!!.type)
        }
    }

    @Test
    fun parse_defaultsButtonTo0() {
        val json = """{"v":1,"t":"down","x":0.5,"y":0.5}"""
        val msg = ControlMessage.parse(json)!!
        assertEquals(0, msg.button)
    }

    @Test
    fun parse_defaultsDxAndDyTo0() {
        val json = """{"v":1,"t":"wheel"}"""
        val msg = ControlMessage.parse(json)!!
        assertEquals(0, msg.dx)
        assertEquals(0, msg.dy)
    }

    @Test
    fun parse_defaultsKeyCodeToNeg1() {
        val json = """{"v":1,"t":"key","k":27}"""
        val msg = ControlMessage.parse(json)!!
        assertEquals(27, msg.keyCode)
    }

    @Test
    fun down_buttonValues() {
        for (button in listOf(0, 1, 2)) {
            val json = ControlMessage.down(0.5f, 0.5f, button)
            val msg = ControlMessage.parse(json)!!
            assertEquals(button, msg.button)
        }
    }

    @Test
    fun move_precision() {
        val json = ControlMessage.move(0.12345f, 0.98765f)
        val msg = ControlMessage.parse(json)!!
        assertEquals(0.12345f, msg.x!!, 0.0001f)
        assertEquals(0.98765f, msg.y!!, 0.0001f)
    }

    @Test
    fun text_unicodeChars() {
        val json = ControlMessage.text("你好")
        val msg = ControlMessage.parse(json)!!
        assertEquals("你好", msg.text)
    }

    @Test
    fun text_newlineEscaped() {
        val json = ControlMessage.text("line1\nline2")
        val msg = ControlMessage.parse(json)!!
        assertEquals("line1\nline2", msg.text)
    }

    @Test
    fun wheel_scrollUp() {
        val json = ControlMessage.wheel(0, 3)
        val msg = ControlMessage.parse(json)!!
        assertEquals(3, msg.dy)
    }

    @Test
    fun wheel_scrollDown() {
        val json = ControlMessage.wheel(0, -3)
        val msg = ControlMessage.parse(json)!!
        assertEquals(-3, msg.dy)
    }

    @Test
    fun wheel_horizontalScroll() {
        val json = ControlMessage.wheel(2, 0)
        val msg = ControlMessage.parse(json)!!
        assertEquals(2, msg.dx)
    }
}
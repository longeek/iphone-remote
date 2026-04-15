package com.iphoneremote.remote

import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.hasTestTag
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodes
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.performClick
import androidx.test.ext.junit.runners.AndroidJUnit4
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith

@RunWith(AndroidJUnit4::class)
class MainActivityInteractionTest {

    @get:Rule
    val composeRule = createAndroidComposeRule<MainActivity>()

    @Test
    fun loginScreen_showsE2ETaggedControls() {
        composeRule.onNodeWithTag("e2e_login_root").assertIsDisplayed()
        composeRule.onNodeWithTag("e2e_field_signaling").assertExists()
        composeRule.onNodeWithTag("e2e_field_room").assertExists()
        composeRule.onNodeWithTag("e2e_button_connect").assertExists()
    }

    @Test
    fun connectButton_opensSessionSurface() {
        composeRule.onNodeWithTag("e2e_button_connect").performClick()
        composeRule.waitUntil(timeoutMillis = 15_000) {
            composeRule.onAllNodes(hasTestTag("e2e_session_root")).fetchSemanticsNodes().isNotEmpty()
        }
        composeRule.onNodeWithTag("e2e_session_root").assertIsDisplayed()
        composeRule.onNodeWithTag("e2e_remote_surface").assertIsDisplayed()
    }

    @Test
    fun sessionScreen_showsDisconnectAndKeyboardButtons() {
        composeRule.onNodeWithTag("e2e_button_connect").performClick()
        composeRule.waitUntil(timeoutMillis = 15_000) {
            composeRule.onAllNodes(hasTestTag("e2e_session_root")).fetchSemanticsNodes().isNotEmpty()
        }
        composeRule.onNodeWithTag("e2e_button_disconnect").assertExists()
        composeRule.onNodeWithTag("e2e_button_keyboard").assertExists()
    }

    @Test
    fun disconnectButton_returnsToLoginScreen() {
        composeRule.onNodeWithTag("e2e_button_connect").performClick()
        composeRule.waitUntil(timeoutMillis = 15_000) {
            composeRule.onAllNodes(hasTestTag("e2e_session_root")).fetchSemanticsNodes().isNotEmpty()
        }
        composeRule.onNodeWithTag("e2e_button_disconnect").performClick()
        composeRule.waitUntil(timeoutMillis = 15_000) {
            composeRule.onAllNodes(hasTestTag("e2e_login_root")).fetchSemanticsNodes().isNotEmpty()
        }
        composeRule.onNodeWithTag("e2e_login_root").assertIsDisplayed()
    }

    @Test
    fun keyboardToggle_showsInputPanel() {
        composeRule.onNodeWithTag("e2e_button_connect").performClick()
        composeRule.waitUntil(timeoutMillis = 15_000) {
            composeRule.onAllNodes(hasTestTag("e2e_session_root")).fetchSemanticsNodes().isNotEmpty()
        }
        composeRule.onNodeWithTag("e2e_button_keyboard").performClick()
        composeRule.waitUntil(timeoutMillis = 5_000) {
            composeRule.onAllNodes(hasTestTag("e2e_text_input")).fetchSemanticsNodes().isNotEmpty()
        }
        composeRule.onNodeWithTag("e2e_text_input").assertExists()
    }
}
// Prevents additional console window on Windows in release
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

fn main() {
    // Enable remote debugging so Chrome can inspect the Tauri webview
    // Open chrome://inspect in Chrome to connect
    #[cfg(debug_assertions)]
    std::env::set_var("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "--remote-debugging-port=9222");

    wellversed_lib::run()
}

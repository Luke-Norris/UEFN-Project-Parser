mod commands;
mod file_watcher;
mod lsp_bridge;
mod sidecar;

use commands::AppState;
use lsp_bridge::LspBridge;
use sidecar::ForgeBridge;
use std::path::PathBuf;
use std::sync::{Arc, Mutex};
use tauri::{Emitter, Manager};

pub fn run() {
    tauri::Builder::default()
        .plugin(tauri_plugin_mcp_bridge::init())
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_fs::init())
        .plugin(tauri_plugin_shell::init())
        .setup(|app| {
            let repo_root = if cfg!(debug_assertions) {
                let manifest_dir = PathBuf::from(env!("CARGO_MANIFEST_DIR"));
                manifest_dir
                    .parent() // app/
                    .and_then(|p| p.parent()) // repo root
                    .unwrap_or(&manifest_dir)
                    .to_path_buf()
            } else {
                app.path()
                    .resource_dir()
                    .unwrap_or_else(|_| PathBuf::from("."))
            };

            let assets_dir = repo_root.join("app").join("fortnite_assets");
            let fonts_dir = repo_root.join("app").join("fonts");

            let bridge = Arc::new(ForgeBridge::new());
            let repo_root_str = repo_root.to_string_lossy().to_string();

            // Spawn sidecar build+start on a background thread so the window appears immediately
            let bridge_clone = Arc::clone(&bridge);
            std::thread::spawn(move || {
                if let Err(e) = bridge_clone.start(&repo_root_str) {
                    eprintln!("[wellversed] Failed to start sidecar: {}", e);
                }
            });

            let lsp = Arc::new(LspBridge::new());

            // Forward LSP notifications (diagnostics, etc.) as Tauri events
            let app_handle = app.handle().clone();
            lsp.set_notification_handler(move |method, params| {
                let event_name = format!("lsp:{}", method.replace('/', ":"));
                let _ = app_handle.emit(&event_name, params);
            });

            app.manage(AppState {
                bridge,
                lsp,
                assets_dir,
                fonts_dir,
                file_watcher: Mutex::new(None),
            });

            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            commands::forge_ping,
            commands::forge_status,
            commands::forge_list_projects,
            commands::forge_add_project,
            commands::forge_remove_project,
            commands::forge_activate_project,
            commands::forge_scan_projects,
            commands::forge_list_levels,
            commands::forge_audit,
            commands::forge_browse_content,
            commands::forge_inspect_asset,
            commands::forge_list_devices,
            commands::forge_inspect_device,
            commands::forge_list_user_assets,
            commands::forge_list_epic_assets,
            commands::forge_read_verse,
            commands::forge_list_staged,
            commands::forge_apply_staged,
            commands::forge_discard_staged,
            commands::forge_validate_spec,
            commands::forge_build_uasset,
            commands::forge_generate_verse,
            // Widget parsing commands
            commands::forge_list_project_widgets,
            commands::forge_parse_widget,
            commands::forge_widget_texture,
            commands::forge_list_library_widgets,
            commands::forge_list_libraries,
            commands::forge_add_library,
            commands::forge_remove_library,
            commands::forge_activate_library,
            commands::forge_index_library,
            commands::forge_get_library_verse_files,
            commands::forge_get_library_assets_by_type,
            commands::forge_browse_library_dir,
            commands::forge_search_library_index,
            // Device Encyclopedia commands
            commands::forge_encyclopedia_search,
            commands::forge_encyclopedia_device_reference,
            commands::forge_encyclopedia_common_configs,
            commands::forge_encyclopedia_list_devices,
            // Device Behavior Simulator commands
            commands::forge_simulate_game_loop,
            commands::forge_simulate_event,
            // System extraction commands
            commands::forge_analyze_level_systems,
            commands::forge_analyze_project_systems,
            // Project diff / snapshot commands
            commands::forge_take_snapshot,
            commands::forge_list_snapshots,
            commands::forge_compare_snapshot,
            // CUE4Parse preview commands
            commands::forge_preview_init,
            commands::forge_preview_status,
            commands::forge_preview_search,
            commands::forge_preview_texture,
            commands::forge_preview_mesh_info,
            commands::forge_preview_export_mesh,
            commands::forge_preview_export_mesh_batch,
            // Local file system commands
            commands::scan_assets,
            commands::get_asset_data,
            commands::get_fonts,
            commands::get_font_data,
            commands::export_png,
            commands::export_batch,
            commands::import_to_assets,
            commands::create_asset_folder,
            commands::delete_asset,
            commands::read_text_file,
            commands::list_directory,
            // Verse LSP commands
            commands::lsp_status,
            commands::lsp_start,
            commands::lsp_stop,
            commands::lsp_did_open,
            commands::lsp_did_change,
            commands::lsp_completion,
            commands::lsp_hover,
            commands::lsp_definition,
            commands::lsp_document_symbols,
            commands::lsp_signature_help,
            // File watcher commands
            commands::start_file_watcher,
            commands::stop_file_watcher,
            commands::file_watcher_status,
            // UEFN Bridge commands
            commands::forge_bridge_connect,
            commands::forge_bridge_status,
            commands::forge_bridge_command,
        ])
        .run(tauri::generate_context!())
        .expect("error while running WellVersed");
}

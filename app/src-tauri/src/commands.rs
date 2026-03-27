use crate::file_watcher::FileWatcher;
use crate::lsp_bridge::LspBridge;
use crate::sidecar::ForgeBridge;
use serde_json::{json, Value};
use std::fs;
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};
use tauri::State;

pub struct AppState {
    pub bridge: Arc<ForgeBridge>,
    pub lsp: Arc<LspBridge>,
    pub assets_dir: PathBuf,
    pub fonts_dir: PathBuf,
    pub file_watcher: Mutex<Option<FileWatcher>>,
}

// ─── Sidecar passthrough commands ────────────────────────────────────────────
// These all forward to the .NET sidecar via NDJSON bridge.

#[tauri::command]
pub async fn forge_ping(state: State<'_, AppState>) -> Result<Value, String> {
    state
        .bridge
        .call("ping", json!({}))
        .await
        .or_else(|_| Ok(json!({"error": "Sidecar not running"})))
}

#[tauri::command]
pub async fn forge_status(state: State<'_, AppState>) -> Result<Value, String> {
    state.bridge.call("status", json!({})).await.or_else(|_| {
        Ok(json!({
            "isConfigured": false,
            "projectName": "No Project",
            "mode": "None",
            "assetCount": 0,
            "verseCount": 0
        }))
    })
}

#[tauri::command]
pub async fn forge_list_projects(state: State<'_, AppState>) -> Result<Value, String> {
    state
        .bridge
        .call("list-projects", json!({}))
        .await
        .or_else(|_| Ok(json!({"activeProjectId": null, "projects": []})))
}

#[tauri::command]
pub async fn forge_add_project(
    state: State<'_, AppState>,
    path: String,
    project_type: String,
) -> Result<Value, String> {
    state
        .bridge
        .call("add-project", json!({"path": path, "type": project_type}))
        .await
}

#[tauri::command]
pub async fn forge_remove_project(
    state: State<'_, AppState>,
    id: String,
) -> Result<Value, String> {
    state
        .bridge
        .call("remove-project", json!({"id": id}))
        .await
}

#[tauri::command]
pub async fn forge_activate_project(
    state: State<'_, AppState>,
    id: String,
) -> Result<Value, String> {
    state
        .bridge
        .call("activate-project", json!({"id": id}))
        .await
}

#[tauri::command]
pub async fn forge_scan_projects(
    state: State<'_, AppState>,
    path: String,
) -> Result<Value, String> {
    state
        .bridge
        .call("scan-projects", json!({"path": path}))
        .await
}

#[tauri::command]
pub async fn forge_list_levels(state: State<'_, AppState>) -> Result<Value, String> {
    state.bridge.call("list-levels", json!({})).await
}

#[tauri::command]
pub async fn forge_audit(
    state: State<'_, AppState>,
    level: Option<String>,
) -> Result<Value, String> {
    let params = match level {
        Some(l) => json!({"level": l}),
        None => json!({}),
    };
    state.bridge.call("audit", params).await.or_else(|_| {
        Ok(
            json!({"status": "Error", "findings": [], "error": {"message": "No active project or audit failed"}}),
        )
    })
}

#[tauri::command]
pub async fn forge_browse_content(
    state: State<'_, AppState>,
    path: Option<String>,
) -> Result<Value, String> {
    let params = match path {
        Some(p) => json!({"path": p}),
        None => json!({}),
    };
    state
        .bridge
        .call("browse-content", params)
        .await
        .or_else(|_| Ok(json!({"currentPath": "", "relativePath": "", "entries": []})))
}

#[tauri::command]
pub async fn forge_inspect_asset(
    state: State<'_, AppState>,
    path: String,
) -> Result<Value, String> {
    state
        .bridge
        .call("inspect-asset", json!({"path": path}))
        .await
}

#[tauri::command]
pub async fn forge_list_devices(
    state: State<'_, AppState>,
    level_path: Option<String>,
) -> Result<Value, String> {
    let params = match level_path {
        Some(p) => json!({"levelPath": p}),
        None => json!({}),
    };
    state
        .bridge
        .call("list-devices", params)
        .await
        .or_else(|_| Ok(json!({"levelPath": "", "devices": []})))
}

#[tauri::command]
pub async fn forge_inspect_device(
    state: State<'_, AppState>,
    path: String,
) -> Result<Value, String> {
    state
        .bridge
        .call("inspect-device", json!({"path": path}))
        .await
}

#[tauri::command]
pub async fn forge_list_user_assets(state: State<'_, AppState>) -> Result<Value, String> {
    state
        .bridge
        .call("list-user-assets", json!({}))
        .await
        .or_else(|_| Ok(json!({"assets": [], "totalCount": 0})))
}

#[tauri::command]
pub async fn forge_list_epic_assets(state: State<'_, AppState>) -> Result<Value, String> {
    state
        .bridge
        .call("list-epic-assets", json!({}))
        .await
        .or_else(|_| {
            Ok(
                json!({"types": [], "totalPlaced": 0, "uniqueTypes": 0, "deviceCount": 0, "propCount": 0}),
            )
        })
}

#[tauri::command]
pub async fn forge_read_verse(
    state: State<'_, AppState>,
    path: String,
) -> Result<Value, String> {
    state
        .bridge
        .call("read-verse", json!({"path": path}))
        .await
}

#[tauri::command]
pub async fn forge_list_staged(state: State<'_, AppState>) -> Result<Value, String> {
    state
        .bridge
        .call("list-staged", json!({}))
        .await
        .or_else(|_| Ok(json!({"files": [], "totalSize": 0})))
}

#[tauri::command]
pub async fn forge_apply_staged(state: State<'_, AppState>) -> Result<Value, String> {
    state.bridge.call("apply-staged", json!({})).await
}

#[tauri::command]
pub async fn forge_discard_staged(state: State<'_, AppState>) -> Result<Value, String> {
    state.bridge.call("discard-staged", json!({})).await
}

#[tauri::command]
pub async fn forge_validate_spec(
    state: State<'_, AppState>,
    spec: Value,
) -> Result<Value, String> {
    state
        .bridge
        .call("validate-spec", json!({"spec": spec}))
        .await
}

#[tauri::command]
pub async fn forge_build_uasset(
    state: State<'_, AppState>,
    spec: Value,
    output_dir: String,
    variables: Option<Value>,
) -> Result<Value, String> {
    let mut params = json!({"spec": spec, "outputDir": output_dir});
    if let Some(vars) = variables {
        params["variables"] = vars;
    }
    state.bridge.call("build-uasset", params).await
}

#[tauri::command]
pub async fn forge_generate_verse(
    state: State<'_, AppState>,
    spec: Value,
) -> Result<Value, String> {
    state
        .bridge
        .call("generate-verse", json!({"spec": spec}))
        .await
}

// ─── Widget parsing commands ─────────────────────────────────────────────────

#[tauri::command]
pub async fn forge_list_project_widgets(state: State<'_, AppState>) -> Result<Value, String> {
    state
        .bridge
        .call("list-project-widgets", json!({}))
        .await
        .or_else(|_| Ok(json!({"widgets": []})))
}

#[tauri::command]
pub async fn forge_parse_widget(
    state: State<'_, AppState>,
    path: String,
) -> Result<Value, String> {
    state
        .bridge
        .call("parse-widget", json!({"path": path}))
        .await
}

#[tauri::command]
pub async fn forge_widget_texture(
    state: State<'_, AppState>,
    texture_path: String,
) -> Result<Value, String> {
    state
        .bridge
        .call("widget-texture", json!({"texturePath": texture_path}))
        .await
}

#[tauri::command]
pub async fn forge_list_library_widgets(state: State<'_, AppState>) -> Result<Value, String> {
    state
        .bridge
        .call("list-library-widgets", json!({}))
        .await
        .or_else(|_| Ok(json!({"widgets": []})))
}

// ─── Library management commands ─────────────────────────────────────────────

#[tauri::command]
pub async fn forge_list_libraries(state: State<'_, AppState>) -> Result<Value, String> {
    state
        .bridge
        .call("list-libraries", json!({}))
        .await
        .or_else(|_| Ok(json!({"activeLibraryId": null, "libraries": []})))
}

#[tauri::command]
pub async fn forge_add_library(
    state: State<'_, AppState>,
    path: String,
) -> Result<Value, String> {
    state
        .bridge
        .call("add-library", json!({"path": path}))
        .await
}

#[tauri::command]
pub async fn forge_remove_library(
    state: State<'_, AppState>,
    id: String,
) -> Result<Value, String> {
    state
        .bridge
        .call("remove-library", json!({"id": id}))
        .await
}

#[tauri::command]
pub async fn forge_activate_library(
    state: State<'_, AppState>,
    id: String,
) -> Result<Value, String> {
    state
        .bridge
        .call("activate-library", json!({"id": id}))
        .await
}

#[tauri::command]
pub async fn forge_index_library(
    state: State<'_, AppState>,
    id: Option<String>,
) -> Result<Value, String> {
    let params = match id {
        Some(i) => json!({"id": i}),
        None => json!({}),
    };
    state.bridge.call("index-library", params).await
}

#[tauri::command]
pub async fn forge_get_library_verse_files(
    state: State<'_, AppState>,
    filter: Option<String>,
) -> Result<Value, String> {
    let params = match filter {
        Some(f) => json!({"filter": f}),
        None => json!({}),
    };
    state.bridge.call("get-library-verse-files", params).await
}

#[tauri::command]
pub async fn forge_get_library_assets_by_type(state: State<'_, AppState>) -> Result<Value, String> {
    state
        .bridge
        .call("get-library-assets-by-type", json!({}))
        .await
}

#[tauri::command]
pub async fn forge_browse_library_dir(
    state: State<'_, AppState>,
    path: Option<String>,
) -> Result<Value, String> {
    let params = match path {
        Some(p) => json!({"path": p}),
        None => json!({}),
    };
    state
        .bridge
        .call("browse-library-dir", params)
        .await
        .or_else(|_| Ok(json!({"entries": []})))
}

#[tauri::command]
pub async fn forge_search_library_index(
    state: State<'_, AppState>,
    query: String,
) -> Result<Value, String> {
    state
        .bridge
        .call("search-library-index", json!({"query": query}))
        .await
}

// ─── Device Encyclopedia commands ────────────────────────────────────────────

#[tauri::command]
pub async fn forge_encyclopedia_search(
    state: State<'_, AppState>,
    query: String,
) -> Result<Value, String> {
    state
        .bridge
        .call("encyclopedia-search", json!({"query": query}))
        .await
        .or_else(|_| Ok(json!({"query": query, "resultCount": 0, "results": []})))
}

#[tauri::command]
pub async fn forge_encyclopedia_device_reference(
    state: State<'_, AppState>,
    device_class: String,
) -> Result<Value, String> {
    state
        .bridge
        .call(
            "encyclopedia-device-reference",
            json!({"deviceClass": device_class}),
        )
        .await
}

#[tauri::command]
pub async fn forge_encyclopedia_common_configs(
    state: State<'_, AppState>,
    device_class: String,
) -> Result<Value, String> {
    state
        .bridge
        .call(
            "encyclopedia-common-configs",
            json!({"deviceClass": device_class}),
        )
        .await
}

#[tauri::command]
pub async fn forge_encyclopedia_list_devices(
    state: State<'_, AppState>,
) -> Result<Value, String> {
    state
        .bridge
        .call("encyclopedia-list-devices", json!({}))
        .await
        .or_else(|_| Ok(json!({"deviceCount": 0, "devices": []})))
}

// ─── System extraction commands ──────────────────────────────────────────────

#[tauri::command]
pub async fn forge_analyze_level_systems(
    state: State<'_, AppState>,
    level_path: String,
) -> Result<Value, String> {
    state
        .bridge
        .call("analyze-level-systems", json!({"levelPath": level_path}))
        .await
}

#[tauri::command]
pub async fn forge_analyze_project_systems(
    state: State<'_, AppState>,
) -> Result<Value, String> {
    state
        .bridge
        .call("analyze-project-systems", json!({}))
        .await
}

// ─── Device Behavior Simulator commands ──────────────────────────────────────

#[tauri::command]
pub async fn forge_simulate_game_loop(
    state: State<'_, AppState>,
    level_path: String,
) -> Result<Value, String> {
    state
        .bridge
        .call("simulate-game-loop", json!({"levelPath": level_path}))
        .await
        .or_else(|_| {
            Ok(
                json!({"initialTrigger": "GameStart", "stepCount": 0, "steps": [], "warnings": ["Simulation failed"], "gameLoop": {"phases": [], "transitions": []}}),
            )
        })
}

#[tauri::command]
pub async fn forge_simulate_event(
    state: State<'_, AppState>,
    level_path: String,
    device_name: String,
    event_name: String,
) -> Result<Value, String> {
    state
        .bridge
        .call(
            "simulate-event",
            json!({"levelPath": level_path, "deviceName": device_name, "eventName": event_name}),
        )
        .await
        .or_else(|_| {
            Ok(
                json!({"initialTrigger": "", "stepCount": 0, "steps": [], "warnings": ["Simulation failed"]}),
            )
        })
}

// ─── Project diff / snapshot commands ────────────────────────────────────────

#[tauri::command]
pub async fn forge_take_snapshot(
    state: State<'_, AppState>,
    description: Option<String>,
) -> Result<Value, String> {
    let params = match description {
        Some(d) => json!({"description": d}),
        None => json!({}),
    };
    state.bridge.call("take-snapshot", params).await
}

#[tauri::command]
pub async fn forge_list_snapshots(state: State<'_, AppState>) -> Result<Value, String> {
    state
        .bridge
        .call("list-snapshots", json!({}))
        .await
        .or_else(|_| Ok(json!({"count": 0, "snapshots": []})))
}

#[tauri::command]
pub async fn forge_compare_snapshot(
    state: State<'_, AppState>,
    snapshot_id: String,
) -> Result<Value, String> {
    state
        .bridge
        .call("compare-snapshot", json!({"snapshotId": snapshot_id}))
        .await
}

// ─── CUE4Parse preview commands ──────────────────────────────────────────────

#[tauri::command]
pub async fn forge_preview_init(
    state: State<'_, AppState>,
    fortnite_path: String,
) -> Result<Value, String> {
    state
        .bridge
        .call("preview-init", json!({"fortnitePath": fortnite_path}))
        .await
}

#[tauri::command]
pub async fn forge_preview_status(state: State<'_, AppState>) -> Result<Value, String> {
    state.bridge.call("preview-status", json!({})).await.or_else(|_| {
        Ok(json!({"initialized": false, "fileCount": 0, "gamePath": null}))
    })
}

#[tauri::command]
pub async fn forge_preview_search(
    state: State<'_, AppState>,
    query: String,
    limit: Option<i32>,
) -> Result<Value, String> {
    state
        .bridge
        .call("preview-search", json!({"query": query, "limit": limit.unwrap_or(50)}))
        .await
}

#[tauri::command]
pub async fn forge_preview_texture(
    state: State<'_, AppState>,
    asset_path: String,
) -> Result<Value, String> {
    state
        .bridge
        .call("preview-texture", json!({"assetPath": asset_path}))
        .await
}

#[tauri::command]
pub async fn forge_preview_mesh_info(
    state: State<'_, AppState>,
    asset_path: String,
) -> Result<Value, String> {
    state
        .bridge
        .call("preview-mesh-info", json!({"assetPath": asset_path}))
        .await
}

#[tauri::command]
pub async fn forge_preview_export_mesh(
    state: State<'_, AppState>,
    device_class: String,
) -> Result<Value, String> {
    state
        .bridge
        .call("preview-export-mesh", json!({"deviceClass": device_class}))
        .await
}

#[tauri::command]
pub async fn forge_preview_export_mesh_batch(
    state: State<'_, AppState>,
    device_classes: Vec<String>,
) -> Result<Value, String> {
    state
        .bridge
        .call("preview-export-mesh-batch", json!({"deviceClasses": device_classes}))
        .await
}

// ─── Local file system commands (replace Electron fs/dialog handlers) ────────

#[tauri::command]
pub async fn scan_assets(state: State<'_, AppState>) -> Result<Value, String> {
    let assets_dir = &state.assets_dir;
    if !assets_dir.exists() {
        return Ok(json!({"categories": [], "totalAssets": 0}));
    }
    let index = scan_directory_recursive(assets_dir, assets_dir);
    let total = count_assets(&index);
    Ok(json!({
        "categories": [{"name": "Fortnite Assets", "path": assets_dir.to_string_lossy(), "assets": index.assets, "subcategories": index.subcategories}],
        "totalAssets": total
    }))
}

#[derive(serde::Serialize)]
struct ScannedCategory {
    name: String,
    path: String,
    assets: Vec<ScannedAsset>,
    subcategories: Vec<ScannedCategory>,
}

#[derive(serde::Serialize)]
struct ScannedAsset {
    name: String,
    filename: String,
    path: String,
    #[serde(rename = "relativePath")]
    relative_path: String,
    category: String,
    extension: String,
}

fn scan_directory_recursive(dir: &Path, base: &Path) -> ScannedCategory {
    let image_exts = ["png", "jpg", "jpeg", "webp", "gif", "svg"];
    let name = dir
        .file_name()
        .unwrap_or_default()
        .to_string_lossy()
        .to_string();
    let mut assets = Vec::new();
    let mut subcategories = Vec::new();

    if let Ok(entries) = fs::read_dir(dir) {
        let mut entries: Vec<_> = entries.filter_map(|e| e.ok()).collect();
        entries.sort_by_key(|e| e.file_name());

        for entry in entries {
            let path = entry.path();
            if path.is_dir() {
                let sub = scan_directory_recursive(&path, base);
                if !sub.assets.is_empty() || !sub.subcategories.is_empty() {
                    subcategories.push(sub);
                }
            } else if path.is_file() {
                let ext = path
                    .extension()
                    .unwrap_or_default()
                    .to_string_lossy()
                    .to_lowercase();
                if image_exts.contains(&ext.as_str()) {
                    let filename = path
                        .file_name()
                        .unwrap_or_default()
                        .to_string_lossy()
                        .to_string();
                    let stem = path
                        .file_stem()
                        .unwrap_or_default()
                        .to_string_lossy()
                        .to_string();
                    let rel = path
                        .strip_prefix(base)
                        .unwrap_or(&path)
                        .to_string_lossy()
                        .to_string();
                    assets.push(ScannedAsset {
                        name: stem,
                        filename,
                        path: path.to_string_lossy().to_string(),
                        relative_path: rel,
                        category: name.clone(),
                        extension: format!(".{}", ext),
                    });
                }
            }
        }
    }

    ScannedCategory {
        name,
        path: dir.to_string_lossy().to_string(),
        assets,
        subcategories,
    }
}

fn count_assets(cat: &ScannedCategory) -> usize {
    let mut total = cat.assets.len();
    for sub in &cat.subcategories {
        total += count_assets(sub);
    }
    total
}

#[tauri::command]
pub async fn get_asset_data(file_path: String) -> Result<Option<String>, String> {
    let path = Path::new(&file_path);
    if !path.exists() {
        return Ok(None);
    }
    let data = fs::read(path).map_err(|e| e.to_string())?;
    let ext = path
        .extension()
        .unwrap_or_default()
        .to_string_lossy()
        .to_lowercase();
    let mime = match ext.as_str() {
        "png" => "image/png",
        "jpg" | "jpeg" => "image/jpeg",
        "svg" => "image/svg+xml",
        "webp" => "image/webp",
        "gif" => "image/gif",
        _ => "image/png",
    };
    use base64::Engine;
    let b64 = base64::engine::general_purpose::STANDARD.encode(&data);
    Ok(Some(format!("data:{};base64,{}", mime, b64)))
}

#[tauri::command]
pub async fn get_fonts(state: State<'_, AppState>) -> Result<Vec<String>, String> {
    let fonts_dir = &state.fonts_dir;
    if !fonts_dir.exists() {
        return Ok(vec![]);
    }
    let font_exts = ["ttf", "otf", "woff", "woff2"];
    let mut fonts = Vec::new();
    if let Ok(entries) = fs::read_dir(fonts_dir) {
        for entry in entries.filter_map(|e| e.ok()) {
            let path = entry.path();
            if path.is_file() {
                let ext = path
                    .extension()
                    .unwrap_or_default()
                    .to_string_lossy()
                    .to_lowercase();
                if font_exts.contains(&ext.as_str()) {
                    if let Some(name) = path.file_name() {
                        fonts.push(name.to_string_lossy().to_string());
                    }
                }
            }
        }
    }
    fonts.sort();
    Ok(fonts)
}

#[tauri::command]
pub async fn get_font_data(
    state: State<'_, AppState>,
    font_filename: String,
) -> Result<Option<Value>, String> {
    let font_path = state.fonts_dir.join(&font_filename);
    if !font_path.exists() {
        return Ok(None);
    }
    let data = fs::read(&font_path).map_err(|e| e.to_string())?;
    let ext = font_path
        .extension()
        .unwrap_or_default()
        .to_string_lossy()
        .to_lowercase();
    let mime = match ext.as_str() {
        "otf" => "font/otf",
        "woff" => "font/woff",
        "woff2" => "font/woff2",
        _ => "font/ttf",
    };
    use base64::Engine;
    let b64 = base64::engine::general_purpose::STANDARD.encode(&data);
    Ok(Some(json!({
        "data": format!("data:{};base64,{}", mime, b64),
        "filename": font_filename
    })))
}

#[tauri::command]
pub async fn export_png(data_url: String, file_path: String) -> Result<Option<String>, String> {
    let base64_data = data_url
        .strip_prefix("data:image/png;base64,")
        .unwrap_or(&data_url);
    use base64::Engine;
    let bytes = base64::engine::general_purpose::STANDARD
        .decode(base64_data)
        .map_err(|e| e.to_string())?;
    fs::write(&file_path, bytes).map_err(|e| e.to_string())?;
    Ok(Some(file_path))
}

#[tauri::command]
pub async fn export_batch(
    items: Vec<Value>,
    output_dir: String,
) -> Result<Vec<String>, String> {
    let dir = Path::new(&output_dir);
    if !dir.exists() {
        fs::create_dir_all(dir).map_err(|e| e.to_string())?;
    }

    let mut results = Vec::new();
    for item in &items {
        let data_url = item["dataUrl"].as_str().unwrap_or("");
        let filename = item["filename"].as_str().unwrap_or("output.png");
        let base64_data = data_url
            .strip_prefix("data:image/png;base64,")
            .unwrap_or(data_url);
        use base64::Engine;
        let bytes = base64::engine::general_purpose::STANDARD
            .decode(base64_data)
            .map_err(|e| e.to_string())?;
        let out_path = dir.join(filename);
        fs::write(&out_path, bytes).map_err(|e| e.to_string())?;
        results.push(out_path.to_string_lossy().to_string());
    }
    Ok(results)
}

#[tauri::command]
pub async fn import_to_assets(
    state: State<'_, AppState>,
    target_folder: String,
    file_paths: Vec<String>,
) -> Result<Value, String> {
    let assets_dir = &state.assets_dir;
    let target_dir = assets_dir.join(&target_folder);

    // Validate target is within assets dir
    let canonical_assets = fs::canonicalize(assets_dir).unwrap_or(assets_dir.clone());
    if !target_dir.starts_with(&canonical_assets) && !target_dir.starts_with(assets_dir) {
        return Ok(json!({"success": false, "imported": [], "error": "Invalid target folder"}));
    }

    if !target_dir.exists() {
        fs::create_dir_all(&target_dir).map_err(|e| e.to_string())?;
    }

    let mut imported = Vec::new();
    for src_path_str in &file_paths {
        let src_path = Path::new(src_path_str);
        if let Some(filename) = src_path.file_name() {
            let mut dest = target_dir.join(filename);
            // Avoid overwrite
            if dest.exists() {
                let stem = src_path
                    .file_stem()
                    .unwrap_or_default()
                    .to_string_lossy()
                    .to_string();
                let ext = src_path
                    .extension()
                    .unwrap_or_default()
                    .to_string_lossy()
                    .to_string();
                let mut counter = 1;
                while dest.exists() {
                    dest = target_dir.join(format!("{} ({}){}", stem, counter, if ext.is_empty() { String::new() } else { format!(".{}", ext) }));
                    counter += 1;
                }
            }
            fs::copy(src_path, &dest).map_err(|e| e.to_string())?;
            imported.push(dest.to_string_lossy().to_string());
        }
    }

    Ok(json!({"success": true, "imported": imported}))
}

#[tauri::command]
pub async fn create_asset_folder(
    state: State<'_, AppState>,
    folder_name: String,
    parent_path: String,
) -> Result<Value, String> {
    let parent_dir = state.assets_dir.join(&parent_path);
    let new_folder = parent_dir.join(&folder_name);

    if new_folder.exists() {
        return Ok(json!({"success": false, "error": "Folder already exists"}));
    }

    fs::create_dir_all(&new_folder).map_err(|e| e.to_string())?;
    Ok(json!({"success": true, "path": new_folder.to_string_lossy()}))
}

#[tauri::command]
pub async fn delete_asset(
    state: State<'_, AppState>,
    file_path: String,
) -> Result<Value, String> {
    let resolved = Path::new(&file_path);
    if !resolved.starts_with(&state.assets_dir) {
        return Ok(json!({"success": false, "error": "File is not within assets directory"}));
    }
    if !resolved.exists() {
        return Ok(json!({"success": false, "error": "File not found"}));
    }
    fs::remove_file(resolved).map_err(|e| e.to_string())?;
    Ok(json!({"success": true}))
}

#[tauri::command]
pub async fn read_text_file(file_path: String) -> Result<Value, String> {
    let path = Path::new(&file_path);
    if !path.exists() {
        return Ok(json!({"error": format!("File not found: {}", file_path)}));
    }
    let content = fs::read_to_string(path).map_err(|e| e.to_string())?;
    let name = path
        .file_name()
        .unwrap_or_default()
        .to_string_lossy()
        .to_string();
    Ok(json!({"content": content, "name": name}))
}

#[tauri::command]
pub async fn list_directory(dir_path: String) -> Result<Value, String> {
    let path = Path::new(&dir_path);
    if !path.exists() {
        return Ok(json!({"error": format!("Directory not found: {}", dir_path), "entries": []}));
    }
    let mut entries = Vec::new();
    if let Ok(dir_entries) = fs::read_dir(path) {
        for entry in dir_entries.filter_map(|e| e.ok()) {
            let full_path = entry.path();
            let metadata = fs::metadata(&full_path);
            entries.push(json!({
                "name": entry.file_name().to_string_lossy(),
                "path": full_path.to_string_lossy(),
                "isDirectory": full_path.is_dir(),
                "size": metadata.as_ref().map(|m| m.len()).unwrap_or(0)
            }));
        }
    }
    Ok(json!({"entries": entries}))
}

// ─── LSP Commands ───────────────────────────────────────────────────────────

#[tauri::command]
pub async fn lsp_status(state: State<'_, AppState>) -> Result<Value, String> {
    let binary = LspBridge::find_lsp_binary();
    Ok(json!({
        "available": binary.is_some(),
        "binaryPath": binary,
        "ready": state.lsp.is_ready(),
        "capabilities": state.lsp.capabilities(),
    }))
}

#[tauri::command]
pub async fn lsp_start(state: State<'_, AppState>, workspace_path: String) -> Result<Value, String> {
    if state.lsp.is_ready() {
        return Ok(json!({"status": "already_running"}));
    }

    let binary = LspBridge::find_lsp_binary()
        .ok_or_else(|| "Epic's verse-lsp.exe not found. Install the Verse VS Code extension.".to_string())?;

    state.lsp.start(&binary, &workspace_path)?;

    Ok(json!({
        "status": "started",
        "capabilities": state.lsp.capabilities(),
    }))
}

#[tauri::command]
pub async fn lsp_stop(state: State<'_, AppState>) -> Result<Value, String> {
    state.lsp.stop();
    Ok(json!({"status": "stopped"}))
}

#[tauri::command]
pub async fn lsp_did_open(state: State<'_, AppState>, uri: String, content: String) -> Result<Value, String> {
    if !state.lsp.is_ready() {
        return Err("LSP not running".to_string());
    }
    state.lsp.send_notification("textDocument/didOpen", json!({
        "textDocument": {
            "uri": uri,
            "languageId": "verse",
            "version": 1,
            "text": content,
        }
    }))?;
    Ok(json!({"ok": true}))
}

#[tauri::command]
pub async fn lsp_did_change(state: State<'_, AppState>, uri: String, content: String, version: i32) -> Result<Value, String> {
    if !state.lsp.is_ready() {
        return Err("LSP not running".to_string());
    }
    state.lsp.send_notification("textDocument/didChange", json!({
        "textDocument": { "uri": uri, "version": version },
        "contentChanges": [{ "text": content }],
    }))?;
    Ok(json!({"ok": true}))
}

#[tauri::command]
pub async fn lsp_completion(state: State<'_, AppState>, uri: String, line: u32, character: u32) -> Result<Value, String> {
    if !state.lsp.is_ready() {
        return Err("LSP not running".to_string());
    }
    let rx = state.lsp.send_request_async("textDocument/completion", json!({
        "textDocument": { "uri": uri },
        "position": { "line": line, "character": character },
    }))?;
    rx.await.map_err(|_| "LSP completion request cancelled".to_string())
}

#[tauri::command]
pub async fn lsp_hover(state: State<'_, AppState>, uri: String, line: u32, character: u32) -> Result<Value, String> {
    if !state.lsp.is_ready() {
        return Err("LSP not running".to_string());
    }
    let rx = state.lsp.send_request_async("textDocument/hover", json!({
        "textDocument": { "uri": uri },
        "position": { "line": line, "character": character },
    }))?;
    rx.await.map_err(|_| "LSP hover request cancelled".to_string())
}

#[tauri::command]
pub async fn lsp_definition(state: State<'_, AppState>, uri: String, line: u32, character: u32) -> Result<Value, String> {
    if !state.lsp.is_ready() {
        return Err("LSP not running".to_string());
    }
    let rx = state.lsp.send_request_async("textDocument/definition", json!({
        "textDocument": { "uri": uri },
        "position": { "line": line, "character": character },
    }))?;
    rx.await.map_err(|_| "LSP definition request cancelled".to_string())
}

#[tauri::command]
pub async fn lsp_document_symbols(state: State<'_, AppState>, uri: String) -> Result<Value, String> {
    if !state.lsp.is_ready() {
        return Err("LSP not running".to_string());
    }
    let rx = state.lsp.send_request_async("textDocument/documentSymbol", json!({
        "textDocument": { "uri": uri },
    }))?;
    rx.await.map_err(|_| "LSP symbols request cancelled".to_string())
}

#[tauri::command]
pub async fn lsp_signature_help(state: State<'_, AppState>, uri: String, line: u32, character: u32) -> Result<Value, String> {
    if !state.lsp.is_ready() {
        return Err("LSP not running".to_string());
    }
    let rx = state.lsp.send_request_async("textDocument/signatureHelp", json!({
        "textDocument": { "uri": uri },
        "position": { "line": line, "character": character },
    }))?;
    rx.await.map_err(|_| "LSP signature help request cancelled".to_string())
}

// ─── File watcher commands ──────────────────────────────────────────────────

#[tauri::command]
pub async fn start_file_watcher(
    state: State<'_, AppState>,
    app: tauri::AppHandle,
    project_path: String,
) -> Result<Value, String> {
    // Stop any existing watcher first
    {
        let mut guard = state.file_watcher.lock().map_err(|e| e.to_string())?;
        if let Some(existing) = guard.take() {
            drop(existing);
        }
    }

    let bridge = Arc::clone(&state.bridge);
    let watcher = FileWatcher::start(app, &project_path, bridge)?;

    {
        let mut guard = state.file_watcher.lock().map_err(|e| e.to_string())?;
        *guard = Some(watcher);
    }

    Ok(json!({
        "watching": true,
        "projectPath": project_path,
    }))
}

#[tauri::command]
pub async fn stop_file_watcher(state: State<'_, AppState>) -> Result<Value, String> {
    let mut guard = state.file_watcher.lock().map_err(|e| e.to_string())?;
    let was_watching = guard.is_some();
    *guard = None;
    Ok(json!({
        "stopped": was_watching,
    }))
}

#[tauri::command]
pub async fn file_watcher_status(state: State<'_, AppState>) -> Result<Value, String> {
    let guard = state.file_watcher.lock().map_err(|e| e.to_string())?;
    match &*guard {
        Some(watcher) => Ok(json!({
            "watching": true,
            "projectPath": watcher.project_path(),
        })),
        None => Ok(json!({
            "watching": false,
            "projectPath": null,
        })),
    }
}

// ─── UEFN Bridge commands ────────────────────────────────────────────────────

#[tauri::command]
pub async fn forge_bridge_connect(
    state: State<'_, AppState>,
    port: Option<u16>,
) -> Result<Value, String> {
    let params = match port {
        Some(p) => json!({"port": p}),
        None => json!({}),
    };
    state
        .bridge
        .call("bridge-connect", params)
        .await
        .or_else(|_| {
            Ok(json!({
                "connected": false,
                "error": "Sidecar not running"
            }))
        })
}

#[tauri::command]
pub async fn forge_bridge_status(state: State<'_, AppState>) -> Result<Value, String> {
    state
        .bridge
        .call("bridge-status", json!({}))
        .await
        .or_else(|_| {
            Ok(json!({
                "connected": false,
                "message": "Sidecar not running"
            }))
        })
}

#[tauri::command]
pub async fn forge_bridge_command(
    state: State<'_, AppState>,
    command: String,
    params: Option<Value>,
) -> Result<Value, String> {
    state
        .bridge
        .call(
            "bridge-command",
            json!({
                "command": command,
                "params": params.unwrap_or(json!({}))
            }),
        )
        .await
}

use notify::{Config, Event, EventKind, RecommendedWatcher, RecursiveMode, Watcher};
use serde::Serialize;
use std::collections::HashSet;
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};
use tauri::{AppHandle, Emitter};

/// Extensions we care about — everything else is ignored.
const WATCHED_EXTENSIONS: &[&str] = &["uasset", "umap", "verse", "uexp"];

/// How long to wait after the last file change before emitting an event.
/// UEFN writes dozens of files in rapid succession during a save.
const DEBOUNCE_DURATION: Duration = Duration::from_secs(2);

/// Minimum interval between auto-snapshots (seconds).
const AUTO_SNAPSHOT_COOLDOWN: Duration = Duration::from_secs(30);

/// Payload emitted to the Tauri frontend via `project:files-changed`.
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
pub struct FileChangeEvent {
    pub changed_files: Vec<String>,
    pub change_type: String, // "modified" | "created" | "deleted"
    pub timestamp: u64,
}

/// Per-type accumulator used during debouncing.
struct PendingChanges {
    modified: HashSet<String>,
    created: HashSet<String>,
    deleted: HashSet<String>,
    last_event_at: Instant,
}

impl PendingChanges {
    fn new() -> Self {
        Self {
            modified: HashSet::new(),
            created: HashSet::new(),
            deleted: HashSet::new(),
            last_event_at: Instant::now(),
        }
    }

    fn is_empty(&self) -> bool {
        self.modified.is_empty() && self.created.is_empty() && self.deleted.is_empty()
    }

    fn clear(&mut self) {
        self.modified.clear();
        self.created.clear();
        self.deleted.clear();
    }
}

pub struct FileWatcher {
    /// The `notify` watcher handle — dropping it stops the OS-level watch.
    _watcher: RecommendedWatcher,
    /// Flag the debounce thread checks before continuing.
    alive: Arc<AtomicBool>,
    /// The project path currently being watched.
    project_path: String,
}

impl FileWatcher {
    /// Start watching `project_path/Content/` for asset changes.
    ///
    /// The watcher will:
    /// 1. Debounce rapid changes (2 s after last change).
    /// 2. Emit `project:files-changed` Tauri events.
    /// 3. Optionally trigger an auto-snapshot via the sidecar (throttled to once per 30 s).
    pub fn start(
        app: AppHandle,
        project_path: &str,
        bridge: Arc<crate::sidecar::ForgeBridge>,
    ) -> Result<Self, String> {
        let content_dir = PathBuf::from(project_path).join("Content");
        if !content_dir.exists() {
            return Err(format!(
                "Content directory does not exist: {}",
                content_dir.display()
            ));
        }

        let alive = Arc::new(AtomicBool::new(true));
        let pending = Arc::new(Mutex::new(PendingChanges::new()));

        // ── notify channel ──────────────────────────────────────────────
        let (tx, rx) = std::sync::mpsc::channel::<notify::Result<Event>>();

        let mut watcher = RecommendedWatcher::new(tx, Config::default())
            .map_err(|e| format!("Failed to create watcher: {}", e))?;

        watcher
            .watch(&content_dir, RecursiveMode::Recursive)
            .map_err(|e| format!("Failed to watch {}: {}", content_dir.display(), e))?;

        eprintln!(
            "[file-watcher] Watching: {}",
            content_dir.display()
        );

        // ── Receiver thread — collects raw events into PendingChanges ───
        let pending_clone = Arc::clone(&pending);
        let alive_recv = Arc::clone(&alive);
        let project_path_owned = project_path.to_string();

        std::thread::spawn(move || {
            while alive_recv.load(Ordering::Relaxed) {
                match rx.recv_timeout(Duration::from_millis(500)) {
                    Ok(Ok(event)) => {
                        Self::accumulate_event(&event, &pending_clone, &project_path_owned);
                    }
                    Ok(Err(e)) => {
                        eprintln!("[file-watcher] Watch error: {}", e);
                    }
                    Err(std::sync::mpsc::RecvTimeoutError::Timeout) => {
                        // Just loop and check alive flag.
                    }
                    Err(std::sync::mpsc::RecvTimeoutError::Disconnected) => {
                        break;
                    }
                }
            }
        });

        // ── Debounce thread — flushes accumulated changes after quiet period ─
        let pending_flush = Arc::clone(&pending);
        let alive_flush = Arc::clone(&alive);
        let app_flush = app.clone();
        let bridge_flush = Arc::clone(&bridge);
        let last_snapshot = Arc::new(Mutex::new(Instant::now() - AUTO_SNAPSHOT_COOLDOWN));

        std::thread::spawn(move || {
            while alive_flush.load(Ordering::Relaxed) {
                std::thread::sleep(Duration::from_millis(500));

                let should_flush = {
                    let p = pending_flush.lock().unwrap();
                    !p.is_empty() && p.last_event_at.elapsed() >= DEBOUNCE_DURATION
                };

                if should_flush {
                    let (modified, created, deleted) = {
                        let mut p = pending_flush.lock().unwrap();
                        let m = p.modified.drain().collect::<Vec<_>>();
                        let c = p.created.drain().collect::<Vec<_>>();
                        let d = p.deleted.drain().collect::<Vec<_>>();
                        p.clear();
                        (m, c, d)
                    };

                    let now = SystemTime::now()
                        .duration_since(UNIX_EPOCH)
                        .unwrap_or_default()
                        .as_millis() as u64;

                    // Emit per-type events.
                    if !modified.is_empty() {
                        let _ = app_flush.emit(
                            "project:files-changed",
                            FileChangeEvent {
                                changed_files: modified,
                                change_type: "modified".into(),
                                timestamp: now,
                            },
                        );
                    }
                    if !created.is_empty() {
                        let _ = app_flush.emit(
                            "project:files-changed",
                            FileChangeEvent {
                                changed_files: created,
                                change_type: "created".into(),
                                timestamp: now,
                            },
                        );
                    }
                    if !deleted.is_empty() {
                        let _ = app_flush.emit(
                            "project:files-changed",
                            FileChangeEvent {
                                changed_files: deleted,
                                change_type: "deleted".into(),
                                timestamp: now,
                            },
                        );
                    }

                    // Auto-snapshot (throttled).
                    let should_snapshot = {
                        let last = last_snapshot.lock().unwrap();
                        last.elapsed() >= AUTO_SNAPSHOT_COOLDOWN
                    };

                    if should_snapshot && bridge_flush.is_ready() {
                        *last_snapshot.lock().unwrap() = Instant::now();
                        let bridge_snap = Arc::clone(&bridge_flush);
                        // Spawn onto the tokio runtime — we're on a plain std::thread,
                        // so we use spawn_blocking's reverse: build a one-shot runtime.
                        std::thread::spawn(move || {
                            let rt = tokio::runtime::Builder::new_current_thread()
                                .enable_all()
                                .build();
                            if let Ok(rt) = rt {
                                rt.block_on(async {
                                    match bridge_snap
                                        .call(
                                            "take-snapshot",
                                            serde_json::json!({"description": "Auto-snapshot — UEFN file change detected"}),
                                        )
                                        .await
                                    {
                                        Ok(_) => eprintln!("[file-watcher] Auto-snapshot taken"),
                                        Err(e) => eprintln!("[file-watcher] Auto-snapshot failed: {}", e),
                                    }
                                });
                            }
                        });
                    }
                }
            }
            eprintln!("[file-watcher] Debounce thread exiting");
        });

        Ok(Self {
            _watcher: watcher,
            alive,
            project_path: project_path.to_string(),
        })
    }

    /// Classify a notify event and add its paths to the pending set.
    fn accumulate_event(event: &Event, pending: &Arc<Mutex<PendingChanges>>, project_path: &str) {
        // Determine which bucket to put files in.
        let bucket = match &event.kind {
            EventKind::Create(_) => "created",
            EventKind::Modify(_) => "modified",
            EventKind::Remove(_) => "deleted",
            _ => return, // Access, Other, etc. — not interesting.
        };

        let wellversed_dir = PathBuf::from(project_path).join(".wellversed");

        for path in &event.paths {
            // Skip files inside .wellversed/ (our own staged writes / snapshots).
            if path.starts_with(&wellversed_dir) {
                continue;
            }

            // Only watch specific extensions.
            let ext = path
                .extension()
                .and_then(|e| e.to_str())
                .unwrap_or("")
                .to_lowercase();
            if !WATCHED_EXTENSIONS.contains(&ext.as_str()) {
                continue;
            }

            let path_str = path.to_string_lossy().to_string();

            let mut p = pending.lock().unwrap();
            p.last_event_at = Instant::now();
            match bucket {
                "created" => {
                    p.created.insert(path_str);
                }
                "modified" => {
                    // If we already recorded a create for this path, leave it as created.
                    if !p.created.contains(&path_str) {
                        p.modified.insert(path_str);
                    }
                }
                "deleted" => {
                    // Remove from created/modified if present, add to deleted.
                    p.created.remove(&path_str);
                    p.modified.remove(&path_str);
                    p.deleted.insert(path_str);
                }
                _ => {}
            }
        }
    }

    /// Returns the project path being watched.
    pub fn project_path(&self) -> &str {
        &self.project_path
    }
}

impl Drop for FileWatcher {
    fn drop(&mut self) {
        self.alive.store(false, Ordering::Relaxed);
        eprintln!("[file-watcher] Stopped watching: {}", self.project_path);
    }
}

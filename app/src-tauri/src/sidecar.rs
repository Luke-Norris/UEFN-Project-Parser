use serde::{Deserialize, Serialize};
use serde_json::Value;
use std::collections::HashMap;
use std::io::{BufRead, BufReader, Write};
use std::process::{Child, Command, Stdio};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use tokio::sync::oneshot;

#[derive(Debug, Serialize)]
struct SidecarRequest {
    id: String,
    method: String,
    params: Value,
}

#[derive(Debug, Deserialize)]
struct SidecarResponse {
    id: String,
    result: Option<Value>,
    error: Option<SidecarError>,
}

#[derive(Debug, Deserialize, Serialize, Clone)]
pub struct SidecarError {
    pub code: String,
    pub message: String,
    pub details: Option<Value>,
}

impl std::fmt::Display for SidecarError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        write!(f, "{}", self.message)
    }
}

impl std::error::Error for SidecarError {}

type PendingMap = Arc<Mutex<HashMap<String, oneshot::Sender<Result<Value, SidecarError>>>>>;

pub struct ForgeBridge {
    process: Mutex<Option<Child>>,
    pending: PendingMap,
    request_id: AtomicU64,
    stdin: Mutex<Option<std::process::ChildStdin>>,
    ready: Arc<tokio::sync::Notify>,
    is_ready: Arc<std::sync::atomic::AtomicBool>,
}

impl ForgeBridge {
    pub fn new() -> Self {
        Self {
            process: Mutex::new(None),
            pending: Arc::new(Mutex::new(HashMap::new())),
            request_id: AtomicU64::new(0),
            stdin: Mutex::new(None),
            ready: Arc::new(tokio::sync::Notify::new()),
            is_ready: Arc::new(std::sync::atomic::AtomicBool::new(false)),
        }
    }

    /// Start the .NET sidecar. Pre-builds, then runs with --no-build for clean stdout.
    pub fn start(&self, repo_root: &str) -> Result<(), String> {
        let cli_project = format!("{}/src/FortniteForge.CLI", repo_root);

        // Pre-build the sidecar
        eprintln!("[forge-bridge] Building sidecar...");
        let build_status = Command::new("dotnet")
            .args(["build", &cli_project, "-v", "q"])
            .current_dir(repo_root)
            .stdout(Stdio::null())
            .stderr(Stdio::piped())
            .status()
            .map_err(|e| format!("Failed to spawn dotnet build: {}", e))?;

        if !build_status.success() {
            return Err("Sidecar build failed".to_string());
        }

        eprintln!("[forge-bridge] Build succeeded, starting sidecar...");

        let mut child = Command::new("dotnet")
            .args([
                "run",
                "--project",
                &cli_project,
                "--no-build",
                "--",
                "sidecar",
            ])
            .current_dir(repo_root)
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .spawn()
            .map_err(|e| format!("Failed to spawn sidecar: {}", e))?;

        let stdout = child.stdout.take().ok_or("No stdout")?;
        let stdin = child.stdin.take().ok_or("No stdin")?;
        let stderr = child.stderr.take().ok_or("No stderr")?;

        *self.stdin.lock().unwrap() = Some(stdin);
        *self.process.lock().unwrap() = Some(child);

        // Spawn stderr reader
        std::thread::spawn(move || {
            let reader = BufReader::new(stderr);
            for line in reader.lines() {
                if let Ok(line) = line {
                    eprintln!("[forge] {}", line);
                }
            }
        });

        // Spawn stdout reader (NDJSON responses)
        let pending = self.pending.clone();
        let ready = self.ready.clone();
        let is_ready = self.is_ready.clone();

        std::thread::spawn(move || {
            let reader = BufReader::new(stdout);
            for line in reader.lines() {
                let line = match line {
                    Ok(l) => l,
                    Err(_) => break,
                };

                let response: SidecarResponse = match serde_json::from_str(&line) {
                    Ok(r) => r,
                    Err(_) => continue, // Skip non-JSON lines
                };

                // Handle ready signal
                if response.id == "ready" {
                    eprintln!("[forge-bridge] Sidecar ready");
                    is_ready.store(true, Ordering::SeqCst);
                    ready.notify_waiters();
                    continue;
                }

                // Route response to pending request
                let sender = {
                    let mut map = pending.lock().unwrap();
                    map.remove(&response.id)
                };

                if let Some(sender) = sender {
                    let result = if let Some(err) = response.error {
                        Err(err)
                    } else {
                        Ok(response.result.unwrap_or(Value::Null))
                    };
                    let _ = sender.send(result);
                }
            }
        });

        Ok(())
    }

    /// Send a request to the sidecar and wait for the response.
    pub async fn call(&self, method: &str, params: Value) -> Result<Value, String> {
        // If not ready, wait briefly — don't block for ages
        if !self.is_ready.load(Ordering::SeqCst) {
            // Check if stdin exists (sidecar process spawned but not yet ready)
            let has_stdin = self.stdin.lock().unwrap().is_some();
            if !has_stdin {
                // Sidecar hasn't even spawned yet — wait up to 30s for the build
                tokio::select! {
                    _ = self.ready.notified() => {},
                    _ = tokio::time::sleep(std::time::Duration::from_secs(30)) => {
                        return Err("Sidecar is still starting — please wait a moment and try again".to_string());
                    }
                }
            } else {
                // Process spawned but hasn't sent ready signal — short wait
                tokio::select! {
                    _ = self.ready.notified() => {},
                    _ = tokio::time::sleep(std::time::Duration::from_secs(5)) => {
                        eprintln!("[forge-bridge] Ready timeout — proceeding anyway");
                    }
                }
            }
        }

        let id = format!("req-{}", self.request_id.fetch_add(1, Ordering::SeqCst));

        let (tx, rx) = oneshot::channel();

        {
            let mut map = self.pending.lock().unwrap();
            map.insert(id.clone(), tx);
        }

        let request = SidecarRequest {
            id: id.clone(),
            method: method.to_string(),
            params,
        };

        let line =
            serde_json::to_string(&request).map_err(|e| format!("Serialize error: {}", e))?;

        {
            let mut stdin_guard = self.stdin.lock().unwrap();
            if let Some(ref mut stdin) = *stdin_guard {
                writeln!(stdin, "{}", line).map_err(|e| format!("Write error: {}", e))?;
                stdin.flush().map_err(|e| format!("Flush error: {}", e))?;
            } else {
                return Err("Sidecar not running".to_string());
            }
        }

        match tokio::time::timeout(std::time::Duration::from_secs(30), rx).await {
            Ok(Ok(Ok(value))) => Ok(value),
            Ok(Ok(Err(sidecar_err))) => Err(sidecar_err.message),
            Ok(Err(_)) => Err("Sidecar channel closed".to_string()),
            Err(_) => {
                // Remove pending request on timeout
                let mut map = self.pending.lock().unwrap();
                map.remove(&id);
                Err("Sidecar request timeout".to_string())
            }
        }
    }

    pub fn is_ready(&self) -> bool {
        self.is_ready.load(Ordering::SeqCst)
    }
}

impl Drop for ForgeBridge {
    fn drop(&mut self) {
        // Close stdin to signal the sidecar to exit
        *self.stdin.lock().unwrap() = None;
        if let Some(mut child) = self.process.lock().unwrap().take() {
            let _ = child.kill();
        }
    }
}

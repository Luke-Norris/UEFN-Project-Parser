use serde_json::Value;
use std::collections::HashMap;
use std::io::{BufRead, BufReader, Write};
use std::process::{Child, Command, Stdio};
use std::sync::atomic::{AtomicI64, Ordering};
use std::sync::{Arc, Mutex};
use tokio::sync::oneshot;

type LspPendingMap = Arc<Mutex<HashMap<i64, oneshot::Sender<Value>>>>;

/// Callback for LSP notifications (diagnostics, etc.)
type NotificationCallback = Arc<Mutex<Option<Box<dyn Fn(String, Value) + Send + 'static>>>>;

/// Bridge to Epic's verse-lsp.exe process.
/// Communicates via standard LSP JSON-RPC over stdio with Content-Length framing.
pub struct LspBridge {
    process: Mutex<Option<Child>>,
    pending: LspPendingMap,
    request_id: AtomicI64,
    stdin: Mutex<Option<std::process::ChildStdin>>,
    initialized: Arc<std::sync::atomic::AtomicBool>,
    server_capabilities: Arc<Mutex<Option<Value>>>,
    on_notification: NotificationCallback,
}

impl LspBridge {
    pub fn new() -> Self {
        Self {
            process: Mutex::new(None),
            pending: Arc::new(Mutex::new(HashMap::new())),
            request_id: AtomicI64::new(1),
            stdin: Mutex::new(None),
            initialized: Arc::new(std::sync::atomic::AtomicBool::new(false)),
            server_capabilities: Arc::new(Mutex::new(None)),
            on_notification: Arc::new(Mutex::new(None)),
        }
    }

    /// Set a callback for LSP notifications (diagnostics, etc.)
    pub fn set_notification_handler<F>(&self, handler: F)
    where
        F: Fn(String, Value) + Send + 'static,
    {
        *self.on_notification.lock().unwrap() = Some(Box::new(handler));
    }

    /// Find Epic's verse-lsp.exe binary
    pub fn find_lsp_binary() -> Option<String> {
        // Check VS Code extensions directory
        if let Some(home) = dirs_next_home() {
            let vscode_ext = std::path::Path::new(&home).join(".vscode").join("extensions");
            if vscode_ext.exists() {
                // Find the epicgames.verse-* extension
                if let Ok(entries) = std::fs::read_dir(&vscode_ext) {
                    let mut verse_dirs: Vec<_> = entries
                        .filter_map(|e| e.ok())
                        .filter(|e| {
                            e.file_name()
                                .to_string_lossy()
                                .starts_with("epicgames.verse-")
                        })
                        .collect();
                    // Sort to get the latest version
                    verse_dirs.sort_by(|a, b| b.file_name().cmp(&a.file_name()));

                    for dir in verse_dirs {
                        let lsp_path = dir.path().join("bin").join("Win64").join("verse-lsp.exe");
                        if lsp_path.exists() {
                            return Some(lsp_path.to_string_lossy().to_string());
                        }
                    }
                }
            }
        }
        None
    }

    /// Copy binary to temp to avoid file locking (same approach as Epic's extension)
    fn copy_to_temp(lsp_binary: &str) -> Result<String, String> {
        let src = std::path::Path::new(lsp_binary);
        let ext = src.extension().map(|e| e.to_string_lossy().to_string()).unwrap_or_default();
        let pid = std::process::id();
        let temp_name = format!("verse-lsp-wellversed-{}.{}", pid, ext);
        let temp_path = std::env::temp_dir().join(&temp_name);

        std::fs::copy(src, &temp_path)
            .map_err(|e| format!("Failed to copy verse-lsp to temp: {}", e))?;

        Ok(temp_path.to_string_lossy().to_string())
    }

    /// Start the LSP server and send initialize request
    pub fn start(&self, lsp_binary: &str, workspace_root: &str) -> Result<(), String> {
        // Copy to temp dir to avoid file locking
        let binary_path = Self::copy_to_temp(lsp_binary)?;

        let mut child = Command::new(&binary_path)
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped())
            .spawn()
            .map_err(|e| format!("Failed to spawn verse-lsp: {}", e))?;

        let stdout = child.stdout.take().ok_or("No stdout")?;
        let stderr = child.stderr.take().ok_or("No stderr")?;
        let stdin = child.stdin.take().ok_or("No stdin")?;

        *self.stdin.lock().unwrap() = Some(stdin);
        *self.process.lock().unwrap() = Some(child);

        // Spawn stdout reader (LSP responses)
        let pending = Arc::clone(&self.pending);
        let capabilities = Arc::clone(&self.server_capabilities);
        let initialized = Arc::clone(&self.initialized);
        let on_notification = Arc::clone(&self.on_notification);
        std::thread::spawn(move || {
            let mut reader = BufReader::new(stdout);
            loop {
                match read_lsp_message(&mut reader) {
                    Ok(Some(msg)) => {
                        if let Some(id) = msg.get("id").and_then(|v| v.as_i64()) {
                            // Response to a request
                            if let Some(result) = msg.get("result") {
                                // Check if this is the initialize response
                                if let Some(caps) = result.get("capabilities") {
                                    *capabilities.lock().unwrap() = Some(caps.clone());
                                    initialized.store(true, Ordering::SeqCst);
                                }
                                if let Ok(mut pending) = pending.lock() {
                                    if let Some(sender) = pending.remove(&id) {
                                        let _ = sender.send(result.clone());
                                    }
                                }
                            } else if let Some(error) = msg.get("error") {
                                if let Ok(mut pending) = pending.lock() {
                                    if let Some(sender) = pending.remove(&id) {
                                        let _ = sender.send(error.clone());
                                    }
                                }
                            }
                        } else if let Some(method) = msg.get("method").and_then(|v| v.as_str()) {
                            // Notification (no id) — diagnostics, progress, etc.
                            let params = msg.get("params").cloned().unwrap_or(Value::Null);
                            if let Ok(handler) = on_notification.lock() {
                                if let Some(ref cb) = *handler {
                                    cb(method.to_string(), params);
                                }
                            }
                        }
                    }
                    Ok(None) => break, // EOF
                    Err(e) => {
                        eprintln!("[verse-lsp] Read error: {}", e);
                        break;
                    }
                }
            }
        });

        // Spawn stderr reader (log messages)
        std::thread::spawn(move || {
            let reader = BufReader::new(stderr);
            for line in reader.lines() {
                if let Ok(line) = line {
                    eprintln!("[verse-lsp stderr] {}", line);
                }
            }
        });

        // Send initialize request
        let root_uri = format!("file:///{}", workspace_root.replace('\\', "/"));
        let init_params = serde_json::json!({
            "processId": std::process::id(),
            "rootUri": root_uri,
            "capabilities": {
                "textDocument": {
                    "completion": {
                        "completionItem": {
                            "snippetSupport": true,
                            "labelDetailsSupport": true
                        }
                    },
                    "hover": {
                        "contentFormat": ["markdown", "plaintext"]
                    },
                    "signatureHelp": {
                        "signatureInformation": {
                            "parameterInformation": {
                                "labelOffsetSupport": true
                            }
                        }
                    },
                    "definition": {},
                    "documentSymbol": {
                        "hierarchicalDocumentSymbolSupport": true
                    },
                    "publishDiagnostics": {}
                },
                "workspace": {
                    "workspaceFolders": true
                }
            },
            "workspaceFolders": [{
                "uri": root_uri,
                "name": "project"
            }]
        });

        self.send_request("initialize", init_params)?;

        // Wait for initialize response (up to 10 seconds)
        let start = std::time::Instant::now();
        while !self.initialized.load(Ordering::SeqCst) {
            if start.elapsed().as_secs() > 10 {
                return Err("LSP initialize timed out".to_string());
            }
            std::thread::sleep(std::time::Duration::from_millis(100));
        }

        // Send initialized notification
        self.send_notification("initialized", serde_json::json!({}))?;

        Ok(())
    }

    /// Send an LSP request and return a receiver for the response
    pub fn send_request_async(&self, method: &str, params: Value) -> Result<oneshot::Receiver<Value>, String> {
        let id = self.request_id.fetch_add(1, Ordering::SeqCst);
        let msg = serde_json::json!({
            "jsonrpc": "2.0",
            "id": id,
            "method": method,
            "params": params,
        });

        let (tx, rx) = oneshot::channel();
        self.pending.lock().unwrap().insert(id, tx);

        self.write_lsp_message(&msg)?;
        Ok(rx)
    }

    /// Send an LSP request and block for the response
    fn send_request(&self, method: &str, params: Value) -> Result<(), String> {
        let id = self.request_id.fetch_add(1, Ordering::SeqCst);
        let msg = serde_json::json!({
            "jsonrpc": "2.0",
            "id": id,
            "method": method,
            "params": params,
        });

        let (tx, _rx) = oneshot::channel();
        self.pending.lock().unwrap().insert(id, tx);

        self.write_lsp_message(&msg)
    }

    /// Send an LSP notification (no response expected)
    pub fn send_notification(&self, method: &str, params: Value) -> Result<(), String> {
        let msg = serde_json::json!({
            "jsonrpc": "2.0",
            "method": method,
            "params": params,
        });
        self.write_lsp_message(&msg)
    }

    /// Check if LSP is initialized and ready
    pub fn is_ready(&self) -> bool {
        self.initialized.load(Ordering::SeqCst)
    }

    /// Get server capabilities
    pub fn capabilities(&self) -> Option<Value> {
        self.server_capabilities.lock().unwrap().clone()
    }

    /// Write a JSON-RPC message with Content-Length framing
    fn write_lsp_message(&self, msg: &Value) -> Result<(), String> {
        let body = serde_json::to_string(msg).map_err(|e| e.to_string())?;
        let header = format!("Content-Length: {}\r\n\r\n", body.len());

        let mut stdin = self.stdin.lock().unwrap();
        if let Some(ref mut stdin) = *stdin {
            stdin
                .write_all(header.as_bytes())
                .map_err(|e| e.to_string())?;
            stdin
                .write_all(body.as_bytes())
                .map_err(|e| e.to_string())?;
            stdin.flush().map_err(|e| e.to_string())?;
            Ok(())
        } else {
            Err("LSP stdin not available".to_string())
        }
    }

    /// Shut down the LSP server
    pub fn stop(&self) {
        // Send shutdown request
        let _ = self.send_request("shutdown", Value::Null);
        std::thread::sleep(std::time::Duration::from_millis(500));

        // Send exit notification
        let _ = self.send_notification("exit", Value::Null);

        // Kill process if still running
        if let Ok(mut process) = self.process.lock() {
            if let Some(ref mut child) = *process {
                let _ = child.kill();
            }
            *process = None;
        }

        *self.stdin.lock().unwrap() = None;
        self.initialized.store(false, Ordering::SeqCst);
    }
}

impl Drop for LspBridge {
    fn drop(&mut self) {
        self.stop();
    }
}

// ─── LSP message framing ────────────────────────────────────────────────────

fn read_lsp_message(reader: &mut BufReader<std::process::ChildStdout>) -> Result<Option<Value>, String> {
    // Read headers until empty line
    let mut content_length: usize = 0;
    loop {
        let mut header = String::new();
        let bytes_read = reader.read_line(&mut header).map_err(|e| e.to_string())?;
        if bytes_read == 0 {
            return Ok(None); // EOF
        }
        let trimmed = header.trim();
        if trimmed.is_empty() {
            break;
        }
        if let Some(len_str) = trimmed.strip_prefix("Content-Length: ") {
            content_length = len_str
                .parse()
                .map_err(|e: std::num::ParseIntError| e.to_string())?;
        }
    }

    if content_length == 0 {
        return Err("No Content-Length header".to_string());
    }

    // Read body
    let mut body = vec![0u8; content_length];
    std::io::Read::read_exact(reader, &mut body).map_err(|e| e.to_string())?;
    let text = String::from_utf8(body).map_err(|e| e.to_string())?;
    let value: Value = serde_json::from_str(&text).map_err(|e| e.to_string())?;
    Ok(Some(value))
}

/// Get user's home directory
fn dirs_next_home() -> Option<String> {
    std::env::var("USERPROFILE")
        .or_else(|_| std::env::var("HOME"))
        .ok()
}

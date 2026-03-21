"""Take a full-resolution screenshot of the Tauri WebView2 app via CDP."""
import websocket, json, base64, urllib.request, sys, os

def screenshot(filename="screenshot.png", navigate_to=None):
    pages = json.loads(urllib.request.urlopen('http://localhost:9222/json').read())
    if not pages:
        print("No WebView2 pages found"); return

    page_id = pages[0]['id']
    ws = websocket.create_connection(f'ws://localhost:9222/devtools/page/{page_id}')
    msg_id = 0

    def send(method, params=None):
        nonlocal msg_id
        msg_id += 1
        ws.send(json.dumps({'id': msg_id, 'method': method, 'params': params or {}}))
        return json.loads(ws.recv())

    # Navigate if requested (click sidebar items via JS)
    if navigate_to:
        send('Runtime.evaluate', {'expression': navigate_to})
        import time; time.sleep(2)

    # Capture at full resolution
    result = send('Page.captureScreenshot', {'format': 'png', 'captureBeyondViewport': False})

    if 'result' in result and 'data' in result['result']:
        img = base64.b64decode(result['result']['data'])
        out = os.path.join(os.path.dirname(__file__), '..', filename)
        with open(out, 'wb') as f:
            f.write(img)
        print(f"Saved {out} ({len(img)} bytes)")
    else:
        print("Error:", result)

    ws.close()

if __name__ == '__main__':
    name = sys.argv[1] if len(sys.argv) > 1 else "screenshot.png"
    js = sys.argv[2] if len(sys.argv) > 2 else None
    screenshot(name, js)

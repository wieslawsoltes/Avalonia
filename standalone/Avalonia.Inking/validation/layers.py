#!/usr/bin/env python3
"""Regress the inspector scrollbar overlap through actual browser hit testing and clicks."""
import functools,http.server,json,pathlib,threading
from playwright.sync_api import sync_playwright
ROOT=pathlib.Path(__file__).resolve().parents[1]
OUT=ROOT/'artifacts'/'validation';OUT.mkdir(parents=True,exist_ok=True)
root=next(p.parent for p in (ROOT/'artifacts'/'browser').rglob('index.html') if (p.parent/'_framework').is_dir())
class Handler(http.server.SimpleHTTPRequestHandler):
    def log_message(self,*_):pass
server=http.server.ThreadingHTTPServer(('127.0.0.1',0),functools.partial(Handler,directory=str(root)))
threading.Thread(target=server.serve_forever,daemon=True).start()
with sync_playwright() as pw:
    browser=pw.chromium.launch(headless=True,args=['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader'])
    page=browser.new_page(viewport={'width':1440,'height':1000})
    page.goto(f'http://127.0.0.1:{server.server_port}/')
    page.wait_for_function('window.inkTest && window.inkTest.Ready()',timeout=90000)
    def settle():page.evaluate('()=>new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r)))')
    def diag(label):
        data=page.evaluate('JSON.parse(window.inkTest.LayerDiagnostics())')
        print('LAYER STATE',label,json.dumps(data),flush=True)
        return data
    page.evaluate('window.inkTest.Reset()');settle();diag('reset')
    page.evaluate('window.inkTest.ScrollTo("add-layer")');settle();before=diag('scrolled')
    assert before['sameSession'] and before['buttonCommandMatches'] and before['buttonEnabled'],before
    assert any('add-layer' in name for name in before['hitPath']),before
    r=page.evaluate('JSON.parse(window.inkTest.Bounds("add-layer"))')
    page.mouse.click(r['x']+r['width']/2,r['y']+r['height']/2);settle()
    after=diag('clicked');page.screenshot(path=str(OUT/'layers-focused.png'))
    (OUT/'layers-focused.json').write_text(json.dumps({'before':before,'after':after},indent=2))
    browser.close();server.shutdown()
    assert after['documentLayers']==2 and after['clicks']==1 and after['presses']==1 and after['releases']==1,after
print('PASS: layer command, center-point hit target and real routed pointer click',flush=True)

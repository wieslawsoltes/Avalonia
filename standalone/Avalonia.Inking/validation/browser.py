#!/usr/bin/env python3
"""Exercise the published Avalonia WebAssembly application through real browser input.

Probe methods only provide deterministic fixtures and observations. Drawing, editing,
commands, clipboard and files go through the browser/Avalonia input and storage paths.
"""
from __future__ import annotations
import functools
import http.server
import json
import math
import pathlib
import threading
import time
import traceback
import xml.etree.ElementTree as ET
from PIL import Image
from playwright.sync_api import sync_playwright

ROOT = pathlib.Path(__file__).resolve().parents[1]
OUT = ROOT / 'artifacts' / 'validation'
OUT.mkdir(parents=True, exist_ok=True)
DOWNLOADS = OUT / 'downloads'
DOWNLOADS.mkdir(exist_ok=True)
results, console, errors = [], [], []

class Handler(http.server.SimpleHTTPRequestHandler):
    extensions_map = {**http.server.SimpleHTTPRequestHandler.extensions_map, '.wasm': 'application/wasm', '.js': 'text/javascript', '.mjs': 'text/javascript', '.json': 'application/json'}
    def end_headers(self):
        self.send_header('Cache-Control', 'no-store')
        super().end_headers()
    def log_message(self, *_):
        pass

# .NET 10 fingerprints framework JavaScript. Require the actual framework directory,
# not a particular unfingerprinted loader filename. The generated import map owns URLs.
candidates = [p.parent for p in (ROOT / 'artifacts' / 'browser').rglob('index.html')
              if (p.parent / '_framework').is_dir() and any((p.parent / '_framework').glob('dotnet*.js'))]
if not candidates:
    listing = [str(p.relative_to(ROOT)) for p in (ROOT / 'artifacts' / 'browser').rglob('*') if p.is_file()]
    print(json.dumps({'publishedFiles': listing}, indent=2), flush=True)
    raise RuntimeError('Published index.html and WebAssembly framework assets were not found')
webroot = sorted(candidates, key=lambda p: len(p.parts))[0]
print('Browser web root:', webroot, flush=True)
server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), functools.partial(Handler, directory=str(webroot)))
threading.Thread(target=server.serve_forever, daemon=True).start()
url = f'http://127.0.0.1:{server.server_port}/'

with sync_playwright() as playwright:
    browser = playwright.chromium.launch(headless=True, args=['--use-gl=angle', '--use-angle=swiftshader', '--enable-unsafe-swiftshader'])
    context = browser.new_context(viewport={'width': 1440, 'height': 1000}, device_scale_factor=1, accept_downloads=True, permissions=['clipboard-read', 'clipboard-write'])
    page = context.new_page()
    page.on('console', lambda msg: console.append({'type': msg.type, 'text': msg.text}))
    page.on('pageerror', lambda error: errors.append(str(error)))
    cdp = context.new_cdp_session(page)

    def settle():
        page.evaluate('() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)))')

    def ready():
        page.wait_for_function('() => (window.inkStartupErrors?.length > 0) || (window.inkTest && window.inkTest.Ready())', timeout=120000)
        startup = page.evaluate('window.inkStartupErrors || []')
        assert not startup, startup
        settle()

    def snapshot():
        return page.evaluate('JSON.parse(window.inkTest.Snapshot())')

    def document():
        return page.evaluate('window.inkTest.DocumentJson()')

    def reset():
        cdp.send('Emulation.setTouchEmulationEnabled', {'enabled': False})
        page.evaluate('window.inkTest.Reset()')
        settle()

    def bounds(id):
        r = page.evaluate('(id) => JSON.parse(window.inkTest.Bounds(id))', id)
        assert r and r['width'] > 0 and r['height'] > 0, f'Missing visual bounds: {id}'
        return r

    def click(id, scroll=False):
        if scroll:
            page.evaluate('(id) => window.inkTest.ScrollTo(id)', id)
            settle()
        r = bounds(id)
        assert 0 <= r['y'] + r['height'] / 2 < page.viewport_size['height'], f'{id} is outside viewport: {r}'
        page.mouse.click(r['x'] + r['width'] / 2, r['y'] + r['height'] / 2)
        settle()

    def at(x, y):
        r = bounds('ink-canvas')
        return r['x'] + x, r['y'] + y

    def draw(points, steps=5):
        page.mouse.move(*at(*points[0])); page.mouse.down()
        for p in points[1:]:
            page.mouse.move(*at(*p), steps=steps)
        page.mouse.up(); settle()

    def drag_screen(a, b, steps=12):
        page.mouse.move(*a); page.mouse.down(); page.mouse.move(*b, steps=steps); page.mouse.up(); settle()

    def pen(points):
        for i, (x, y, pressure) in enumerate(points):
            px, py = at(x, y)
            cdp.send('Input.dispatchMouseEvent', {'type': 'mousePressed' if i == 0 else 'mouseMoved', 'x': px, 'y': py, 'button': 'left', 'buttons': 1, 'clickCount': 1, 'pointerType': 'pen', 'force': pressure, 'tiltX': 35, 'tiltY': -15, 'twist': 55})
        px, py = at(points[-1][0], points[-1][1])
        cdp.send('Input.dispatchMouseEvent', {'type': 'mouseReleased', 'x': px, 'y': py, 'button': 'left', 'buttons': 0, 'clickCount': 1, 'pointerType': 'pen', 'force': 0})
        settle()

    def touch_point(id, x, y):
        px, py = at(x, y)
        return {'id': id, 'x': px, 'y': py, 'radiusX': 2, 'radiusY': 2, 'force': .5}

    def test(name, action):
        start = time.perf_counter()
        try:
            detail = action()
            results.append({'name': name, 'passed': True, 'milliseconds': round((time.perf_counter()-start)*1000, 2), 'detail': detail})
            print('PASS', name, flush=True)
        except Exception as error:
            results.append({'name': name, 'passed': False, 'milliseconds': round((time.perf_counter()-start)*1000, 2), 'detail': str(error), 'traceback': traceback.format_exc()})
            print('FAIL', name, str(error), flush=True)
            try:
                page.mouse.up(); page.keyboard.press('Escape')
                page.screenshot(path=str(OUT / f'failure-{len(results):02d}.png'))
                (OUT / f'failure-{len(results):02d}.json').write_text(json.dumps(snapshot(), indent=2))
            except Exception:
                pass

    try:
        page.goto(url, wait_until='domcontentloaded'); ready()
        page.screenshot(path=str(OUT / 'studio-light.png'))

        def boot():
            s = snapshot()
            assert s['browser'] and s['ready'] and s['presenter'] and s['adorner'], s
            assert page.locator('canvas').count() > 0 and bounds('ink-canvas')['width'] >= 600
            return {'runtime': s['avalonia'], 'canvas': bounds('ink-canvas'), 'browser': browser.version}
        test('Actual Avalonia WebAssembly boots with both template parts', boot)

        def core_specs():
            r = page.evaluate('JSON.parse(window.inkTest.RunCoreTests())')
            (OUT / 'core-browser.json').write_text(json.dumps(r, indent=2))
            assert r['failed'] == 0 and r['total'] >= 78, r
            return {'passed': r['passed'], 'total': r['total']}
        test('Shared engine regression suite executes in WebAssembly', core_specs)

        def contracts():
            r = page.evaluate('JSON.parse(window.inkTest.ControlContracts())')
            assert all(r.values()), r
            return r
        test('Lookless controls retain DataContext and independent default sessions', contracts)

        def mouse_ink():
            reset(); draw([(90,110),(180,135),(270,100),(380,145)],15)
            s=snapshot(); assert s['strokes']==1 and s['undo']==1 and s['last']['samples']>=20, s
            assert s['last']['pressureMin']==1 and s['pointer']=='Mouse', s
            return s['last']
        test('Mouse drawing commits one multi-sample stroke and one undo item', mouse_ink)

        def tap():
            reset(); draw([(140,130)]); s=snapshot()
            assert s['strokes']==1 and s['last']['samples']>=1, s
        test('Stationary tap remains ink', tap)

        def history():
            reset(); draw([(100,120),(330,120)],20); original=document()
            page.keyboard.press('Control+z'); settle(); assert snapshot()['strokes']==0
            page.keyboard.press('Control+Shift+z'); settle(); assert document()==original
            click('undo'); assert snapshot()['strokes']==0
            click('redo'); assert document()==original
        test('Keyboard and toolbar undo/redo restore exact document snapshots', history)

        def cancel_ink():
            reset(); before=document(); page.mouse.move(*at(100,100)); page.mouse.down(); page.mouse.move(*at(300,200),steps=18)
            page.keyboard.press('Escape'); page.mouse.up(); settle(); s=snapshot()
            assert document()==before and s['undo']==0 and s['cancelled']>0, s
        test('Escape cancels live ink without consuming undo history', cancel_ink)

        def pressure():
            reset(); pen([(90+i*9,140+math.sin(i*.3)*10,.08+.9*i/29) for i in range(30)])
            s=snapshot(); assert s['strokes']==1 and s['pointer']=='Pen', s
            assert s['last']['pressureMin']<.2 and s['last']['pressureMax']>.85, s
            assert s['last']['tiltMax']>=30 and s['last']['twistMax']>=50, s
            return s['last']
        test('Browser pen pressure, tilt and twist reach the managed stroke', pressure)

        def brushes():
            reset(); click('tool-Pencil'); draw([(100,160),(330,160)],20)
            assert snapshot()['last']['kind']=='Pencil'
            click('tool-Highlighter'); draw([(100,220),(330,220)],20); s=snapshot()
            assert s['strokes']==2 and s['last']['kind']=='Highlighter' and s['last']['opacity']<.5, s
        test('Pencil and highlighter tool buttons change brush behavior', brushes)

        def shapes():
            reset(); click('tool-Line'); page.keyboard.down('Shift'); draw([(100,100),(300,130)]); page.keyboard.up('Shift')
            p=json.loads(document())['layers'][0]['strokes'][0]['points']; assert abs(p[0][1]-p[-1][1])<.01
            click('tool-Rectangle'); page.keyboard.down('Shift'); draw([(100,200),(230,250)]); page.keyboard.up('Shift')
            r=snapshot()['last']['bounds']; assert abs(r['width']-r['height'])<.01
            click('tool-Ellipse'); draw([(350,220),(470,330)]); assert snapshot()['strokes']==3
        test('Constrained line, rectangle and ellipse tools produce editable shapes', shapes)

        def selection_move():
            reset(); draw([(100,180),(350,180)],25); click('tool-Select')
            draw([(80,155),(380,205)]); assert snapshot()['selected']==1
            draw([(200,180),(240,215)],12); s=snapshot()
            assert abs(s['last']['transform'][4]-40)<1 and abs(s['last']['transform'][5]-35)<1, s
            page.keyboard.press('Control+z'); settle(); assert snapshot()['last']['transform']==[1,0,0,1,0,0]
        test('Box selection and pointer translation preserve raw ink and undo', selection_move)

        def lasso():
            reset(); draw([(100,170),(350,170)],20); draw([(100,290),(350,290)],20)
            click('tool-Lasso'); draw([(70,130),(380,130),(380,205),(70,205),(70,130)],8)
            assert snapshot()['selected']==1
            click('delete'); assert snapshot()['strokes']==1
        test('Lasso selects crossing ink and deletion retains unselected strokes', lasso)

        def resize_rotate():
            reset(); click('tool-Rectangle'); draw([(130,180),(280,280)]); click('select-all')
            before=snapshot(); a=before['handles'][4]; drag_screen(a,[a[0]+80,a[1]+50])
            after=snapshot(); assert after['selectionBounds']['width']>before['selectionBounds']['width']+60, after
            handle=after['handles'][8]; r=bounds('ink-canvas'); box=after['selectionBounds']
            drag_screen(handle,[r['x']+box['x']+box['width']+30,r['y']+box['y']+box['height']/2])
            s=snapshot(); assert abs(s['last']['transform'][1])>.3, s
            return {'before':before['selectionBounds'],'after':s['selectionBounds']}
        test('Selection handles resize and rotate affine geometry', resize_rotate)

        def duplicate_nudge():
            reset(); draw([(100,160),(250,160)],16); click('select-all'); click('duplicate')
            s=snapshot(); assert s['strokes']==2 and s['selected']==1, s
            click('tool-Select'); before=snapshot()['last']['transform'][4]
            page.keyboard.press('Shift+ArrowRight'); settle(); assert abs(snapshot()['last']['transform'][4]-before-10)<.01
        test('Duplicate allocates new identities and keyboard nudge edits selection', duplicate_nudge)

        def erasers():
            reset(); draw([(100,190),(400,190)],20); click('tool-PointEraser'); draw([(250,140),(250,240)],1)
            s=snapshot(); assert s['strokes']==2 and s['undo']==2, s
            click('undo'); assert snapshot()['strokes']==1
            click('tool-StrokeEraser'); draw([(250,140),(250,240)],1); assert snapshot()['strokes']==0
        test('Fast partial eraser splits strokes and whole-stroke eraser removes them', erasers)

        def cancel_eraser():
            reset(); draw([(100,200),(400,200)],20); before=document(); count=snapshot()['undo']
            click('tool-PointEraser'); page.mouse.move(*at(230,160)); page.mouse.down(); page.mouse.move(*at(230,240),steps=3)
            assert snapshot()['editing']; page.keyboard.press('Escape'); page.mouse.up(); settle()
            assert document()==before and snapshot()['undo']==count
        test('Cancelled eraser restores exact ink and history', cancel_eraser)

        def layers():
            reset(); click('add-layer',True); assert snapshot()['layers']==2
            click('layer-lock',True); assert snapshot()['activeLocked']
            draw([(90,120),(270,120)],10); assert snapshot()['strokes']==0
            click('layer-lock',True); draw([(90,120),(270,120)],10); assert snapshot()['strokes']==1
            click('layer-visibility',True); assert not snapshot()['activeVisible']
            draw([(90,160),(270,160)],10); assert snapshot()['strokes']==1
            click('layer-visibility',True); assert snapshot()['activeVisible']
        test('Layer controls enforce lock and visibility on input', layers)

        def read_only():
            reset(); draw([(90,120),(270,120)],10); before=document()
            click('read-only',True); assert snapshot()['readOnly']
            draw([(90,170),(270,170)],10); assert document()==before
            click('select-all'); click('delete'); assert document()==before
            click('read-only',True); assert not snapshot()['readOnly']
        test('Read-only state blocks drawing and deletion', read_only)

        def zoom():
            reset(); x,y=300,210; page.mouse.move(*at(x,y)); page.keyboard.down('Control'); page.mouse.wheel(0,-180); page.keyboard.up('Control'); settle()
            s=snapshot(); assert s['zoom']>1, s
            assert abs((x-s['panX'])/s['zoom']-x)<.05 and abs((y-s['panY'])/s['zoom']-y)<.05, s
            click('zoom-reset'); assert snapshot()['zoom']==1 and snapshot()['panX']==0
        test('Wheel zoom preserves the document point beneath the pointer', zoom)

        def pan():
            reset(); page.keyboard.down('Space'); draw([(160,170),(230,215)],10); page.keyboard.up('Space')
            s=snapshot(); assert s['strokes']==0 and abs(s['panX']-70)<1 and abs(s['panY']-45)<1, s
        test('Space drag pans without creating ink', pan)

        def touch_pan():
            reset(); cdp.send('Emulation.setTouchEmulationEnabled',{'enabled':True,'maxTouchPoints':5})
            try:
                cdp.send('Input.dispatchTouchEvent',{'type':'touchStart','touchPoints':[touch_point(1,180,200)]})
                cdp.send('Input.dispatchTouchEvent',{'type':'touchMove','touchPoints':[touch_point(1,250,240)]})
                cdp.send('Input.dispatchTouchEvent',{'type':'touchEnd','touchPoints':[]}); settle()
                s=snapshot(); assert s['strokes']==0 and s['panX']>50, s
            finally:
                cdp.send('Emulation.setTouchEmulationEnabled',{'enabled':False})
        test('One-finger touch defaults to navigation', touch_pan)

        def pinch():
            reset(); cdp.send('Emulation.setTouchEmulationEnabled',{'enabled':True,'maxTouchPoints':5})
            try:
                cdp.send('Input.dispatchTouchEvent',{'type':'touchStart','touchPoints':[touch_point(1,200,210),touch_point(2,320,210)]})
                cdp.send('Input.dispatchTouchEvent',{'type':'touchMove','touchPoints':[touch_point(1,170,210),touch_point(2,350,210)]})
                cdp.send('Input.dispatchTouchEvent',{'type':'touchEnd','touchPoints':[]}); settle()
                s=snapshot(); assert s['strokes']==0 and 1.35<s['zoom']<1.65, s
            finally:
                cdp.send('Emulation.setTouchEmulationEnabled',{'enabled':False})
        test('Two-finger pinch navigates the real Avalonia canvas', pinch)

        saved_path=DOWNLOADS/'roundtrip.ink.json'
        def save_file():
            reset(); draw([(90,120),(320,150),(420,100)],14); expected=document()
            with page.expect_download(timeout=20000) as pending: click('save')
            pending.value.save_as(saved_path)
            text=saved_path.read_text(); assert text==expected
            page.wait_for_function('!JSON.parse(window.inkTest.Snapshot()).dirty')
            return {'bytes':saved_path.stat().st_size,'schema':json.loads(text)['schema']}
        test('Save downloads exact versioned JSON through the browser', save_file)

        def confirmation():
            reset(); draw([(90,120),(260,120)],10); before=document()
            click('new'); click('confirm-cancel'); assert document()==before
            click('new'); click('confirm-discard'); assert snapshot()['strokes']==0
        test('Unsaved changes require an explicit discard decision', confirmation)

        def open_file():
            reset()
            with page.expect_file_chooser(timeout=15000) as pending: click('open')
            pending.value.set_files(saved_path)
            page.wait_for_function('JSON.parse(window.inkTest.Snapshot()).strokes > 0')
            assert document()==saved_path.read_text()
        test('Real browser file chooser reopens the saved document', open_file)

        def invalid_import():
            before=document(); path=DOWNLOADS/'invalid.json'; path.write_text('{"schema":"bad"}')
            with page.expect_file_chooser(timeout=15000) as pending: click('open')
            pending.value.set_files(path); page.wait_for_timeout(300)
            assert document()==before
            assert 'Invalid' in snapshot()['status'] or 'Unsupported' in snapshot()['status'], snapshot()
        test('Malformed import leaves the open document intact', invalid_import)

        def clipboard():
            reset(); draw([(100,150),(300,150)],16); page.keyboard.press('Control+a'); page.keyboard.press('Control+c')
            page.wait_for_function('async () => (await navigator.clipboard.readText()).includes("avalonia-ink")')
            page.keyboard.press('Control+v'); page.wait_for_function('JSON.parse(window.inkTest.Snapshot()).strokes === 2')
            assert snapshot()['selected']==1, snapshot()
        test('System clipboard transfers editable ink with new identities', clipboard)

        def export_svg():
            page.evaluate('window.inkTest.Fixture("showcase")'); settle()
            with page.expect_download(timeout=20000) as pending: click('export-svg')
            path=DOWNLOADS/'ink.svg'; pending.value.save_as(path)
            paths=ET.parse(path).getroot().findall('.//{http://www.w3.org/2000/svg}path')
            assert len(paths)==snapshot()['strokes'] and all(p.attrib.get('fill-rule')=='nonzero' for p in paths)
            return {'paths':len(paths),'bytes':path.stat().st_size}
        test('SVG download contains pressure-aware vector outlines', export_svg)

        def export_png():
            with page.expect_download(timeout=20000) as pending: click('export-png')
            path=DOWNLOADS/'ink.png'; pending.value.save_as(path)
            with Image.open(path) as image:
                assert image.width>500 and image.height>300 and len(image.convert('RGB').getcolors(image.width*image.height) or [])>10
                return {'width':image.width,'height':image.height,'bytes':path.stat().st_size}
        test('PNG export renders document ink through Avalonia', export_png)

        def highlighter_alpha():
            page.evaluate('window.inkTest.Fixture("highlighter")'); settle(); page.mouse.move(5,5); settle()
            path=OUT/'highlighter-pixels.png'; page.screenshot(path=str(path)); r=bounds('ink-canvas')
            with Image.open(path) as source:
                image=source.convert('RGB')
                pixels=[image.getpixel((round(r['x']+x),round(r['y']+130))) for x in range(100,351,5)]
                spread=[max(p[c] for p in pixels)-min(p[c] for p in pixels) for c in range(3)]
                assert max(spread)<=3 and pixels[0][2]<230, {'spread':spread,'pixels':pixels[:8]}
                return {'samples':len(pixels),'rgb':pixels[0],'maximumChannelSpread':max(spread)}
        test('Highlighter pixels retain uniform opacity across overlapping samples', highlighter_alpha)

        def recovery():
            reset(); draw([(100,160),(330,190)],20); original=document()
            page.wait_for_function('() => localStorage.getItem("avalonia-inking-studio:recovery:v1") === window.inkTest.DocumentJson()',timeout=10000)
            page.reload(wait_until='domcontentloaded'); ready()
            assert document()==original and 'Recovered' in snapshot()['recovery'], snapshot()
        test('Recovery draft survives an actual browser reload', recovery)

        def dark_theme():
            page.evaluate('window.inkTest.Fixture("showcase")'); settle(); click('theme'); settle()
            path=OUT/'studio-dark.png'; page.screenshot(path=str(path)); r=bounds('ink-canvas')
            with Image.open(path) as image:
                pixel=image.convert('RGB').getpixel((round(r['x']+35),round(r['y']+35)))
                assert max(pixel)<90, pixel
            click('theme')
        test('Dark theme updates the rendered canvas and templates', dark_theme)

        def responsive():
            page.set_viewport_size({'width':900,'height':760}); settle(); r=bounds('ink-canvas')
            assert r['width']>650 and r['height']>450, r
            page.screenshot(path=str(OUT/'studio-compact.png'))
            click('tool-Pen'); draw([(80,70),(200,90)],12); assert snapshot()['strokes']>11
            page.set_viewport_size({'width':1440,'height':1000}); settle()
        test('Compact layout retains a working shared canvas', responsive)

        def large_document():
            start=time.perf_counter(); page.evaluate('window.inkTest.Fixture("large")'); settle(); elapsed=(time.perf_counter()-start)*1000
            s=snapshot(); assert s['strokes']==10000 and 0<s['visibleStrokes']<s['strokes']/2, s
            assert s['cachedStrokes']<=1200 and s['cachedPoints']<=200000, s
            page.keyboard.down('Space'); draw([(300,220),(150,200)],10); page.keyboard.up('Space'); settle()
            after=snapshot(); assert after['cachedStrokes']<=1200 and after['cacheHits']>s['cacheHits'], after
            page.screenshot(path=str(OUT/'studio-10000-strokes.png'))
            return {'loadAndFirstFramesMs':round(elapsed,2),'strokes':after['strokes'],'visibleStrokes':after['visibleStrokes'],'cachedStrokes':after['cachedStrokes'],'cachedPoints':after['cachedPoints'],'cacheHits':after['cacheHits'],'cacheMisses':after['cacheMisses']}
        test('Ten thousand strokes are culled and geometry caches stay bounded', large_document)

        def clean_runtime():
            assert not errors, errors
            assert not page.evaluate('window.inkStartupErrors'), page.evaluate('window.inkStartupErrors')
            s=snapshot(); assert s['failures']==0, s
            return {'pageErrors':len(errors),'operationFailures':s['failures']}
        test('No uncaught browser errors or ink operation failures', clean_runtime)
        page.evaluate('window.inkTest.Fixture("showcase")'); settle(); page.screenshot(path=str(OUT/'studio-final.png'))
    except Exception as error:
        results.append({'name':'Browser startup or harness failure','passed':False,'detail':str(error),'traceback':traceback.format_exc()})
        print(traceback.format_exc(),flush=True)
        try:
            page.screenshot(path=str(OUT/'startup-failure.png'))
            print('STARTUP BODY:',page.locator('body').inner_text()[:16000],flush=True)
        except Exception:
            pass
    finally:
        report={'browser':browser.version,'webroot':str(webroot.relative_to(ROOT)),'total':len(results),'passed':sum(r['passed'] for r in results),'failed':sum(not r['passed'] for r in results),'tests':results,'pageErrors':errors}
        (OUT/'browser.json').write_text(json.dumps(report,indent=2))
        (OUT/'browser-console.json').write_text(json.dumps(console,indent=2))
        print('BROWSER SUMMARY',json.dumps({k:report[k] for k in ('total','passed','failed')}),flush=True)
        for entry in console:
            if entry['type']=='error': print('BROWSER CONSOLE ERROR:',entry['text'][:16000],flush=True)
        for error in errors: print('BROWSER PAGE ERROR:',error,flush=True)
        context.close(); browser.close(); server.shutdown()
if any(not result['passed'] for result in results):
    raise SystemExit(1)

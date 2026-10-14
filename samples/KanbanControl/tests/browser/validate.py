#!/usr/bin/env python3
"""Exercise the published Avalonia WASM sample with real Chromium input.
The sample bridge exposes managed state and realized bounds, never synthetic DOM cards.
"""
import argparse
import json
import os
import time
from pathlib import Path
from playwright.sync_api import sync_playwright

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--url', default='http://127.0.0.1:8080')
parser.add_argument('--output', type=Path, default=Path('artifacts/validation'))
parser.add_argument('--executable', default=os.environ.get('CHROMIUM_EXECUTABLE'))
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
results, errors, console = [], [], []
start = time.perf_counter()
with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, executable_path=args.executable,
        args=['--no-sandbox', '--disable-dev-shm-usage'])
    context = browser.new_context(viewport={'width': 1480, 'height': 1050}, device_scale_factor=1)
    page = context.new_page()
    page.on('pageerror', lambda error: errors.append(str(error)))
    page.on('console', lambda message: console.append({'type': message.type, 'text': message.text}))
    def snap():
        return page.evaluate('kanbanTest.snapshot()')
    def action(name, value=''):
        result = page.evaluate('([n,v]) => kanbanTest.action(n,v)', [name, str(value)])
        page.wait_for_timeout(180)
        return result
    def check(name, condition, detail=None):
        ok = bool(condition)
        results.append({'name': name, 'passed': ok, 'detail': detail})
        print(('PASS ' if ok else 'FAIL ') + name, flush=True)
        if not ok:
            page.screenshot(path=str(args.output / f'failed-{len(results):02d}.png'), full_page=True)
    def settle():
        page.wait_for_timeout(250)
    def item(state, id):
        return next(x for x in state['source'] if x['id'] == id)
    def column(state, key, lane=''):
        return next(x for x in state['columns'] if x['key'] == key and x['lane'] == lane)
    def xy(rect, fx=.5, fy=.5):
        return (rect['x'] + rect['width']*fx, rect['y'] + rect['height']*fy)
    def control(name):
        return next(c for c in snap()['controls'] if c['name'] == name)
    def click_control(name):
        page.mouse.click(*xy(control(name)['bounds'])); settle()
    def card(id, scroll=True):
        for attempt in range(20):
            state = snap(); source = item(state,id); col = column(state, source['column'], source['lane'])
            for c in state['cards']:
                if c['id'] != id or not c['visible'] or c['height'] < 10: continue
                x,y = xy(c)
                if col['listY'] + 4 < y < min(col['listY'] + col['listHeight'] - 4, page.viewport_size['height'] - 55) and 0 < x < page.viewport_size['width']:
                    return c
            if not scroll: break
            page.mouse.move(col['listX'] + col['width']*.5, min(col['listY'] + max(50,col['listHeight']*.6),page.viewport_size['height']-90))
            page.mouse.wheel(0, 180); settle()
        raise AssertionError(f'Card {id} not realized within its viewport')
    def click_card(id, modifier=None):
        c=card(id)
        if modifier: page.keyboard.down(modifier)
        page.mouse.click(*xy(c, .4, .34))
        if modifier: page.keyboard.up(modifier)
        settle()
    def drag(id, target_column, before=None, lane='', release=True):
        c=card(id)
        if before:
            t=card(before); target=xy(t,.5,.18)
        else:
            col=column(snap(),target_column,lane)
            # Just below the header = before the first card. It is inside the actual ListBox.
            target=(col['x']+col['width']*.52,col['listY']+12)
        origin=xy(c,.4,.34)
        page.mouse.move(*origin);page.mouse.down();page.mouse.move(origin[0]+10,origin[1]+2,steps=3)
        page.mouse.move(*target,steps=18);page.wait_for_timeout(130)
        during=snap()
        if release: page.mouse.up();settle()
        return during
    try:
        page.goto(args.url, wait_until='domcontentloaded', timeout=120000)
        page.wait_for_function('globalThis.kanbanReady === true', timeout=180000)
        settle(); state=snap()
        check('Real Avalonia browser application starts',state['ready'] and state['total']==16)
        check('No data diagnostics',not state['diagnostics'],state['diagnostics'])
        check('Four actual templated columns',len(state['columns'])==4)
        check('Card controls have real layout bounds',len(state['cards'])>0 and all(c['width']>100 for c in state['cards']))
        page.screenshot(path=str(args.output/'01-light.png'),full_page=True)
        click_card('ATL-101');check('Native pointer selects original model',snap()['selected']==['ATL-101'])
        page.keyboard.press('Alt+ArrowRight');settle();state=snap()
        check('Alt+Right moves selected card',item(state,'ATL-101')['column']=='doing',state['status'])
        page.keyboard.press('Control+z');settle()
        check('Ctrl+Z reverses keyboard move',item(snap(),'ATL-101')['column']=='backlog')
        page.keyboard.press('Control+y');settle()
        check('Ctrl+Y replays keyboard move',item(snap(),'ATL-101')['column']=='doing')
        action('reset')
        during=drag('ATL-101','doing',before='ATL-105')
        check('Pointer capture starts live drag',during['dragging'])
        check('Drop preview exposes allowed target',any(c['dropTarget'] and c['dropAllowed'] for c in during['columns']))
        state=snap();check('Native drag updates managed placement',item(state,'ATL-101')['column']=='doing',state['status'])
        doing=sorted((x for x in state['source'] if x['column']=='doing'),key=lambda x:x['order'])
        check('Native drop honors insertion anchor',doing[0]['id']=='ATL-101',[x['id'] for x in doing])
        page.screenshot(path=str(args.output/'02-after-drag.png'),full_page=True)
        click_control('UndoButton');check('Undo button reverts pointer transaction',item(snap(),'ATL-101')['column']=='backlog')
        click_control('RedoButton');check('Redo button replays pointer transaction',item(snap(),'ATL-101')['column']=='doing')
        action('reset')
        drag('ATL-102','backlog',before='ATL-101')
        state=snap();order=sorted((x for x in state['source'] if x['column']=='backlog'),key=lambda x:x['order'])
        check('Same-column drag reorder',order[0]['id']=='ATL-102',[x['id'] for x in order])
        action('reset')
        click_card('ATL-101');click_card('ATL-102','Control')
        check('Ctrl-click multi-selection',set(snap()['selected'])=={'ATL-101','ATL-102'},snap()['selected'])
        during=drag('ATL-101','doing',before='ATL-105')
        state=snap();check('Multi-card drag is a single transaction',state['history']==1 and all(item(state,i)['column']=='doing' for i in ('ATL-101','ATL-102')),state['status'])
        action('undo');check('Multi-card undo restores both',all(item(snap(),i)['column']=='backlog' for i in ('ATL-101','ATL-102')))
        action('reset');action('limit','doing:4')
        during=drag('ATL-101','doing',before='ATL-105')
        state=snap();check('WIP-full drop rejected without mutation',item(state,'ATL-101')['column']=='backlog' and state['history']==0,state['status'])
        check('Rejected hover explains WIP limit',any(c['dropTarget'] and not c['dropAllowed'] for c in during['columns']))
        action('reset');action('readonly','true')
        click_card('ATL-101');page.keyboard.press('Alt+ArrowRight');settle()
        check('Read-only board blocks keyboard move',item(snap(),'ATL-101')['column']=='backlog')
        action('reset');click_card('ATL-104');page.keyboard.press('Alt+ArrowRight');settle()
        check('Locked card blocks keyboard move',item(snap(),'ATL-104')['column']=='backlog')
        action('reset')
        during=drag('ATL-101','doing',before='ATL-105',release=False)
        page.keyboard.press('Escape');page.mouse.up();settle()
        check('Escape cancels captured drag',not snap()['dragging'] and item(snap(),'ATL-101')['column']=='backlog')
        action('reset')
        during=drag('ATL-101','doing',before='ATL-105',release=False)
        action('external','ATL-102');page.mouse.up();settle()
        check('External revision change rejects stale drop',item(snap(),'ATL-101')['column']=='backlog' and snap()['history']==0,snap()['status'])
        action('reset');action('policy','reject')
        drag('ATL-101','doing',before='ATL-105')
        check('Host workflow policy rejects pointer drop',item(snap(),'ATL-101')['column']=='backlog')
        action('policy','');action('reset')
        click_control('SearchBox');page.keyboard.type('onboarding');settle()
        check('Native search text filters visible cards',snap()['visible']==1,snap()['visible'])
        check('Filtering preserves total column counts',column(snap(),'backlog')['totalCount']==4)
        page.keyboard.press('Control+a');page.keyboard.type('no-card-matches');settle()
        check('Filtered empty state keeps all columns',snap()['visible']==0 and len(snap()['columns'])==4)
        action('reset');click_card('ATL-101');page.keyboard.press('Enter');settle()
        check('Enter opens application card editor',control('Editor')['visible'])
        click_control('TitleEdit');page.keyboard.press('Control+a');page.keyboard.type('Validated browser title');click_control('SaveCardButton')
        check('Native editor commits validated title',item(snap(),'ATL-101')['title']=='Validated browser title')
        action('reset');click_control('NewButton')
        check('New-card command adds model and opens editor',snap()['total']==17 and control('Editor')['visible'])
        # Reset also dismisses the editor before subsequent input tests.
        action('reset')
        col=column(snap(),'backlog');collapse=next(b for b in col['buttons'] if b['name']=='PART_Collapse')
        page.mouse.click(*xy(collapse['bounds']));settle()
        check('Native column collapse changes width',column(snap(),'backlog')['collapsed'] and column(snap(),'backlog')['width']<100)
        col=column(snap(),'backlog');collapse=next(b for b in col['buttons'] if b['name']=='PART_Collapse');page.mouse.click(*xy(collapse['bounds']));settle()
        check('Column expands without losing cards',not column(snap(),'backlog')['collapsed'] and column(snap(),'backlog')['totalCount']==4)
        click_control('LanesBox');settle()
        check('Native swimlane toggle projects eight cells',len(snap()['columns'])==8 and not snap()['diagnostics'])
        page.screenshot(path=str(args.output/'03-swimlanes.png'),full_page=True)
        action('reset');click_control('ThemeButton');settle()
        check('Native theme toggle applies dark resources','Dark' in str(snap()['theme']))
        page.screenshot(path=str(args.output/'04-dark.png'),full_page=True)
        click_control('CompactBox');settle();page.screenshot(path=str(args.output/'05-compact.png'),full_page=True)
        exported=action('export');action('remove','ATL-101');action('import',exported)
        check('Browser JSON round trip preserves models',snap()['total']==16 and item(snap(),'ATL-101')['title']=='Map the onboarding journey')
        action('dark','false');click_control('StressButton');settle();state=snap()
        check('10,000-card browser data set loads',state['total']==10000 and state['visible']==10000)
        check('Browser virtualizes card containers',0<state['renderedCards']<160,{'realized':state['renderedCards'],'source':state['total'],'loadMs':state['lastLoadMilliseconds']})
        col=column(state,'backlog');page.mouse.move(col['listX']+120,col['listY']+180);page.mouse.wheel(0,20000);settle();state=snap()
        check('Virtualized scroll remains bounded',0<state['renderedCards']<160,state['renderedCards'])
        page.screenshot(path=str(args.output/'06-10000-cards.png'),full_page=True)
        action('reset');page.set_viewport_size({'width':760,'height':1000});settle()
        check('Narrow viewport keeps live board',len(snap()['columns'])==4 and snap()['total']==16)
        page.screenshot(path=str(args.output/'07-narrow.png'),full_page=True)
        check('No uncaught browser runtime errors',not errors,errors)
    except Exception as exc:
        results.append({'name':'Unhandled validation failure','passed':False,'detail':str(exc)})
        try: page.screenshot(path=str(args.output/'failure.png'),full_page=True)
        except Exception: pass
        try: (args.output/'failure-state.json').write_text(json.dumps(snap(),indent=2))
        except Exception: pass
        raise
    finally:
        report={'passed':sum(x['passed'] for x in results),'failed':sum(not x['passed'] for x in results),'seconds':time.perf_counter()-start,'checks':results,'runtimeErrors':errors,'console':console,'browser':browser.version}
        (args.output/'report.json').write_text(json.dumps(report,indent=2))
        browser.close()

if any(not result["passed"] for result in results):
    raise SystemExit(1)

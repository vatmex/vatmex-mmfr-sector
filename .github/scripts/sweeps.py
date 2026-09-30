#!/usr/bin/env python3
"""Pre-release integrity sweeps for the vatSys MMFR sector.

vatSys fails silently: a reference that does not resolve is skipped, never reported.
Run from the repo root before tagging a release:  python3 .github/scripts/sweeps.py
Exit 0 = pass, 1 = fail. Lives under .github/ so the release zip (which excludes
*.git*) never ships it.

Instrument traps, all hit on 2026-09-29/30 while building the 2610a gate:
  * split <Point>/<Line> text on '/' only -- VRP names contain spaces;
  * coordinates are DMS (+DDMMSS.sss-DDDMMSS.sss) OR decimal (+18.152749-096.080979);
  * Tracks.xml <Point1..3>/<Line> hold "x,y" symbol drawing offsets, not coordinates;
  * resolve Positions.xml defaults against <Map Name>, never against filenames.
  * match the .xml extension case-insensitively: MMTL_RWY12.XML, MMTL_RWY30.XML and
    MMLP_TMA.XML are uppercase, and a lowercase-only walk silently skips them.
Known residue is allow-listed below; anything new fails.
"""
import os, re, sys, xml.etree.ElementTree as ET

ALLOWED_S4  = {'IPL', 'OLS'}                        # US VORs, not defined here
ALLOWED_S5  = {'MULLT', 'OLS', 'TCATE'}             # classified in MMFR-LOG
ALLOWED_S5B = {'IPL.V137.MXL', 'OLS.V393.HMO', 'OLS.V625.HMO'}
MAX_S8      = 0                                     # unresolved default maps
DMS = re.compile(r'^[+-]\d{6}(\.\d+)?[+-]\d{7}(\.\d+)?$')
DEC = re.compile(r'^([+-]\d{1,2}\.\d+)([+-]\d{1,3}\.\d+)$')
OFF = re.compile(r'^-?\d+(\.\d+)?,\s*-?\d+(\.\d+)?$')
fails = []

def report(name, ok, detail):
    print('%-4s %-4s %s' % (name, 'PASS' if ok else 'FAIL', detail))
    if not ok: fails.append(name)

def coord(t):
    if DMS.match(t): return True
    m = DEC.match(t)
    return bool(m) and abs(float(m.group(1))) <= 90 and abs(float(m.group(2))) <= 180

xmlf, bad6 = [], []
for d, _, fs in os.walk('.'):
    if '.git' in d.split(os.sep): continue
    for f in fs:
        if f.lower().endswith('.xml'):
            p = os.path.join(d, f); xmlf.append(p)
            try: ET.parse(p)
            except Exception as e: bad6.append('%s: %s' % (p, e))
report('S6', not bad6, '%d xml files, %d unparseable %s' % (len(xmlf), len(bad6), bad6[:3]))
if bad6: sys.exit(1)

A = ET.parse('Airspace.xml').getroot()
known = {e.get('Name') for e in A.iter() if e.get('Name')} | \
        {e.get('ICAO') for e in A.iter('Airport') if e.get('ICAO')}
aws = {a.get('Name'): [x for x in (a.text or '').strip().split('/') if x] for a in A.iter('Airway')}

def resolves(t):
    if coord(t) or t in known: return True
    if t.count('.') == 2 and t[0] not in '+-':
        a, w, b = t.split('.'); return a in known and b in known and w in aws
    return False

s1 = sorted({t for tg in ('SID', 'STAR', 'Approach') for el in A.iter(tg)
             for r in el.iter('Route') for t in (r.text or '').split()
             if not coord(t) and t not in known})
report('S1', not s1, 'route waypoints undefined: %s' % s1[:10])
ap = {e.get('Name') for e in A.iter('Approach')}
s2 = sorted({e.get('ApproachName') for e in A.iter()
             if e.get('ApproachName') and e.get('ApproachName') not in ap})
report('S2', not s2, 'ApproachName without Approach: %s' % s2[:10])
dn = {(e.tag, e.get('Name')) for e in A.iter() if e.tag in ('SID', 'STAR') and e.get('Airport')}
s3 = sorted({(a.get('ICAO'), e.tag, e.get('Name')) for a in A.iter('Airport') for e in a.iter()
             if e.tag in ('SID', 'STAR') and e.get('Name') and (e.tag, e.get('Name')) not in dn})
report('S3', not s3, 'airport SID/STAR refs undefined: %s' % s3[:10])
s4 = {m for v in aws.values() for m in v if m not in known}
report('S4', s4 <= ALLOWED_S4, 'airway members undefined: %s (new: %s)' % (sorted(s4), sorted(s4 - ALLOWED_S4)))

s5, s5b, idx = set(), set(), set()
for d, _, fs in os.walk('Maps'):
    for f in fs:
        if not f.lower().endswith('.xml'): continue
        r = ET.parse(os.path.join(d, f)).getroot()
        for m in r.iter('Map'):
            if m.get('Name'): idx.add(os.path.relpath(d, 'Maps') + '/' + m.get('Name'))
        for tg, bucket in (('Point', s5), ('Line', s5b)):
            for el in r.iter(tg):
                for t in (el.text or '').split('/'):
                    t = t.strip()
                    if t and t[0] not in '+-' and not resolves(t): bucket.add(t)
report('S5', s5 <= ALLOWED_S5, 'map <Point> refs undefined: %s (new: %s)' % (sorted(s5), sorted(s5 - ALLOWED_S5)))
report('S5b', s5b <= ALLOWED_S5B, 'map <Line> members undefined: %s (new: %s)' % (sorted(s5b), sorted(s5b - ALLOWED_S5B)))

trk_bad, trk_n = [], 0
for el in ET.parse('Tracks.xml').getroot().iter():
    t = (el.text or '').strip()
    if t and t[:1] in '+-0123456789' and ',' in t:
        for tok in (x.strip() for x in t.split('/')):
            if tok:
                trk_n += 1
                if not OFF.match(tok): trk_bad.append(tok)
report('TRK', trk_n > 50 and not trk_bad, 'Tracks.xml x,y offsets %d, malformed %s' % (trk_n, trk_bad[:5]))

s7, n7 = [], 0
for p in xmlf:
    if os.path.basename(p) == 'Tracks.xml': continue
    for el in ET.parse(p).getroot().iter():
        if el.tag in ('Lat', 'Long'):
            n7 += 1
            if not re.match(r'^-?\d{1,3}\.\d+$', (el.text or '').strip()): s7.append((p, el.text))
            continue
        for t in (x.strip() for x in (el.text or '').split('/')):
            if t[:1] in ('+', '-') and len(t) > 12 and t[1:2].isdigit():
                n7 += 1
                if not coord(t): s7.append((p, t[:60]))
        for v in el.attrib.values():
            if v[:1] in ('+', '-') and len(v) >= 15 and v[1:2].isdigit():
                n7 += 1
                if not coord(v): s7.append((p, v[:60]))
report('S7', not s7, '%d coordinate tokens, malformed %s' % (n7, s7[:5]))

refs = [m.get('Name') for p in ET.parse('Positions.xml').getroot().iter('Position') for m in p.iter('Map')]
n8 = sum(1 for n in refs if n not in idx)
report('S8', n8 <= MAX_S8, '%d of %d default map refs unresolved (max %d), distinct %d'
       % (n8, len(refs), MAX_S8, len({n for n in refs if n not in idx})))

mag = len(xmlf) > 200 and len(aws) > 300 and len(known) > 3000 and len(refs) > 1400 and n7 > 10000
report('MAG', mag, 'xml=%d airways=%d names=%d maprefs=%d coords=%d' % (len(xmlf), len(aws), len(known), len(refs), n7))
v = ET.parse('Profile.xml').getroot().find('.//Version')
print('info Profile %s %s %s' % (v.get('AIRAC'), v.get('Revision'), v.get('PublishDate')))
print('GATE %s' % ('FAILED: ' + ', '.join(fails) if fails else 'PASSED'))
sys.exit(1 if fails else 0)

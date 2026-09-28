// Builds Prove/data/obsolete-api-members.tsv: the SOLIDWORKS API members that the
// API help marks obsolete, refined (Z-C21.6).
//
// Usage:
//   node Controlli/obsolete-api-list.js <folder with the extracted help>
//
// The folder is the one where sldworksapi.chm and obsoleteapi.chm were
// extracted (read only) during Z-C21.1: it must contain `estratto` (the pages of
// sldworksapi.chm) and `estratto_obsoleteapi` (the pages of obsoleteapi.chm).
// The help is Dassault's and is NOT copied into this repository: only member
// names, which are already public in the interop assemblies, end up in the TSV.
//
// WHY A REFINED LIST AND NOT THE RAW ONE (note-interne/audit/obsoleti.json).
// The raw list took every method or property page containing the word
// "obsolete" or "superseded by". That catches 27 false positives, all of the
// same shape: the page of a CURRENT member saying "the difference between this
// method and the now obsolete X is..." (FeatureExtrusion3, SaveAs3 on
// IModelDocExtension, GetSuppression2, Get6...). It also missed about 110 real
// ones (GetSystemValue2, FeatureFillet3, FeatureLinearPattern5...), because the
// marker sits right after the parameter table and the sentence splitter glued it
// to the page header, beyond its 400-character cut.
//
// THE RULE USED HERE, page by page:
//   - "obsolete": the description block (between the language header and
//     ".NET Syntax") says "Obsolete." / "Obsolete. See ..." / "Superseded by
//     ..." / "This method is obsolete", or the Remarks say "making this method
//     obsolete" (InsertProtrusionSwept4, InsertSweepSurface3);
//   - "partial": obsolete only for some uses ("As of SOLIDWORKS 2020, this
//     method is obsolete for creating: constant radius fillets..." on
//     FeatureFillet3; "Obsolete for documents created in SOLIDWORKS 2016 and
//     later"). For the static check a partial member counts as obsolete: the
//     exception comment says which use is the measured one;
//   - every member page of obsoleteapi.chm is obsolete by construction,
//     unless sldworksapi.chm has a CURRENT page for the same member (the
//     obsolete guide also keeps old forms of members that still exist, e.g.
//     IWizardHoleFeatureData2.Standard): then the current guide wins;
//   - a member that a current page calls "the now obsolete I::X" is obsolete
//     too (guide column "mention"), when neither guide has a current page for
//     it: ICurve.Length2 has no page of its own, only a mention in
//     ICurve~GetLength3.html.
//
// A member NAME can be obsolete on one interface and current on another
// (Select2 on IEntity vs IFeature, GetBodies2 on IComponent2 vs IPartDoc). The
// fourth column lists the interfaces where the same name is current, so that the
// contract can tell the two apart by the receiver type.
//
// Columns: member (Interface.Name) TAB class TAB guide TAB current-on (comma list)
'use strict';
const fs = require('fs');
const path = require('path');

const root = process.argv[2];
if (!root) { console.error('usage: node obsolete-api-list.js <extracted help folder>'); process.exit(2); }
const main = path.join(root, 'estratto');
const obsoleteGuide = path.join(root, 'estratto_obsoleteapi');
for (const d of [main, obsoleteGuide])
  if (!fs.existsSync(d)) { console.error('missing folder: ' + d); process.exit(2); }

const clean = h => h.replace(/<script[\s\S]*?<\/script>/gi, ' ').replace(/<style[\s\S]*?<\/style>/gi, ' ')
  .replace(/<[^>]+>/g, ' ').replace(/&nbsp;/g, ' ').replace(/&amp;/g, '&')
  .replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/\s+/g, ' ');

const HEADER = 'Visual Basic (Declaration) Visual Basic (Usage) C# C++/CLI';
const declared = new Map();   // name -> Set of interfaces declaring it
const members = new Map();    // "I.Name" -> { cls, guide }
const currentPages = new Set(); // "I.Name" with a current (not obsolete) page in sldworksapi.chm
const declare = (iface, name) => {
  if (!declared.has(name)) declared.set(name, new Set());
  declared.get(name).add(iface);
};

// Members that a CURRENT page names as "the now obsolete X::Y" (ICurve::Length2
// in ICurve~GetLength3.html). Some of them have no page of their own in either
// guide, so the page loop alone never sees them. They are added at the end,
// only when no guide has a current page for them.
const mentioned = new Set();
const mentionRx = /now obsolete\s+(?:(?:method|property)\s+)?((?:I\w+::\w+)(?:\s*(?:,|and)\s*I\w+::\w+)*)/gi;

const pageName = /^SolidWorks\.Interop\.sldworks~SolidWorks\.Interop\.sldworks\.(I?\w+)~(\w+)\.html$/;
for (const f of fs.readdirSync(main)) {
  const m = f.match(pageName);
  if (!m) continue;
  const iface = m[1], name = m[2];
  const html = fs.readFileSync(path.join(main, f), 'utf8');
  const title = (html.match(/<title>([^<]*)<\/title>/i) || [])[1] || '';
  if (!/^\s*\w+\s+(Method|Property)\s+\(/.test(title)) continue;
  declare(iface, name);
  const t = clean(html);
  for (const mm of t.matchAll(mentionRx))
    for (const ref of mm[1].match(/I\w+::\w+/g)) mentioned.add(ref.replace('::', '.'));
  const h = t.indexOf(HEADER), s = t.indexOf('.NET Syntax');
  const desc = h >= 0 && s > h ? t.slice(h + HEADER.length, s) : '';
  const r = t.indexOf(' Remarks ');
  const remarks = r >= 0 ? t.slice(r) : '';
  let cls = null;
  if (/\b(Obsolete\s*\.|Superseded by |This (method|property) is obsolete)/i.test(desc))
    cls = /Obsolete for (documents|creating)/i.test(desc) ? 'partial' : 'obsolete';
  else if (/Obsolete for [^.]*\./i.test(desc)) cls = 'partial';
  else if (/As of SOLIDWORKS \d+, this (method|property) is obsolete/i.test(remarks)) cls = 'partial';
  else if (/making this (method|property) obsolete/i.test(remarks)) cls = 'obsolete';
  if (cls) members.set(iface + '.' + name, { cls, guide: 'sldworksapi' });
  else currentPages.add(iface + '.' + name);
}

// The obsolete guide also documents OLD forms of members that the current
// guide still lists as current (IWizardHoleFeatureData2.Standard: once settable,
// now get-only). When sldworksapi.chm has a current page for the member, the
// current guide wins.
let overridden = 0;
for (const dir of fs.readdirSync(obsoleteGuide)) {
  const p = path.join(obsoleteGuide, dir);
  if (!fs.statSync(p).isDirectory()) continue;
  for (const f of fs.readdirSync(p)) {
    const m = f.match(/^(\w+)__(\w+)\.htm$/);
    if (!m) continue;
    const iface = 'I' + m[1], name = m[2];
    declare(iface, name);
    const key = iface + '.' + name;
    if (currentPages.has(key)) { overridden++; continue; }
    if (!members.has(key)) members.set(key, { cls: 'obsolete', guide: 'obsoleteapi' });
  }
}

let byMention = 0;
for (const key of mentioned) {
  if (members.has(key) || currentPages.has(key)) continue;
  const [iface, name] = key.split('.');
  declare(iface, name);
  members.set(key, { cls: 'obsolete', guide: 'mention' });
  byMention++;
}

const rows = [];
for (const [key, v] of members) {
  const name = key.split('.')[1];
  const current = [...(declared.get(name) || [])].filter(i => !members.has(i + '.' + name)).sort();
  rows.push([key, v.cls, v.guide, current.join(',')].join('\t'));
}
rows.sort();
const out = path.join(__dirname, '..', 'Prove', 'data', 'obsolete-api-members.tsv');
fs.mkdirSync(path.dirname(out), { recursive: true });
const head = [
  '# SOLIDWORKS API members marked obsolete by the API help (2021), refined.',
  '# Generated by Controlli/obsolete-api-list.js from sldworksapi.chm and obsoleteapi.chm.',
  '# Do not edit by hand: regenerate. Columns: member, class, guide, current-on.',
];
fs.writeFileSync(out, head.concat(rows).join('\n') + '\n', 'utf8');
const count = c => rows.filter(r => r.split('\t')[1] === c).length;
console.log(rows.length + ' members (' + count('obsolete') + ' obsolete, ' + count('partial') + ' partial; ' +
  overridden + ' obsoleteapi.chm pages left out because sldworksapi.chm lists them as current; ' +
  byMention + ' known only from a "now obsolete" mention) -> ' + out);

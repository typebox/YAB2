import { readFileSync, writeFileSync, existsSync, readdirSync } from 'fs';
import { join, dirname } from 'path';
import { fileURLToPath } from 'url';
import { buildSync } from 'esbuild';

const __dirname = dirname(fileURLToPath(import.meta.url));
const SRC = join(__dirname, 'src');
const FONTS_DIR = join(__dirname, 'fonts');
const OUT = join(__dirname, '..', 'Resources', 'PortalTemplate.html');

// --- 1. Bundle vendor JS (marked + prism + sql.js) ---
const vendorEntry = join(__dirname, '_vendor_entry.js');
writeFileSync(vendorEntry, `
import { marked } from 'marked';
import Prism from 'prismjs';
import 'prismjs/components/prism-csharp.js';
import 'prismjs/components/prism-gherkin.js';
import initSqlJs from 'sql.js';
window.marked = marked;
window.Prism = Prism;
window.initSqlJs = initSqlJs;
`);
const vendorResult = buildSync({
    entryPoints: [vendorEntry],
    bundle: true, minify: true, format: 'iife',
    write: false, platform: 'browser',
    define: { 'process.env.NODE_ENV': '"production"' },
});
const vendorJs = vendorResult.outputFiles[0].text;
try { require('fs').unlinkSync(vendorEntry); } catch {}

// --- 2. Wasm Base64 ---
const wasmB64 = readFileSync(join(__dirname, 'node_modules', 'sql.js', 'dist', 'sql-wasm.wasm')).toString('base64');

// --- 3. Font CSS ---
function fontFace(family, weight, file) {
    const p = join(FONTS_DIR, file);
    if (!existsSync(p)) { console.warn('Missing font: ' + file); return ''; }
    const b64 = readFileSync(p).toString('base64');
    return `@font-face{font-family:'${family}';font-style:normal;font-weight:${weight};font-display:swap;src:url(data:font/woff2;base64,${b64}) format('woff2')}`;
}
const fontsCss = [
    fontFace('Inter', 400, 'inter-latin-400.woff2'),
    fontFace('Inter', 500, 'inter-latin-500.woff2'),
    fontFace('Inter', 600, 'inter-latin-600.woff2'),
    fontFace('Inter', 700, 'inter-latin-700.woff2'),
    fontFace('JetBrains Mono', 400, 'jetbrains-mono-latin-400.woff2'),
    fontFace('JetBrains Mono', 500, 'jetbrains-mono-latin-500.woff2'),
].filter(Boolean).join('\n');

// --- 4. Prism theme CSS ---
let prismCss = '';
const prismPath = join(__dirname, 'node_modules', 'prismjs', 'themes', 'prism-tomorrow.min.css');
if (existsSync(prismPath)) prismCss = readFileSync(prismPath, 'utf-8');
else {
    const alt = prismPath.replace('.min.css', '.css');
    if (existsSync(alt)) prismCss = readFileSync(alt, 'utf-8');
}

// --- 5. App CSS (concatenate all css files in order) ---
const cssOrder = ['layout.css', 'components.css', 'coverage.css', 'search.css'];
const appCss = cssOrder
    .map(f => join(SRC, 'styles', f))
    .filter(existsSync)
    .map(f => readFileSync(f, 'utf-8'))
    .join('\n');

// --- 6. App JS (concatenate all js files in order) ---
const jsOrder = ['db.js', 'ui.js', 'render.js', 'coverage.js', 'search.js', 'concept.js', 'boot.js'];
const appJs = jsOrder
    .map(f => join(SRC, 'js', f))
    .filter(existsSync)
    .map(f => readFileSync(f, 'utf-8'))
    .join('\n');

// --- 7. Assemble ---
let html = readFileSync(join(SRC, 'template.html'), 'utf-8');
html = html.replace('{{BUNDLED_FONTS_CSS}}', fontsCss);
html = html.replace('{{PRISM_THEME_CSS}}', prismCss);
html = html.replace('{{APP_CSS}}', appCss);
html = html.replace('{{BUNDLED_VENDOR_JS}}', vendorJs);
html = html.replace('{{SQLJS_WASM_BASE64}}', wasmB64);
html = html.replace('{{APP_JS}}', appJs);

writeFileSync(OUT, html, 'utf-8');
console.log(`✅ Built PortalTemplate.html (${Math.round(Buffer.byteLength(html) / 1024)} KB)`);

let DB = null;
async function start() {
    // 1. Read Wasm base64 and build data URL
    const wasmB64 = document.getElementById('yab-wasm-base64').textContent.trim();
    const wasmUrl = 'data:application/wasm;base64,' + wasmB64;
    
    // 2. Init sql.js
    const SQL = await initSqlJs({ locateFile: () => wasmUrl });
    
    // 3. Read SQLite base64 and open DB
    const dbB64 = document.getElementById('yab-sqlite-base64').textContent.trim();
    const raw = atob(dbB64);
    const arr = new Uint8Array(raw.length);
    for (let i = 0; i < raw.length; i++) arr[i] = raw.charCodeAt(i);
    DB = new SQL.Database(arr);
    
    // 4. Git commit
    const meta = queryOne(DB, "SELECT Value FROM Metadata WHERE Key = 'GitCommit'");
    document.getElementById('commit-id').innerText = 'Commit: ' + (meta?.Value || 'Unknown');
    
    // 5. Build nav from concepts
    const concepts = queryAll(DB, "SELECT DISTINCT ConceptName FROM Concepts ORDER BY ConceptName");
    const nav = document.getElementById('nav-concepts');
    
    // Restore sidebar/layout state
    if (localStorage.getItem('yab-sidebar-collapsed') === 'true') document.body.classList.add('sidebar-collapsed');
    setLayout(localStorage.getItem('yab-layout') || 'both');
    
    concepts.forEach((row, i) => {
        const name = row.ConceptName;
        // Check status for nav indicator
        const blocks = queryAll(DB, `SELECT Status, SemanticMessage FROM Blocks b 
            JOIN Concepts c ON b.Name = c.BlockName WHERE c.ConceptName = ?`, [name]);
        const isDrifted = blocks.some(b => b.Status !== 'VERIFIED');
        const hasFail = blocks.some(b => b.SemanticMessage && b.SemanticMessage.toLowerCase().includes('blocked'));
        let statusClass = 'verified';
        if (hasFail) statusClass = 'semantic-fail';
        else if (isDrifted) statusClass = 'drifted';
        
        const a = document.createElement('div');
        a.className = 'nav-item' + (i === 0 ? ' active' : '');
        a.innerHTML = '<span>' + name + '</span><div class="nav-status ' + statusClass + '"></div>';
        a.onclick = () => { switchConcept(name); document.querySelectorAll('.nav-item').forEach(el => el.classList.remove('active')); a.classList.add('active'); };
        nav.appendChild(a);
    });
    
    if (concepts.length > 0) switchConcept(concepts[0].ConceptName);
    document.getElementById('loading-overlay').style.display = 'none';
}

start().catch(e => console.error('YAB boot failed:', e));

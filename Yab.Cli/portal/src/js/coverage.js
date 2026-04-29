function renderCoverageMapv2(implementations, tests) {
    if (implementations.length === 0) return '';
    
    // Filter out internal step definitions
    const allTests = tests
        .filter(t => !t.Name.includes("Steps") && !t.Content.includes("[Binding]"))
        .map(t => ({ name: t.Name, type: t.FilePath.endsWith('.feature') ? 'bdd' : 'unit', block: t }));
    
    if (allTests.length === 0) return '';

    // Calculate stats
    const totalCells = allTests.length * implementations.length;
    let hitCells = 0;
    
    const matrix = {}; // testName -> Set of implName
    allTests.forEach(t => {
        matrix[t.name] = new Set();
        // 1. From VerifyingTests
        const vt = queryAll(DB, "SELECT BlockName FROM VerifyingTests WHERE TestId = ?", [t.name]);
        vt.forEach(row => matrix[t.name].add(row.BlockName));
        
        // 2. From BlockReferences
        const br = queryAll(DB, "SELECT BlockName FROM BlockReferences WHERE RefName = ?", [t.name]);
        br.forEach(row => matrix[t.name].add(row.BlockName));
    });

    implementations.forEach(impl => {
        allTests.forEach(t => { if (matrix[t.name].has(impl.Name)) hitCells++; });
    });
    const overallPct = Math.round((hitCells / totalCells) * 100);
    
    let m = '<div class="coverage-map-section"><h3>Coverage Map</h3>';
    m += `<div class="coverage-stats-bar">
        <div class="coverage-stat"><span class="coverage-stat-value">${allTests.length}</span><span class="coverage-stat-label">Tests</span></div>
        <div class="coverage-stat"><span class="coverage-stat-value">${implementations.length}</span><span class="coverage-stat-label">Blocks</span></div>
        <div class="coverage-stat"><span class="coverage-stat-value">${hitCells}<span style="font-size:0.8rem;color:var(--text-secondary)">/${totalCells}</span></span><span class="coverage-stat-label">Connections</span></div>
        <div class="coverage-stat"><span class="coverage-stat-value ${pctColorClass(overallPct)}">${overallPct}%</span><span class="coverage-stat-label">Density</span></div>
    </div>`;

    m += '<div class="coverage-map-wrapper"><table class="coverage-matrix"><thead><tr><th class="corner-cell"></th>';
    allTests.forEach(t => {
        const short = t.name.split('.').pop();
        m += `<th class="col-header"><div class="col-label-wrapper" title="${escapeHtml(t.name)}"><span class="test-indicator-badge ${t.type}"><span>${t.type==='bdd'?'B':'U'}</span></span><span>${escapeHtml(short)}</span></div></th>`;
    });
    m += '<th>Coverage</th></tr></thead><tbody>';
    
    implementations.forEach(impl => {
        let blockHits = 0;
        m += `<tr><td class="row-header-cell" onclick="scrollToImplCard('${escapeHtml(impl.Name)}')">${escapeHtml(impl.Name)}</td>`;
        allTests.forEach(t => {
            const hit = matrix[t.name].has(impl.Name);
            if (hit) blockHits++;
            m += `<td><div class="coverage-cell ${hit?'hit':'miss'}">${hit?'<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3"><path d="M5 13l4 4L19 7"/></svg>':''}</div></td>`;
        });
        const pct = Math.round((blockHits / allTests.length) * 100);
        m += `<td class="test-count-cell"><span class="coverage-pct ${pctColorClass(pct)}">${pct}%</span></td></tr>`;
    });
    
    m += '</tbody></table></div></div>';
    return m;
}

function pctColorClass(pct) {
    if (pct >= 100) return 'full';
    if (pct >= 75) return 'high';
    if (pct >= 40) return 'mid';
    if (pct > 0) return 'low';
    return 'none';
}

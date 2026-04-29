function switchConcept(name) {
    const docPane = document.getElementById('doc-content');
    const implPane = document.getElementById('impl-content');
    
    const docs = queryAll(DB, "SELECT * FROM MarkdownFiles WHERE Concept = ?", [name]);
    const impls = queryAll(DB, `SELECT b.* FROM Blocks b JOIN Concepts c ON b.Name = c.BlockName 
        WHERE c.ConceptName = ? AND b.IsTest = 0`, [name]);
    const tests = queryAll(DB, `SELECT b.* FROM Blocks b JOIN Concepts c ON b.Name = c.BlockName 
        WHERE c.ConceptName = ? AND b.IsTest = 1`, [name]);
    
    if (docs.length === 0) return;
    
    // Group by type
    const businessDocs = docs.filter(f => !f.Type || f.Type === 'business-rule');
    const opsDocs = docs.filter(f => f.Type === 'ops-playbook');
    const archDocs = docs.filter(f => f.Type === 'architecture');
    
    const tabs = [];
    if (businessDocs.length > 0) tabs.push({ id: 'business', label: 'Business Logic', docs: businessDocs });
    if (opsDocs.length > 0) tabs.push({ id: 'ops', label: 'Operations', docs: opsDocs });
    if (archDocs.length > 0) tabs.push({ id: 'arch', label: 'Architecture', docs: archDocs });
    
    let h = '<div class="doc-section">';
    h += '<div class="metadata-pill"><span>Status: <strong>' + (docs[0].Status || 'Active') + '</strong></span><span style="color:#e2e8f0">|</span><span>Audience: <strong>' + (docs[0].Audience || 'Developer') + '</strong></span></div>';
    h += '<h1>' + name + '</h1>';
    
    if (tabs.length > 1) {
        h += '<div class="doc-tabs">';
        tabs.forEach((tab, i) => h += '<button class="doc-tab' + (i === 0 ? ' active' : '') + '" onclick="switchTab(\'' + tab.id + '\')">' + tab.label + '<span class="tab-count">' + tab.docs.length + '</span></button>');
        h += '</div>';
    }
    
    tabs.forEach((tab, i) => {
        h += '<div id="tab-' + tab.id + '" class="doc-tab-content' + (i === 0 ? ' active' : '') + '">';
        tab.docs.forEach(file => {
            let md = (file.Content || '').trim();
            md = md.replace(/^#\s+.*?\n/, '').trim().replace(/## Physical Anchors[\s\S]*?(?=\n##|$)/g, '').trim();
            const proc = md.replace(/\[yab-hash:.*?:.*?\]/g, match => '<span class="physical-anchor">' + match + '</span>')
                           .replace(/\[yab-audit:.*?:.*?\]/g, match => '<span class="physical-anchor" style="background:#fef2f2; color:#b91c1c; border-color:#fee2e2">' + match + '</span>');
            
            const html = typeof marked !== 'undefined' ? marked.parse(proc) : '<pre>' + escapeHtml(proc) + '</pre>';
            if (file.Description) h += '<p class="concept-desc">' + file.Description + '</p>';
            h += '<div class="markdown-body">' + html + '</div>';
        });
        h += '</div>';
    });
    
    // Tests
    const featureTests = tests.filter(t => t.FilePath.endsWith('.feature'));
    const unitTests = tests.filter(t => !t.FilePath.endsWith('.feature'));
    const allTests = [...featureTests, ...unitTests];
    
    if (allTests.length > 0) {
        h += '<div class="tests-section"><h3 style="font-size:0.75rem; text-transform:uppercase; color:var(--text-secondary); font-weight:700; letter-spacing:0.1em; margin-bottom:1.5rem;">Behavioral Scenarios & Tests</h3>';
        allTests.forEach(t => {
            const isGherkin = t.FilePath.endsWith('.feature');
            let stepsHtml = '';
            if (isGherkin) {
                const lines = t.Content.split('\n').map(l => l.trim()).filter(l => l && !l.startsWith('Scenario:') && !l.startsWith('@'));
                const steps = lines.map(l => {
                    const parts = l.split(' ');
                    return `<div class="step-row"><span class="step-keyword">${parts[0]}</span><span class="step-text">${parts.slice(1).join(' ')}</span></div>`;
                });
                stepsHtml = `<div class="scenario-steps">${steps.join('')}</div>`;
            }
            const short = t.Name.replace(/Scenario:\s*/, '').replace(/([A-Z])/g, ' $1').trim();
            h += `<div class="test-item"><div class="test-header"><span class="test-type ${isGherkin?'bdd':'unit'}">${isGherkin?'Scenario':'Unit Test'}</span><span class="test-name">${short}</span></div>${stepsHtml}</div>`;
        });
        h += '</div>';
    }
    
    h += renderCoverageMapv2(impls, tests);
    
    // AI Audit
    const audits = impls.filter(i => i.SemanticMessage);
    if (audits.length > 0) {
        h += '<div style="margin-top:3rem; border-top:2px solid #f1f5f9; padding-top:2rem;"><h3>AI Audit Insights</h3>';
        audits.forEach(b => {
            const fail = b.SemanticMessage.toLowerCase().includes('blocked');
            h += `<div class="audit-rationale-card ${fail?'':'passed'}"><h3><div class="status-icon ${fail?'blocked':'passed'}">${fail?'!':'✓'}</div> ${fail?'Logic Compliance Mismatch':'Semantic Verification Passed'}</h3><p><strong>${b.Name}:</strong> ${escapeHtml(b.SemanticMessage)}</p></div>`;
        });
        h += '</div>';
    }
    
    // Rules
    const rules = queryAll(DB, "SELECT * FROM BusinessRules WHERE MdPath = ?", [docs[0].Path]);
    if (rules.length > 0) {
        h += '<div style="margin-top:3rem"><h3>Business Rules</h3>';
        rules.forEach(r => h += `<div class="rule-card"><span class="rule-id">${r.RuleId}</span><p>${r.Description}</p></div>`);
        h += '</div>';
    }
    
    h += '</div>';
    docPane.innerHTML = h;
    
    let ih = '<div class="impl-title-main">Implementation & Verification</div>';
    impls.forEach(b => ih += renderCard(b));
    const mainTests = tests.filter(t => !t.Name.includes("Steps") && !t.Content.includes("[Binding]"));
    if (mainTests.length > 0) {
        ih += '<div class="impl-title-main" style="margin-top:4rem">Verification Tests</div>';
        mainTests.forEach(t => ih += renderCard(t));
    }
    implPane.innerHTML = ih;
    if (typeof Prism !== 'undefined') Prism.highlightAll();
    docPane.scrollTop = 0; implPane.scrollTop = 0;
}

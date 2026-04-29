function renderCard(block) {
    const color = block.ConfidenceScore > 90 ? 'var(--success)' : (block.ConfidenceScore > 0 ? 'var(--warning)' : 'var(--danger)');
    const isBlocked = block.SemanticMessage && block.SemanticMessage.toLowerCase().includes('blocked');
    const isGherkin = block.FilePath.toLowerCase().endsWith('.feature');
    const langClass = isGherkin ? 'language-gherkin' : 'language-csharp';
    
    let c = '<div class="impl-card"><div class="impl-card-header"><span class="impl-filename">' + block.Name + (block.RuntimeVerified ? '<span class="runtime-badge">Runtime Verified</span>' : '') + '</span><div class="impl-confidence"><span>' + block.ConfidenceScore + '% Match</span><div class="conf-bar"><div class="conf-fill" style="width:' + block.ConfidenceScore + '%; background:' + color + '"></div></div></div></div>';
    if (block.Intent) c += '<div class="impl-intent">' + block.Intent + '</div>';
    c += '<pre><code class="' + langClass + '">' + escapeHtml(block.Content) + '</code></pre>';

    if (block.StatementsCovered > 0) {
        c += '<div style="padding:0.25rem 1.25rem; font-size:0.65rem; color:#94a3b8; background:#0f172a">' + 
             block.StatementsCovered + ' statements covered</div>';
    }

    // Get coverage summary
    const bddCount = queryOne(DB, "SELECT COUNT(*) as c FROM CoverageOverlap WHERE BlockName = ? AND TestType = 'bdd'", [block.Name]).c;
    const unitCount = queryOne(DB, "SELECT COUNT(*) as c FROM CoverageOverlap WHERE BlockName = ? AND TestType = 'unit'", [block.Name]).c;
    
    if (bddCount > 0 || unitCount > 0) {
        c += '<div style="padding:0.75rem 1.25rem; background:#0f172a; border-top:1px solid #334155; font-size:0.75rem; color:#94a3b8">';
        c += '<strong style="color:#6366f1">Coverage Overlap:</strong> ';
        c += bddCount + ' BDD + ' + unitCount + ' Unit tests hit this code';
        c += '</div>';
    }

    if (block.SemanticMessage) {
        c += `<div class="ai-feedback ${isBlocked ? '' : 'passed'}"><b>AI Semantic Review</b>${escapeHtml(block.SemanticMessage).replace(/\n/g, '<br>')}</div>`;
    }
    if (block.Status !== 'VERIFIED') c += '<div class="drift-badge">⚠️ ' + block.Status + '</div>';
    return c + '</div>';
}

function escapeHtml(s) { if (!s) return ''; return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;'); }

let searchTimer = null;
function debounceSearch(val) {
    clearTimeout(searchTimer);
    searchTimer = setTimeout(() => doSearch(val), 200);
}

function doSearch(query) {
    const resultsDiv = document.getElementById('search-results');
    if (!query || query.length < 2) {
        resultsDiv.innerHTML = '';
        return;
    }

    const rows = queryAll(DB, `
        SELECT Name, ConceptNames FROM BlockSearch 
        WHERE BlockSearch MATCH ? 
        ORDER BY rank LIMIT 10`, [query + '*']);

    let h = '';
    rows.forEach(r => {
        const firstConcept = r.ConceptNames.split(',')[0].trim();
        h += `<div class="nav-item" onclick="jumpToResult('${r.Name}', '${firstConcept}')">
                <span>${r.Name}</span>
              </div>`;
    });
    resultsDiv.innerHTML = h;
}

function jumpToResult(blockName, conceptName) {
    // 1. Switch to the concept
    switchConcept(conceptName);
    
    // 2. Update nav selection
    document.querySelectorAll('.nav-item').forEach(el => {
        if (el.textContent.includes(conceptName)) el.classList.add('active');
        else el.classList.remove('active');
    });
    
    // 3. Scroll to the block
    setTimeout(() => scrollToImplCard(blockName), 100);
    
    // 4. Clear search
    document.getElementById('search-input').value = '';
    document.getElementById('search-results').innerHTML = '';
}

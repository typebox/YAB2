function switchTab(tabId) {
    document.querySelectorAll('.doc-tab').forEach(t => t.classList.remove('active'));
    document.querySelectorAll('.doc-tab-content').forEach(t => t.classList.remove('active'));
    event.target.classList.add('active');
    const el = document.getElementById('tab-' + tabId);
    if (el) el.classList.add('active');
}

function toggleSidebar() {
    document.body.classList.toggle('sidebar-collapsed');
    localStorage.setItem('yab-sidebar-collapsed', document.body.classList.contains('sidebar-collapsed'));
}

function setLayout(layout) {
    document.body.classList.remove('layout-business', 'layout-code', 'layout-both');
    document.body.classList.add('layout-' + layout);
    document.querySelectorAll('.layout-btn').forEach(btn => btn.classList.remove('active'));
    const activeBtn = document.getElementById('btn-layout-' + layout);
    if (activeBtn) activeBtn.classList.add('active');
    localStorage.setItem('yab-layout', layout);
}

function scrollToImplCard(name) {
    const implPane = document.getElementById('impl-content');
    const cards = implPane.querySelectorAll('.impl-card');
    for (const card of cards) {
        const header = card.querySelector('.impl-filename');
        if (header && header.textContent.includes(name)) {
            card.scrollIntoView({ behavior: 'smooth', block: 'center' });
            card.style.transition = 'box-shadow 0.3s, border-color 0.3s';
            card.style.boxShadow = '0 0 0 2px var(--accent), 0 10px 25px -5px rgba(99,102,241,0.3)';
            card.style.borderColor = 'var(--accent)';
            setTimeout(() => {
                card.style.boxShadow = '0 10px 15px -3px rgba(0,0,0,0.1)';
                card.style.borderColor = '#334155';
            }, 2000);
            break;
        }
    }
}

function openModal() { document.getElementById('manual-modal').style.display = 'flex'; }
function closeModal() { document.getElementById('manual-modal').style.display = 'none'; }

async function submitResults() {
    const text = document.getElementById('ai-results-input').value;
    const res = await fetch('/api/audit-results', { method: 'POST', body: text });
    if (res.ok) {
        alert('Cache updated! Please re-run yab to see changes.');
        closeModal();
    } else {
        alert('Failed to update cache.');
    }
}

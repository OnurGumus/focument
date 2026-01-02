let currentHistoryDocId = null;

async function loadDocuments() {
    const response = await fetch('/api/documents');
    const documents = await response.json();
    const tbody = document.querySelector('#documentsTable tbody');
    tbody.innerHTML = '';

    documents.forEach(doc => {
        const row = document.createElement('tr');
        row.innerHTML = `
            <td class="cell-title">${escapeHtml(doc.title)}</td>
            <td class="cell-body">${escapeHtml(doc.body)}</td>
            <td class="cell-version">v${doc.version}</td>
            <td class="cell-date">${new Date(doc.updatedAt).toLocaleString()}</td>
            <td class="cell-actions">
                <button class="btn-small btn-edit" onclick="editDocument('${doc.id}', '${escapeHtml(doc.title).replace(/'/g, "\\'")}', '${escapeHtml(doc.body).replace(/'/g, "\\'")}')">Edit</button>
                <button class="btn-small btn-history" onclick="showHistory('${doc.id}')">History</button>
            </td>
        `;
        tbody.appendChild(row);
    });
}

function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
}

function editDocument(id, title, body) {
    document.getElementById('docId').value = id;
    document.getElementById('title').value = title;
    document.getElementById('content').value = body;
    document.getElementById('formTitle').textContent = 'Edit Document';
    document.getElementById('submitBtn').textContent = 'Update';
    document.getElementById('cancelEdit').style.display = 'block';
    document.getElementById('title').focus();
}

function cancelEdit() {
    document.getElementById('docId').value = '';
    document.getElementById('docForm').reset();
    document.getElementById('formTitle').textContent = 'Create Document';
    document.getElementById('submitBtn').textContent = 'Create';
    document.getElementById('cancelEdit').style.display = 'none';
}

async function showHistory(id) {
    currentHistoryDocId = id;
    const response = await fetch(`/api/document/${id}/history`);
    const versions = await response.json();
    const tbody = document.querySelector('#historyTable tbody');
    tbody.innerHTML = '';

    versions.forEach(v => {
        const row = document.createElement('tr');
        row.innerHTML = `
            <td class="cell-version">v${v.version}</td>
            <td class="cell-title">${escapeHtml(v.title)}</td>
            <td class="cell-date">${new Date(v.createdAt).toLocaleString()}</td>
            <td class="cell-actions">
                <button class="btn-small btn-restore" onclick="restoreVersion('${v.id}', ${v.version})">Set As Current</button>
            </td>
        `;
        tbody.appendChild(row);
    });

    document.getElementById('historySection').style.display = 'block';
}

function closeHistory() {
    document.getElementById('historySection').style.display = 'none';
    currentHistoryDocId = null;
}

async function restoreVersion(id, version) {
    const formData = new FormData();
    formData.append('Id', id);
    formData.append('Version', version);

    await fetch('/api/document/restore', {
        method: 'POST',
        body: formData
    });

    loadDocuments();
    showHistory(id);
}

document.getElementById('cancelEdit').addEventListener('click', cancelEdit);
document.getElementById('closeHistory').addEventListener('click', closeHistory);

document.getElementById('docForm').addEventListener('submit', async (e) => {
    e.preventDefault();
    const formData = new FormData(e.target);

    await fetch('/api/document', {
        method: 'POST',
        body: formData
    });

    cancelEdit();
    loadDocuments();
});

loadDocuments();

/**
 * Cell Knowledge Assistant — Web UI Single Page Application
 * Pure vanilla ES6+, zero external CDN dependencies, zero telemetry.
 * Communicates with REST and SSE endpoints:
 *   POST /api/query/stream
 *   GET /api/documents/{id}/file
 *   GET /api/history
 *   GET /api/health
 *   POST /api/admin/reindex
 */

'use strict';

// ============================================================================
// 1. Toast Notification Service
// ============================================================================
const Toast = {
  show(message, type = 'info', durationMs = 4000) {
    const container = document.getElementById('toastContainer');
    if (!container) return;

    const toast = document.createElement('div');
    toast.className = `toast toast-${type}`;
    toast.textContent = message;

    container.appendChild(toast);

    setTimeout(() => {
      toast.style.transition = 'opacity 0.3s ease, transform 0.3s ease';
      toast.style.opacity = '0';
      toast.style.transform = 'translateX(100%)';
      setTimeout(() => toast.remove(), 300);
    }, durationMs);
  }
};

// ============================================================================
// 2. Safe Markdown Parser (Zero External Libraries)
// ============================================================================
const Markdown = {
  render(text) {
    if (!text) return '';

    // Step 1: Escape raw HTML entities to prevent XSS
    let escaped = text
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;');

    // Step 2: Code blocks (```language ... ```)
    escaped = escaped.replace(/```([a-zA-Z0-9_-]*)\n([\s\S]*?)```/g, (match, lang, code) => {
      return `<pre><code class="language-${lang}">${code.trim()}</code></pre>`;
    });

    // Step 3: Inline code (`code`)
    escaped = escaped.replace(/`([^`]+)`/g, '<code>$1</code>');

    // Step 4: Headings (# H1, ## H2, ### H3)
    escaped = escaped.replace(/^### (.*$)/gim, '<h3 id="sec-$1">$1</h3>');
    escaped = escaped.replace(/^## (.*$)/gim, '<h2 id="sec-$1">$1</h2>');
    escaped = escaped.replace(/^# (.*$)/gim, '<h1 id="sec-$1">$1</h1>');

    // Step 5: Bold and Italics
    escaped = escaped.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
    escaped = escaped.replace(/\*([^*]+)\*/g, '<em>$1</em>');

    // Step 6: Simple Markdown Tables (| a | b |)
    const tableRegex = /((?:\|[^\n]+\|\r?\n)+)/g;
    escaped = escaped.replace(tableRegex, (match) => {
      const rows = match.trim().split('\n');
      if (rows.length < 2) return match;

      let html = '<table><thead>';
      const headers = rows[0].split('|').filter(c => c.trim() !== '');
      html += '<tr>' + headers.map(h => `<th>${h.trim()}</th>`).join('') + '</tr></thead><tbody>';

      // Skip row 1 (table header separator |---|---|)
      for (let i = 2; i < rows.length; i++) {
        const cols = rows[i].split('|').filter(c => c.trim() !== '');
        if (cols.length > 0) {
          html += '<tr>' + cols.map(c => `<td>${c.trim()}</td>`).join('') + '</tr>';
        }
      }
      html += '</tbody></table>';
      return html;
    });

    // Step 7: Unordered lists (- item or * item)
    escaped = escaped.replace(/^\s*[-*]\s+(.*$)/gim, '<li>$1</li>');
    escaped = escaped.replace(/(<li>.*<\/li>)/gims, (match) => `<ul>${match}</ul>`);

    // Clean duplicate nested <ul> wrappers
    escaped = escaped.replace(/<\/ul>\s*<ul>/g, '');

    // Step 8: Paragraphs
    const paragraphs = escaped.split(/\n{2,}/);
    return paragraphs
      .map(p => {
        const trimmed = p.trim();
        if (trimmed.startsWith('<h') || trimmed.startsWith('<pre') || trimmed.startsWith('<table') || trimmed.startsWith('<ul')) {
          return trimmed;
        }
        return `<p>${trimmed.replace(/\n/g, '<br>')}</p>`;
      })
      .join('\n');
  }
};

// ============================================================================
// 3. API Communication Client
// ============================================================================
const ApiClient = {
  // Streams SSE from POST /api/query/stream using fetch + ReadableStream
  async streamQuery(requestDto, onMetadata, onToken, onDone, onError, abortSignal) {
    try {
      const response = await fetch('/api/query/stream', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Accept': 'text/event-stream'
        },
        body: JSON.stringify(requestDto),
        signal: abortSignal
      });

      if (!response.ok) {
        let errMessage = `HTTP ${response.status} error`;
        try {
          const errData = await response.json();
          errMessage = errData.message || errMessage;
        } catch (_) {}
        throw new Error(errMessage);
      }

      const reader = response.body.getReader();
      const decoder = new TextDecoder('utf-8');
      let buffer = '';

      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        buffer += decoder.decode(value, { stream: true });
        const events = buffer.split('\n\n');
        buffer = events.pop(); // Keep incomplete tail

        for (const rawEvent of events) {
          if (!rawEvent.trim()) continue;
          const lines = rawEvent.split('\n');
          let eventType = 'message';
          let eventData = '';

          for (const line of lines) {
            if (line.startsWith('event:')) {
              eventType = line.substring(6).trim();
            } else if (line.startsWith('data:')) {
              eventData = line.substring(5).trim();
            }
          }

          if (eventType === 'metadata') {
            try {
              const meta = JSON.parse(eventData);
              onMetadata(meta);
            } catch (e) {
              console.error('Failed to parse metadata event', e);
            }
          } else if (eventType === 'token') {
            try {
              const tokenPayload = JSON.parse(eventData);
              onToken(tokenPayload.token || '');
            } catch (e) {
              onToken(eventData);
            }
          } else if (eventType === 'done') {
            onDone();
          } else if (eventType === 'error') {
            try {
              const errPayload = JSON.parse(eventData);
              onError(errPayload.message || 'Stream processing error');
            } catch (_) {
              onError(eventData);
            }
          }
        }
      }

      onDone();
    } catch (err) {
      if (err.name === 'AbortError') {
        onDone('Query aborted by user.');
      } else {
        onError(err.message || 'Network communication error');
      }
    }
  },

  async getHistory(limit = 20) {
    const res = await fetch(`/api/history?limit=${limit}`);
    if (!res.ok) throw new Error(`History fetch failed: HTTP ${res.status}`);
    return await res.json();
  },

  async getHealth() {
    const res = await fetch('/api/health');
    return {
      status: res.status,
      data: await res.json()
    };
  },

  async triggerReindex() {
    const res = await fetch('/api/admin/reindex', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' }
    });
    if (!res.ok) throw new Error(`Reindex failed: HTTP ${res.status}`);
    return await res.json();
  },

  async getDocumentFile(documentId) {
    const res = await fetch(`/api/documents/${documentId}/file`);
    if (!res.ok) {
      if (res.status === 403) throw new Error('Access forbidden: path outside vault root.');
      if (res.status === 404) throw new Error('Document not found in knowledge vault.');
      throw new Error(`File retrieval failed (HTTP ${res.status})`);
    }
    const contentType = res.headers.get('content-type') || '';
    if (contentType.includes('application/pdf')) {
      return { isPdf: true, url: `/api/documents/${documentId}/file` };
    }
    const text = await res.text();
    return { isPdf: false, text, url: `/api/documents/${documentId}/file` };
  }
};

// ============================================================================
// 4. UI Manager / State Machine
// ============================================================================
const App = {
  state: {
    currentTab: 'chat',
    isStreaming: false,
    abortController: null,
    pollTimer: null,
    equipmentFilter: '',
    folderFilter: ''
  },

  init() {
    this.bindDomEvents();
    this.loadSearchHistory();
    this.checkHealth();

    // Start background health polling every 10 seconds
    setInterval(() => this.checkHealth(), 10000);
  },

  bindDomEvents() {
    // Navigation Tabs
    document.getElementById('tabChat').addEventListener('click', () => this.switchTab('chat'));
    document.getElementById('tabAdmin').addEventListener('click', () => this.switchTab('admin'));

    // Sidebar Toggle
    document.getElementById('sidebarToggleBtn').addEventListener('click', () => {
      document.getElementById('historySidebar').classList.toggle('collapsed');
    });

    // New Chat
    document.getElementById('newChatBtn').addEventListener('click', () => this.resetChat());

    // Filters
    const eqSelect = document.getElementById('equipmentFilter');
    const folderSelect = document.getElementById('folderFilter');
    eqSelect.addEventListener('change', (e) => { this.state.equipmentFilter = e.target.value; });
    folderSelect.addEventListener('change', (e) => { this.state.folderFilter = e.target.value; });
    document.getElementById('clearFiltersBtn').addEventListener('click', () => {
      eqSelect.value = '';
      folderSelect.value = '';
      this.state.equipmentFilter = '';
      this.state.folderFilter = '';
    });

    // Prompt Chips
    document.querySelectorAll('.chip').forEach(chip => {
      chip.addEventListener('click', () => {
        const q = chip.getAttribute('data-query');
        if (q) {
          document.getElementById('queryInput').value = q;
          this.submitQuery();
        }
      });
    });

    // Input auto-resize and Enter submit
    const textarea = document.getElementById('queryInput');
    const counter = document.getElementById('charCounter');
    textarea.addEventListener('input', () => {
      textarea.style.height = 'auto';
      textarea.style.height = Math.min(textarea.scrollHeight, 180) + 'px';
      counter.textContent = `${textarea.value.length} / 2000`;
    });

    textarea.addEventListener('keydown', (e) => {
      if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault();
        this.submitQuery();
      }
    });

    // Form submit
    document.getElementById('queryForm').addEventListener('submit', (e) => {
      e.preventDefault();
      this.submitQuery();
    });

    // Stop Stream Button
    document.getElementById('stopStreamBtn').addEventListener('click', () => {
      if (this.state.abortController) {
        this.state.abortController.abort();
      }
    });

    // Admin Dashboard Controls
    document.getElementById('refreshAdminBtn').addEventListener('click', () => this.loadAdminMetrics());
    document.getElementById('reindexBtn').addEventListener('click', () => this.triggerReindex());

    // Document Viewer Modal Close
    document.getElementById('modalCloseBtn').addEventListener('click', () => this.closeModal());
    document.getElementById('docViewerModal').addEventListener('click', (e) => {
      if (e.target.id === 'docViewerModal') this.closeModal();
    });

    document.addEventListener('keydown', (e) => {
      if (e.key === 'Escape') this.closeModal();
    });
  },

  switchTab(tab) {
    this.state.currentTab = tab;
    const tabChat = document.getElementById('tabChat');
    const tabAdmin = document.getElementById('tabAdmin');
    const viewChat = document.getElementById('chatView');
    const viewAdmin = document.getElementById('adminView');

    if (tab === 'chat') {
      tabChat.classList.add('active');
      tabAdmin.classList.remove('active');
      viewChat.classList.add('active');
      viewAdmin.classList.remove('active');
    } else {
      tabAdmin.classList.add('active');
      tabChat.classList.remove('active');
      viewAdmin.classList.add('active');
      viewChat.classList.remove('active');
      this.loadAdminMetrics();
    }
  },

  resetChat() {
    const container = document.getElementById('messagesContainer');
    container.innerHTML = `
      <div id="emptyState" class="empty-state">
        <div class="empty-state-icon">
          <svg viewBox="0 0 24 24" class="svg-icon-hero"><path d="M21 6h-2v9H6v2c0 .55.45 1 1 1h11l4 4V7c0-.55-.45-1-1-1zm-4 6V3c0-.55-.45-1-1-1H3c-.55 0-1 .45-1 1v14l4-4h10c.55 0 1-.45 1-1z"/></svg>
        </div>
        <h2 class="empty-state-title">Industrial Knowledge Assistant</h2>
        <p class="empty-state-desc">Ask natural language or diagnostic questions against cell SOPs, manuals, PLC code logic, and electrical schematics.</p>
        <div class="prompt-chips">
          <button class="chip" data-query="What causes gripper timeout?">
            <span class="chip-query">"What causes gripper timeout?"</span>
            <span class="chip-tag">FLT-002 / ROBOT01</span>
          </button>
          <button class="chip" data-query="What causes alarm ALM-205?">
            <span class="chip-query">"What causes alarm ALM-205?"</span>
            <span class="chip-tag">ALM-205 / CONV01</span>
          </button>
          <button class="chip" data-query="What inputs are used for Safety Gate 2 interlocks?">
            <span class="chip-query">"What inputs are used for Safety Gate 2 interlocks?"</span>
            <span class="chip-tag">PLC01 / Safety</span>
          </button>
          <button class="chip" data-query="How to reset conveyor drive overload on VFD01?">
            <span class="chip-query">"How to reset conveyor drive overload on VFD01?"</span>
            <span class="chip-tag">VFD01 / Faults</span>
          </button>
        </div>
      </div>
    `;

    // Re-bind prompt chips
    document.querySelectorAll('.chip').forEach(chip => {
      chip.addEventListener('click', () => {
        const q = chip.getAttribute('data-query');
        if (q) {
          document.getElementById('queryInput').value = q;
          this.submitQuery();
        }
      });
    });

    document.getElementById('queryInput').value = '';
    document.getElementById('queryInput').style.height = 'auto';
    document.getElementById('charCounter').textContent = '0 / 2000';
  },

  // Submits query and coordinates SSE stream
  async submitQuery() {
    const input = document.getElementById('queryInput');
    const question = input.value.trim();
    if (!question || this.state.isStreaming) return;

    // Remove empty state if present
    const emptyState = document.getElementById('emptyState');
    if (emptyState) emptyState.remove();

    const eqFilter = this.state.equipmentFilter;
    const folderFilter = this.state.folderFilter;

    // 1. Render User Message Bubble
    this.renderUserMessage(question, eqFilter, folderFilter);
    input.value = '';
    input.style.height = 'auto';
    document.getElementById('charCounter').textContent = '0 / 2000';

    // 2. Prepare Assistant Message Bubble
    const { bubbleEl, bodyEl, badgeEl, citationsEl, copyBtn } = this.createAssistantMessageBubble();

    // 3. UI State: Streaming Active
    this.state.isStreaming = true;
    document.getElementById('sendBtn').disabled = true;
    document.getElementById('stopStreamBtn').classList.remove('hidden');

    this.state.abortController = new AbortController();

    let fullAnswer = '';
    let sources = [];

    const requestDto = {
      question: question,
      equipmentIdFilter: eqFilter || null,
      folderFilter: folderFilter || null
    };

    await ApiClient.streamQuery(
      requestDto,
      // onMetadata
      (meta) => {
        this.updateConfidenceBadge(badgeEl, meta.confidence);
        sources = meta.sources || [];
      },
      // onToken
      (token) => {
        fullAnswer += token;
        bodyEl.innerHTML = Markdown.render(fullAnswer) + '<span class="streaming-cursor"></span>';
        this.scrollToBottom();
      },
      // onDone
      () => {
        this.state.isStreaming = false;
        document.getElementById('sendBtn').disabled = false;
        document.getElementById('stopStreamBtn').classList.add('hidden');

        // Remove cursor and finish rendering
        bodyEl.innerHTML = Markdown.render(fullAnswer);

        // Render Citations
        if (sources && sources.length > 0) {
          this.renderCitations(citationsEl, sources);
        }

        // Enable copy button
        copyBtn.addEventListener('click', () => {
          navigator.clipboard.writeText(fullAnswer).then(() => {
            copyBtn.classList.add('copied');
            copyBtn.innerHTML = `
              <svg viewBox="0 0 24 24" class="svg-icon-xs"><path d="M9 16.2L4.8 12l-1.4 1.4L9 19 21 7l-1.4-1.4L9 16.2z"/></svg>
              <span>Copied!</span>
            `;
            setTimeout(() => {
              copyBtn.classList.remove('copied');
              copyBtn.innerHTML = `
                <svg viewBox="0 0 24 24" class="svg-icon-xs"><path d="M16 1H4c-1.1 0-2 .9-2 2v14h2V3h12V1zm3 4H8c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h11c1.1 0 2-.9 2-2V7c0-1.1-.9-2-2-2zm0 16H8V7h11v14z"/></svg>
                <span>Copy Answer</span>
              `;
            }, 2000);
          });
        });

        this.scrollToBottom();
        this.loadSearchHistory();
      },
      // onError
      (errorMsg) => {
        this.state.isStreaming = false;
        document.getElementById('sendBtn').disabled = false;
        document.getElementById('stopStreamBtn').classList.add('hidden');
        bodyEl.innerHTML = `<div style="color: var(--status-red);"><strong>Error:</strong> ${errorMsg}</div>`;
        Toast.show(errorMsg, 'error');
      },
      this.state.abortController.signal
    );
  },

  renderUserMessage(question, eqFilter, folderFilter) {
    const container = document.getElementById('messagesContainer');
    const row = document.createElement('div');
    row.className = 'message-row';

    let filtersHtml = '';
    if (eqFilter || folderFilter) {
      filtersHtml = '<div class="user-filters">';
      if (eqFilter) filtersHtml += `<span class="filter-badge">Eq: ${eqFilter}</span>`;
      if (folderFilter) filtersHtml += `<span class="filter-badge">Folder: ${folderFilter}</span>`;
      filtersHtml += '</div>';
    }

    row.innerHTML = `
      <div class="message-bubble user-message">
        <div class="user-text">${this.escapeHtml(question)}</div>
        ${filtersHtml}
      </div>
    `;

    container.appendChild(row);
    this.scrollToBottom();
  },

  createAssistantMessageBubble() {
    const container = document.getElementById('messagesContainer');
    const row = document.createElement('div');
    row.className = 'message-row';

    row.innerHTML = `
      <div class="message-bubble assistant-message">
        <div class="message-header">
          <div class="message-sender">
            <svg viewBox="0 0 24 24" class="svg-icon-xs"><path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z"/></svg>
            <span>Cell Knowledge Assistant</span>
            <span class="confidence-badge-slot"></span>
          </div>
          <div class="message-actions">
            <button class="btn-copy" title="Copy answer to clipboard">
              <svg viewBox="0 0 24 24" class="svg-icon-xs"><path d="M16 1H4c-1.1 0-2 .9-2 2v14h2V3h12V1zm3 4H8c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h11c1.1 0 2-.9 2-2V7c0-1.1-.9-2-2-2zm0 16H8V7h11v14z"/></svg>
              <span>Copy Answer</span>
            </button>
          </div>
        </div>
        <div class="message-body">
          <span class="streaming-cursor"></span>
        </div>
        <div class="citations-slot"></div>
      </div>
    `;

    container.appendChild(row);
    this.scrollToBottom();

    return {
      bubbleEl: row.querySelector('.assistant-message'),
      bodyEl: row.querySelector('.message-body'),
      badgeEl: row.querySelector('.confidence-badge-slot'),
      citationsEl: row.querySelector('.citations-slot'),
      copyBtn: row.querySelector('.btn-copy')
    };
  },

  // Sets confidence badge following Feature 23 E2E test standard
  updateConfidenceBadge(badgeEl, confidence) {
    if (!badgeEl) return;
    const level = (confidence || 'Low').trim();
    let badgeClass = 'badge-secondary';

    if (level.equalsIgnoreCase('High')) {
      badgeClass = 'badge-success'; // Green
    } else if (level.equalsIgnoreCase('Medium')) {
      badgeClass = 'badge-warning'; // Yellow / Amber
    } else if (level.equalsIgnoreCase('Low')) {
      badgeClass = 'badge-danger';  // Red
    }

    badgeEl.innerHTML = `<span class="badge ${badgeClass}"><span class="badge-dot"></span>${level} Confidence</span>`;
  },

  // Renders Citation Cards with Click-to-Open
  renderCitations(citationsEl, sources) {
    if (!citationsEl || !sources || sources.length === 0) return;

    let html = `
      <div class="citations-wrapper">
        <div class="citations-header">
          <svg viewBox="0 0 24 24" class="svg-icon-xs"><path d="M14 2H6c-1.1 0-1.99.9-1.99 2L4 20c0 1.1.89 2 1.99 2H18c1.1 0 2-.9 2-2V8l-6-6zm2 16H8v-2h8v2zm0-4H8v-2h8v2zm-3-5V3.5L18.5 9H13z"/></svg>
          <span>Sources Referenced (${sources.length})</span>
        </div>
        <div class="citation-cards-grid">
    `;

    sources.forEach((src) => {
      const isPdf = src.fileName.toLowerCase().endsWith('.pdf');
      const badgeType = isPdf ? '<span class="file-badge-type type-pdf">PDF</span>' : '<span class="file-badge-type type-md">MD</span>';
      
      let locText = '';
      if (src.pageNumber) {
        locText = `<span>Page ${src.pageNumber}</span>`;
      } else if (src.sectionHeading) {
        locText = `<span>§ ${this.escapeHtml(src.sectionHeading)}</span>`;
      } else {
        locText = `<span>${this.escapeHtml(src.relativePath)}</span>`;
      }

      html += `
        <div class="citation-card" data-doc-id="${src.documentId}" data-filename="${this.escapeHtml(src.fileName)}" data-path="${this.escapeHtml(src.relativePath)}" data-page="${src.pageNumber || ''}" data-section="${this.escapeHtml(src.sectionHeading || '')}">
          <div class="citation-card-top">
            ${badgeType}
            <span class="citation-file-name" title="${this.escapeHtml(src.fileName)}">${this.escapeHtml(src.fileName)}</span>
          </div>
          <div class="citation-details">
            ${locText}
          </div>
          <div class="citation-path">${this.escapeHtml(src.relativePath)}</div>
        </div>
      `;
    });

    html += `</div></div>`;
    citationsEl.innerHTML = html;

    // Attach click handlers to open viewer modal
    citationsEl.querySelectorAll('.citation-card').forEach(card => {
      card.addEventListener('click', () => {
        const docId = card.getAttribute('data-doc-id');
        const fileName = card.getAttribute('data-filename');
        const relPath = card.getAttribute('data-path');
        const pageNumber = card.getAttribute('data-page');
        const sectionHeading = card.getAttribute('data-section');

        this.openDocumentModal({
          documentId: docId,
          fileName,
          relativePath: relPath,
          pageNumber: pageNumber ? parseInt(pageNumber, 10) : null,
          sectionHeading
        });
      });
    });
  },

  // Opens Document Viewer Modal (PDF or Markdown)
  async openDocumentModal(source) {
    const modal = document.getElementById('docViewerModal');
    const titleEl = document.getElementById('modalDocTitle');
    const pathEl = document.getElementById('modalDocPath');
    const badgeEl = document.getElementById('modalDocBadge');
    const extLink = document.getElementById('modalExternalLink');
    const loadingEl = document.getElementById('modalLoading');
    const errorEl = document.getElementById('modalError');
    const mdContent = document.getElementById('modalMarkdownContent');
    const pdfContainer = document.getElementById('modalPdfContainer');
    const pdfFrame = document.getElementById('modalPdfFrame');

    modal.classList.remove('hidden');
    titleEl.textContent = source.fileName;
    pathEl.textContent = source.relativePath;
    
    if (source.pageNumber) {
      badgeEl.textContent = `Page ${source.pageNumber}`;
    } else if (source.sectionHeading) {
      badgeEl.textContent = `§ ${source.sectionHeading}`;
    } else {
      badgeEl.textContent = 'Document';
    }

    loadingEl.classList.remove('hidden');
    errorEl.classList.add('hidden');
    mdContent.innerHTML = '';
    pdfContainer.classList.add('hidden');
    pdfFrame.src = 'about:blank';

    const isPdf = source.fileName.toLowerCase().endsWith('.pdf');
    const fileUrl = `/api/documents/${source.documentId}/file` + (source.pageNumber ? `#page=${source.pageNumber}` : '');
    extLink.href = fileUrl;

    try {
      if (isPdf) {
        loadingEl.classList.add('hidden');
        pdfContainer.classList.remove('hidden');
        pdfFrame.src = fileUrl;
      } else {
        const docData = await ApiClient.getDocumentFile(source.documentId);
        loadingEl.classList.add('hidden');
        mdContent.innerHTML = Markdown.render(docData.text);

        // If a section heading is specified, scroll to it and highlight
        if (source.sectionHeading) {
          setTimeout(() => {
            const headingCandidates = mdContent.querySelectorAll('h1, h2, h3');
            for (const h of headingCandidates) {
              if (h.textContent.toLowerCase().includes(source.sectionHeading.toLowerCase())) {
                h.classList.add('highlight-heading');
                h.scrollIntoView({ behavior: 'smooth', block: 'start' });
                break;
              }
            }
          }, 100);
        }
      }
    } catch (err) {
      loadingEl.classList.add('hidden');
      errorEl.classList.remove('hidden');
      errorEl.textContent = `Failed to load document: ${err.message}`;
    }
  },

  closeModal() {
    const modal = document.getElementById('docViewerModal');
    modal.classList.add('hidden');
    document.getElementById('modalPdfFrame').src = 'about:blank';
  },

  // Loads search history from GET /api/history
  async loadSearchHistory() {
    const listEl = document.getElementById('historyList');
    try {
      const history = await ApiClient.getHistory(20);
      if (!history || history.length === 0) {
        listEl.innerHTML = '<div class="history-empty">No search queries yet.</div>';
        return;
      }

      listEl.innerHTML = '';
      history.forEach(item => {
        const itemEl = document.createElement('div');
        itemEl.className = 'history-item';
        
        let confClass = 'badge-secondary';
        const conf = (item.confidence || 'Low').toLowerCase();
        if (conf === 'high') confClass = 'badge-success';
        else if (conf === 'medium') confClass = 'badge-warning';
        else if (conf === 'low') confClass = 'badge-danger';

        const askedTime = item.askedUtc ? new Date(item.askedUtc).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }) : '';

        itemEl.innerHTML = `
          <div class="history-question" title="${this.escapeHtml(item.question)}">${this.escapeHtml(item.question)}</div>
          <div class="history-meta">
            <span class="badge ${confClass}">${item.confidence}</span>
            <span>${askedTime}</span>
          </div>
        `;

        itemEl.addEventListener('click', () => {
          this.switchTab('chat');
          document.getElementById('queryInput').value = item.question;
          // Replay conversation in chat viewport
          const emptyState = document.getElementById('emptyState');
          if (emptyState) emptyState.remove();

          this.renderUserMessage(item.question, null, null);
          const { bodyEl, badgeEl, citationsEl, copyBtn } = this.createAssistantMessageBubble();
          this.updateConfidenceBadge(badgeEl, item.confidence);
          bodyEl.innerHTML = Markdown.render(item.answer || '');
          if (item.sources && item.sources.length > 0) {
            this.renderCitations(citationsEl, item.sources);
          }

          copyBtn.addEventListener('click', () => {
            navigator.clipboard.writeText(item.answer || '').then(() => {
              Toast.show('Answer copied to clipboard!', 'info');
            });
          });

          this.scrollToBottom();
        });

        listEl.appendChild(itemEl);
      });
    } catch (err) {
      listEl.innerHTML = '<div class="history-empty">Could not load history.</div>';
    }
  },

  // Checks /api/health and updates status indicator
  async checkHealth() {
    const badge = document.getElementById('systemHealthBadge');
    const text = document.getElementById('systemHealthText');

    try {
      const res = await ApiClient.getHealth();
      const isHealthy = res.status === 200 && (res.data.status === 'Healthy' || res.data.api === 'Healthy');

      badge.classList.remove('healthy', 'unhealthy');
      if (isHealthy) {
        badge.classList.add('healthy');
        text.textContent = 'System Online';
      } else {
        badge.classList.add('unhealthy');
        text.textContent = res.data.status || 'Degraded';
      }

      // If viewing admin dashboard, update subsystem cards
      if (this.state.currentTab === 'admin') {
        this.updateHealthCards(res.data);
      }
    } catch (_) {
      badge.classList.remove('healthy');
      badge.classList.add('unhealthy');
      text.textContent = 'Offline';
    }
  },

  // Loads admin dashboard metrics
  async loadAdminMetrics() {
    const docCountEl = document.getElementById('metricDocCount');
    const vectorCountEl = document.getElementById('metricVectorCount');
    const lastRunEl = document.getElementById('metricLastRun');
    const indexerStateEl = document.getElementById('metricIndexerState');

    try {
      const res = await ApiClient.getHealth();
      const healthData = res.data;

      this.updateHealthCards(healthData);

      // Extract metrics if available or use descriptors
      if (healthData.components) {
        const comps = healthData.components;
        if (comps.indexer) {
          indexerStateEl.textContent = comps.indexer.status || 'Idle';
          lastRunEl.textContent = comps.indexer.description ? comps.indexer.description.replace('Watcher active, ', '') : 'Recent';
        }
        if (comps.sqlite && comps.sqlite.description) {
          const m = comps.sqlite.description.match(/(\d+)\s+docs/);
          if (m) docCountEl.textContent = m[1];
        }
        if (comps.qdrant && comps.qdrant.description) {
          const m = comps.qdrant.description.match(/(\d+)\s+vectors/);
          if (m) vectorCountEl.textContent = m[1];
        }
      }

      // If flattened format
      if (indexerStateEl.textContent === '--') {
        indexerStateEl.textContent = healthData.indexer || 'Idle';
      }
    } catch (err) {
      Toast.show('Failed to load system metrics: ' + err.message, 'error');
    }
  },

  updateHealthCards(healthData) {
    const updateCard = (badgeId, descId, status, desc) => {
      const badge = document.getElementById(badgeId);
      const descEl = document.getElementById(descId);
      if (!badge) return;

      const isHealthy = (status || '').toLowerCase() === 'healthy';
      badge.className = `badge ${isHealthy ? 'badge-success' : 'badge-danger'}`;
      badge.textContent = status || 'Unknown';
      if (desc && descEl) descEl.textContent = desc;
    };

    if (healthData.components) {
      const c = healthData.components;
      if (c.api) updateCard('healthApiBadge', 'healthApiDesc', c.api.status, c.api.description);
      if (c.ollama) updateCard('healthOllamaBadge', 'healthOllamaDesc', c.ollama.status, c.ollama.description);
      if (c.qdrant) updateCard('healthQdrantBadge', 'healthQdrantDesc', c.qdrant.status, c.qdrant.description);
      if (c.sqlite) updateCard('healthSqliteBadge', 'healthSqliteDesc', c.sqlite.status, c.sqlite.description);
      if (c.indexer) updateCard('healthIndexerBadge', 'healthIndexerDesc', c.indexer.status, c.indexer.description);
    } else {
      // Flattened HealthResponseDto
      updateCard('healthApiBadge', 'healthApiDesc', healthData.api);
      updateCard('healthOllamaBadge', 'healthOllamaDesc', healthData.ollama);
      updateCard('healthQdrantBadge', 'healthQdrantDesc', healthData.qdrant);
      updateCard('healthSqliteBadge', 'healthSqliteDesc', healthData.sqlite);
      updateCard('healthIndexerBadge', 'healthIndexerDesc', healthData.indexer);
    }
  },

  // Triggers manual reindex via POST /api/admin/reindex
  async triggerReindex() {
    const btn = document.getElementById('reindexBtn');
    const btnText = document.getElementById('reindexBtnText');
    const icon = document.getElementById('reindexIcon');

    btn.disabled = true;
    icon.classList.add('spin');
    btnText.textContent = 'Reindexing in progress...';

    try {
      const res = await ApiClient.triggerReindex();
      Toast.show('Vault reindex initiated! Reconciling SHA-256 hashes...', 'info', 5000);

      // Fast polling until indexer returns to idle
      let pollAttempts = 0;
      const pollInterval = setInterval(async () => {
        pollAttempts++;
        const health = await ApiClient.getHealth();
        const indexerStatus = health.data.indexer || (health.data.components && health.data.components.indexer ? health.data.components.indexer.status : '');

        if (indexerStatus.toLowerCase() === 'idle' || indexerStatus.toLowerCase() === 'healthy' || pollAttempts > 15) {
          clearInterval(pollInterval);
          btn.disabled = false;
          icon.classList.remove('spin');
          btnText.textContent = 'Trigger Vault Reindex';
          Toast.show('Knowledge vault reindex complete! Vectors and metadata synchronized.', 'success', 5000);
          this.loadAdminMetrics();
        }
      }, 1500);
    } catch (err) {
      btn.disabled = false;
      icon.classList.remove('spin');
      btnText.textContent = 'Trigger Vault Reindex';
      Toast.show('Reindex failed: ' + err.message, 'error');
    }
  },

  scrollToBottom() {
    const container = document.getElementById('messagesContainer');
    container.scrollTop = container.scrollHeight;
  },

  escapeHtml(str) {
    if (!str) return '';
    return str
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&#039;');
  }
};

// Polyfill helper for string case-insensitive comparison
String.prototype.equalsIgnoreCase = function(other) {
  return typeof other === 'string' && this.toLowerCase() === other.toLowerCase();
};

// Initialize Application once DOM is ready
document.addEventListener('DOMContentLoaded', () => {
  App.init();
});

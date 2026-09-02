// Pipeline Bags Explorer & Raw JSON Editing Logic

function setBagViewMode(mode) {
  currentBagViewMode = mode;
  const btnCards = document.getElementById('btnModeCards');
  const btnJson = document.getElementById('btnModeJson');
  if (btnCards) btnCards.classList.toggle('active', mode === 'cards');
  if (btnJson) btnJson.classList.toggle('active', mode === 'json');
  renderBagContent();
}

function openEditBagModal() {
  if (!isAuthenticatedUser) {
    showToast('Please login as Admin to edit bags', 'warning');
    openLoginModal();
    return;
  }

  setBagViewMode('json');
  const textarea = document.getElementById('bagJsonTextarea');
  if (textarea) {
    textarea.focus();
    textarea.scrollIntoView({ behavior: 'smooth' });
  }
}

async function handleSaveRawJsonInline() {
  const textarea = document.getElementById('bagJsonTextarea');
  const saveBtn = document.getElementById('btnSaveRawJson');
  const errorMsg = document.getElementById('jsonInlineErrorMsg');

  if (!textarea) return;

  let rawJsonInput = textarea.value.trim();

  // If empty input, default to {} for worker status or [] for item bags
  if (!rawJsonInput) {
    if (currentSelectedBag && currentSelectedBag.startsWith('worker_')) {
      rawJsonInput = '{}';
    } else {
      rawJsonInput = '[]';
    }
    textarea.value = rawJsonInput;
  }

  let parsedObject;
  // Client-side JSON Syntax Validation
  try {
    parsedObject = JSON.parse(rawJsonInput);
  } catch (jsonErr) {
    if (errorMsg) {
      errorMsg.innerText = `Invalid JSON syntax: ${jsonErr.message}`;
      errorMsg.style.display = 'block';
    }
    showToast('JSON syntax error! Please fix before saving.', 'error');
    return;
  }

  if (saveBtn) saveBtn.disabled = true;
  if (errorMsg) errorMsg.style.display = 'none';

  try {
    const res = await fetch(`/api/dashboard/bags/${encodeURIComponent(currentSelectedBag)}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(parsedObject)
    });

    if (!res.ok) {
      const errData = await res.json().catch(() => ({}));
      throw new Error(errData.message || `HTTP Error ${res.status}`);
    }

    const data = await res.json();
    showToast(data.message || `Saved changes to ${currentSelectedBag}!`, 'success');

    // Reload active view to refresh cards & stats
    fetchDashboardOverview(false);
    loadSelectedBag(currentSelectedBag);
  } catch (err) {
    console.error('Error saving raw JSON content:', err);
    if (errorMsg) {
      errorMsg.innerText = err.message || 'Failed to save bag content.';
      errorMsg.style.display = 'block';
    }
    showToast('Failed to save bag content', 'error');
  } finally {
    if (saveBtn) saveBtn.disabled = false;
  }
}

function formatRawJson() {
  const textarea = document.getElementById('bagJsonTextarea');
  const errorMsg = document.getElementById('jsonInlineErrorMsg');
  if (!textarea) return;

  try {
    const parsed = JSON.parse(textarea.value);
    textarea.value = JSON.stringify(parsed, null, 2);
    if (errorMsg) errorMsg.style.display = 'none';
    showToast('Formatted JSON structure', 'info');
  } catch (err) {
    if (errorMsg) {
      errorMsg.innerText = `Cannot format: ${err.message}`;
      errorMsg.style.display = 'block';
    }
    showToast('Cannot format invalid JSON', 'error');
  }
}

function resetRawJson() {
  const textarea = document.getElementById('bagJsonTextarea');
  const errorMsg = document.getElementById('jsonInlineErrorMsg');
  if (textarea) textarea.value = currentRawJson;
  if (errorMsg) errorMsg.style.display = 'none';
  showToast('Reset to original bag content', 'info');
}

// Open Bag in Explorer Tab
function openBagInExplorer(bagFileName) {
  switchTab('bags');
  loadSelectedBag(bagFileName);
}

// Load Bag Contents
async function loadSelectedBag(bagFileName) {
  currentSelectedBag = bagFileName;
  
  // Highlight active sidebar item
  document.querySelectorAll('.bag-item-btn').forEach(btn => {
    const isTarget = btn.innerText.includes(bagFileName);
    btn.classList.toggle('active', isTarget);
  });

  const titleEl = document.getElementById('selectedBagTitle');
  const redactedBadge = document.getElementById('redactedBadge');
  const editBtn = document.getElementById('btnEditBag');

  if (titleEl) {
    titleEl.innerHTML = `<i data-lucide="${getBagIcon(bagFileName)}" style="color: ${getBagColor(bagFileName)};"></i> ${bagFileName}`;
    if (window.lucide) lucide.createIcons({ targets: [titleEl] });
  }

  try {
    const res = await fetch(`/api/dashboard/bags/${encodeURIComponent(bagFileName)}`);
    
    // Handle 403 Forbidden (Guest trying to read sensitive config/env files)
    if (res.status === 403) {
      isCurrentBagRedacted = true;
      currentRawJson = JSON.stringify({ error: "Access Restricted", message: "This sensitive configuration file requires Admin login." }, null, 2);
      currentBagData = [];

      if (redactedBadge) redactedBadge.style.display = 'inline-flex';
      if (editBtn) editBtn.style.display = 'none';

      const countEl = document.getElementById('selectedBagItemCount');
      if (countEl) countEl.innerText = 'Restricted';

      renderRestrictedState(bagFileName);
      return;
    }

    if (!res.ok) throw new Error(`HTTP ${res.status}`);

    const data = await res.json();
    currentRawJson = data.rawJson || '[]';
    currentBagData = data.parsed || [];
    isCurrentBagRedacted = data.isRedacted || false;

    // Redacted badge & edit button visibility
    if (redactedBadge) {
      redactedBadge.style.display = isCurrentBagRedacted ? 'inline-flex' : 'none';
    }

    if (editBtn) {
      editBtn.style.display = isAuthenticatedUser ? 'inline-flex' : 'none';
    }

    const textarea = document.getElementById('bagJsonTextarea');
    if (textarea) {
      textarea.value = currentRawJson;
      textarea.readOnly = !isAuthenticatedUser;
    }

    const countEl = document.getElementById('selectedBagItemCount');
    if (countEl) {
      const count = Array.isArray(currentBagData) ? currentBagData.length : (currentBagData ? 1 : 0);
      countEl.innerText = `${count} item(s)`;
    }

    try {
      renderBagSummaryBanner();
      renderBagContent();
    } catch (renderErr) {
      console.error('Error rendering bag UI content:', renderErr);
    }
  } catch (err) {
    console.error(`Error loading bag ${bagFileName}:`, err);
    showToast(`Failed to load content for ${bagFileName}`, 'error');
  }
}

// Render Restricted state for 403 files
function renderRestrictedState(fileName) {
  const cardsContainer = document.getElementById('cardsViewContainer');
  const jsonContainer = document.getElementById('jsonViewContainer');
  const summaryBanner = document.getElementById('inspectorSummaryBanner');

  if (summaryBanner) summaryBanner.style.display = 'none';
  if (jsonContainer) jsonContainer.style.display = 'none';

  if (cardsContainer) {
    cardsContainer.style.display = 'grid';
    cardsContainer.innerHTML = `
      <div class="empty-state" style="grid-column: 1 / -1; padding: 3rem 1.5rem;">
        <i data-lucide="shield-lock" class="empty-icon" style="color: #f43f5e; width: 48px; height: 48px;"></i>
        <h3 style="color: white; margin-top: 1rem; font-size: 1.1rem;">Sensitive File Access Restricted</h3>
        <p style="color: var(--text-muted); max-width: 420px; margin: 0.5rem auto 1.5rem auto; font-size: 0.9rem; line-height: 1.5;">
          <strong>${escapeHtml(fileName)}</strong> contains sensitive system settings or security credentials. Log in as an Administrator to view and manage this file.
        </p>
        <button class="btn btn-primary" onclick="openLoginModal()">
          <i data-lucide="log-in" style="width: 14px; height: 14px;"></i> Login as Admin
        </button>
      </div>
    `;
    if (window.lucide) lucide.createIcons({ targets: [cardsContainer] });
  }
}

// Render Top Visual Summary Banner for Bag Inspector
function renderBagSummaryBanner() {
  const container = document.getElementById('inspectorSummaryBanner');
  if (!container) return;

  let items = Array.isArray(currentBagData) ? currentBagData : (currentBagData ? [currentBagData] : []);

  if (items.length === 0) {
    container.style.display = 'none';
    return;
  }

  container.style.display = 'flex';

  // Extract metrics
  const streamers = new Set();
  const gameKinds = new Set();
  let failedCount = 0;

  items.forEach(item => {
    if (!item || typeof item !== 'object') return;
    const desc = typeof item.Description === 'string' ? item.Description : (item.Description ? String(item.Description) : '');
    const kindKey = typeof item.KindKey === 'string' ? item.KindKey : (item.KindKey ? String(item.KindKey) : '');

    if (desc) streamers.add(desc);
    if (kindKey && kindKey !== '***SENSITIVE_DATA_REDACTED***') gameKinds.add(kindKey.toUpperCase());
    if (desc && desc.includes('FAILED')) failedCount++;
  });

  let html = `
    <div class="summary-stat-pill">
      <i data-lucide="layers" style="color: var(--primary);"></i>
      <span>Total: <strong>${items.length}</strong></span>
    </div>
  `;

  if (gameKinds.size > 0) {
    html += `
      <div class="summary-stat-pill">
        <i data-lucide="gamepad-2" style="color: var(--accent-cyan);"></i>
        <span>Games: <strong>${Array.from(gameKinds).join(', ')}</strong></span>
      </div>
    `;
  }

  if (streamers.size > 0) {
    html += `
      <div class="summary-stat-pill">
        <i data-lucide="twitch" style="color: #9146FF;"></i>
        <span>Streamers (${streamers.size}): <strong>${Array.from(streamers).slice(0, 4).join(', ')}${streamers.size > 4 ? '...' : ''}</strong></span>
      </div>
    `;
  }

  if (failedCount > 0) {
    html += `
      <div class="summary-stat-pill warning">
        <i data-lucide="alert-triangle" style="color: var(--accent-rose);"></i>
        <span>Failed: <strong>${failedCount}</strong></span>
      </div>
    `;
  }

  if (isCurrentBagRedacted) {
    html += `
      <div class="summary-stat-pill warning">
        <i data-lucide="eye-off" style="color: #fda4af;"></i>
        <span>Security: <strong>Guest Sanitized</strong></span>
      </div>
    `;
  }

  container.innerHTML = html;
  if (window.lucide) lucide.createIcons({ targets: [container] });
}

// Render Bag Content according to View Mode & Filter Search
function renderBagContent() {
  const searchTerm = (document.getElementById('bagSearchInput')?.value || '').toLowerCase();
  const cardsContainer = document.getElementById('cardsViewContainer');
  const jsonContainer = document.getElementById('jsonViewContainer');

  if (currentBagViewMode === 'json') {
    cardsContainer.style.display = 'none';
    jsonContainer.style.display = 'block';
    return;
  }

  cardsContainer.style.display = 'grid';
  jsonContainer.style.display = 'none';

  let itemsToRender = currentBagData;
  if (!Array.isArray(itemsToRender)) {
    itemsToRender = itemsToRender ? [itemsToRender] : [];
  }

  if (searchTerm) {
    itemsToRender = itemsToRender.filter(item => JSON.stringify(item).toLowerCase().includes(searchTerm));
  }

  if (itemsToRender.length === 0) {
    cardsContainer.innerHTML = `
      <div class="empty-state" style="grid-column: 1 / -1;">
        <i data-lucide="inbox" class="empty-icon"></i>
        <p>No items found matching criteria.</p>
      </div>
    `;
    if (window.lucide) lucide.createIcons({ targets: [cardsContainer] });
    return;
  }

  cardsContainer.innerHTML = itemsToRender.map((item, idx) => renderVisualItemCard(item, idx)).join('');

  if (window.lucide) lucide.createIcons({ targets: [cardsContainer] });
}

// Smart Visual Card Renderer per domain type (ClipInfo, MontageItem, etc.)
function renderVisualItemCard(item, idx) {
  if (!item || typeof item !== 'object') {
    return `<div class="item-card"><div class="item-value">${escapeHtml(String(item))}</div></div>`;
  }

  // Detect ClipInfo model (has Link, Description, Start, End)
  if (item.Link || (item.Start && item.End)) {
    return renderClipInfoCard(item, idx);
  }

  // Detect MontageItem model (has MontageId, Clips, FinalVideoPath)
  if (item.MontageId || item.Clips) {
    return renderMontageItemCard(item, idx);
  }

  // Fallback: Clean generic card
  const jsonStr = escapeJsString(JSON.stringify(item));
  return `
    <div class="item-card">
      <div class="item-card-header">
        <span class="item-badge primary">Record #${idx + 1}</span>
        <button class="icon-btn-small" onclick="copyToClipboard('${jsonStr}', 'Item JSON')" title="Copy Item JSON">
          <i data-lucide="copy" style="width: 13px; height: 13px;"></i>
        </button>
      </div>
      <div class="item-card-body">
        ${renderItemFields(item)}
      </div>
    </div>
  `;
}

// Specialized Visual Card for Twitch Clips (ClipInfo)
function renderClipInfoCard(item, idx) {
  const desc = typeof item.Description === 'string' ? item.Description : (item.Description ? String(item.Description) : '');
  const streamer = desc || 'Unknown Streamer';
  const isFailed = desc.includes('FAILED');
  const isTemp = currentSelectedBag === 'bag_download_temp.json';

  const kindKeyStr = typeof item.KindKey === 'string' ? item.KindKey : (item.KindKey ? String(item.KindKey) : '');
  const gameTag = (kindKeyStr && kindKeyStr !== '***SENSITIVE_DATA_REDACTED***') ? kindKeyStr.toUpperCase() : 'GAME';

  const subKindStr = typeof item.SubKindKey === 'string' ? item.SubKindKey : (item.SubKindKey ? String(item.SubKindKey) : '');
  const subKindTag = (subKindStr && subKindStr !== '***SENSITIVE_DATA_REDACTED***') ? subKindStr : '';
  const isPentas = subKindTag.toLowerCase().includes('penta');

  const startStr = item.Start !== undefined && item.Start !== null ? String(item.Start) : '';
  const endStr = item.End !== undefined && item.End !== null ? String(item.End) : '';
  const duration = (startStr && endStr) ? `${startStr} ➔ ${endStr}` : '';
  const importedDate = item.ImportedAt ? formatDate(item.ImportedAt) : '';
  const linkStr = item.Link ? String(item.Link) : '';

  return `
    <div class="item-card ${isFailed ? 'card-failed' : ''} ${isTemp ? 'card-temp-staging' : ''}">
      <div class="item-card-header">
        <div style="display: flex; align-items: center; gap: 0.5rem; flex-wrap: wrap;">
          <span class="streamer-badge">
            <i data-lucide="twitch" style="width: 12px; height: 12px; color: #9146FF;"></i>
            ${escapeHtml(streamer)}
          </span>
          <span class="tag-chip game">${escapeHtml(gameTag)}</span>
          ${subKindTag ? `<span class="tag-chip ${isPentas ? 'penta' : 'subkind'}">${isPentas ? '🔥 ' : ''}${escapeHtml(subKindTag)}</span>` : ''}
          ${isTemp ? `<span class="tag-chip warning"><i data-lucide="loader" style="width: 10px; height: 10px; animation: spin 2s linear infinite;"></i> Staging</span>` : ''}
        </div>
        <span class="item-badge secondary">#${idx + 1}</span>
      </div>

      <div class="item-card-body">
        ${duration ? `
          <div class="clip-time-bar">
            <i data-lucide="clock" style="width: 14px; height: 14px; color: var(--accent-cyan);"></i>
            <span>Segment: <strong>${escapeHtml(duration)}</strong></span>
          </div>
        ` : ''}

        ${linkStr ? `
          <div class="clip-action-bar">
            <a href="${escapeHtml(linkStr)}" target="_blank" rel="noopener" class="btn btn-secondary btn-sm" style="flex: 1; justify-content: center; text-decoration: none;">
              <i data-lucide="external-link" style="width: 14px; height: 14px;"></i> Watch Twitch Clip
            </a>
            <button class="btn btn-secondary btn-sm" onclick="copyToClipboard('${escapeHtml(linkStr)}', 'Clip Link')" title="Copy URL">
              <i data-lucide="copy" style="width: 14px; height: 14px;"></i>
            </button>
          </div>
        ` : ''}

        ${importedDate ? `
          <div class="item-meta-footer">
            <i data-lucide="calendar" style="width: 12px; height: 12px;"></i> ${importedDate}
          </div>
        ` : ''}
      </div>
    </div>
  `;
}

// Specialized Visual Card for Montage Packages (MontageItem)
function renderMontageItemCard(item, idx) {
  const title = item.GeneratedTitle || item.MontageId || `Montage #${idx + 1}`;
  const clipsCount = Array.isArray(item.Clips) ? item.Clips.length : 0;
  const isDone = item.Completed === true;

  return `
    <div class="item-card card-montage">
      <div class="item-card-header">
        <span class="tag-chip ${isDone ? 'success' : 'warning'}">
          <i data-lucide="${isDone ? 'check-circle' : 'loader'}" style="width: 12px; height: 12px;"></i>
          ${isDone ? 'Published' : 'Pending Upload'}
        </span>
        <span class="item-badge secondary">${clipsCount} Clips</span>
      </div>

      <div class="item-card-body">
        <h4 style="font-size: 0.95rem; font-weight: 700; color: white; margin-bottom: 0.5rem; line-height: 1.4;">
          ${escapeHtml(title)}
        </h4>

        ${item.TotalDuration ? `
          <div class="clip-time-bar">
            <i data-lucide="film" style="width: 14px; height: 14px; color: var(--accent-amber);"></i>
            <span>Duration: <strong>${escapeHtml(item.TotalDuration)}</strong></span>
          </div>
        ` : ''}

        ${item.YoutubeUrl ? `
          <div class="clip-action-bar" style="margin-top: 0.5rem;">
            <a href="${escapeHtml(item.YoutubeUrl)}" target="_blank" rel="noopener" class="btn btn-primary btn-sm" style="flex: 1; justify-content: center; text-decoration: none; background: #FF0000; border-color: #FF0000;">
              <i data-lucide="youtube" style="width: 14px; height: 14px;"></i> Open YouTube
            </a>
          </div>
        ` : ''}
      </div>
    </div>
  `;
}

// Render key-value pairs for non-specialized cards
function renderItemFields(obj) {
  if (typeof obj !== 'object' || obj === null) {
    return `<div class="item-value">${escapeHtml(String(obj))}</div>`;
  }

  return Object.entries(obj).map(([key, val]) => {
    let displayVal = val;
    if (typeof val === 'object' && val !== null) displayVal = JSON.stringify(val, null, 1);

    const isUrl = typeof displayVal === 'string' && (displayVal.startsWith('http://') || displayVal.startsWith('https://'));
    const formattedVal = isUrl 
      ? `<a href="${displayVal}" target="_blank" rel="noopener">${escapeHtml(displayVal)}</a>` 
      : escapeHtml(String(displayVal));

    return `
      <div class="item-field">
        <span class="item-label">${escapeHtml(key)}</span>
        <div class="item-value">${formattedVal}</div>
      </div>
    `;
  }).join('');
}

// Render Bags Grid on Overview Tab
function renderBagsOverviewGrid(bags) {
  const container = document.getElementById('bagsOverviewGrid');
  if (!container || !bags) return;

  container.innerHTML = bags.map(bag => `
    <div class="card">
      <div class="card-header">
        <div class="card-title">
          <i data-lucide="${getBagIcon(bag.fileName)}" style="color: ${getBagColor(bag.fileName)};"></i>
          ${bag.fileName}
        </div>
        <span class="card-count">${bag.itemCount} items</span>
      </div>
      <div style="font-size: 0.85rem; color: var(--text-muted); margin-bottom: 1rem;">
        <div>Size: <strong>${formatFileSize(bag.sizeBytes)}</strong></div>
        <div>Modified: <strong>${formatDate(bag.lastModified)}</strong></div>
      </div>
      <div style="margin-top: auto; display: flex; justify-content: flex-end;">
        <button class="btn btn-secondary" style="padding: 0.35rem 0.75rem; font-size: 0.8rem;" onclick="openBagInExplorer('${bag.fileName}')">
          <i data-lucide="eye"></i> Inspect Items
        </button>
      </div>
    </div>
  `).join('');

  if (window.lucide) lucide.createIcons({ targets: [container] });
}

// Render Bags Sidebar List
function renderBagsSidebarList(bags) {
  const container = document.getElementById('bagsSidebarList');
  if (!container || !bags) return;

  container.innerHTML = bags.map(bag => `
    <button class="bag-item-btn ${bag.fileName === currentSelectedBag ? 'active' : ''}" onclick="loadSelectedBag('${bag.fileName}')">
      <span class="bag-item-name">
        <i data-lucide="${getBagIcon(bag.fileName)}" style="width: 16px; height: 16px; color: ${getBagColor(bag.fileName)};"></i>
        ${bag.fileName}
      </span>
      <span style="font-size: 0.75rem; opacity: 0.8;">${bag.itemCount}</span>
    </button>
  `).join('');

  if (window.lucide) lucide.createIcons({ targets: [container] });
}

// Helper icons and colors per bag
function getBagIcon(name) {
  if (name.includes('download')) return 'download';
  if (name.includes('trimmed')) return 'scissors';
  if (name.includes('montage')) return 'film';
  if (name.includes('youtube_done')) return 'check-circle-2';
  if (name.includes('youtube')) return 'youtube';
  if (name.includes('failed')) return 'alert-circle';
  if (name.includes('history')) return 'history';
  if (name.includes('worker')) return 'cpu';
  return 'file-json';
}

function getBagColor(name) {
  if (name.includes('download')) return 'var(--accent-cyan)';
  if (name.includes('trimmed')) return 'var(--primary)';
  if (name.includes('montage')) return 'var(--accent-amber)';
  if (name.includes('youtube_done')) return 'var(--accent-emerald)';
  if (name.includes('youtube')) return '#a855f7';
  if (name.includes('failed')) return 'var(--accent-rose)';
  return 'var(--text-muted)';
}

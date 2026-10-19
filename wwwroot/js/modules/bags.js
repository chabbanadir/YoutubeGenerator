// Pipeline Bags Explorer & Raw JSON Editing Module
import { state, showToast, escapeHtml, escapeJsString, formatFileSize, formatDate, getApiUrl, getYoutubeIcon, getTwitchIcon } from '../core/api.js';

export function setBagViewMode(mode) {
  state.currentBagViewMode = mode;
  const btnCards = document.getElementById('btnModeCards');
  const btnJson = document.getElementById('btnModeJson');
  if (btnCards) btnCards.classList.toggle('active', mode === 'cards');
  if (btnJson) btnJson.classList.toggle('active', mode === 'json');
  renderBagContent();
}

export function openEditBagModal(callbacks = {}) {
  if (!state.isAuthenticatedUser) {
    showToast('Please login as Admin to edit bags', 'warning');
    if (callbacks.onOpenLoginModal) callbacks.onOpenLoginModal();
    return;
  }

  setBagViewMode('json');
  const textarea = document.getElementById('bagJsonTextarea');
  if (textarea) {
    textarea.focus();
    textarea.scrollIntoView({ behavior: 'smooth' });
  }
}

export async function handleSaveRawJsonInline(callbacks = {}) {
  const textarea = document.getElementById('bagJsonTextarea');
  const saveBtn = document.getElementById('btnSaveRawJson');
  const errorMsg = document.getElementById('jsonInlineErrorMsg');

  if (!textarea) return;

  let rawJsonInput = textarea.value.trim();

  // If empty input, default to {} for worker status or [] for item bags
  if (!rawJsonInput) {
    if (state.currentSelectedBag && state.currentSelectedBag.startsWith('worker_')) {
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
    const res = await fetch(getApiUrl(`/api/dashboard/bags/${encodeURIComponent(state.currentSelectedBag)}`), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(parsedObject)
    });

    if (!res.ok) {
      const errData = await res.json().catch(() => ({}));
      throw new Error(errData.message || `HTTP Error ${res.status}`);
    }

    const data = await res.json();
    showToast(data.message || `Saved changes to ${state.currentSelectedBag}!`, 'success');

    // Reload active view to refresh cards & stats
    if (callbacks.onOverviewRefresh) callbacks.onOverviewRefresh();
    loadSelectedBag(state.currentSelectedBag, callbacks);
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

export function formatRawJson() {
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

export function resetRawJson() {
  const textarea = document.getElementById('bagJsonTextarea');
  const errorMsg = document.getElementById('jsonInlineErrorMsg');
  if (textarea) textarea.value = state.currentRawJson;
  if (errorMsg) errorMsg.style.display = 'none';
  showToast('Reset to original bag content', 'info');
}

// Open Bag in Explorer Tab
export function openBagInExplorer(bagFileName, callbacks = {}) {
  if (callbacks.onSwitchTab) callbacks.onSwitchTab('bags');
  loadSelectedBag(bagFileName, callbacks);
}

// Load Bag Contents
export async function loadSelectedBag(bagFileName, callbacks = {}) {
  state.currentSelectedBag = bagFileName;
  
  // Highlight active sidebar item and mobile ribbon pill
  document.querySelectorAll('.bag-item-btn, .mobile-bag-pill').forEach(btn => {
    const fn = btn.getAttribute('data-filename');
    const isTarget = fn ? fn === bagFileName : btn.innerText.includes(bagFileName);
    btn.classList.toggle('active', isTarget);
  });

  const titleEl = document.getElementById('selectedBagTitle');
  const redactedBadge = document.getElementById('redactedBadge');
  const editBtn = document.getElementById('btnEditBag');

  if (titleEl) {
    const stageInfo = getBagStageInfo(bagFileName);
    titleEl.innerHTML = `
      <div class="selected-stage-header">
        <i data-lucide="${stageInfo.icon}" style="color: ${stageInfo.color}; width: 16px; height: 16px;"></i>
        <span class="stage-title-text">${stageInfo.title}</span>
        <span class="code-filename-badge">${bagFileName}</span>
      </div>
    `;
    if (window.lucide) lucide.createIcons({ targets: [titleEl] });
  }

  try {
    const res = await fetch(getApiUrl(`/api/dashboard/bags/${encodeURIComponent(bagFileName)}`));
    
    // Handle 403 Forbidden (Guest trying to read sensitive config/env files)
    if (res.status === 403) {
      state.isCurrentBagRedacted = true;
      state.currentRawJson = JSON.stringify({ error: "Access Restricted", message: "This sensitive configuration file requires Admin login." }, null, 2);
      state.currentBagData = [];

      if (redactedBadge) redactedBadge.style.display = 'inline-flex';
      if (editBtn) editBtn.style.display = 'none';

      const countEl = document.getElementById('selectedBagItemCount');
      if (countEl) countEl.innerText = 'Restricted';

      renderRestrictedState(bagFileName, callbacks);
      return;
    }

    if (!res.ok) throw new Error(`HTTP ${res.status}`);

    const data = await res.json();
    state.currentRawJson = data.rawJson || '[]';
    state.currentBagData = data.parsed || [];
    state.isCurrentBagRedacted = data.isRedacted || false;

    // Redacted badge & edit button visibility
    if (redactedBadge) {
      redactedBadge.style.display = state.isCurrentBagRedacted ? 'inline-flex' : 'none';
    }

    if (editBtn) {
      editBtn.style.display = state.isAuthenticatedUser ? 'inline-flex' : 'none';
    }

    const textarea = document.getElementById('bagJsonTextarea');
    if (textarea) {
      textarea.value = state.currentRawJson;
      textarea.readOnly = !state.isAuthenticatedUser;
    }

    const countEl = document.getElementById('selectedBagItemCount');
    if (countEl) {
      const count = Array.isArray(state.currentBagData) ? state.currentBagData.length : (state.currentBagData ? 1 : 0);
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
export function renderRestrictedState(fileName, callbacks = {}) {
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
        <button class="btn btn-primary" id="restrictedStateLoginBtn">
          <i data-lucide="log-in" style="width: 14px; height: 14px;"></i> Login as Admin
        </button>
      </div>
    `;
    const loginBtn = document.getElementById('restrictedStateLoginBtn');
    if (loginBtn && callbacks.onOpenLoginModal) {
      loginBtn.onclick = callbacks.onOpenLoginModal;
    }
    if (window.lucide) lucide.createIcons({ targets: [cardsContainer] });
  }
}

// Render Top Visual Summary Banner for Bag Inspector
export function renderBagSummaryBanner() {
  const container = document.getElementById('inspectorSummaryBanner');
  if (!container) return;

  let items = Array.isArray(state.currentBagData) ? state.currentBagData : (state.currentBagData ? [state.currentBagData] : []);

  if (items.length === 0) {
    container.style.display = 'none';
    return;
  }

  container.style.display = 'flex';

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
        ${getTwitchIcon(14, 'color: #9146FF; margin-right: 4px;')}
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

  if (state.isCurrentBagRedacted) {
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
export function renderBagContent() {
  const searchTerm = (document.getElementById('bagSearchInput')?.value || '').toLowerCase();
  const cardsContainer = document.getElementById('cardsViewContainer');
  const jsonContainer = document.getElementById('jsonViewContainer');

  if (state.currentBagViewMode === 'json') {
    cardsContainer.style.display = 'none';
    jsonContainer.style.display = 'block';
    return;
  }

  cardsContainer.style.display = 'grid';
  jsonContainer.style.display = 'none';

  let itemsToRender = state.currentBagData;
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

// Smart Visual Card Renderer per domain type
export function renderVisualItemCard(item, idx) {
  if (!item || typeof item !== 'object') {
    return `<div class="item-card"><div class="item-value">${escapeHtml(String(item))}</div></div>`;
  }

  if (item.Link || (item.Start && item.End)) {
    return renderClipInfoCard(item, idx);
  }

  if (item.ShortId || (item.FilePath && item.DurationSeconds !== undefined)) {
    return renderShortItemCard(item, idx);
  }

  if (item.MontageId || item.Clips) {
    return renderMontageItemCard(item, idx);
  }

  const jsonStr = escapeJsString(JSON.stringify(item));
  return `
    <div class="item-card">
      <div class="item-card-header">
        <span class="item-badge primary">Record #${idx + 1}</span>
        <button class="icon-btn-small" onclick="window.copyToClipboard('${jsonStr}', 'Item JSON')" title="Copy Item JSON">
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
export function renderClipInfoCard(item, idx) {
  const desc = typeof item.Description === 'string' ? item.Description : (item.Description ? String(item.Description) : '');
  const streamer = desc || 'Unknown Streamer';
  const isFailed = desc.includes('FAILED');
  const isTemp = state.currentSelectedBag === 'bag_download_temp.json';

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
            ${getTwitchIcon(12, 'color: #9146FF; margin-right: 4px;')}
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
            <button class="btn btn-secondary btn-sm" onclick="window.copyToClipboard('${escapeHtml(linkStr)}', 'Clip Link')" title="Copy URL">
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

// Specialized Visual Card for YouTube Shorts (ShortItem)
export function renderShortItemCard(item, idx) {
  const title = item.Title || `Short #${idx + 1}`;
  const streamer = item.Streamer || 'Streamer';
  const kind = (item.KindKey || 'gaming').toUpperCase();
  const subKind = item.SubKindKey || '';
  const isUploaded = item.IsUploaded === true;
  const scheduledTime = item.ScheduledPublishTime ? formatDate(item.ScheduledPublishTime) : 'Pending';
  const safeShortId = escapeJsString(item.ShortId || '');

  return `
    <div class="item-card card-short" style="border-top: 3px solid ${isUploaded ? '#10b981' : '#a855f7'};">
      <div class="item-card-header">
        <div style="display: flex; align-items: center; gap: 0.5rem; flex-wrap: wrap;">
          <span class="streamer-badge">
            ${getTwitchIcon(12, 'color: #9146FF; margin-right: 4px;')}
            ${escapeHtml(streamer)}
          </span>
          <span class="tag-chip game">${escapeHtml(kind)}</span>
          ${subKind ? `<span class="tag-chip subkind">${escapeHtml(subKind)}</span>` : ''}
          <span class="tag-chip ${isUploaded ? 'success' : 'warning'}">
            <i data-lucide="${isUploaded ? 'check-circle' : 'clock'}" style="width: 12px; height: 12px;"></i>
            ${isUploaded ? 'Uploaded' : 'Scheduled'}
          </span>
        </div>
        <span class="item-badge secondary">#${idx + 1}</span>
      </div>

      <div class="item-card-body">
        <h4 style="font-size: 0.95rem; font-weight: 700; color: white; margin-bottom: 0.4rem; line-height: 1.4;">
          ${escapeHtml(title)}
        </h4>

        <div style="display: flex; flex-direction: column; gap: 0.35rem; font-size: 0.78rem; background: rgba(0,0,0,0.25); padding: 0.5rem; border-radius: 6px; margin-bottom: 0.5rem;">
          <div style="display: flex; justify-content: space-between;">
            <span style="color: var(--text-muted);"><i data-lucide="calendar" style="width: 12px; height: 12px; display: inline;"></i> Scheduled:</span>
            <span style="color: #c084fc; font-weight: 600; font-family: monospace;">${escapeHtml(scheduledTime)}</span>
          </div>
          ${item.DurationSeconds ? `
            <div style="display: flex; justify-content: space-between;">
              <span style="color: var(--text-muted);"><i data-lucide="clock" style="width: 12px; height: 12px; display: inline;"></i> Duration:</span>
              <span style="color: #38bdf8; font-weight: 600;">${item.DurationSeconds.toFixed(1)}s</span>
            </div>
          ` : ''}
        </div>

        <div class="clip-action-bar" style="display: flex; gap: 0.5rem; flex-wrap: wrap;">
          ${item.YoutubeVideoId ? `
            <a href="https://youtube.com/shorts/${escapeHtml(item.YoutubeVideoId)}" target="_blank" rel="noopener" class="btn btn-primary btn-sm" style="flex: 1; justify-content: center; text-decoration: none; background: #FF0000; border-color: #FF0000;">
              ${getYoutubeIcon(14, 'margin-right: 4px;')} View Short
            </a>
          ` : ''}

          ${(!isUploaded && state.isAuthenticatedUser && safeShortId) ? `
            <button type="button" class="btn btn-sm" style="background: linear-gradient(135deg, #10b981, #059669); color: white; font-weight: 700; border: none; display: inline-flex; align-items: center; gap: 4px; padding: 0.35rem 0.75rem; border-radius: 6px; box-shadow: 0 2px 8px rgba(16,185,129,0.3); cursor: pointer;" onclick="window.uploadShortImmediately('${safeShortId}')" title="Upload this short to YouTube immediately">
              <i data-lucide="zap" style="width: 13px; height: 13px;"></i> Upload Now
            </button>
          ` : ''}
        </div>
      </div>
    </div>
  `;
}

// Specialized Visual Card for Montage Packages
export function renderMontageItemCard(item, idx) {
  const title = item.GeneratedTitle || item.MontageId || `Montage #${idx + 1}`;
  const clipsCount = Array.isArray(item.Clips) ? item.Clips.length : 0;
  const isDone = item.Completed === true;
  const safeMontageId = escapeJsString(item.MontageId || '');

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

        <div class="clip-action-bar" style="margin-top: 0.5rem; display: flex; gap: 0.5rem;">
          ${item.YoutubeUrl ? `
            <a href="${escapeHtml(item.YoutubeUrl)}" target="_blank" rel="noopener" class="btn btn-primary btn-sm" style="flex: 1; justify-content: center; text-decoration: none; background: #FF0000; border-color: #FF0000;">
              ${getYoutubeIcon(14, 'margin-right: 4px;')} Open YouTube
            </a>
          ` : ''}

          ${(!isDone && state.isAuthenticatedUser && safeMontageId) ? `
            <button type="button" class="btn btn-sm" style="background: linear-gradient(135deg, #10b981, #059669); color: white; font-weight: 700; border: none; display: inline-flex; align-items: center; gap: 4px; padding: 0.35rem 0.75rem; border-radius: 6px; box-shadow: 0 2px 8px rgba(16,185,129,0.3); cursor: pointer;" onclick="window.uploadMontageImmediately('${safeMontageId}')" title="Upload this montage to YouTube immediately">
              <i data-lucide="zap" style="width: 13px; height: 13px;"></i> Upload Now
            </button>
          ` : ''}
        </div>
      </div>
    </div>
  `;
}

// Render key-value pairs for non-specialized cards
export function renderItemFields(obj) {
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

// Human Readable Stage Information for System Data Bags
export function getBagStageInfo(name) {
  if (name.includes('download_temp')) return { title: 'Staging Download', shortTitle: 'Staging', emoji: '📥', icon: 'download-cloud', color: 'var(--accent-cyan)' };
  if (name.includes('download')) return { title: 'Download Queue', shortTitle: 'Download', emoji: '📥', icon: 'download', color: 'var(--accent-cyan)' };
  if (name.includes('trimmed')) return { title: 'Trimmed Clips', shortTitle: 'Trimmed', emoji: '✂️', icon: 'scissors', color: 'var(--primary)' };
  if (name.includes('montage')) return { title: 'Montage Packages', shortTitle: 'Montage', emoji: '🎬', icon: 'film', color: 'var(--accent-amber)' };
  if (name.includes('youtube_done')) return { title: 'Published Videos', shortTitle: 'Published', emoji: '✅', icon: 'check-circle-2', color: 'var(--accent-emerald)' };
  if (name.includes('youtube')) return { title: 'Ready for Upload', shortTitle: 'Upload', emoji: '🚀', icon: 'youtube', color: '#a855f7' };
  if (name.includes('failed')) return { title: 'Failed Items', shortTitle: 'Failed', emoji: '⚠️', icon: 'alert-triangle', color: 'var(--accent-rose)' };
  if (name.includes('history')) return { title: 'System History', shortTitle: 'History', emoji: '📜', icon: 'history', color: 'var(--text-muted)' };
  if (name.includes('worker')) return { title: 'Worker State', shortTitle: 'Worker', emoji: '⚙️', icon: 'cpu', color: 'var(--accent-amber)' };
  return { title: name, shortTitle: name, emoji: '📄', icon: 'file-json', color: 'var(--text-muted)' };
}

// Render Bags Overview Grid with VPS-styled Telemetry Cards
export function renderBagsOverviewGrid(bags) {
  const container = document.getElementById('bagsOverviewGrid');
  if (!container || !bags) return;

  container.innerHTML = bags.map(bag => {
    const info = getBagStageInfo(bag.fileName);
    const isFailed = bag.fileName.includes('failed') && bag.itemCount > 0;
    const isSensitive = bag.isSensitive;

    return `
      <div class="card bag-overview-card ${isFailed ? 'card-failed' : ''}">
        <div class="card-header" style="margin-bottom: 0.75rem;">
          <div class="card-title" style="font-size: 0.95rem;">
            <div style="width: 32px; height: 32px; border-radius: 6px; background: rgba(30, 41, 59, 0.8); display: flex; align-items: center; justify-content: center; flex-shrink: 0; border: 1px solid var(--border-color);">
              <i data-lucide="${info.icon}" style="color: ${info.color}; width: 16px; height: 16px;"></i>
            </div>
            <div style="display: flex; flex-direction: column; gap: 0.1rem;">
              <span>${info.title}</span>
              <span style="font-family: var(--font-mono); font-size: 0.7rem; color: var(--text-muted); font-weight: 400;">${bag.fileName}</span>
            </div>
          </div>
          <span class="card-count ${isFailed ? 'count-failed-badge' : ''}" style="font-family: var(--font-mono); font-weight: 700; font-size: 0.85rem; padding: 0.2rem 0.65rem;">
            ${bag.itemCount} item(s)
          </span>
        </div>

        <div class="bag-card-meta-grid" style="background: rgba(15, 23, 42, 0.6); border: 1px solid var(--border-color); border-radius: 6px; padding: 0.6rem 0.75rem; display: flex; flex-direction: column; gap: 0.35rem; margin-bottom: 1rem; font-size: 0.78rem;">
          <div style="display: flex; justify-content: space-between; align-items: center;">
            <span style="color: var(--text-muted);">Storage Size</span>
            <strong style="font-family: var(--font-mono); color: var(--text-main);">${formatFileSize(bag.sizeBytes)}</strong>
          </div>
          <div style="display: flex; justify-content: space-between; align-items: center;">
            <span style="color: var(--text-muted);">Last Modified</span>
            <strong style="font-family: var(--font-mono); color: var(--text-main);">${formatDate(bag.lastModified)}</strong>
          </div>
          ${isSensitive ? `
            <div style="display: flex; justify-content: space-between; align-items: center; border-top: 1px solid rgba(255, 255, 255, 0.05); padding-top: 0.3rem; margin-top: 0.1rem;">
              <span style="color: #fca5a5; font-weight: 600;">Access</span>
              <span class="tag-chip warning" style="font-size: 0.65rem;"><i data-lucide="lock" style="width: 10px; height: 10px;"></i> Restricted</span>
            </div>
          ` : ''}
        </div>

        <div style="margin-top: auto; display: flex; justify-content: space-between; align-items: center;">
          <span class="tag-chip subkind" style="font-size: 0.68rem; text-transform: uppercase;">${bag.type || 'JSON'}</span>
          <button class="btn btn-secondary btn-sm" onclick="window.openBagInExplorer('${bag.fileName}')">
            <i data-lucide="eye" style="width: 13px; height: 13px;"></i> Inspect Bag
          </button>
        </div>
      </div>
    `;
  }).join('');

  if (window.lucide) lucide.createIcons({ targets: [container] });
}

// Render Bags Sidebar List and Mobile Ribbon
export function renderBagsSidebarList(bags) {
  const sidebarContainer = document.getElementById('bagsSidebarList');
  const ribbonContainer = document.getElementById('mobileBagsRibbon');

  if (!bags) return;

  if (sidebarContainer) {
    sidebarContainer.innerHTML = bags.map(bag => {
      const info = getBagStageInfo(bag.fileName);
      const isFailed = bag.fileName.includes('failed') && bag.itemCount > 0;
      return `
        <button class="bag-item-btn ${bag.fileName === state.currentSelectedBag ? 'active' : ''} ${isFailed ? 'has-failures' : ''}" data-filename="${bag.fileName}" onclick="window.loadSelectedBag('${bag.fileName}')">
          <span class="bag-item-name">
            <i data-lucide="${info.icon}" style="width: 16px; height: 16px; color: ${info.color};"></i>
            <span style="font-weight: 600;">${info.title}</span>
          </span>
          <span class="bag-count-pill ${isFailed ? 'pill-danger' : ''}">${bag.itemCount}</span>
        </button>
      `;
    }).join('');
    if (window.lucide) lucide.createIcons({ targets: [sidebarContainer] });
  }

  if (ribbonContainer) {
    ribbonContainer.innerHTML = bags.map(bag => {
      const info = getBagStageInfo(bag.fileName);
      const isFailed = bag.fileName.includes('failed') && bag.itemCount > 0;
      return `
        <button class="mobile-bag-pill ${bag.fileName === state.currentSelectedBag ? 'active' : ''} ${isFailed ? 'pill-failed-alert' : ''}" data-filename="${bag.fileName}" onclick="window.loadSelectedBag('${bag.fileName}')">
          <i data-lucide="${info.icon}" style="width: 13px; height: 13px; color: ${info.color};"></i>
          <span>${info.shortTitle}</span>
          <span class="pill-count ${isFailed ? 'count-danger' : ''}">${bag.itemCount}</span>
        </button>
      `;
    }).join('');
    if (window.lucide) lucide.createIcons({ targets: [ribbonContainer] });
  }
}

// Immediate Upload API Triggers
export async function uploadShortImmediately(shortId) {
  if (!state.isAuthenticatedUser) {
    showToast('Please login as Admin to force uploads', 'warning');
    return;
  }
  if (!shortId) return;

  const confirmed = confirm(`Are you sure you want to upload Short '${shortId}' to YouTube immediately?`);
  if (!confirmed) return;

  showToast(`⚡ Uploading short '${shortId}' now...`, 'info');

  try {
    const res = await fetch(getApiUrl(`/api/dashboard/shorts/${encodeURIComponent(shortId)}/upload-now`), {
      method: 'POST'
    });
    const data = await res.json().catch(() => ({}));

    if (res.ok && data.success) {
      showToast(data.message || `Short '${shortId}' uploaded successfully!`, 'success');
      if (state.currentSelectedBag) {
        await loadSelectedBag(state.currentSelectedBag);
      }
    } else {
      showToast(data.message || `Failed to upload short '${shortId}'`, 'error');
    }
  } catch (err) {
    console.error('Error triggering immediate short upload:', err);
    showToast(`Network error: ${err.message}`, 'error');
  }
}

export async function uploadMontageImmediately(montageId) {
  if (!state.isAuthenticatedUser) {
    showToast('Please login as Admin to force uploads', 'warning');
    return;
  }
  if (!montageId) return;

  const confirmed = confirm(`Are you sure you want to trigger immediate upload for Montage '${montageId}'?`);
  if (!confirmed) return;

  showToast(`⚡ Triggering immediate upload for montage '${montageId}'...`, 'info');

  try {
    const res = await fetch(getApiUrl(`/api/dashboard/montages/${encodeURIComponent(montageId)}/upload-now`), {
      method: 'POST'
    });
    const data = await res.json().catch(() => ({}));

    if (res.ok && data.success) {
      showToast(data.message || `Montage '${montageId}' immediate upload initiated!`, 'success');
      if (state.currentSelectedBag) {
        await loadSelectedBag(state.currentSelectedBag);
      }
    } else {
      showToast(data.message || `Failed to trigger montage upload: ${montageId}`, 'error');
    }
  } catch (err) {
    console.error('Error triggering immediate montage upload:', err);
    showToast(`Network error: ${err.message}`, 'error');
  }
}

export async function triggerImmediateUploadAll() {
  if (!state.isAuthenticatedUser) {
    showToast('Please login as Admin to force uploads', 'warning');
    return;
  }

  showToast('⚡ Processing next scheduled short and triggering YouTube upload...', 'info');

  try {
    const res = await fetch(getApiUrl('/api/dashboard/trigger-immediate-upload'), {
      method: 'POST'
    });
    const data = await res.json().catch(() => ({}));

    if (res.ok && data.success) {
      showToast(data.message || 'Immediate upload cycle triggered successfully!', 'success');
      if (state.currentSelectedBag) {
        await loadSelectedBag(state.currentSelectedBag);
      }
    } else {
      showToast(data.message || 'Failed to trigger upload cycle', 'error');
    }
  } catch (err) {
    console.error('Error triggering immediate upload all:', err);
    showToast(`Network error: ${err.message}`, 'error');
  }
}

// Bind to window for direct HTML onclick execution
window.uploadShortImmediately = uploadShortImmediately;
window.uploadMontageImmediately = uploadMontageImmediately;
window.triggerImmediateUploadAll = triggerImmediateUploadAll;

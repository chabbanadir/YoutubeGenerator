// Game Kinds Management Module
import { state, showToast, escapeHtml, escapeJsString, formatDate, fetchWithAuth, getApiUrl, getYoutubeIcon } from '../core/api.js';

export function renderKindsGrid(kinds, callbacks = {}) {
  const container = document.getElementById('kindsGrid');
  if (!container) return;

  if (!kinds || kinds.length === 0) {
    container.innerHTML = '<div class="text-muted" style="grid-column: 1/-1; padding: 1.5rem; text-align: center; background: rgba(15,23,42,0.4); border-radius: 8px;">No Game Kind profiles found in <code>workspace/profiles/kinds</code>.</div>';
    return;
  }

  container.innerHTML = kinds.map(k => {
    const data = k.data || {};
    const uploadCfg = data.UploadConfig || {};
    const isEnabled = uploadCfg.Enabled === true;
    const kindKey = (k.name || '').toLowerCase();
    const kindTitle = k.name ? k.name.toUpperCase() : 'UNKNOWN';
    const escapedFileName = escapeHtml(k.fileName);
    const safeFileName = escapeJsString(k.fileName);

    const badgeThemeClass = kindKey === 'lol' ? 'lol' : (kindKey === 'valorant' ? 'valorant' : 'default');
    const topAccentColor = isEnabled ? '#10b981' : '#64748b';

    const formattedLastUpload = uploadCfg.LastUploadTime 
      ? formatDate(uploadCfg.LastUploadTime) 
      : 'Never';

    const hasRefreshToken = !!(data.YoutubeRefreshToken && data.YoutubeRefreshToken.trim().length > 0);
    const channelTitle = data.YoutubeChannelTitle || '';
    const channelId = data.YoutubeChannelId || '';
    const safeKindKey = escapeJsString(kindKey);

    return `
      <div class="profile-card" data-kind-file="${escapedFileName}" style="border-top: 3px solid ${topAccentColor};">
        <div class="profile-card-header" style="display: flex; justify-content: space-between; align-items: center;">
          <div class="profile-title-group">
            <span class="profile-kind-badge ${badgeThemeClass}">
              ${escapeHtml(kindTitle)}
            </span>
            <span class="profile-filename" title="${escapedFileName}">${escapedFileName}</span>
          </div>
          <div style="display: flex; align-items: center; gap: 0.5rem;">
            ${state.isAuthenticatedUser ? `
              <button type="button" class="save-kind-btn" id="save-btn-${escapedFileName}" style="display: none;" onclick="window.saveKindCard('${safeFileName}')">
                <i data-lucide="save" style="width: 14px; height: 14px;"></i> Save
              </button>
              <button type="button" class="btn-delete-profile" onclick="window.promptDeleteProfile('kind', '${safeFileName}')" title="Delete Kind Profile">
                <i data-lucide="trash-2" style="width: 14px; height: 14px;"></i> Delete
              </button>
            ` : ''}
          </div>
        </div>

        <div class="profile-section">
          <div class="profile-section-title" style="display: flex; justify-content: space-between; align-items: center;">
            <span>${getYoutubeIcon(14, 'color: #ef4444; margin-right: 4px;')} YouTube Channel Integration</span>
            ${hasRefreshToken 
              ? `<span class="badge" style="background: rgba(16, 185, 129, 0.15); color: #34d399; border: 1px solid rgba(16, 185, 129, 0.3); font-size: 0.72rem; font-weight: 600; padding: 0.15rem 0.5rem;"><i data-lucide="check-circle" style="width: 11px; height: 11px; display: inline;"></i> Connected</span>`
              : `<span class="badge" style="background: rgba(239, 68, 68, 0.15); color: #f87171; border: 1px solid rgba(239, 68, 68, 0.3); font-size: 0.72rem; font-weight: 600; padding: 0.15rem 0.5rem;"><i data-lucide="alert-triangle" style="width: 11px; height: 11px; display: inline;"></i> Not Connected</span>`}
          </div>

          ${channelTitle ? `
            <div style="font-size: 0.85rem; font-weight: 700; color: #f8fafc; margin-top: 0.25rem; display: flex; align-items: center; gap: 0.4rem;">
              <i data-lucide="tv" style="width: 13px; height: 13px; color: #ff4444;"></i> ${escapeHtml(channelTitle)}
            </div>
          ` : ''}

          <div class="profile-channel-bar" style="gap: 0.5rem; margin-top: 0.35rem;">
            ${state.isAuthenticatedUser ? `
              <input type="text"
                     class="form-control channel-id-input"
                     id="channel-input-${escapedFileName}"
                     value="${escapeHtml(channelId)}"
                     placeholder="Channel ID (e.g. UCz...)"
                     oninput="window.onKindCardModified('${safeFileName}')"
                     style="background: rgba(0, 0, 0, 0.4); border: 1px solid rgba(96, 165, 250, 0.3); color: #60a5fa; font-family: monospace; font-size: 0.82rem; font-weight: 600; padding: 0.3rem 0.55rem; border-radius: 6px; flex: 1; outline: none;" />
              <div style="display: flex; gap: 0.3rem;">
                ${channelId ? `
                  <a class="btn btn-secondary" style="padding: 0.25rem 0.45rem; font-size: 0.72rem; text-decoration: none;" href="https://www.youtube.com/channel/${escapeHtml(channelId)}" target="_blank" title="Open YouTube Channel">
                    <i data-lucide="external-link" style="width: 12px; height: 12px;"></i>
                  </a>
                ` : ''}
                <button type="button" class="btn btn-secondary" style="padding: 0.25rem 0.45rem; font-size: 0.72rem;" onclick="window.copyToClipboard(document.getElementById('channel-input-${escapedFileName}').value, 'Channel ID')" title="Copy Channel ID">
                  <i data-lucide="copy" style="width: 12px; height: 12px;"></i>
                </button>
              </div>
            ` : `
              <code class="profile-channel-id" style="flex: 1;">${escapeHtml(channelId || 'Not Configured')}</code>
              <div style="display: flex; gap: 0.3rem;">
                ${channelId ? `
                  <a class="btn btn-secondary" style="padding: 0.2rem 0.45rem; font-size: 0.72rem; text-decoration: none;" href="https://www.youtube.com/channel/${escapeHtml(channelId)}" target="_blank" title="Open YouTube Channel">
                    <i data-lucide="external-link" style="width: 12px; height: 12px;"></i>
                  </a>
                ` : ''}
                <button type="button" class="btn btn-secondary" style="padding: 0.2rem 0.45rem; font-size: 0.72rem;" onclick="window.copyToClipboard('${escapeJsString(channelId)}', 'Channel ID')" title="Copy Channel ID">
                  <i data-lucide="copy" style="width: 12px; height: 12px;"></i>
                </button>
              </div>
            `}
          </div>
        </div>

        <div class="profile-section">
          <div class="profile-section-title">
            <i data-lucide="zap" style="width: 14px; height: 14px; color: #f59e0b;"></i> Upload Automation Rules
          </div>
          <div class="profile-stats-grid">
            <div class="profile-stat-box" style="position: relative; padding: 0.4rem 0.5rem; display: flex; flex-direction: column; align-items: center;">
              <span class="profile-stat-label">Min Delay</span>
              <div style="display: flex; align-items: center; justify-content: center; gap: 0.35rem; margin-top: 0.15rem;">
                ${state.isAuthenticatedUser ? `
                  <button type="button" class="btn-step-counter" onclick="window.stepKindMinDelay('${safeFileName}', -1)" title="Decrease delay by 1 hour">
                    -
                  </button>
                ` : ''}
                <div style="display: flex; align-items: baseline; gap: 2px;">
                  <span class="profile-stat-value min-delay-value" id="min-delay-val-${escapedFileName}" style="color: #60a5fa; font-weight: 700;">${uploadCfg.MinDelayHours ?? 8}</span>
                  <span style="font-size: 0.75rem; color: #60a5fa; font-weight: 600;">hrs</span>
                </div>
                ${state.isAuthenticatedUser ? `
                  <button type="button" class="btn-step-counter" onclick="window.stepKindMinDelay('${safeFileName}', 1)" title="Increase delay by 1 hour">
                    +
                  </button>
                ` : ''}
              </div>
            </div>
            <div class="profile-stat-box">
              <span class="profile-stat-label">Counter</span>
              <span class="profile-stat-value" style="color: #34d399;">${uploadCfg.UploadCounter ?? 0}</span>
            </div>
            <div class="profile-stat-box">
              <span class="profile-stat-label">Status</span>
              <span class="profile-stat-value" style="color: ${isEnabled ? '#34d399' : '#94a3b8'}; font-size: 0.75rem;">${isEnabled ? 'ACTIVE' : 'PAUSED'}</span>
            </div>
          </div>
          <div style="font-size: 0.74rem; color: var(--text-muted); display: flex; align-items: center; justify-content: space-between; margin-top: 0.15rem; padding: 0 0.2rem;">
            <span><i data-lucide="clock" style="width: 11px; height: 11px; display: inline;"></i> Last Upload:</span>
            <span style="font-family: monospace; color: var(--text-main); font-weight: 600;">${escapeHtml(formattedLastUpload)}</span>
          </div>
        </div>

        <div class="profile-section" style="display: flex; align-items: center; justify-content: space-between; gap: 0.5rem; background: rgba(0,0,0,0.2); padding: 0.5rem 0.75rem;">
          ${hasRefreshToken ? `
            <div style="font-size: 0.78rem; color: #34d399; font-weight: 600; display: flex; align-items: center; gap: 0.35rem;">
              <i data-lucide="shield-check" style="width: 14px; height: 14px;"></i> YouTube Linked & Ready
            </div>
            <button type="button" class="btn-youtube-disconnect" onclick="window.disconnectYoutubeChannel('${safeKindKey}')" title="Disconnect YouTube channel">
              <i data-lucide="log-out" style="width: 13px; height: 13px;"></i> Logout
            </button>
          ` : `
            <div style="font-size: 0.78rem; color: var(--text-muted); display: flex; align-items: center; gap: 0.35rem;">
              <i data-lucide="alert-circle" style="width: 14px; height: 14px; color: #f59e0b;"></i> Unlinked Channel
            </div>
            <button type="button" class="btn-youtube-connect" style="background: #ef4444; color: white; border: none; font-weight: 700; display: flex; align-items: center; justify-content: center; gap: 0.4rem; padding: 0.5rem 0.85rem; border-radius: 6px; box-shadow: 0 4px 14px rgba(239, 68, 68, 0.4); text-transform: uppercase; font-size: 0.8rem; letter-spacing: 0.02em;" onclick="window.connectYoutubeChannel('${safeKindKey}')" title="Link YouTube channel via Google OAuth">
              ${getYoutubeIcon(16, 'margin-right: 4px;')} CONNECT YOUTUBE
            </button>
          `}
        </div>
      </div>
    `;
  }).join('');

  if (window.lucide) lucide.createIcons();
}

export function stepKindMinDelay(fileName, delta) {
  const safeFileName = escapeJsString(fileName);
  const valSpan = document.getElementById(`min-delay-val-${safeFileName}`);
  if (!valSpan) return;
  
  let currentVal = parseInt(valSpan.textContent, 10) || 8;
  let newVal = Math.max(1, currentVal + delta);
  valSpan.textContent = newVal;
  
  onKindCardModified(fileName);
}

export function onKindCardModified(fileName) {
  const safeFileName = escapeJsString(fileName);
  const saveBtn = document.getElementById(`save-btn-${safeFileName}`);
  if (saveBtn) {
    saveBtn.style.display = 'inline-flex';
    if (window.lucide) lucide.createIcons({ targets: [saveBtn] });
  }
  const card = document.querySelector(`[data-kind-file="${safeFileName}"]`);
  if (card) {
    card.classList.add('card-modified');
  }
}

export async function saveKindCard(fileName) {
  const safeFileName = escapeJsString(fileName);
  const saveBtn = document.getElementById(`save-btn-${safeFileName}`);
  const channelInput = document.getElementById(`channel-input-${safeFileName}`);
  const minDelaySpan = document.getElementById(`min-delay-val-${safeFileName}`);

  if (!saveBtn) return;

  const originalHtml = saveBtn.innerHTML;
  saveBtn.disabled = true;
  saveBtn.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true" style="width: 12px; height: 12px; margin-right: 4px;"></span> Saving...';

  const channelId = channelInput ? channelInput.value.trim() : '';
  const minDelayHours = minDelaySpan ? (parseInt(minDelaySpan.textContent, 10) || 8) : 8;

  try {
    const res = await fetch(getApiUrl(`/api/dashboard/profiles/kinds/${encodeURIComponent(fileName)}`), {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        YoutubeChannelId: channelId,
        UploadConfig: {
          MinDelayHours: minDelayHours
        }
      })
    });

    if (res.ok) {
      showToast(`Saved changes for ${fileName}`, 'success');
      saveBtn.style.display = 'none';
      const card = document.querySelector(`[data-kind-file="${safeFileName}"]`);
      if (card) {
        card.classList.remove('card-modified');
      }
    } else {
      const errData = await res.json().catch(() => ({}));
      showToast(errData.message || `Failed to save changes for ${fileName}`, 'error');
    }
  } catch (err) {
    console.error('Error saving kind profile card:', err);
    showToast(`Failed to save changes for ${fileName}`, 'error');
  } finally {
    saveBtn.disabled = false;
    saveBtn.innerHTML = originalHtml;
    if (window.lucide) lucide.createIcons({ targets: [saveBtn] });
  }
}

export function openCreateKindModal(callbacks = {}) {
  if (!state.isAuthenticatedUser) {
    showToast('You must be logged in to create a Kind profile.', 'warning');
    if (callbacks.onOpenLoginModal) callbacks.onOpenLoginModal();
    return;
  }
  document.getElementById('createKindForm').reset();
  document.getElementById('createKindErrorMsg').style.display = 'none';
  document.getElementById('createKindModal').classList.add('active');
}

export function closeCreateKindModal() {
  document.getElementById('createKindModal').classList.remove('active');
}

export async function handleCreateKind(e, callbacks = {}) {
  e.preventDefault();
  const keyInput = document.getElementById('newKindKey').value.trim().toLowerCase();
  if (!keyInput) return;

  const errEl = document.getElementById('createKindErrorMsg');
  const submitBtn = document.getElementById('createKindSubmitBtn');

  const channelId = document.getElementById('newKindChannelId').value.trim();
  const minDelayHours = parseInt(document.getElementById('newKindMinDelayHours').value, 10) || 8;

  const payload = {
    KindKey: keyInput,
    YoutubeChannelId: channelId,
    UploadConfig: {
      MinDelayHours: minDelayHours,
      UploadCounter: 0
    }
  };

  try {
    submitBtn.disabled = true;
    submitBtn.innerText = 'Creating...';
    errEl.style.display = 'none';

    const res = await fetchWithAuth('/api/dashboard/profiles/kinds', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });

    const data = await res.json();
    if (!res.ok) {
      throw new Error(data.message || 'Failed to create Kind profile.');
    }

    showToast(`Kind profile '${keyInput}' created successfully!`, 'success');
    closeCreateKindModal();
    if (callbacks.onFetchProfiles) await callbacks.onFetchProfiles();
  } catch (err) {
    errEl.innerText = err.message;
    errEl.style.display = 'block';
  } finally {
    submitBtn.disabled = false;
    submitBtn.innerText = 'Create Kind Profile';
  }
}

export async function handleCreateAndConnectKind(e, callbacks = {}) {
  if (e) e.preventDefault();
  const keyInput = document.getElementById('newKindKey').value.trim().toLowerCase();
  const errEl = document.getElementById('createKindErrorMsg');
  if (!keyInput) {
    if (errEl) {
      errEl.innerText = 'Please enter a valid Kind Key / Game Identifier.';
      errEl.style.display = 'block';
    }
    return;
  }

  const createAndConnectBtn = document.getElementById('createAndConnectBtn');
  const channelId = document.getElementById('newKindChannelId').value.trim();
  const minDelayHours = parseInt(document.getElementById('newKindMinDelayHours').value, 10) || 8;

  const payload = {
    KindKey: keyInput,
    YoutubeChannelId: channelId,
    UploadConfig: {
      MinDelayHours: minDelayHours,
      UploadCounter: 0
    }
  };

  try {
    if (createAndConnectBtn) {
      createAndConnectBtn.disabled = true;
      createAndConnectBtn.innerHTML = '<i data-lucide="loader-2" class="spin" style="width: 14px; height: 14px;"></i> Creating...';
    }
    if (errEl) errEl.style.display = 'none';

    const res = await fetchWithAuth('/api/dashboard/profiles/kinds', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });

    const data = await res.json();
    if (!res.ok) {
      throw new Error(data.message || 'Failed to create Kind profile.');
    }

    showToast(`Kind profile '${keyInput}' created! Launching YouTube connection...`, 'info');
    closeCreateKindModal();
    await connectYoutubeChannel(keyInput);
  } catch (err) {
    if (errEl) {
      errEl.innerText = err.message;
      errEl.style.display = 'block';
    }
    if (createAndConnectBtn) {
      createAndConnectBtn.disabled = false;
      createAndConnectBtn.innerHTML = `${getYoutubeIcon(14, 'margin-right: 4px;')} Create & Connect`;
    }
  }
}

export async function connectYoutubeChannel(kindKey) {
  try {
    const res = await fetch(getApiUrl(`/api/youtube/auth-url?kind=${encodeURIComponent(kindKey)}`));
    const data = await res.json();
    if (!res.ok) {
      showToast(data.error || 'Failed to get YouTube OAuth authorization URL. Check server configuration.', 'error');
      return;
    }
    if (data.url) {
      window.location.href = data.url;
    } else {
      showToast('No authorization URL returned.', 'error');
    }
  } catch (err) {
    console.error('Failed to initiate YouTube connection:', err);
    showToast('Failed to connect to YouTube authorization service.', 'error');
  }
}

export async function disconnectYoutubeChannel(kindKey, callbacks = {}) {
  if (!state.isAuthenticatedUser) {
    showToast('You must be logged in to disconnect YouTube channel.', 'warning');
    if (callbacks.onOpenLoginModal) callbacks.onOpenLoginModal();
    return;
  }
  if (!confirm(`Are you sure you want to disconnect YouTube channel for '${kindKey.toUpperCase()}'? This will remove the stored Refresh Token.`)) {
    return;
  }

  try {
    const res = await fetchWithAuth(`/api/youtube/disconnect/${encodeURIComponent(kindKey)}`, {
      method: 'POST'
    });
    const data = await res.json();
    if (res.ok && data.success) {
      showToast(data.message || `Disconnected YouTube channel for '${kindKey}'.`, 'success');
      if (callbacks.onFetchProfiles) await callbacks.onFetchProfiles();
    } else {
      showToast(data.error || data.message || 'Failed to disconnect YouTube channel.', 'error');
    }
  } catch (err) {
    console.error('Failed to disconnect YouTube channel:', err);
    showToast('Error disconnecting YouTube channel.', 'error');
  }
}

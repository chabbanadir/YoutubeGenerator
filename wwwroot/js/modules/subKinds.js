// SubKinds Profile Management Module
import { state, showToast, escapeHtml, escapeJsString, formatDate, fetchWithAuth } from '../core/api.js';

export function renderSubKindsGrid(subKinds, callbacks = {}) {
  const container = document.getElementById('subKindsGrid');
  if (!container) return;

  if (!subKinds || subKinds.length === 0) {
    container.innerHTML = '<div class="text-muted" style="grid-column: 1/-1; padding: 1.5rem; text-align: center; background: rgba(15,23,42,0.4); border-radius: 8px;">No SubKind profiles found in <code>workspace/profiles/subkinds</code>.</div>';
    return;
  }

  container.innerHTML = subKinds.map(sk => {
    const data = sk.data || {};
    const uploadCfg = data.UploadConfig || {};
    const isEnabled = uploadCfg.Enabled === true;
    const subKindKey = (sk.name || '').toLowerCase();
    const parentKind = (data.KindKey || 'generic').toUpperCase();
    const escapedFileName = escapeHtml(sk.fileName);
    const safeFileName = escapeJsString(sk.fileName);

    const badgeThemeClass = parentKind.toLowerCase() === 'lol' ? 'lol' : (parentKind.toLowerCase() === 'valorant' ? 'valorant' : 'default');
    const topAccentColor = isEnabled ? '#3b82f6' : '#64748b';

    const formattedLastUpload = uploadCfg.LastUploadTime 
      ? formatDate(uploadCfg.LastUploadTime) 
      : 'Never';

    return `
      <div class="profile-card" data-subkind-file="${escapedFileName}" style="border-top: 3px solid ${topAccentColor};">
        <div class="profile-card-header" style="display: flex; justify-content: space-between; align-items: center;">
          <div class="profile-title-group">
            <span class="profile-kind-badge ${badgeThemeClass}">
              ${escapeHtml(parentKind)}
            </span>
            <span class="profile-filename" title="${escapedFileName}">${escapedFileName}</span>
          </div>
          <div style="display: flex; align-items: center; gap: 0.5rem;">
            ${state.isAuthenticatedUser ? `
              <button type="button" class="save-kind-btn" id="save-sk-btn-${escapedFileName}" style="display: none;" onclick="window.saveSubKindCard('${safeFileName}')">
                <i data-lucide="save" style="width: 14px; height: 14px;"></i> Save
              </button>
              <button type="button" class="btn-delete-profile" onclick="window.promptDeleteProfile('subkind', '${safeFileName}')" title="Delete SubKind Profile">
                <i data-lucide="trash-2" style="width: 14px; height: 14px;"></i> Delete
              </button>
            ` : ''}
          </div>
        </div>

        <div class="profile-section">
          <div class="profile-section-title">
            <i data-lucide="tag" style="width: 14px; height: 14px; color: #a855f7;"></i> SubKind Metadata & Identity
          </div>
          <div style="display: flex; flex-direction: column; gap: 0.4rem; background: rgba(0, 0, 0, 0.25); padding: 0.5rem; border-radius: 6px; border: 1px solid rgba(255,255,255,0.05);">
            <div style="display: flex; justify-content: space-between; font-size: 0.8rem;">
              <span style="color: var(--text-muted);">SubKind Key:</span>
              <span style="font-family: monospace; font-weight: 700; color: #a855f7;">${escapeHtml(subKindKey)}</span>
            </div>
            <div style="display: flex; justify-content: space-between; font-size: 0.8rem;">
              <span style="color: var(--text-muted);">Target Channel ID:</span>
              <span style="font-family: monospace; color: #60a5fa;">${escapeHtml(data.TargetYoutubeChannelId || 'Inherit Kind default')}</span>
            </div>
          </div>
        </div>

        <div class="profile-section">
          <div class="profile-section-title">
            <i data-lucide="zap" style="width: 14px; height: 14px; color: #f59e0b;"></i> SubKind Automation Rules
          </div>
          <div class="profile-stats-grid">
            <div class="profile-stat-box" style="position: relative; padding: 0.4rem 0.5rem; display: flex; flex-direction: column; align-items: center;">
              <span class="profile-stat-label">Min Delay</span>
              <div style="display: flex; align-items: center; justify-content: center; gap: 0.35rem; margin-top: 0.15rem;">
                ${state.isAuthenticatedUser ? `
                  <button type="button" class="btn-step-counter" onclick="window.stepSubKindMinDelay('${safeFileName}', -1)" title="Decrease delay by 1 hour">
                    -
                  </button>
                ` : ''}
                <div style="display: flex; align-items: baseline; gap: 2px;">
                  <span class="profile-stat-value min-delay-value" id="sk-min-delay-val-${escapedFileName}" style="color: #60a5fa; font-weight: 700;">${uploadCfg.MinDelayHours ?? 8}</span>
                  <span style="font-size: 0.75rem; color: #60a5fa; font-weight: 600;">hrs</span>
                </div>
                ${state.isAuthenticatedUser ? `
                  <button type="button" class="btn-step-counter" onclick="window.stepSubKindMinDelay('${safeFileName}', 1)" title="Increase delay by 1 hour">
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
      </div>
    `;
  }).join('');

  if (window.lucide) lucide.createIcons();
}

export function stepSubKindMinDelay(fileName, delta) {
  const safeFileName = escapeJsString(fileName);
  const valSpan = document.getElementById(`sk-min-delay-val-${safeFileName}`);
  if (!valSpan) return;
  
  let currentVal = parseInt(valSpan.textContent, 10) || 8;
  let newVal = Math.max(1, currentVal + delta);
  valSpan.textContent = newVal;
  
  onSubKindCardModified(fileName);
}

export function onSubKindCardModified(fileName) {
  const safeFileName = escapeJsString(fileName);
  const saveBtn = document.getElementById(`save-sk-btn-${safeFileName}`);
  if (saveBtn) {
    saveBtn.style.display = 'inline-flex';
    if (window.lucide) lucide.createIcons({ targets: [saveBtn] });
  }
  const card = document.querySelector(`[data-subkind-file="${safeFileName}"]`);
  if (card) {
    card.classList.add('card-modified');
  }
}

export async function saveSubKindCard(fileName) {
  const safeFileName = escapeJsString(fileName);
  const saveBtn = document.getElementById(`save-sk-btn-${safeFileName}`);
  const minDelaySpan = document.getElementById(`sk-min-delay-val-${safeFileName}`);

  if (!saveBtn) return;

  const originalHtml = saveBtn.innerHTML;
  saveBtn.disabled = true;
  saveBtn.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true" style="width: 12px; height: 12px; margin-right: 4px;"></span> Saving...';

  const minDelayHours = minDelaySpan ? (parseInt(minDelaySpan.textContent, 10) || 8) : 8;

  try {
    const res = await fetch(`/api/dashboard/profiles/subkinds/${encodeURIComponent(fileName)}`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        UploadConfig: {
          MinDelayHours: minDelayHours
        }
      })
    });

    if (res.ok) {
      showToast(`Saved changes for ${fileName}`, 'success');
      saveBtn.style.display = 'none';
      const card = document.querySelector(`[data-subkind-file="${safeFileName}"]`);
      if (card) {
        card.classList.remove('card-modified');
      }
    } else {
      const errData = await res.json().catch(() => ({}));
      showToast(errData.message || `Failed to save changes for ${fileName}`, 'error');
    }
  } catch (err) {
    console.error('Error saving subkind profile card:', err);
    showToast(`Failed to save changes for ${fileName}`, 'error');
  } finally {
    saveBtn.disabled = false;
    saveBtn.innerHTML = originalHtml;
    if (window.lucide) lucide.createIcons({ targets: [saveBtn] });
  }
}

export function openCreateSubKindModal(callbacks = {}) {
  if (!state.isAuthenticatedUser) {
    showToast('You must be logged in to create a SubKind profile.', 'warning');
    if (callbacks.onOpenLoginModal) callbacks.onOpenLoginModal();
    return;
  }
  document.getElementById('createSubKindForm').reset();
  document.getElementById('createSubKindErrorMsg').style.display = 'none';
  
  // Populate parent kind dropdown if available
  const parentSelect = document.getElementById('newSubKindParentKind');
  if (parentSelect && state.kinds) {
    parentSelect.innerHTML = state.kinds.map(k => `<option value="${escapeHtml(k.name)}">${escapeHtml(k.name.toUpperCase())}</option>`).join('');
  }

  document.getElementById('createSubKindModal').classList.add('active');
}

export function closeCreateSubKindModal() {
  document.getElementById('createSubKindModal').classList.remove('active');
}

export async function handleCreateSubKind(e, callbacks = {}) {
  e.preventDefault();
  const subKindKey = document.getElementById('newSubKindKey').value.trim().toLowerCase();
  const parentKindKey = document.getElementById('newSubKindParentKind').value.trim().toLowerCase();
  
  if (!subKindKey || !parentKindKey) return;

  const errEl = document.getElementById('createSubKindErrorMsg');
  const submitBtn = document.getElementById('createSubKindSubmitBtn');

  const channelId = document.getElementById('newSubKindChannelId').value.trim();
  const minDelayHours = parseInt(document.getElementById('newSubKindMinDelayHours').value, 10) || 8;

  const payload = {
    SubKindKey: subKindKey,
    KindKey: parentKindKey,
    TargetYoutubeChannelId: channelId || null,
    UploadConfig: {
      MinDelayHours: minDelayHours,
      UploadCounter: 0
    }
  };

  try {
    submitBtn.disabled = true;
    submitBtn.innerText = 'Creating...';
    errEl.style.display = 'none';

    const res = await fetchWithAuth('/api/dashboard/profiles/subkinds', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });

    const data = await res.json();
    if (!res.ok) {
      throw new Error(data.message || 'Failed to create SubKind profile.');
    }

    showToast(`SubKind profile '${subKindKey}' created successfully!`, 'success');
    closeCreateSubKindModal();
    if (callbacks.onFetchProfiles) await callbacks.onFetchProfiles();
  } catch (err) {
    errEl.innerText = err.message;
    errEl.style.display = 'block';
  } finally {
    submitBtn.disabled = false;
    submitBtn.innerText = 'Create SubKind Profile';
  }
}

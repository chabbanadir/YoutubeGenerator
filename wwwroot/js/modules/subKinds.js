// SubKinds Profile Management Module
import { state, showToast, escapeHtml, escapeJsString, formatDate, fetchWithAuth, getApiUrl } from '../core/api.js';

export function renderSubKindsGrid(subKinds, callbacks = {}) {
  const container = document.getElementById('subKindsGrid');
  if (!container) return;

  if (!subKinds || subKinds.length === 0) {
    container.innerHTML = '<div class="text-muted" style="grid-column: 1/-1; padding: 1.5rem; text-align: center; background: rgba(15,23,42,0.4); border-radius: 8px;">No SubKind profiles found in <code>workspace/profiles/subkinds</code>.</div>';
    return;
  }

  container.innerHTML = subKinds.map(sk => {
    const data = sk.data || {};
    const subKindKey = (data.SubKindKey || sk.name || '').toLowerCase();
    const parentKind = (data.KindKey || 'generic').toUpperCase();
    const escapedFileName = escapeHtml(sk.fileName);
    const safeFileName = escapeJsString(sk.fileName);

    const badgeThemeClass = parentKind.toLowerCase() === 'lol' ? 'lol' : (parentKind.toLowerCase() === 'valorant' ? 'valorant' : 'default');
    const topAccentColor = '#a855f7';

    const minMontageMinutes = data.MinMontageMinutes ?? 12;
    const transitionAnim = data.TransitionAnimation || 'fade';
    const transitionDur = data.TransitionDuration ?? 2;
    const introFile = data.IntroFile || 'None';

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
            <i data-lucide="tag" style="width: 14px; height: 14px; color: #a855f7;"></i> SubKind Assembly & Intro
          </div>
          <div style="display: flex; flex-direction: column; gap: 0.4rem; background: rgba(0, 0, 0, 0.25); padding: 0.5rem; border-radius: 6px; border: 1px solid rgba(255,255,255,0.05);">
            <div style="display: flex; justify-content: space-between; font-size: 0.8rem;">
              <span style="color: var(--text-muted);">SubKind Key:</span>
              <span style="font-family: monospace; font-weight: 700; color: #a855f7;">${escapeHtml(subKindKey)}</span>
            </div>
            <div style="display: flex; justify-content: space-between; font-size: 0.8rem;">
              <span style="color: var(--text-muted);">Intro File:</span>
              <span style="font-family: monospace; color: #60a5fa;">${escapeHtml(introFile)}</span>
            </div>
            <div style="display: flex; justify-content: space-between; font-size: 0.8rem;">
              <span style="color: var(--text-muted);">Transition:</span>
              <span style="font-family: monospace; color: #34d399;">${escapeHtml(transitionAnim)} (${transitionDur}s)</span>
            </div>
          </div>
        </div>

        <div class="profile-section">
          <div class="profile-section-title">
            <i data-lucide="film" style="width: 14px; height: 14px; color: #38bdf8;"></i> Montage Video Target Duration
          </div>
          <div class="profile-stats-grid" style="grid-template-columns: 1fr;">
            <div class="profile-stat-box" style="position: relative; padding: 0.5rem; display: flex; flex-direction: column; align-items: center;">
              <span class="profile-stat-label" style="font-size: 0.75rem; color: var(--text-muted); font-weight: 600;">VIDEO DURATION (MINUTES)</span>
              <div style="display: flex; align-items: center; justify-content: center; gap: 0.5rem; margin-top: 0.25rem;">
                ${state.isAuthenticatedUser ? `
                  <button type="button" class="btn-step-counter" onclick="window.stepSubKindDuration('${safeFileName}', -1)" title="Decrease target duration by 1 minute">
                    -
                  </button>
                ` : ''}
                <div style="display: flex; align-items: baseline; gap: 4px;">
                  <span class="profile-stat-value duration-value" id="sk-duration-val-${escapedFileName}" style="color: #38bdf8; font-weight: 700; font-size: 1.25rem;">${minMontageMinutes}</span>
                  <span style="font-size: 0.85rem; color: #38bdf8; font-weight: 600;">min</span>
                </div>
                ${state.isAuthenticatedUser ? `
                  <button type="button" class="btn-step-counter" onclick="window.stepSubKindDuration('${safeFileName}', 1)" title="Increase target duration by 1 minute">
                    +
                  </button>
                ` : ''}
              </div>
            </div>
          </div>
        </div>
      </div>
    `;
  }).join('');

  if (window.lucide) lucide.createIcons();
}

export function stepSubKindDuration(fileName, delta) {
  const safeFileName = escapeJsString(fileName);
  const valSpan = document.getElementById(`sk-duration-val-${safeFileName}`);
  if (!valSpan) return;

  let currentVal = parseInt(valSpan.textContent, 10) || 12;
  let newVal = Math.max(1, currentVal + delta);
  valSpan.textContent = newVal;

  onSubKindCardModified(fileName);
}

export function stepSubKindMinDelay(fileName, delta) {
  stepSubKindDuration(fileName, delta);
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
  const durationSpan = document.getElementById(`sk-duration-val-${safeFileName}`);

  if (!saveBtn) return;

  const originalHtml = saveBtn.innerHTML;
  saveBtn.disabled = true;
  saveBtn.innerHTML = '<span class="spinner-border spinner-border-sm" role="status" aria-hidden="true" style="width: 12px; height: 12px; margin-right: 4px;"></span> Saving...';

  const minMontageMinutes = durationSpan ? (parseInt(durationSpan.textContent, 10) || 12) : 12;

  try {
    const res = await fetchWithAuth(`/api/dashboard/profiles/subkinds/${encodeURIComponent(fileName)}`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify({
        MinMontageMinutes: minMontageMinutes
      })
    });

    if (res.ok) {
      showToast(`Saved duration for ${fileName}`, 'success');
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

export async function openCreateSubKindModal(callbacks = {}) {
  if (!state.isAuthenticatedUser) {
    showToast('You must be logged in to create a SubKind profile.', 'warning');
    if (callbacks.onOpenLoginModal) callbacks.onOpenLoginModal();
    return;
  }

  const form = document.getElementById('createSubkindForm') || document.getElementById('createSubKindForm');
  if (form) form.reset();

  const errEl = document.getElementById('createSubkindErrorMsg') || document.getElementById('createSubKindErrorMsg');
  if (errEl) errEl.style.display = 'none';

  // Populate parent kind dropdown
  const parentSelect = document.getElementById('newSubkindKindKey') || document.getElementById('newSubKindParentKind');
  if (parentSelect && state.kinds) {
    parentSelect.innerHTML = '<option value="">Select Kind...</option>' + 
      state.kinds.map(k => `<option value="${escapeHtml(k.name)}">${escapeHtml(k.name.toUpperCase())}</option>`).join('');
  }

  // Populate resource intro files dropdown
  const introSelect = document.getElementById('newSubkindIntroFile');
  if (introSelect) {
    try {
      const res = await fetch(getApiUrl('/api/dashboard/resources'));
      if (res.ok) {
        const resources = await res.json();
        introSelect.innerHTML = '<option value="">None / Select...</option>' + 
          resources.map(r => `<option value="${escapeHtml(r)}">${escapeHtml(r)}</option>`).join('');
      }
    } catch (e) {
      console.warn('Failed to load resource files:', e);
    }
  }

  const modal = document.getElementById('createSubkindModal') || document.getElementById('createSubKindModal');
  if (modal) modal.classList.add('active');
}

export function closeCreateSubKindModal() {
  const modal = document.getElementById('createSubkindModal') || document.getElementById('createSubKindModal');
  if (modal) modal.classList.remove('active');
}

export async function handleCreateSubKind(e, callbacks = {}) {
  e.preventDefault();

  const kindSelect = document.getElementById('newSubkindKindKey') || document.getElementById('newSubKindParentKind');
  const subKindInput = document.getElementById('newSubkindSubKindKey') || document.getElementById('newSubKindKey');
  const introSelect = document.getElementById('newSubkindIntroFile');
  const transitionAnimSelect = document.getElementById('newSubkindTransitionAnimation');
  const transitionDurInput = document.getElementById('newSubkindTransitionDuration');
  const minMontageInput = document.getElementById('newSubkindMinMontageMinutes');

  const kindKey = (kindSelect ? kindSelect.value : '').trim().toLowerCase();
  const subKindKey = (subKindInput ? subKindInput.value : '').trim().toLowerCase();

  const errEl = document.getElementById('createSubkindErrorMsg') || document.getElementById('createSubKindErrorMsg');
  const submitBtn = document.getElementById('createSubkindSubmitBtn') || document.getElementById('createSubKindSubmitBtn');

  if (!kindKey || !subKindKey) {
    if (errEl) {
      errEl.innerText = 'Kind Key and SubKind Key are required.';
      errEl.style.display = 'block';
    }
    return;
  }

  const introFile = introSelect ? introSelect.value : '';
  const transitionAnimation = transitionAnimSelect ? transitionAnimSelect.value : 'fade';
  const transitionDuration = transitionDurInput ? (parseFloat(transitionDurInput.value) || 2) : 2;
  const minMontageMinutes = minMontageInput ? (parseFloat(minMontageInput.value) || 12) : 12;

  const payload = {
    KindKey: kindKey,
    SubKindKey: subKindKey,
    IntroFile: introFile,
    TransitionAnimation: transitionAnimation,
    TransitionDuration: transitionDuration,
    MinMontageMinutes: minMontageMinutes
  };

  try {
    if (submitBtn) {
      submitBtn.disabled = true;
      submitBtn.innerText = 'Creating...';
    }
    if (errEl) errEl.style.display = 'none';

    const res = await fetchWithAuth('/api/dashboard/profiles/subkinds', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });

    const data = await res.json();
    if (!res.ok) {
      throw new Error(data.message || 'Failed to create SubKind profile.');
    }

    showToast(`SubKind profile '${kindKey}_${subKindKey}' created successfully!`, 'success');
    closeCreateSubKindModal();
    if (callbacks.onFetchProfiles) await callbacks.onFetchProfiles();
  } catch (err) {
    if (errEl) {
      errEl.innerText = err.message;
      errEl.style.display = 'block';
    }
  } finally {
    if (submitBtn) {
      submitBtn.disabled = false;
      submitBtn.innerText = 'Create SubKind Profile';
    }
  }
}


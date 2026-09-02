// SubKinds Assembly Profiles Management Logic

let cachedResourceFilesList = null;

async function fetchResourcesList() {
  if (cachedResourceFilesList) return cachedResourceFilesList;
  try {
    const res = await fetchWithAuth('/api/dashboard/resources');
    const data = await res.json();
    if (data && data.success && Array.isArray(data.files)) {
      cachedResourceFilesList = data.files;
      return cachedResourceFilesList;
    } else if (Array.isArray(data)) {
      cachedResourceFilesList = data;
      return cachedResourceFilesList;
    }
  } catch (err) {
    console.error('Failed to fetch resources:', err);
  }
  return [];
}

function renderSubkindsGrid(subkinds) {
  const container = document.getElementById('subkindsGrid');
  if (!container) return;

  if (!subkinds || subkinds.length === 0) {
    container.innerHTML = '<div class="text-muted" style="grid-column: 1/-1; padding: 1.5rem; text-align: center; background: rgba(15,23,42,0.4); border-radius: 8px;">No SubKind assembly profiles found in <code>workspace/profiles/subkinds</code>.</div>';
    return;
  }

  const ffmpegEffects = ['fade', 'dissolve', 'pixelize', 'fadeblack', 'fadewhite', 'wipeleft', 'wiperight', 'wipeup', 'wipedown', 'slideleft', 'slideright', 'slideup', 'slidedown', 'circlecrop', 'rectcrop', 'distance'];
  const resourceFiles = cachedResourceFilesList || [];

  container.innerHTML = subkinds.map(sk => {
    const data = sk.data || {};
    const kindKey = (data.KindKey || sk.name.split('_')[0] || '').toLowerCase();
    const kindTitle = kindKey.toUpperCase();
    const subKindKey = (data.SubKindKey || sk.name.split('_').slice(1).join('_') || 'default').toLowerCase();
    const escapedFileName = escapeHtml(sk.fileName);
    const safeFileName = escapeJsString(sk.fileName);

    const badgeThemeClass = kindKey === 'lol' ? 'lol' : (kindKey === 'valorant' ? 'valorant' : 'default');
    const subKindThemeClass = subKindKey === 'pentas' ? 'pentas' : (subKindKey === 'aces' ? 'aces' : 'normal');

    let sideBorderColor = 'var(--primary)';
    if (subKindKey === 'pentas') sideBorderColor = '#f59e0b';
    else if (subKindKey === 'aces') sideBorderColor = '#f43f5e';
    else if (subKindKey === 'normal') sideBorderColor = '#10b981';

    const currentTransition = (data.TransitionAnimation || 'fade').toLowerCase();
    const currentIntroFile = data.IntroFile || '';

    // Combine current intro file with available files to ensure active option is included
    let availableIntros = [...resourceFiles];
    if (currentIntroFile && !availableIntros.includes(currentIntroFile)) {
      availableIntros.unshift(currentIntroFile);
    }

    return `
      <div class="profile-card" data-subkind-file="${escapedFileName}" style="border-left: 4px solid ${sideBorderColor};">
        <div class="profile-card-header">
          <div class="profile-title-group">
            <span class="profile-kind-badge ${badgeThemeClass}" style="font-size: 0.75rem; padding: 0.15rem 0.5rem;">
              ${escapeHtml(kindTitle)}
            </span>
            <span class="profile-subkind-badge ${subKindThemeClass}">
              ${escapeHtml(subKindKey.toUpperCase())}
            </span>
            <span class="profile-filename" title="${escapedFileName}">${escapedFileName}</span>
          </div>
          <div style="display: flex; align-items: center; gap: 0.5rem;">
            ${isAuthenticatedUser ? `
              <button type="button" class="save-kind-btn" id="save-subkind-btn-${escapedFileName}" style="display: none;" onclick="saveSubKindCard('${safeFileName}')">
                <i data-lucide="save" style="width: 14px; height: 14px;"></i> Save
              </button>
              <button type="button" class="btn-delete-profile" onclick="promptDeleteProfile('subkind', '${safeFileName}')" title="Delete SubKind Profile">
                <i data-lucide="trash-2" style="width: 14px; height: 14px;"></i> Delete
              </button>
            ` : ''}
          </div>
        </div>

        <div class="profile-section">
          <div class="profile-section-title">
            <i data-lucide="file-video" style="width: 14px; height: 14px; color: #38bdf8;"></i> Video Intro Asset
          </div>
          <div class="profile-channel-bar">
            ${isAuthenticatedUser ? `
              <select id="subkind-intro-input-${escapedFileName}" onchange="onSubKindCardModified('${safeFileName}')" style="background: rgba(0, 0, 0, 0.4); border: 1px solid var(--border); color: #e2e8f0; font-family: monospace; font-size: 0.8rem; padding: 0.25rem 0.5rem; border-radius: 6px; flex: 1; outline: none; cursor: pointer;">
                <option value="" ${!currentIntroFile ? 'selected' : ''}>None (No Intro)</option>
                ${availableIntros.map(f => `
                  <option value="${escapeHtml(f)}" ${currentIntroFile === f ? 'selected' : ''}>${escapeHtml(f)}</option>
                `).join('')}
              </select>
            ` : `
              <code style="font-family: monospace; font-size: 0.8rem; color: #e2e8f0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; max-width: 260px;">
                ${escapeHtml(data.IntroFile || 'None')}
              </code>
            `}
            <button class="btn btn-secondary" style="padding: 0.2rem 0.4rem; font-size: 0.72rem;" onclick="copyToClipboard('${escapeJsString(data.IntroFile || '')}', 'Intro File Path')">
              <i data-lucide="copy" style="width: 12px; height: 12px;"></i>
            </button>
          </div>
        </div>

        <div class="profile-section">
          <div class="profile-section-title">
            <i data-lucide="sliders" style="width: 14px; height: 14px; color: #a78bfa;"></i> Assembly & Render Settings
          </div>
          <div class="profile-stats-grid">
            <div class="profile-stat-box">
              <span class="profile-stat-label">Transition</span>
              ${isAuthenticatedUser ? `
                <select id="subkind-transition-select-${escapedFileName}" onchange="onSubKindCardModified('${safeFileName}')" style="background: rgba(0, 0, 0, 0.4); border: 1px solid rgba(167, 139, 250, 0.3); color: #c084fc; font-weight: 600; font-size: 0.8rem; padding: 0.2rem 0.3rem; border-radius: 6px; outline: none; cursor: pointer; text-transform: capitalize;">
                  ${ffmpegEffects.map(fx => `
                    <option value="${fx}" ${currentTransition === fx ? 'selected' : ''}>${fx}</option>
                  `).join('')}
                </select>
              ` : `
                <span class="profile-stat-value" style="color: #c084fc; text-transform: capitalize;">${escapeHtml(data.TransitionAnimation || 'fade')}</span>
              `}
            </div>

            <div class="profile-stat-box">
              <span class="profile-stat-label">Duration</span>
              ${isAuthenticatedUser ? `
                <div style="display: flex; align-items: center; justify-content: center; gap: 0.3rem; margin-top: 0.2rem;">
                  <button type="button" class="btn btn-secondary" style="padding: 0.1rem 0.4rem; font-size: 0.75rem; font-weight: bold; line-height: 1;" onclick="stepSubKindDuration('${safeFileName}', -1)">-</button>
                  <span id="subkind-duration-val-${escapedFileName}" class="profile-stat-value" style="color: #38bdf8; min-width: 24px; text-align: center;">${data.TransitionDuration ?? 2}s</span>
                  <button type="button" class="btn btn-secondary" style="padding: 0.1rem 0.4rem; font-size: 0.75rem; font-weight: bold; line-height: 1;" onclick="stepSubKindDuration('${safeFileName}', 1)">+</button>
                </div>
              ` : `
                <span class="profile-stat-value" style="color: #38bdf8;">${data.TransitionDuration ?? 2}s</span>
              `}
            </div>

            <div class="profile-stat-box">
              <span class="profile-stat-label">Min Target</span>
              ${isAuthenticatedUser ? `
                <div style="display: flex; align-items: center; justify-content: center; gap: 0.3rem; margin-top: 0.2rem;">
                  <button type="button" class="btn btn-secondary" style="padding: 0.1rem 0.4rem; font-size: 0.75rem; font-weight: bold; line-height: 1;" onclick="stepSubKindMinTarget('${safeFileName}', -1)">-</button>
                  <span id="subkind-mintarget-val-${escapedFileName}" class="profile-stat-value" style="color: #fbbf24; min-width: 30px; text-align: center;">${data.MinMontageMinutes ?? 10}m</span>
                  <button type="button" class="btn btn-secondary" style="padding: 0.1rem 0.4rem; font-size: 0.75rem; font-weight: bold; line-height: 1;" onclick="stepSubKindMinTarget('${safeFileName}', 1)">+</button>
                </div>
              ` : `
                <span class="profile-stat-value" style="color: #fbbf24;">${data.MinMontageMinutes ?? 10}m</span>
              `}
            </div>
          </div>
        </div>
      </div>
    `;
  }).join('');

  if (window.lucide) lucide.createIcons({ targets: [container] });
}

function stepSubKindDuration(fileName, delta) {
  const safeFileName = escapeJsString(fileName);
  const valEl = document.getElementById(`subkind-duration-val-${safeFileName}`);
  if (!valEl) return;
  let curVal = parseInt(valEl.innerText.replace('s', ''), 10) || 2;
  curVal += delta;
  if (curVal < 1) curVal = 1;
  valEl.innerText = `${curVal}s`;
  onSubKindCardModified(fileName);
}

function stepSubKindMinTarget(fileName, delta) {
  const safeFileName = escapeJsString(fileName);
  const valEl = document.getElementById(`subkind-mintarget-val-${safeFileName}`);
  if (!valEl) return;
  let curVal = parseInt(valEl.innerText.replace('m', ''), 10) || 10;
  curVal += delta;
  if (curVal < 1) curVal = 1;
  valEl.innerText = `${curVal}m`;
  onSubKindCardModified(fileName);
}

function onSubKindCardModified(fileName) {
  const safeFileName = escapeJsString(fileName);
  const card = document.querySelector(`[data-subkind-file="${safeFileName}"]`);
  if (card) {
    card.classList.add('card-modified');
  }
  const saveBtn = document.getElementById(`save-subkind-btn-${safeFileName}`);
  if (saveBtn) {
    saveBtn.style.display = 'inline-flex';
    if (window.lucide) lucide.createIcons({ targets: [saveBtn] });
  }
}

async function saveSubKindCard(fileName) {
  const safeFileName = escapeJsString(fileName);
  const card = document.querySelector(`[data-subkind-file="${safeFileName}"]`);
  const saveBtn = document.getElementById(`save-subkind-btn-${safeFileName}`);
  const introInput = document.getElementById(`subkind-intro-input-${safeFileName}`);
  const transitionSelect = document.getElementById(`subkind-transition-select-${safeFileName}`);
  const durationValEl = document.getElementById(`subkind-duration-val-${safeFileName}`);
  const minTargetValEl = document.getElementById(`subkind-mintarget-val-${safeFileName}`);

  const introFile = introInput ? introInput.value.trim() : '';
  const transitionAnim = transitionSelect ? transitionSelect.value : 'fade';
  const duration = durationValEl ? (parseInt(durationValEl.innerText.replace('s', ''), 10) || 2) : 2;
  const minMontageMinutes = minTargetValEl ? (parseInt(minTargetValEl.innerText.replace('m', ''), 10) || 10) : 10;

  const payload = {
    IntroFile: introFile,
    TransitionAnimation: transitionAnim,
    TransitionDuration: duration,
    MinMontageMinutes: minMontageMinutes
  };

  const originalHtml = saveBtn ? saveBtn.innerHTML : 'Save';

  try {
    if (saveBtn) {
      saveBtn.disabled = true;
      saveBtn.innerHTML = `<i data-lucide="loader-2" class="spin"></i> Saving...`;
      if (window.lucide) lucide.createIcons({ targets: [saveBtn] });
    }

    const res = await fetchWithAuth(`/api/dashboard/profiles/subkinds/${encodeURIComponent(fileName)}`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify(payload)
    });

    const data = await res.json();
    if (res.ok && data.success) {
      showToast(`SubKind profile '${fileName}' saved successfully!`, 'success');
      if (card) card.classList.remove('card-modified');
      if (saveBtn) saveBtn.style.display = 'none';
    } else {
      showToast(data.message || 'Failed to save SubKind profile', 'error');
    }
  } catch (err) {
    showToast('Network error while saving SubKind profile', 'error');
    console.error(err);
  } finally {
    if (saveBtn) {
      saveBtn.disabled = false;
      saveBtn.innerHTML = originalHtml;
      if (window.lucide) lucide.createIcons({ targets: [saveBtn] });
    }
  }
}

async function openCreateSubkindModal() {
  if (!isAuthenticatedUser) {
    showToast('You must be logged in to create a SubKind profile.', 'warning');
    openLoginModal();
    return;
  }

  const modal = document.getElementById('createSubkindModal');
  const form = document.getElementById('createSubkindForm');
  const errEl = document.getElementById('createSubkindErrorMsg');
  const kindSelect = document.getElementById('newSubkindKindKey');
  const introSelect = document.getElementById('newSubkindIntroFile');

  if (form) form.reset();
  if (errEl) errEl.style.display = 'none';

  // Populate Kind Key options dynamically from existing kinds API
  if (kindSelect) {
    let kindsList = [];
    try {
      let profilesObj = (window.state && window.state.profilesData) || window.profilesData;
      if (!profilesObj || !Array.isArray(profilesObj.kinds) || profilesObj.kinds.length === 0) {
        const pRes = await fetch('/api/dashboard/profiles');
        if (pRes.ok) {
          profilesObj = await pRes.json();
          window.state = window.state || {};
          window.state.profilesData = profilesObj;
          window.profilesData = profilesObj;
        }
      }
      if (profilesObj && Array.isArray(profilesObj.kinds)) {
        kindsList = profilesObj.kinds.map(k => {
          const rawName = k.name || k.fileName || '';
          const cleanName = rawName.replace(/\.json$/i, '');
          const kKey = cleanName || (k.data && (k.data.KindKey || k.data.kindKey)) || '';
          return kKey.toLowerCase();
        }).filter(Boolean);
      }
    } catch (err) {
      console.error('Failed to load kinds list for dropdown:', err);
    }

    if (kindsList.length === 0) {
      kindsList = ['lol', 'valorant'];
    }
    kindsList = [...new Set(kindsList)];
    kindSelect.innerHTML = kindsList.map(k => `<option value="${escapeHtml(k)}" style="background-color: #1e293b; color: #f8fafc;">${escapeHtml(k.toUpperCase())}</option>`).join('');
  }

  // Populate Intro Video Asset File Path options dynamically from resources API
  if (introSelect) {
    introSelect.innerHTML = '<option value="" style="background-color: #1e293b; color: #94a3b8;">Loading files...</option>';
    const files = await fetchResourcesList();
    if (files && files.length > 0) {
      introSelect.innerHTML = '<option value="" style="background-color: #1e293b; color: #94a3b8;">None (No Intro)</option>' + 
        files.map(f => `<option value="${escapeHtml(f)}" style="background-color: #1e293b; color: #f8fafc;">${escapeHtml(f)}</option>`).join('');
    } else {
      introSelect.innerHTML = '<option value="" style="background-color: #1e293b; color: #94a3b8;">None (No Intro)</option>';
    }
  }

  if (modal) {
    modal.classList.add('active');
    if (window.lucide) lucide.createIcons({ targets: [modal] });
  }
}

function closeCreateSubkindModal() {
  const modal = document.getElementById('createSubkindModal');
  if (modal) modal.classList.remove('active');
}

async function handleCreateSubkind(e) {
  if (e && typeof e.preventDefault === 'function') {
    e.preventDefault();
  }
  const kindKey = document.getElementById('newSubkindKindKey').value.trim().toLowerCase();
  const subKindKey = document.getElementById('newSubkindSubKindKey').value.trim().toLowerCase();
  if (!kindKey || !subKindKey) return;

  const errEl = document.getElementById('createSubkindErrorMsg');
  const submitBtn = document.getElementById('createSubkindSubmitBtn');

  const introFile = document.getElementById('newSubkindIntroFile').value.trim();
  const transitionAnim = document.getElementById('newSubkindTransitionAnimation').value;
  const duration = parseInt(document.getElementById('newSubkindTransitionDuration').value, 10) || 2;
  const minMontageMinutes = parseInt(document.getElementById('newSubkindMinMontageMinutes').value, 10) || 10;

  const payload = {
    KindKey: kindKey,
    SubKindKey: subKindKey,
    IntroFile: introFile,
    TransitionAnimation: transitionAnim,
    TransitionDuration: duration,
    MinMontageMinutes: minMontageMinutes
  };

  try {
    submitBtn.disabled = true;
    submitBtn.innerText = 'Creating...';
    errEl.style.display = 'none';

    const res = await fetchWithAuth('/api/dashboard/profiles/subkinds', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json'
      },
      body: JSON.stringify(payload)
    });

    const result = await res.json();
    if (res.ok && result.success) {
      showToast(`SubKind profile '${kindKey}_${subKindKey}.json' created successfully!`, 'success');
      closeCreateSubkindModal();
      await fetchProfilesData();
    } else {
      errEl.innerText = result.message || 'Failed to create SubKind profile.';
      errEl.style.display = 'block';
    }
  } catch (err) {
    errEl.innerText = 'Network error while creating SubKind profile.';
    errEl.style.display = 'block';
    console.error(err);
  } finally {
    submitBtn.disabled = false;
    submitBtn.innerText = 'Create SubKind Profile';
  }
}

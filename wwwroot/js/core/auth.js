// Core Authentication Module
import { state, escapeHtml, showToast, getApiUrl } from './api.js';

export async function checkAuthStatus(callbacks = {}) {
  try {
    const res = await fetch(getApiUrl('/api/auth/status'));
    if (res.ok) {
      const status = await res.json();
      state.isAuthenticatedUser = status.isAuthenticated;
      updateAuthUI(status, callbacks);
    }
  } catch (err) {
    console.error('Failed to check auth status:', err);
  } finally {
    if (callbacks.onOverviewRefresh) callbacks.onOverviewRefresh();
  }
}

export function updateAuthUI(status, callbacks = {}) {
  const pill = document.getElementById('authStatusPill');
  const text = document.getElementById('authStatusText');
  const loginBtn = document.getElementById('loginModalBtn');
  const logoutBtn = document.getElementById('logoutBtn');
  const editBagBtn = document.getElementById('btnEditBag');
  const textarea = document.getElementById('bagJsonTextarea');
  const saveRawJsonBtn = document.getElementById('btnSaveRawJson');
  const jsonEditorStatus = document.getElementById('jsonEditorStatus');
  const tabBtnProfiles = document.getElementById('tab-btn-profiles');
  const btnManageProfilesYt = document.getElementById('btnManageProfilesYt');
  const newKindBtn = document.getElementById('btnNewKind');
  const newSubkindBtn = document.getElementById('btnNewSubkind');

  if (status && status.isAuthenticated) {
    if (pill) {
      pill.className = 'pill auth-admin';
      pill.style.borderColor = 'rgba(16, 185, 129, 0.4)';
      pill.style.color = '#6ee7b7';
    }
    const username = (status.user && typeof status.user === 'object') ? (status.user.username || 'admin') : (status.user || 'admin');
    if (text) text.innerHTML = `<i data-lucide="shield-check" style="width: 14px; height: 14px; display: inline;"></i> Admin: <strong>${escapeHtml(username)}</strong>`;
    if (loginBtn) loginBtn.style.display = 'none';
    if (logoutBtn) logoutBtn.style.display = 'inline-flex';
    if (editBagBtn) editBagBtn.style.display = 'inline-flex';
    if (saveRawJsonBtn) saveRawJsonBtn.style.display = 'inline-flex';
    if (tabBtnProfiles) tabBtnProfiles.style.display = 'inline-flex';
    if (btnManageProfilesYt) btnManageProfilesYt.style.display = 'inline-flex';
    if (newKindBtn) newKindBtn.style.display = 'inline-flex';
    if (newSubkindBtn) newSubkindBtn.style.display = 'inline-flex';
    if (textarea) textarea.readOnly = false;
    if (jsonEditorStatus) {
      jsonEditorStatus.innerHTML = `
        <span class="badge" style="background: rgba(16, 185, 129, 0.15); color: #6ee7b7; border: 1px solid rgba(16, 185, 129, 0.3); font-weight: 600;">
          <i data-lucide="edit-3" style="width: 12px; height: 12px; display: inline;"></i> Live Editor
        </span>
        <span style="font-size: 0.8rem; color: var(--text-muted); margin-left: 0.4rem;">Admin direct edit enabled</span>
      `;
    }
  } else {
    if (pill) {
      pill.className = 'pill auth-guest';
      pill.style.borderColor = 'rgba(99, 102, 241, 0.4)';
      pill.style.color = '#a5b4fc';
    }
    if (text) text.innerHTML = 'Public View<span class="desktop-only-inline"> (Read-Only)</span>';
    if (loginBtn) loginBtn.style.display = 'inline-flex';
    if (logoutBtn) logoutBtn.style.display = 'none';
    if (editBagBtn) editBagBtn.style.display = 'none';
    if (saveRawJsonBtn) saveRawJsonBtn.style.display = 'none';
    if (tabBtnProfiles) tabBtnProfiles.style.display = 'none';
    if (btnManageProfilesYt) btnManageProfilesYt.style.display = 'none';
    if (newKindBtn) newKindBtn.style.display = 'none';
    if (newSubkindBtn) newSubkindBtn.style.display = 'none';
    if (textarea) textarea.readOnly = true;
    if (jsonEditorStatus) {
      jsonEditorStatus.innerHTML = `
        <i data-lucide="eye" style="color: var(--text-muted); width: 15px; height: 15px;"></i>
        <span style="font-size: 0.85rem; color: var(--text-muted);">Raw JSON View (Read-Only)</span>
      `;
    }

    // Switch back to overview tab if unauthenticated user is on profiles view
    const profilesView = document.getElementById('tab-profiles');
    if (profilesView && profilesView.classList.contains('active')) {
      if (callbacks.onSwitchTab) callbacks.onSwitchTab('overview');
    }
  }

  if (document.getElementById('tab-profiles') && document.getElementById('tab-profiles').classList.contains('active')) {
    if (callbacks.onFetchProfiles) callbacks.onFetchProfiles();
  }

  if (window.lucide) lucide.createIcons({ targets: [pill, jsonEditorStatus] });
}

export function openLoginModal() {
  const modal = document.getElementById('loginModal');
  const errorMsg = document.getElementById('loginErrorMsg');
  if (errorMsg) errorMsg.style.display = 'none';
  if (modal) modal.classList.add('active');
}

export function closeLoginModal() {
  const modal = document.getElementById('loginModal');
  if (modal) modal.classList.remove('active');
}

export async function handleLogin(e, callbacks = {}) {
  e.preventDefault();
  const usernameInput = document.getElementById('loginUsername');
  const passwordInput = document.getElementById('loginPassword');
  const submitBtn = document.getElementById('loginSubmitBtn');
  const errorMsg = document.getElementById('loginErrorMsg');

  if (!usernameInput || !passwordInput) return;

  if (submitBtn) submitBtn.disabled = true;
  if (errorMsg) errorMsg.style.display = 'none';

  try {
    const res = await fetch(getApiUrl('/api/auth/login'), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        username: usernameInput.value.trim(),
        password: passwordInput.value
      })
    });

    if (!res.ok) {
      const errData = await res.json().catch(() => ({}));
      throw new Error(errData.message || 'Invalid credentials');
    }

    const data = await res.json();
    showToast(`Logged in successfully as ${data.user}`, 'success');
    closeLoginModal();
    usernameInput.value = '';
    passwordInput.value = '';

    await checkAuthStatus(callbacks);
    if (callbacks.onLoadSelectedBag) callbacks.onLoadSelectedBag(state.currentSelectedBag);
  } catch (err) {
    console.error('Login error:', err);
    if (errorMsg) {
      errorMsg.innerText = err.message || 'Authentication failed. Please check credentials.';
      errorMsg.style.display = 'block';
    }
  } finally {
    if (submitBtn) submitBtn.disabled = false;
  }
}

export async function handleLogout(callbacks = {}) {
  try {
    const res = await fetch(getApiUrl('/api/auth/logout'), { method: 'POST' });
    if (res.ok) {
      showToast('Logged out successfully', 'info');
      await checkAuthStatus(callbacks);
      if (callbacks.onLoadSelectedBag) callbacks.onLoadSelectedBag(state.currentSelectedBag);
    }
  } catch (err) {
    console.error('Logout error:', err);
    showToast('Failed to logout', 'error');
  }
}

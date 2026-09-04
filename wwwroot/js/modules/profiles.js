// Profiles Manager Module
import { state, showToast, escapeJsString, fetchWithAuth } from '../core/api.js';
import { renderKindsGrid } from './gameKinds.js';
import { renderSubKindsGrid } from './subKinds.js';

let pendingDeleteProfileType = null;
let pendingDeleteFileName = null;

export async function fetchProfiles() {
  const containerKinds = document.getElementById('kindsGrid');
  const containerSubKinds = document.getElementById('subKindsGrid');

  if (containerKinds) containerKinds.innerHTML = '<div class="spinner">Loading Kind profiles...</div>';
  if (containerSubKinds) containerSubKinds.innerHTML = '<div class="spinner">Loading SubKind profiles...</div>';

  try {
    const res = await fetch('/api/dashboard/profiles');

    if (res.ok) {
      const data = await res.json();
      state.kinds = data.kinds || [];
      state.subKinds = data.subkinds || [];
    } else {
      state.kinds = [];
      state.subKinds = [];
    }

    renderKindsGrid(state.kinds);
    renderSubKindsGrid(state.subKinds);
  } catch (err) {
    console.error('Error loading profile configurations:', err);
    if (containerKinds) containerKinds.innerHTML = '<div class="text-danger">Failed to load Kind profiles.</div>';
    if (containerSubKinds) containerSubKinds.innerHTML = '<div class="text-danger">Failed to load SubKind profiles.</div>';
  }
}

export function promptDeleteProfile(type, fileName) {
  if (!state.isAuthenticatedUser) {
    showToast('You must be logged in to delete profiles.', 'warning');
    if (window.openLoginModal) window.openLoginModal();
    return;
  }

  pendingDeleteProfileType = type;
  pendingDeleteFileName = fileName;

  const displayEl = document.getElementById('deleteProfileFileNameDisplay');
  if (displayEl) {
    displayEl.textContent = fileName;
  }
  const modal = document.getElementById('confirmDeleteModal');
  if (modal) modal.classList.add('active');
}

export function closeConfirmDeleteModal() {
  pendingDeleteProfileType = null;
  pendingDeleteFileName = null;
  const modal = document.getElementById('confirmDeleteModal');
  if (modal) modal.classList.remove('active');
}

export async function executeProfileDelete() {
  if (!pendingDeleteProfileType || !pendingDeleteFileName) return;

  const confirmBtn = document.getElementById('confirmDeleteSubmitBtn');
  if (confirmBtn) confirmBtn.disabled = true;

  const type = pendingDeleteProfileType;
  const fileName = pendingDeleteFileName;

  try {
    const endpoint = type === 'kind' 
      ? `/api/dashboard/profiles/kinds/${encodeURIComponent(fileName)}`
      : `/api/dashboard/profiles/subkinds/${encodeURIComponent(fileName)}`;

    const res = await fetchWithAuth(endpoint, { method: 'DELETE' });
    const data = await res.json();

    if (res.ok && data.success) {
      showToast(data.message || `Deleted ${type} profile '${fileName}' successfully.`, 'success');
      closeConfirmDeleteModal();
      await fetchProfiles();
    } else {
      showToast(data.message || `Failed to delete ${type} profile.`, 'error');
    }
  } catch (err) {
    console.error('Error deleting profile:', err);
    showToast('Error executing delete request.', 'error');
  } finally {
    if (confirmBtn) confirmBtn.disabled = false;
  }
}


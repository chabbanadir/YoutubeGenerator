// Main Application Orchestrator (ES6 Module Entry Point)

import { state, showToast, escapeHtml, escapeJsString, formatDate, fetchWithAuth, getApiUrl } from './core/api.js';
import { checkAuthStatus, openLoginModal, closeLoginModal, handleLogin, handleLogout } from './core/auth.js';
import { 
  loadSelectedBag, 
  openBagInExplorer,
  openEditBagModal,
  handleSaveRawJsonInline,
  formatRawJson,
  resetRawJson,
  renderBagsOverviewGrid, 
  renderBagsSidebarList 
} from './modules/bags.js';
import { 
  renderKindsGrid, 
  stepKindMinDelay, 
  onKindCardModified, 
  saveKindCard, 
  openCreateKindModal, 
  closeCreateKindModal, 
  handleCreateKind, 
  handleCreateAndConnectKind, 
  connectYoutubeChannel, 
  disconnectYoutubeChannel 
} from './modules/gameKinds.js';
import { 
  renderSubKindsGrid, 
  stepSubKindMinDelay, 
  stepSubKindDuration,
  onSubKindCardModified, 
  saveSubKindCard, 
  openCreateSubKindModal, 
  closeCreateSubKindModal, 
  handleCreateSubKind 
} from './modules/subKinds.js';
import { 
  fetchProfiles, 
  promptDeleteProfile, 
  closeConfirmDeleteModal, 
  executeProfileDelete 
} from './modules/profiles.js';

// Global tab switching
export function switchTab(tabId) {
  const targetTabBtn = document.getElementById(`tab-btn-${tabId}`);
  const targetTabView = document.getElementById(`tab-${tabId}`);

  if (!targetTabView) return;

  document.querySelectorAll('.tab-btn').forEach(btn => btn.classList.remove('active'));
  if (targetTabBtn) targetTabBtn.classList.add('active');

  document.querySelectorAll('.tab-view').forEach(view => view.classList.remove('active'));
  targetTabView.classList.add('active');

  if (tabId === 'profiles') {
    fetchProfiles();
  } else if (tabId === 'bags' && state.selectedBag) {
    loadSelectedBag(state.selectedBag);
  }
}

// Global dashboard overview fetch
export async function fetchDashboardOverview(showNotification = false) {
  const refreshIcon = document.getElementById('refreshIcon');
  if (refreshIcon) refreshIcon.classList.add('spin');

  try {
    const res = await fetch(getApiUrl('/api/dashboard/overview'));
    if (!res.ok) throw new Error(`HTTP ${res.status}`);

    const data = await res.json();

    // 1. Update Worker Pills
    const collectDot = document.getElementById('collectDot');
    const collectText = document.getElementById('collectStatusText');
    const montageDot = document.getElementById('montageDot');
    const montageText = document.getElementById('montageStatusText');

    const isCollectRunning = data.workers?.collectWorkerActive || false;
    const isMontageRunning = data.workers?.montageWorkerActive || false;

    if (collectDot) collectDot.className = `status-dot ${isCollectRunning ? 'online' : 'offline'}`;
    if (collectText) collectText.innerText = isCollectRunning ? 'Active' : 'Idle';

    if (montageDot) montageDot.className = `status-dot ${isMontageRunning ? 'online' : 'offline'}`;
    if (montageText) montageText.innerText = isMontageRunning ? 'Active' : 'Idle';

    // 2. Update Automation Pipeline Counts
    const bagsMap = {};
    if (Array.isArray(data.bags)) {
      data.bags.forEach(b => {
        bagsMap[b.fileName] = b.itemCount || 0;
      });
    }

    const setVal = (id, count) => {
      const el = document.getElementById(id);
      if (el) el.innerText = count ?? 0;
    };

    setVal('pipe-download-count', bagsMap['bag_download.json']);
    setVal('pipe-trimmed-count', bagsMap['bag_trimmed.json']);
    setVal('pipe-montage-count', bagsMap['bag_montage.json']);
    setVal('pipe-youtube-count', bagsMap['bag_youtube.json']);
    setVal('pipe-done-count', bagsMap['bag_youtube_done.json']);
    setVal('pipe-failed-count', bagsMap['bag_failed.json']);

    // 3. Render Bags Overview Grid & Sidebar
    renderBagsOverviewGrid(data.bags || []);
    renderBagsSidebarList(data.bags || []);

    // Default select first bag if none active
    if (!state.selectedBag && Array.isArray(data.bags) && data.bags.length > 0) {
      state.selectedBag = data.bags[0].fileName;
    }

    if (showNotification) {
      showToast('Dashboard data updated successfully', 'info');
    }

  } catch (err) {
    console.error('Failed to fetch dashboard overview:', err);
    showToast('Error refreshing dashboard data', 'error');
  } finally {
    if (refreshIcon) refreshIcon.classList.remove('spin');
  }
}

// Bind to window scope for backwards compatibility with HTML inline onclick handlers
window.switchTab = switchTab;
window.fetchDashboardOverview = fetchDashboardOverview;
window.showToast = showToast;
window.openLoginModal = openLoginModal;
window.closeLoginModal = closeLoginModal;
window.handleLogin = handleLogin;
window.handleLogout = handleLogout;
window.fetchProfiles = fetchProfiles;
window.promptDeleteProfile = promptDeleteProfile;
window.closeConfirmDeleteModal = closeConfirmDeleteModal;
window.executeProfileDelete = executeProfileDelete;

window.saveKindCard = saveKindCard;
window.onKindCardModified = onKindCardModified;
window.stepKindMinDelay = stepKindMinDelay;
window.openCreateKindModal = () => openCreateKindModal({ onOpenLoginModal: openLoginModal });
window.closeCreateKindModal = closeCreateKindModal;
window.handleCreateKind = (e) => handleCreateKind(e, { onFetchProfiles: fetchProfiles });
window.handleCreateAndConnectKind = (e) => handleCreateAndConnectKind(e, { onFetchProfiles: fetchProfiles });
window.connectYoutubeChannel = connectYoutubeChannel;
window.disconnectYoutubeChannel = (kindKey) => disconnectYoutubeChannel(kindKey, { onOpenLoginModal: openLoginModal, onFetchProfiles: fetchProfiles });

window.saveSubKindCard = saveSubKindCard;
window.saveSubkindCard = saveSubKindCard;
window.onSubKindCardModified = onSubKindCardModified;
window.onSubkindCardModified = onSubKindCardModified;
window.stepSubKindMinDelay = stepSubKindMinDelay;
window.stepSubKindDuration = stepSubKindDuration;
window.stepSubkindDuration = stepSubKindDuration;
window.openCreateSubKindModal = () => openCreateSubKindModal({ onOpenLoginModal: openLoginModal });
window.openCreateSubkindModal = () => openCreateSubKindModal({ onOpenLoginModal: openLoginModal });
window.closeCreateSubKindModal = closeCreateSubKindModal;
window.closeCreateSubkindModal = closeCreateSubKindModal;
window.handleCreateSubKind = (e) => handleCreateSubKind(e, { onFetchProfiles: fetchProfiles });
window.handleCreateSubkind = (e) => handleCreateSubKind(e, { onFetchProfiles: fetchProfiles });

window.loadSelectedBag = (bagFileName) => loadSelectedBag(bagFileName, { onOpenLoginModal: openLoginModal, onOverviewRefresh: fetchDashboardOverview });
window.openBagInExplorer = (bagFileName) => openBagInExplorer(bagFileName, { onSwitchTab: switchTab, onOpenLoginModal: openLoginModal });
window.openEditBagModal = () => openEditBagModal({ onOpenLoginModal: openLoginModal });
window.handleSaveRawJsonInline = () => handleSaveRawJsonInline({ onOverviewRefresh: fetchDashboardOverview });
window.formatRawJson = formatRawJson;
window.resetRawJson = resetRawJson;

// Global ESC key & Backdrop Click listeners to close active modals
document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape' || e.key === 'Esc') {
    closeLoginModal();
    closeCreateKindModal();
    closeCreateSubKindModal();
    closeConfirmDeleteModal();
    document.querySelectorAll('.modal-backdrop.active').forEach(modal => {
      modal.classList.remove('active');
    });
  }
});

document.addEventListener('click', (e) => {
  if (e.target && e.target.classList.contains('modal-backdrop') && e.target.classList.contains('active')) {
    if (e.target.id === 'loginModal') closeLoginModal();
    else if (e.target.id === 'createKindModal') closeCreateKindModal();
    else if (e.target.id === 'createSubKindModal') closeCreateSubKindModal();
    else if (e.target.id === 'confirmDeleteModal') closeConfirmDeleteModal();
    else e.target.classList.remove('active');
  }
});

// DOM Initialization
document.addEventListener('DOMContentLoaded', async () => {
  await checkAuthStatus();
  await fetchDashboardOverview();

  const urlParams = new URLSearchParams(window.location.search);
  const connectedKind = urlParams.get('connected');
  const connectedChannel = urlParams.get('channel');
  const youtubeError = urlParams.get('youtube_error');

  if (connectedKind) {
    switchTab('profiles');
    const channelMsg = connectedChannel ? ` ('${connectedChannel}')` : '';
    showToast(`Successfully connected YouTube channel${channelMsg} for '${connectedKind.toUpperCase()}'!`, 'success');
    window.history.replaceState({}, document.title, window.location.pathname + '#profiles');
  } else if (youtubeError) {
    switchTab('profiles');
    showToast(`YouTube connection error: ${decodeURIComponent(youtubeError)}`, 'error');
    window.history.replaceState({}, document.title, window.location.pathname + '#profiles');
  } else if (window.location.hash === '#profiles') {
    switchTab('profiles');
  }

  if (window.lucide) {
    lucide.createIcons();
  }
});

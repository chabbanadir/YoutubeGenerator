// Main Dashboard Entry Point & Navigation Orchestrator

function switchTab(tabId) {
  const targetTabBtn = document.getElementById(`tab-btn-${tabId}`);
  const targetTabView = document.getElementById(`tab-${tabId}`);

  if (!targetTabView) return;

  // Toggle active class on tab buttons
  document.querySelectorAll('.tab-btn').forEach(btn => {
    btn.classList.remove('active');
  });
  if (targetTabBtn) {
    targetTabBtn.classList.add('active');
  }

  // Toggle active class on tab views
  document.querySelectorAll('.tab-view').forEach(view => {
    view.classList.remove('active');
  });
  targetTabView.classList.add('active');

  // Trigger data fetching for target tab
  if (tabId === 'profiles' && typeof fetchProfilesData === 'function') {
    fetchProfilesData();
  } else if (tabId === 'bags' && typeof loadSelectedBag === 'function' && currentSelectedBag) {
    loadSelectedBag(currentSelectedBag);
  }
}

async function fetchDashboardOverview(showNotification = false) {
  const refreshIcon = document.getElementById('refreshIcon');
  if (refreshIcon) refreshIcon.classList.add('spin');

  try {
    const res = await fetch('/api/dashboard/overview');
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
    if (typeof renderBagsOverviewGrid === 'function') {
      renderBagsOverviewGrid(data.bags || []);
    }
    if (typeof renderBagsSidebarList === 'function') {
      renderBagsSidebarList(data.bags || []);
    }

    // Default select first bag if none active
    if (!currentSelectedBag && Array.isArray(data.bags) && data.bags.length > 0) {
      currentSelectedBag = data.bags[0].fileName;
      if (typeof loadSelectedBag === 'function') {
        loadSelectedBag(currentSelectedBag);
      }
    }

    if (showNotification && typeof showToast === 'function') {
      showToast('Dashboard data updated successfully', 'info');
    }

  } catch (err) {
    console.error('Failed to fetch dashboard overview:', err);
    if (typeof showToast === 'function') {
      showToast('Error refreshing dashboard data', 'error');
    }
  } finally {
    if (refreshIcon) refreshIcon.classList.remove('spin');
  }
}

// Profile Deletion Modal Logic
let pendingDeleteType = null;
let pendingDeleteFileName = null;

function promptDeleteProfile(type, fileName) {
  pendingDeleteType = type;
  pendingDeleteFileName = fileName;

  const displayEl = document.getElementById('deleteProfileFileNameDisplay');
  if (displayEl) {
    displayEl.textContent = fileName;
  }

  const modal = document.getElementById('confirmDeleteModal');
  if (modal) {
    modal.classList.add('active');
  }
}

function closeConfirmDeleteModal() {
  const modal = document.getElementById('confirmDeleteModal');
  if (modal) {
    modal.classList.remove('active');
  }
  pendingDeleteType = null;
  pendingDeleteFileName = null;
}

async function executeProfileDelete() {
  if (!pendingDeleteType || !pendingDeleteFileName) return;

  const confirmBtn = document.getElementById('confirmDeleteSubmitBtn');
  if (confirmBtn) confirmBtn.disabled = true;

  const type = pendingDeleteType;
  const fileName = pendingDeleteFileName;

  try {
    const endpoint = type === 'kind' 
      ? `/api/dashboard/profiles/kinds/${encodeURIComponent(fileName)}`
      : `/api/dashboard/profiles/subkinds/${encodeURIComponent(fileName)}`;

    const res = await fetchWithAuth(endpoint, {
      method: 'DELETE'
    });

    const data = await res.json();
    if (res.ok && data.success) {
      showToast(data.message || `Deleted ${type} profile '${fileName}' successfully.`, 'success');
      closeConfirmDeleteModal();
      if (typeof fetchProfilesData === 'function') {
        await fetchProfilesData();
      }
    } else {
      showToast(data.message || `Failed to delete ${type} profile.`, 'error');
    }
  } catch (err) {
    console.error('Delete profile error:', err);
    showToast('Failed to connect to server while deleting profile.', 'error');
  } finally {
    if (confirmBtn) confirmBtn.disabled = false;
  }
}

// Global ESC key & Backdrop Click listeners to close active modals
document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape' || e.key === 'Esc') {
    if (typeof closeLoginModal === 'function') closeLoginModal();
    if (typeof closeCreateKindModal === 'function') closeCreateKindModal();
    if (typeof closeCreateSubkindModal === 'function') closeCreateSubkindModal();
    if (typeof closeConfirmDeleteModal === 'function') closeConfirmDeleteModal();
    document.querySelectorAll('.modal-backdrop.active').forEach(modal => {
      modal.classList.remove('active');
    });
  }
});

document.addEventListener('click', (e) => {
  if (e.target && e.target.classList.contains('modal-backdrop') && e.target.classList.contains('active')) {
    if (typeof closeLoginModal === 'function' && e.target.id === 'loginModal') closeLoginModal();
    else if (typeof closeCreateKindModal === 'function' && e.target.id === 'createKindModal') closeCreateKindModal();
    else if (typeof closeCreateSubkindModal === 'function' && e.target.id === 'createSubkindModal') closeCreateSubkindModal();
    else if (typeof closeConfirmDeleteModal === 'function' && e.target.id === 'confirmDeleteModal') closeConfirmDeleteModal();
    else e.target.classList.remove('active');
  }
});

// DOM Initialization
document.addEventListener('DOMContentLoaded', async () => {
  // 1. Initialize Auth State
  if (typeof checkAuthStatus === 'function') {
    await checkAuthStatus();
  }

  // 2. Initial Data Load
  await fetchDashboardOverview();

  // 3. Handle YouTube OAuth Callback Redirect Parameters
  const urlParams = new URLSearchParams(window.location.search);
  const connectedKind = urlParams.get('connected');
  const connectedChannel = urlParams.get('channel');
  const youtubeError = urlParams.get('youtube_error');

  if (connectedKind) {
    if (typeof switchTab === 'function') switchTab('profiles');
    const channelMsg = connectedChannel ? ` ('${connectedChannel}')` : '';
    if (typeof showToast === 'function') {
      showToast(`Successfully connected YouTube channel${channelMsg} for '${connectedKind.toUpperCase()}'!`, 'success');
    }
    window.history.replaceState({}, document.title, window.location.pathname + '#profiles');
  } else if (youtubeError) {
    if (typeof switchTab === 'function') switchTab('profiles');
    if (typeof showToast === 'function') {
      showToast(`YouTube connection error: ${decodeURIComponent(youtubeError)}`, 'error');
    }
    window.history.replaceState({}, document.title, window.location.pathname + '#profiles');
  } else if (window.location.hash === '#profiles') {
    if (typeof switchTab === 'function') switchTab('profiles');
  }

  // 4. Initialize Icons
  if (window.lucide) {
    lucide.createIcons();
  }
});

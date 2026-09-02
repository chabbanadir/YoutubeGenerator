// Shared State and Common Utilities API

// Helper for authenticated & credentials-included fetch requests
async function fetchWithAuth(url, options = {}) {
  const defaultOptions = {
    credentials: 'include'
  };
  const mergedOptions = { ...defaultOptions, ...options };
  return fetch(url, mergedOptions);
}

// Global State
let currentOverviewData = null;
let currentSelectedBag = 'bag_download.json';
let currentBagData = [];
let currentRawJson = '';
let currentBagViewMode = 'cards'; // 'cards' | 'json'
let isAuthenticatedUser = false;
let isCurrentBagRedacted = false;
let profilesData = { kinds: [], subkinds: [] };

// Toast Notification Helper
function showToast(message, type = 'info') {
  const container = document.getElementById('toastContainer');
  if (!container) return;

  const toast = document.createElement('div');
  toast.className = `toast ${type}`;
  
  let iconName = 'info';
  if (type === 'success') iconName = 'check-circle';
  if (type === 'error') iconName = 'alert-triangle';
  if (type === 'warning') iconName = 'alert-circle';

  toast.innerHTML = `
    <i data-lucide="${iconName}"></i>
    <span>${message}</span>
  `;

  container.appendChild(toast);
  if (window.lucide) lucide.createIcons({ targets: [toast] });

  setTimeout(() => {
    toast.style.opacity = '0';
    toast.style.transform = 'translateX(100%)';
    toast.style.transition = 'all 0.3s ease';
    setTimeout(() => toast.remove(), 300);
  }, 3500);
}

// Copy to Clipboard Helper
function copyToClipboard(text, label = 'Content') {
  if (!text) return;
  navigator.clipboard.writeText(text).then(() => {
    showToast(`${label} copied to clipboard!`, 'success');
  }).catch(err => {
    console.error('Failed to copy:', err);
    showToast('Failed to copy to clipboard', 'error');
  });
}

// String Sanitization Helpers
function escapeHtml(str) {
  if (typeof str !== 'string') return String(str ?? '');
  return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

function escapeJsString(str) {
  if (str === null || str === undefined) return '';
  return String(str)
    .replace(/\\/g, '\\\\')
    .replace(/'/g, "\\'")
    .replace(/"/g, '&quot;')
    .replace(/\n/g, '\\n')
    .replace(/\r/g, '\\r');
}

// Formatting Helpers
function formatFileSize(bytes) {
  if (!bytes || bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
}

function formatDate(isoStr) {
  if (!isoStr) return 'N/A';
  const date = new Date(isoStr);
  return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' }) + ' (' + date.toLocaleDateString() + ')';
}

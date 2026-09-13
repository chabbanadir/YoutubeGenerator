// Core API & Utility Module

// Shared State Object for Reactive Sync Across Modules
export const state = {
  currentOverviewData: null,
  currentSelectedBag: 'bag_download.json',
  currentBagData: [],
  currentRawJson: '',
  currentBagViewMode: 'cards', // 'cards' | 'json'
  isAuthenticatedUser: false,
  isCurrentBagRedacted: false,
  profilesData: { kinds: [], subkinds: [] }
};

// Helper to resolve API URLs relative to current location path (supports subpath proxying e.g. /youtube/)
export function getApiUrl(url) {
  if (!url) return url;
  if (url.startsWith('http://') || url.startsWith('https://')) return url;
  const cleanPath = url.startsWith('/') ? url.slice(1) : url;

  let basePath = window.location.pathname;
  if (!basePath.endsWith('/')) {
    basePath = basePath.substring(0, basePath.lastIndexOf('/') + 1);
  }
  return window.location.origin + basePath + cleanPath;
}

// Helper for authenticated fetch requests
export async function fetchWithAuth(url, options = {}) {
  const defaultOptions = {
    credentials: 'include'
  };
  const mergedOptions = { ...defaultOptions, ...options };
  return fetch(getApiUrl(url), mergedOptions);
}

// Toast Notification Helper
export function showToast(message, type = 'info') {
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
    <span>${escapeHtml(message)}</span>
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
export function copyToClipboard(text, label = 'Content') {
  if (!text) return;
  navigator.clipboard.writeText(text).then(() => {
    showToast(`${label} copied to clipboard!`, 'success');
  }).catch(err => {
    console.error('Failed to copy:', err);
    showToast('Failed to copy to clipboard', 'error');
  });
}

// String Sanitization Helpers
export function escapeHtml(str) {
  if (typeof str !== 'string') return String(str ?? '');
  return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

export function escapeJsString(str) {
  if (str === null || str === undefined) return '';
  return String(str)
    .replace(/\\/g, '\\\\')
    .replace(/'/g, "\\'")
    .replace(/"/g, '&quot;')
    .replace(/\n/g, '\\n')
    .replace(/\r/g, '\\r');
}

// Formatting Helpers
export function formatFileSize(bytes) {
  if (!bytes || bytes === 0) return '0 B';
  const k = 1024;
  const sizes = ['B', 'KB', 'MB', 'GB'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
}

export function formatDate(isoStr) {
  if (!isoStr) return 'N/A';
  const date = new Date(isoStr);
  return date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' }) + ' (' + date.toLocaleDateString() + ')';
}

// Brand SVG Icons (Lucide removed brand icons like YouTube & Twitch)
export const BRAND_ICONS = {
  youtube: `<svg viewBox="0 0 24 24" width="100%" height="100%" fill="currentColor" style="display:inline-block; vertical-align:middle;"><path d="M23.498 6.186a3.016 3.016 0 0 0-2.122-2.136C19.505 3.545 12 3.545 12 3.545s-7.505 0-9.377.505A3.017 3.017 0 0 0 .502 6.186C0 8.07 0 12 0 12s0 3.93.502 5.814a3.016 3.016 0 0 0 2.122 2.136c1.871.505 9.376.505 9.376.505s7.505 0 9.377-.505a3.015 3.015 0 0 0 2.122-2.136C24 15.93 24 12 24 12s0-3.93-.502-5.814zM9.545 15.568V8.432L15.818 12l-6.273 3.568z"/></svg>`,
  twitch: `<svg viewBox="0 0 24 24" width="100%" height="100%" fill="currentColor" style="display:inline-block; vertical-align:middle;"><path d="M11.571 4.714h1.715v5.143H11.57zm4.715 0H18v5.143h-1.714zM6 0L1.714 4.286v15.428h5.143V24l4.286-4.286h3.428L22.286 12V0zm14.571 11.143l-3.428 3.428h-3.429l-3 3v-3H6.857V1.714h13.714Z"/></svg>`
};

export function getYoutubeIcon(size = 16, style = '') {
  return `<span class="brand-icon yt" style="display:inline-flex; align-items:center; justify-content:center; width:${size}px; height:${size}px; ${style}">${BRAND_ICONS.youtube}</span>`;
}

export function getTwitchIcon(size = 14, style = '') {
  return `<span class="brand-icon twitch" style="display:inline-flex; align-items:center; justify-content:center; width:${size}px; height:${size}px; ${style}">${BRAND_ICONS.twitch}</span>`;
}


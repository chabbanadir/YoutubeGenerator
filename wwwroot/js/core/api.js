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
  twitch: `<svg viewBox="0 0 24 24" width="100%" height="100%" fill="currentColor" style="display:inline-block; vertical-align:middle;"><path d="M11.571 4.714h1.715v5.143H11.57zm4.715 0H18v5.143h-1.714zM6 0L1.714 4.286v15.428h5.143V24l4.286-4.286h3.428L22.286 12V0zm14.571 11.143l-3.428 3.428h-3.429l-3 3v-3H6.857V1.714h13.714Z"/></svg>`,
  tiktok: `<svg viewBox="0 0 24 24" width="100%" height="100%" fill="currentColor" style="display:inline-block; vertical-align:middle;"><path d="M12.525.02c1.31-.02 2.61-.01 3.91-.02.08 1.53.63 3.09 1.75 4.17 1.12 1.11 2.7 1.62 4.24 1.79v4.03c-1.44-.05-2.89-.35-4.2-.97-.57-.26-1.1-.59-1.62-.93-.01 2.92.01 5.84-.02 8.75-.08 1.4-.54 2.79-1.35 3.94-1.31 1.92-3.58 3.17-5.91 3.21-1.43.08-2.86-.31-4.08-1.03-2.02-1.19-3.44-3.37-3.65-5.71-.02-.5-.03-1-.01-1.49.18-1.9 1.12-3.72 2.58-4.96 1.66-1.44 3.98-2.13 6.15-1.72.02 1.48-.04 2.96-.04 4.44-.99-.32-2.15-.23-3.02.37-.63.41-1.11 1.04-1.27 1.76-.23 1.03.04 2.17.71 2.97.77.92 1.98 1.34 3.16 1.16 1.06-.13 2.01-.79 2.51-1.73.34-.65.45-1.4.45-2.14V.02z"/></svg>`
};

export function getYoutubeIcon(size = 16, style = '') {
  return `<span class="brand-icon yt" style="display:inline-flex; align-items:center; justify-content:center; width:${size}px; height:${size}px; ${style}">${BRAND_ICONS.youtube}</span>`;
}

export function getTwitchIcon(size = 14, style = '') {
  return `<span class="brand-icon twitch" style="display:inline-flex; align-items:center; justify-content:center; width:${size}px; height:${size}px; ${style}">${BRAND_ICONS.twitch}</span>`;
}

export function getTiktokIcon(size = 15, style = '') {
  return `<span class="brand-icon tiktok" style="display:inline-flex; align-items:center; justify-content:center; width:${size}px; height:${size}px; ${style}">${BRAND_ICONS.tiktok}</span>`;
}


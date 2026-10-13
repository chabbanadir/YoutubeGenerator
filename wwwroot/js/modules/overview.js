// Overview Dashboard Module: KPIs, YouTube Hub, Video Showcase, Telemetry & Activity Stream
import { state, escapeHtml, escapeJsString, formatDate, formatFileSize, getYoutubeIcon, getTwitchIcon, getTiktokIcon } from '../core/api.js';

export function renderOverview(data) {
  if (!data) return;

  renderKpiCards(data.kpis, data.channels);
  renderYoutubeAccountsHub(data.channels);
  renderPublicationQueue(data.publicationQueue);
  renderRecentPublishedShowcase(data.recentPublished);
  renderWorkerTelemetry(data.workers);
  renderRecentActivity(data.recentActivity);
  renderStorageSnapshot(data.bags);

  if (window.lucide) {
    lucide.createIcons();
  }
}

// 1. Executive KPI Cards
export function renderKpiCards(kpis = {}, channels = []) {
  const container = document.getElementById('overviewKpiGrid');
  if (!container) return;

  const connectedChannels = kpis.connectedChannelsCount ?? 0;
  const totalChannels = kpis.totalChannelsCount ?? channels.length ?? 0;
  const publishedCount = kpis.totalPublishedVideos ?? 0;
  const totalDuration = kpis.totalDurationFormatted || '0m';
  const harvestedCount = kpis.totalClipsHarvested ?? 0;
  const activeQueue = kpis.activeQueueItems ?? 0;
  const successRate = kpis.successRatePercent ?? 100;
  const failedCount = kpis.failedItemsCount ?? 0;

  container.innerHTML = `
    <!-- KPI 1: Linked YouTube Channels -->
    <div class="kpi-card" ${state.isAuthenticatedUser ? 'onclick="window.switchTab(\'profiles\')" style="cursor: pointer;" title="Click to manage channel profiles"' : 'title="Connected YouTube Channels"'}>
      <div class="kpi-card-glow youtube"></div>
      <div class="kpi-header">
        <span class="kpi-title">YouTube Accounts</span>
        <div class="kpi-icon-wrap youtube">
          ${getYoutubeIcon(20)}
        </div>
      </div>
      <div class="kpi-body">
        <span class="kpi-val">${connectedChannels}</span>
        <span class="kpi-unit">/ ${totalChannels} Connected</span>
      </div>
      <div class="kpi-footer">
        <span class="kpi-badge ${connectedChannels > 0 ? 'success' : 'warning'}">
          <i data-lucide="${connectedChannels > 0 ? 'check-circle' : 'alert-circle'}" style="width: 12px; height: 12px;"></i>
          ${connectedChannels > 0 ? 'OAuth Active' : 'No Channel Linked'}
        </span>
        ${state.isAuthenticatedUser ? `
          <span style="font-size: 0.72rem; color: var(--accent-cyan);">Manage Profiles &rarr;</span>
        ` : `
          <span style="font-size: 0.72rem; color: var(--text-muted);">Channels Monitored</span>
        `}
      </div>
    </div>

    <!-- KPI 2: Published YouTube Content -->
    <div class="kpi-card" onclick="window.openBagInExplorer('bag_youtube_done.json')" style="cursor: pointer;" title="Click to view published packages">
      <div class="kpi-card-glow published"></div>
      <div class="kpi-header">
        <span class="kpi-title">Published Videos</span>
        <div class="kpi-icon-wrap published">
          <i data-lucide="play-square" style="width: 20px; height: 20px;"></i>
        </div>
      </div>
      <div class="kpi-body">
        <span class="kpi-val">${publishedCount}</span>
        <span class="kpi-unit">Live Videos</span>
      </div>
      <div class="kpi-footer">
        <span class="kpi-badge info">
          <i data-lucide="clock" style="width: 12px; height: 12px;"></i> ${escapeHtml(totalDuration)} runtime
        </span>
        <span style="font-size: 0.72rem; color: var(--accent-emerald);">View Bag &rarr;</span>
      </div>
    </div>

    <!-- KPI 3: Ingested Twitch Highlights -->
    <div class="kpi-card" onclick="window.openBagInExplorer('bag_history.json')" style="cursor: pointer;" title="Click to view harvest history">
      <div class="kpi-card-glow clips"></div>
      <div class="kpi-header">
        <span class="kpi-title">Clips Harvested</span>
        <div class="kpi-icon-wrap clips">
          <i data-lucide="scissors" style="width: 20px; height: 20px;"></i>
        </div>
      </div>
      <div class="kpi-body">
        <span class="kpi-val">${harvestedCount}</span>
        <span class="kpi-unit">Source Clips</span>
      </div>
      <div class="kpi-footer">
        <span class="kpi-badge info">
          <i data-lucide="layers" style="width: 12px; height: 12px;"></i> ${activeQueue} in pipeline queue
        </span>
        <span style="font-size: 0.72rem; color: var(--accent-indigo);">History Bag &rarr;</span>
      </div>
    </div>

    <!-- KPI 4: Pipeline Health & Reliability -->
    <div class="kpi-card" onclick="window.openBagInExplorer('bag_failed.json')" style="cursor: pointer;" title="Click to inspect failed items">
      <div class="kpi-card-glow health"></div>
      <div class="kpi-header">
        <span class="kpi-title">Pipeline Health</span>
        <div class="kpi-icon-wrap health">
          <i data-lucide="shield-check" style="width: 20px; height: 20px;"></i>
        </div>
      </div>
      <div class="kpi-body">
        <span class="kpi-val">${successRate}%</span>
        <span class="kpi-unit">Reliability</span>
      </div>
      <div class="kpi-footer">
        <span class="kpi-badge ${failedCount === 0 ? 'success' : 'warning'}">
          <i data-lucide="${failedCount === 0 ? 'check' : 'alert-triangle'}" style="width: 12px; height: 12px;"></i>
          ${failedCount} pipeline failure(s)
        </span>
        <span style="font-size: 0.72rem; color: ${failedCount === 0 ? 'var(--text-muted)' : 'var(--accent-rose)'};">
          ${failedCount === 0 ? 'Optimal' : 'Check Failures'}
        </span>
      </div>
    </div>
  `;
}

// 2. YouTube Accounts Integration Hub
export function renderYoutubeAccountsHub(channels = []) {
  const container = document.getElementById('youtubeChannelsGrid');
  if (!container) return;

  if (!channels || channels.length === 0) {
    container.innerHTML = `
      <div class="text-muted" style="grid-column: 1/-1; padding: 1.5rem; text-align: center; background: rgba(15,23,42,0.4); border-radius: 8px;">
        <i data-lucide="alert-circle" style="width: 24px; height: 24px; color: var(--accent-amber); margin-bottom: 0.5rem;"></i>
        <div>No YouTube channel profiles configured yet.</div>
        ${state.isAuthenticatedUser ? `
          <button class="btn btn-primary btn-sm" onclick="window.switchTab('profiles')" style="margin-top: 0.75rem;">
            <i data-lucide="plus" style="width: 14px; height: 14px;"></i> Configure Channel Profile
          </button>
        ` : ''}
      </div>
    `;
    return;
  }

  container.innerHTML = channels.map(ch => {
    const isConnected = ch.isConnected === true;
    const kindKey = (ch.kindKey || ch.name || '').toLowerCase();
    const kindTitle = (ch.name || ch.kindKey || 'Unknown').toUpperCase();
    const channelId = ch.channelId || '';
    const safeChannelId = escapeJsString(channelId);
    const escapedChannelId = escapeHtml(channelId);
    const fallbackTitle = `${kindTitle} Channel`;
    const channelTitle = ch.channelTitle || (channelId ? fallbackTitle : 'Not Configured');
    const minDelay = ch.minDelayHours || 6;
    const uploadCounter = ch.uploadCounter ?? 0;
    const isAutoUpload = ch.enabled === true;
    const lastUploadFormatted = ch.lastUploadTime ? formatDate(ch.lastUploadTime) : 'None Recorded';

    const badgeTheme = kindKey === 'lol' ? 'lol' : (kindKey === 'valorant' ? 'valorant' : 'default');
    const rawTikTokName = ch.tikTokDisplayName || 'TikTok Linked';
    const tikTokDisplay = rawTikTokName.length > 12 ? rawTikTokName.substring(0, 11) + '…' : rawTikTokName;

    return `
      <div class="yt-channel-card">
        <div class="yt-channel-header">
          <div class="yt-channel-identity">
            <div class="yt-avatar-icon">
              ${getYoutubeIcon(20)}
            </div>
            <div class="yt-channel-meta">
              <div class="yt-channel-title">
                <span class="profile-kind-badge ${badgeTheme}" style="font-size: 0.68rem; padding: 0.1rem 0.45rem;">
                  ${escapeHtml(kindTitle)}
                </span>
                <span class="yt-channel-title-text" title="${escapeHtml(channelTitle)}">${escapeHtml(channelTitle)}</span>
              </div>
              <div class="yt-channel-id-row">
                <span>ID:</span>
                <code>${escapedChannelId || 'No Channel ID'}</code>
                ${channelId ? `
                  <button type="button" class="icon-btn-small" onclick="window.copyToClipboard('${safeChannelId}', 'Channel ID')" title="Copy Channel ID">
                    <i data-lucide="copy" style="width: 11px; height: 11px;"></i>
                  </button>
                ` : ''}
              </div>
            </div>
          </div>
          <div class="yt-channel-pills-col">
            <span class="yt-channel-status-pill ${isConnected ? 'connected' : 'disconnected'}" title="YouTube OAuth Status">
              <i data-lucide="${isConnected ? 'check-circle-2' : 'alert-circle'}" style="width: 12px; height: 12px;"></i>
              ${isConnected ? 'YouTube' : 'YouTube Off'}
            </span>
            <span class="yt-channel-status-pill ${ch.isTikTokConnected ? 'connected' : 'disconnected'}" style="${ch.isTikTokConnected ? 'border-color: rgba(34, 211, 238, 0.4); color: #22d3ee;' : ''}" title="TikTok Integration: ${escapeHtml(rawTikTokName)}">
              ${getTiktokIcon(12, 'margin-right: 3px;')}
              ${ch.isTikTokConnected ? escapeHtml(tikTokDisplay) : 'TikTok Off'}
            </span>
          </div>
        </div>

        <div class="yt-stats-row">
          <div class="yt-stat-item">
            <span class="yt-stat-label">Upload Interval</span>
            <span class="yt-stat-value">${minDelay}h delay</span>
          </div>
          <div class="yt-stat-item">
            <span class="yt-stat-label">Total Uploads</span>
            <span class="yt-stat-value">${uploadCounter} vids</span>
          </div>
          <div class="yt-stat-item">
            <span class="yt-stat-label">Automation</span>
            <span class="yt-stat-value" style="color: ${isAutoUpload ? '#34d399' : 'var(--text-muted)'};">
              ${isAutoUpload ? 'Enabled' : 'Paused'}
            </span>
          </div>
        </div>

        <div class="yt-actions-row">
          <span style="font-size: 0.72rem; color: var(--text-muted); display: flex; align-items: center; gap: 0.25rem;">
            <i data-lucide="calendar" style="width: 12px; height: 12px;"></i> Last: <strong>${lastUploadFormatted}</strong>
          </span>

          <div style="display: flex; gap: 0.4rem;">
            ${ch.youtubeUrl ? `
              <a href="${escapeHtml(ch.youtubeUrl)}" target="_blank" rel="noopener" class="btn btn-secondary btn-sm" style="text-decoration: none;" title="Open YouTube Channel">
                <i data-lucide="external-link" style="width: 12px; height: 12px;"></i>
                <span class="desktop-only-inline">View Channel</span>
              </a>
            ` : ''}
            ${state.isAuthenticatedUser ? `
              <button class="btn btn-secondary btn-sm" onclick="window.switchTab('profiles')" title="Edit Profile & OAuth Settings">
                <i data-lucide="settings" style="width: 12px; height: 12px;"></i>
                <span class="desktop-only-inline">Configure</span>
              </button>
            ` : ''}
          </div>
        </div>
      </div>
    `;
  }).join('');
}

// 2.5 Scheduled Publication Queue & Release Radar
let currentQueueData = null;
let currentQueueFilter = 'all';
let isQueueExpanded = false;

export function toggleQueueExpand() {
  isQueueExpanded = !isQueueExpanded;
  renderPublicationQueueItems();
}
window.toggleQueueExpand = toggleQueueExpand;

export function filterPublicationQueue(filter) {
  currentQueueFilter = filter;
  ['all', 'short', 'montage', 'published'].forEach(f => {
    const btn = document.getElementById(`filterQueue${f.charAt(0).toUpperCase() + f.slice(1)}`);
    if (btn) {
      if (f === filter) btn.classList.add('active');
      else btn.classList.remove('active');
    }
  });
  renderPublicationQueueItems();
}
window.filterPublicationQueue = filterPublicationQueue;

export function renderPublicationQueue(data) {
  currentQueueData = data || { totalCount: 0, items: [], recentPublished: [] };
  const allRawItems = currentQueueData.items || [];
  const upcoming = allRawItems.filter(x => !x.isUploadedYt).sort((a, b) => {
    const ta = a.scheduledPublishTimeUtc ? new Date(a.scheduledPublishTimeUtc).getTime() : 0;
    const tb = b.scheduledPublishTimeUtc ? new Date(b.scheduledPublishTimeUtc).getTime() : 0;
    return ta - tb;
  });
  const published = [...allRawItems.filter(x => x.isUploadedYt), ...(currentQueueData.recentPublished || [])];

  const countAll = upcoming.length;
  const countShorts = upcoming.filter(x => x.type === 'short').length;
  const countMontages = upcoming.filter(x => x.type === 'montage').length;
  const countPublished = published.length;

  const elAll = document.getElementById('queueCountAll');
  if (elAll) elAll.textContent = countAll;
  const elShorts = document.getElementById('queueCountShorts');
  if (elShorts) elShorts.textContent = countShorts;
  const elMontages = document.getElementById('queueCountMontages');
  if (elMontages) elMontages.textContent = countMontages;
  const elPub = document.getElementById('queueCountPublished');
  if (elPub) elPub.textContent = countPublished;

  const nextPillText = document.getElementById('queueNextText');
  if (nextPillText) {
    const firstUpcoming = upcoming[0];
    if (firstUpcoming && firstUpcoming.scheduledPublishTimeUtc) {
      const nextDate = new Date(firstUpcoming.scheduledPublishTimeUtc);
      const timeStr = nextDate.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
      nextPillText.textContent = `Next Up: ${timeStr} (${firstUpcoming.countdown || 'Upcoming'})`;
    } else if (countAll > 0) {
      nextPillText.textContent = `${countAll} ready in queue`;
    } else {
      nextPillText.textContent = 'Queue is idle';
    }
  }

  renderPublicationQueueItems();
}

function renderPublicationQueueItems() {
  const container = document.getElementById('publicationQueueContainer');
  if (!container) return;

  if (!currentQueueData) {
    container.innerHTML = `
      <div class="queue-empty-box">
        <i data-lucide="clock" style="width: 24px; height: 24px; color: var(--text-muted);"></i>
        <div>No scheduled publication items found.</div>
      </div>
    `;
    if (window.lucide) lucide.createIcons();
    return;
  }

  const allRawItems = currentQueueData.items || [];
  const upcoming = allRawItems.filter(x => !x.isUploadedYt).sort((a, b) => {
    const ta = a.scheduledPublishTimeUtc ? new Date(a.scheduledPublishTimeUtc).getTime() : 0;
    const tb = b.scheduledPublishTimeUtc ? new Date(b.scheduledPublishTimeUtc).getTime() : 0;
    return ta - tb;
  });

  let allItems = [];
  if (currentQueueFilter === 'all') {
    allItems = upcoming;
  } else if (currentQueueFilter === 'short') {
    allItems = upcoming.filter(x => x.type === 'short');
  } else if (currentQueueFilter === 'montage') {
    allItems = upcoming.filter(x => x.type === 'montage');
  } else if (currentQueueFilter === 'published') {
    const map = new Map();
    [...allRawItems.filter(x => x.isUploadedYt), ...(currentQueueData.recentPublished || [])].forEach(x => {
      if (x.id) map.set(x.id, x);
    });
    allItems = Array.from(map.values());
  }

  const expandBtn = document.getElementById('btnToggleQueueExpand');
  const expandBtnText = document.getElementById('queueExpandBtnText');
  if (expandBtn && expandBtnText) {
    if (allItems.length > 6) {
      expandBtn.style.display = 'inline-flex';
      expandBtnText.textContent = isQueueExpanded ? 'Show Next 6' : `Show All (${allItems.length})`;
      const icon = expandBtn.querySelector('i');
      if (icon) {
        icon.setAttribute('data-lucide', isQueueExpanded ? 'chevrons-up' : 'chevrons-down');
      }
    } else {
      expandBtn.style.display = 'none';
    }
  }

  if (allItems.length === 0) {
    container.innerHTML = `
      <div class="queue-empty-box">
        <i data-lucide="inbox" style="width: 28px; height: 28px; color: var(--accent-cyan); opacity: 0.6;"></i>
        <div style="font-weight: 600; color: var(--text-main);">No items matching this filter</div>
        <div style="font-size: 0.78rem;">${currentQueueFilter === 'published' ? 'No recent published items recorded yet.' : 'Pipeline queue is clear. New clips or montages will appear here once queued.'}</div>
      </div>
    `;
    if (window.lucide) lucide.createIcons();
    return;
  }

  const items = isQueueExpanded ? allItems : allItems.slice(0, 6);

  const rowsHtml = items.map((item, index) => {
    const isShort = item.type === 'short';
    const kindKey = (item.kindKey || 'lol').toLowerCase();
    const streamer = item.streamer ? escapeHtml(item.streamer) : 'Streamer';
    const rawTitle = item.title || 'Untitled Video';
    // Clean display title: strip redundant trailing hashtags like #Shorts #LoL #VALORANT to avoid cut-off ellipses
    const displayTitle = rawTitle.replace(/\s*#\w+(\s*#\w+)*\s*$/g, '').trim() || rawTitle;
    const title = escapeHtml(displayTitle);
    const fullTitle = escapeHtml(rawTitle);
    const safeTitle = escapeJsString(rawTitle);
    const duration = item.durationFormatted || '--';
    const slotNum = String(index + 1).padStart(2, '0');
    const isNext = index === 0 && currentQueueFilter !== 'published';

    // Format Scheduled Time and Status Pill
    let timePrimaryHtml = '--:--';
    let countdownBadgeHtml = '';

    const isHeld = item.status === 'held' || (item.countdown && item.countdown.toLowerCase().includes('held'));

    if (item.scheduledPublishTimeUtc) {
      const pubDate = new Date(item.scheduledPublishTimeUtc);
      const isToday = new Date().toDateString() === pubDate.toDateString();
      const timeStr = pubDate.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
      const dateTagHtml = isToday
        ? `<span class="queue-date-tag is-today">Today</span>`
        : `<span class="queue-date-tag is-future">${pubDate.toLocaleDateString([], { month: 'short', day: 'numeric' })}</span>`;

      const diffMs = pubDate.getTime() - Date.now();
      const diffHours = diffMs / (1000 * 60 * 60);

      let badgeClass = 'future';
      let iconName = 'clock';
      let labelText = item.countdown || 'Scheduled';
      let tooltipText = '';

      if (isHeld) {
        badgeClass = 'held';
        iconName = 'pause-circle';
        labelText = 'Held (Quota Cooldown)';
        tooltipText = item.quotaReason || 'Upload delayed: Daily Shorts quota limit reached or pacing delay active';
      } else if (item.status === 'published' || currentQueueFilter === 'published') {
        badgeClass = 'published';
        iconName = 'check-circle-2';
        labelText = 'Published';
      } else if (item.status === 'ready' || diffHours <= 0) {
        badgeClass = 'ready';
        iconName = 'check-circle';
        labelText = 'Ready to Upload';
      } else if (diffHours > 0 && diffHours <= 1) {
        badgeClass = 'imminent';
        iconName = 'zap';
        labelText = item.countdown;
      } else if (isToday) {
        badgeClass = 'today';
        labelText = item.countdown;
      }

      timePrimaryHtml = `${timeStr} ${dateTagHtml}`;
      countdownBadgeHtml = `
        <span class="queue-countdown-pill ${badgeClass}" ${tooltipText ? `title="${escapeHtml(tooltipText)}"` : ''}>
          <i data-lucide="${iconName}" style="width: 10px; height: 10px;"></i>
          ${escapeHtml(labelText)}
        </span>
      `;
    } else {
      timePrimaryHtml = `Scheduled`;
      countdownBadgeHtml = `
        <span class="queue-countdown-pill ready">
          <i data-lucide="play-circle" style="width: 10px; height: 10px;"></i>
          ${escapeHtml(item.countdown || 'Queued')}
        </span>
      `;
    }

    // Game tag + subtle format tag
    const gameBadgeClass = kindKey === 'lol' ? 'lol' : (kindKey === 'valorant' ? 'valorant' : 'default');
    const formatTagHtml = `
      <span class="queue-format-badge ${isShort ? 'short' : 'montage'}">
        <i data-lucide="${isShort ? 'smartphone' : 'film'}" style="width: 9px; height: 9px;"></i>
        ${isShort ? 'Short' : 'Montage'}
      </span>
    `;

    // Only display subKindKey if it is meaningful (skip generic "all", "normal", "default")
    const subKindClean = (item.subKindKey || '').trim().toLowerCase();
    const isSpecialSubKind = subKindClean && !['all', 'normal', 'default'].includes(subKindClean);

    // Platform status micro-chips
    const ytChip = item.isUploadedYt
      ? `<span class="platform-chip live" title="YouTube: Uploaded & Live"><i data-lucide="check" style="width: 10px; height: 10px;"></i> YT Live</span>`
      : `<span class="platform-chip queued" title="YouTube: In Schedule Pipeline"><i data-lucide="clock" style="width: 10px; height: 10px;"></i> YT Queued</span>`;

    const tikTokChip = item.isUploadedTikTok
      ? `<span class="platform-chip live" title="TikTok: Published"><i data-lucide="check" style="width: 10px; height: 10px;"></i> TT Live</span>`
      : isShort
        ? `<span class="platform-chip queued" title="TikTok: In upload queue">${getTiktokIcon(10, 'margin-right: 2px;')} TT Queued</span>`
        : `<span class="platform-chip off" title="TikTok: Not applicable">—</span>`;

    return `
      <tr class="${isNext ? 'is-next-slot' : ''}">
        <td style="text-align: center;">
          <span class="queue-slot-badge ${isNext ? 'next' : ''}">
            #${slotNum}
          </span>
        </td>
        <td>
          <div class="queue-time-cell">
            <div class="queue-time-primary">${timePrimaryHtml}</div>
            ${countdownBadgeHtml}
          </div>
        </td>
        <td>
          <div class="queue-badges-cell">
            <span class="queue-game-badge ${gameBadgeClass}">
              ${escapeHtml(kindKey.toUpperCase())}
            </span>
            ${formatTagHtml}
          </div>
        </td>
        <td>
          <div class="queue-title-cell">
            <div class="queue-title-text" title="${fullTitle}">${title}</div>
            <div class="queue-title-sub">
              <span class="queue-streamer-sub">${getTwitchIcon(10, 'margin-right: 2px;')} @${streamer}</span>
              <span class="queue-duration-sub"><i data-lucide="clock" style="width: 10px; height: 10px;"></i> ${escapeHtml(duration)}</span>
              ${isSpecialSubKind ? `<span class="queue-subkind-tag"><i data-lucide="tag" style="width: 9px; height: 9px;"></i> ${escapeHtml(item.subKindKey)}</span>` : ''}
            </div>
          </div>
        </td>
        <td>
          <div class="queue-platforms-cell">
            ${ytChip}
            ${tikTokChip}
          </div>
        </td>
        <td style="text-align: right;">
          <div class="queue-actions-cell">
            ${item.youtubeUrl ? `
              <a href="${escapeHtml(item.youtubeUrl)}" target="_blank" rel="noopener" class="btn btn-secondary btn-sm" style="padding: 0.2rem 0.45rem; font-size: 0.68rem; text-decoration: none;" title="Watch Video">
                <i data-lucide="external-link" style="width: 11px; height: 11px;"></i>
              </a>
            ` : `
              <button type="button" class="icon-btn-small" onclick="window.copyToClipboard('${safeTitle}', 'Video Title')" title="Copy Title">
                <i data-lucide="copy" style="width: 11px; height: 11px;"></i>
              </button>
            `}
          </div>
        </td>
      </tr>
    `;
  }).join('');

  container.innerHTML = `
    <div class="queue-table-wrapper">
      <table class="queue-table">
        <thead>
          <tr>
            <th style="width: 50px; text-align: center;">SLOT</th>
            <th style="width: 175px;">RELEASE TIME</th>
            <th style="width: 135px;">GAME / TYPE</th>
            <th>VIDEO TITLE & STREAMER</th>
            <th style="width: 175px;">PLATFORMS</th>
            <th style="width: 65px; text-align: right;">ACTION</th>
          </tr>
        </thead>
        <tbody>
          ${rowsHtml}
        </tbody>
      </table>
    </div>
    <div class="queue-table-footer">
      <span>Showing <strong>${items.length}</strong> of <strong>${allItems.length}</strong> queued items</span>
      ${allItems.length > 6 ? `
        <button class="btn btn-secondary btn-sm" onclick="window.toggleQueueExpand()" style="font-size: 0.72rem; padding: 0.2rem 0.55rem;">
          <i data-lucide="${isQueueExpanded ? 'chevrons-up' : 'chevrons-down'}" style="width: 11px; height: 11px;"></i>
          ${isQueueExpanded ? 'Show Next 6 Only' : `Show All ${allItems.length} Items`}
        </button>
      ` : ''}
    </div>
  `;

  if (window.lucide) {
    lucide.createIcons();
  }
}

// 3. Recent Published Videos Showcase
export function renderRecentPublishedShowcase(videos = []) {
  const container = document.getElementById('recentPublishedGrid');
  if (!container) return;

  if (!videos || videos.length === 0) {
    container.innerHTML = `
      <div class="text-muted" style="grid-column: 1/-1; padding: 2rem; text-align: center; background: rgba(15,23,42,0.4); border-radius: 8px;">
        <i data-lucide="film" style="width: 28px; height: 28px; color: var(--text-muted); margin-bottom: 0.5rem;"></i>
        <div style="font-weight: 600; color: var(--text-main);">No published videos yet</div>
        <div style="font-size: 0.8rem; margin-top: 0.25rem;">Videos published to YouTube will appear here with live playback links and thumbnails.</div>
      </div>
    `;
    return;
  }

  container.innerHTML = videos.map(vid => {
    const title = vid.generatedTitle || `Montage (${vid.kindKey || 'Video'})`;
    const duration = vid.totalDuration || '0:00';
    const kindKey = (vid.kindKey || 'default').toLowerCase();
    const clipCount = vid.clipCount || 0;
    const streamers = Array.isArray(vid.streamers) ? vid.streamers : [];
    const hasYtUrl = !!vid.youtubeUrl;
    const thumbUrl = vid.thumbnailUrl;

    return `
      <div class="video-card">
        <div class="video-thumb-container">
          ${thumbUrl ? `
            <img src="${escapeHtml(thumbUrl)}" class="video-thumb-img" alt="${escapeHtml(title)}" loading="lazy" onerror="this.style.display='none'; this.nextElementSibling.style.display='flex';" />
            <div class="video-thumb-fallback" style="display: none;">
              <i data-lucide="video" style="width: 32px; height: 32px; color: var(--accent-cyan);"></i>
              <span>Preview Unavailable</span>
            </div>
          ` : `
            <div class="video-thumb-fallback">
              <i data-lucide="video" style="width: 32px; height: 32px; color: var(--accent-cyan);"></i>
              <span>YouTube Video</span>
            </div>
          `}
          <span class="video-kind-tag ${kindKey}">${escapeHtml(kindKey)}</span>
          <span class="video-duration-badge">
            <i data-lucide="clock" style="width: 10px; height: 10px;"></i>
            ${escapeHtml(duration)}
          </span>
        </div>

        <div class="video-info-body">
          <div class="video-title" title="${escapeHtml(title)}">
            ${escapeHtml(title)}
          </div>

          <div class="video-streamers-row">
            <span class="tag-chip subkind" style="font-size: 0.65rem;">
              <i data-lucide="layers" style="width: 10px; height: 10px;"></i> ${clipCount} Clips
            </span>
            ${streamers.map(s => `
              <span class="streamer-chip-small">
                ${getTwitchIcon(10, 'margin-right: 2px;')} ${escapeHtml(s)}
              </span>
            `).join('')}
          </div>

          <div class="video-card-footer">
            <button class="btn btn-secondary btn-sm" onclick="window.openBagInExplorer('bag_youtube_done.json')" title="Inspect package details in Bag Inspector">
              <i data-lucide="folder-kanban" style="width: 12px; height: 12px;"></i> Inspect
            </button>

            ${hasYtUrl ? `
              <a href="${escapeHtml(vid.youtubeUrl)}" target="_blank" rel="noopener" class="btn-yt-watch" title="Watch Video on YouTube">
                ${getYoutubeIcon(14, 'margin-right: 4px;')} Watch Video
              </a>
            ` : `
              <span class="badge" style="background: rgba(255,255,255,0.08); font-size: 0.72rem; color: var(--text-muted);">
                ID: ${escapeHtml((vid.montageId || '').substring(0, 10))}
              </span>
            `}
          </div>
        </div>
      </div>
    `;
  }).join('');
}

// 4. Worker Telemetry & Live Activity Stream
export function renderWorkerTelemetry(workers = {}) {
  const container = document.getElementById('workerTelemetryBoxes');
  if (!container) return;

  const collect = workers.collect || {};
  const montage = workers.montage || {};
  const isCollectActive = workers.collectWorkerActive || false;
  const isMontageActive = workers.montageWorkerActive || false;

  container.innerHTML = `
    <!-- Worker 1: Harvest & Trim -->
    <div class="worker-box">
      <div class="worker-box-header">
        <div class="worker-identity">
          <div class="worker-avatar">
            <i data-lucide="download-cloud" style="width: 18px; height: 18px;"></i>
          </div>
          <div>
            <div class="worker-name">Worker 1: Stream Harvester & Trimmer</div>
            <div class="worker-filename">worker_collect.json</div>
          </div>
        </div>
        <span class="status-dot ${isCollectActive ? 'online' : 'offline'}" style="width: 10px; height: 10px;" title="${isCollectActive ? 'Active' : 'Idle'}"></span>
      </div>
      <div class="worker-metrics-grid">
        <div class="worker-metric-box">
          <span class="worker-metric-label">Downloads Processed</span>
          <span class="worker-metric-val">${collect.downloadCounter ?? 0}</span>
        </div>
        <div class="worker-metric-box">
          <span class="worker-metric-label">Trimmed Highlights</span>
          <span class="worker-metric-val">${collect.trimCounter ?? 0}</span>
        </div>
      </div>
    </div>

    <!-- Worker 2: Montage & LLM Compilation -->
    <div class="worker-box">
      <div class="worker-box-header">
        <div class="worker-identity">
          <div class="worker-avatar" style="color: var(--accent-amber);">
            <i data-lucide="film" style="width: 18px; height: 18px;"></i>
          </div>
          <div>
            <div class="worker-name">Worker 2: Montage Compiler & Uploader</div>
            <div class="worker-filename">worker_montage.json</div>
          </div>
        </div>
        <span class="status-dot ${isMontageActive ? 'online' : 'offline'}" style="width: 10px; height: 10px;" title="${isMontageActive ? 'Active' : 'Idle'}"></span>
      </div>
      <div class="worker-metrics-grid">
        <div class="worker-metric-box">
          <span class="worker-metric-label">Montages Assembled</span>
          <span class="worker-metric-val">${montage.finalCounter ?? 0}</span>
        </div>
        <div class="worker-metric-box">
          <span class="worker-metric-label">Compiler Status</span>
          <span class="worker-metric-val" style="font-size: 0.92rem; color: ${isMontageActive ? '#34d399' : 'var(--text-muted)'};">
            ${isMontageActive ? 'Compiling' : 'Standing By'}
          </span>
        </div>
      </div>
    </div>
  `;
}

// 5. Recent Clip Activity Stream
export function renderRecentActivity(activity = []) {
  const container = document.getElementById('recentActivityList');
  if (!container) return;

  if (!activity || activity.length === 0) {
    container.innerHTML = `
      <div class="text-muted" style="padding: 1.5rem; text-align: center;">
        No recent clip harvest events recorded.
      </div>
    `;
    return;
  }

  container.innerHTML = activity.map(act => {
    const streamer = act.streamer || 'Twitch Streamer';
    const kindKey = (act.kindKey || 'lol').toLowerCase();
    const subKind = act.subKindKey || '';
    const dateStr = act.importedAt ? formatDate(act.importedAt) : '';
    const link = act.link || '';
    const duration = (act.start && act.end) ? `${act.start} - ${act.end}` : '';

    return `
      <div class="activity-item">
        <div class="activity-item-left">
          <div class="activity-icon">
            ${getTwitchIcon(14)}
          </div>
          <div class="activity-details">
            <span class="activity-streamer">${escapeHtml(streamer)}</span>
            <div class="activity-tags">
              <span class="tag-chip game" style="font-size: 0.65rem; padding: 0.1rem 0.35rem;">${escapeHtml(kindKey)}</span>
              ${subKind ? `<span class="tag-chip subkind" style="font-size: 0.65rem; padding: 0.1rem 0.35rem;">${escapeHtml(subKind)}</span>` : ''}
              ${duration ? `<span>&bull; ${escapeHtml(duration)}</span>` : ''}
            </div>
          </div>
        </div>
        <div class="activity-item-right">
          ${dateStr ? `<span style="font-size: 0.7rem; color: var(--text-muted);">${dateStr}</span>` : ''}
          ${link ? `
            <a href="${escapeHtml(link)}" target="_blank" rel="noopener" class="icon-btn-small" title="Watch Twitch Clip">
              <i data-lucide="external-link" style="width: 12px; height: 12px;"></i>
            </a>
          ` : ''}
        </div>
      </div>
    `;
  }).join('');
}

// 6. Compact Storage Snapshot Bar
export function renderStorageSnapshot(bags = []) {
  const container = document.getElementById('storageSnapshotContainer');
  if (!container) return;

  const totalBags = bags.length;
  const totalBytes = bags.reduce((acc, b) => acc + (b.sizeBytes || 0), 0);
  const totalItems = bags.reduce((acc, b) => acc + (b.itemCount || 0), 0);

  container.innerHTML = `
    <div class="storage-snapshot-card">
      <div class="storage-snapshot-info">
        <div style="display: flex; align-items: center; gap: 0.6rem;">
          <div style="width: 32px; height: 32px; border-radius: 8px; background: rgba(6, 182, 212, 0.12); color: var(--accent-cyan); display: flex; align-items: center; justify-content: center;">
            <i data-lucide="database" style="width: 18px; height: 18px;"></i>
          </div>
          <div>
            <div style="font-size: 0.88rem; font-weight: 700; color: var(--text-main);">Pipeline Storage & Bags Registry</div>
            <div style="font-size: 0.72rem; color: var(--text-muted);">Decoupled JSON State Bags Persistence</div>
          </div>
        </div>

        <div class="storage-item">
          <span class="storage-item-label">Total Bags:</span>
          <span class="storage-item-val">${totalBags} Files</span>
        </div>

        <div class="storage-item">
          <span class="storage-item-label">Total Stored Items:</span>
          <span class="storage-item-val">${totalItems} Items</span>
        </div>

        <div class="storage-item">
          <span class="storage-item-label">Disk Footprint:</span>
          <span class="storage-item-val">${formatFileSize(totalBytes)}</span>
        </div>
      </div>

      <button class="btn btn-secondary btn-sm" onclick="window.switchTab('bags')">
        <i data-lucide="folder-kanban" style="width: 14px; height: 14px;"></i> Open Bags Inspector &rarr;
      </button>
    </div>
  `;
}

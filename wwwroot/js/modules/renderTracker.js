// Live Montage Render Tracking & Sub-Pipeline Module (ES6)
import { escapeHtml, showToast, getApiUrl } from '../core/api.js';

let eventSource = null;
let autoScroll = true;
let isSubPipelineExpanded = true;
let currentRenderState = null;
let pollFallbackInterval = null;

export function initRenderTracker() {
  setupEventListeners();
  fetchInitialRenderStatus();
  startEventSourceStream();
}

// 1. Initial State Fetch
async function fetchInitialRenderStatus() {
  try {
    const res = await fetch(getApiUrl('/api/dashboard/render-status'));
    if (res.ok) {
      const state = await res.json();
      updateRenderUI(state);
    }
  } catch (err) {
    console.warn('Failed to fetch initial render status:', err);
  }
}

// 2. Server-Sent Events (SSE) Stream with Fallback
function startEventSourceStream() {
  if (eventSource) {
    eventSource.close();
  }

  try {
    eventSource = new EventSource(getApiUrl('/api/dashboard/render-stream'));

    eventSource.onmessage = (event) => {
      try {
        const state = JSON.parse(event.data);
        updateRenderUI(state);
      } catch (err) {
        console.error('Error parsing SSE render state:', err);
      }
    };

    eventSource.onerror = () => {
      if (!pollFallbackInterval) {
        pollFallbackInterval = setInterval(fetchInitialRenderStatus, 2000);
      }
    };

    eventSource.onopen = () => {
      if (pollFallbackInterval) {
        clearInterval(pollFallbackInterval);
        pollFallbackInterval = null;
      }
    };
  } catch (e) {
    console.warn('SSE not supported, using polling fallback:', e);
    if (!pollFallbackInterval) {
      pollFallbackInterval = setInterval(fetchInitialRenderStatus, 2000);
    }
  }
}

// 3. UI Updater Orchestrator
export function updateRenderUI(state) {
  if (!state) return;
  currentRenderState = state;

  updateStep3Card(state);
  updateShortsCard(state);
  updateSubPipelineSteps(state);
  updateTelemetryHUD(state);
  updateTerminalLogs(state.recentLogs || []);

  if (window.lucide) {
    lucide.createIcons();
  }
}

// 4. Update Step 3 Card in Automation Pipeline Flow
function updateStep3Card(state) {
  const step3Card = document.getElementById('pipelineStep3Card');
  const step3Badge = document.getElementById('step3LiveBadge');
  const step3Progress = document.getElementById('step3InlineProgress');
  const step3ProgressBar = document.getElementById('step3InlineProgressBar');
  const step3Footer = document.getElementById('step3FooterText');

  if (!step3Card) return;

  const isActive = state.isActive;
  const overallPercent = Math.round(state.overallProgressPercent || 0);
  const stepIndex = state.currentStepIndex || 1;

  if (isActive) {
    step3Card.classList.add('rendering-active');
    if (step3Badge) {
      step3Badge.style.display = 'inline-flex';
      step3Badge.className = 'tag-chip warning pulse-glow';
      step3Badge.innerHTML = `<span class="pulse-dot"></span> STAGE 3.${stepIndex} (${overallPercent}%)`;
    }
    if (step3Progress) {
      step3Progress.style.display = 'block';
      if (step3ProgressBar) {
        step3ProgressBar.style.width = `${Math.max(4, overallPercent)}%`;
      }
    }
    if (step3Footer) {
      step3Footer.innerText = `Rendering: ${state.currentStepName || 'Active'}`;
    }
  } else if (state.status === 'Completed' || state.status === 6) {
    step3Card.classList.remove('rendering-active');
    if (step3Badge) {
      step3Badge.style.display = 'inline-flex';
      step3Badge.className = 'tag-chip success';
      step3Badge.innerHTML = `<i data-lucide="check" style="width: 10px; height: 10px;"></i> RENDER READY`;
    }
    if (step3Progress) {
      step3Progress.style.display = 'none';
    }
    if (step3Footer) {
      step3Footer.innerText = 'Packages ready';
    }
  } else {
    step3Card.classList.remove('rendering-active');
    if (step3Badge) {
      step3Badge.className = 'tag-chip warning';
      step3Badge.innerText = 'STAGE 3';
    }
    if (step3Progress) {
      step3Progress.style.display = 'none';
    }
    if (step3Footer) {
      step3Footer.innerText = 'Video assembly';
    }
  }
}

// 4b. Update YouTube Shorts Card in Automation Pipeline Flow
function updateShortsCard(state) {
  const card = document.getElementById('pipelineShortsCard');
  const badge = document.getElementById('shortsLiveBadge');
  const progress = document.getElementById('shortsInlineProgress');
  const progressBar = document.getElementById('shortsInlineProgressBar');
  const footer = document.getElementById('shortsFooterText');

  if (!card) return;

  const shortState = state.activeShort;
  if (shortState && shortState.isActive) {
    card.classList.add('rendering-active');
    if (badge) {
      badge.style.display = 'inline-flex';
      badge.className = 'tag-chip rose pulse-glow';
      badge.innerHTML = `<span class="pulse-dot"></span> ${Math.round(shortState.progressPercent || 0)}% LIVE`;
    }
    if (progress) {
      progress.style.display = 'block';
      if (progressBar) {
        progressBar.style.width = `${Math.max(4, Math.round(shortState.progressPercent || 0))}%`;
      }
    }
    if (footer) {
      footer.innerText = shortState.step || `@${shortState.streamer}`;
    }
  } else {
    card.classList.remove('rendering-active');
    if (badge) {
      badge.className = 'tag-chip';
      badge.style.background = 'rgba(244, 63, 94, 0.15)';
      badge.style.color = '#f43f5e';
      badge.style.border = '1px solid rgba(244, 63, 94, 0.3)';
      badge.innerText = '9:16 FEED';
    }
    if (progress) {
      progress.style.display = 'none';
    }
    if (footer) {
      footer.innerText = '10m Rolling Queue';
    }
  }
}

// 5. Update the 5-Stage Sub-Pipeline
function updateSubPipelineSteps(state) {
  const container = document.getElementById('subPipelineStepper');
  if (!container) return;

  const subSteps = state.subSteps || [];
  const currentStep = state.currentStepIndex || 1;
  const isActive = state.isActive;

  container.innerHTML = subSteps.map((step) => {
    let statusClass = 'pending';
    let statusLabel = 'WAITING';
    let iconName = 'clock';

    if (step.status === 2 || step.status === 'Completed') {
      statusClass = 'completed';
      statusLabel = 'DONE';
      iconName = 'check-circle';
    } else if (step.status === 1 || step.status === 'Active' || (isActive && step.index === currentStep)) {
      statusClass = 'active';
      statusLabel = 'IN PROGRESS';
      iconName = 'loader-2';
    } else if (step.status === 3 || step.status === 'Failed') {
      statusClass = 'failed';
      statusLabel = 'FAILED';
      iconName = 'alert-triangle';
    }

    const progress = Math.round(step.progressPercent || 0);

    return `
      <div class="sub-step-card ${statusClass}">
        <div class="sub-step-header">
          <span class="sub-step-num">3.${step.index}</span>
          <span class="sub-step-chip ${statusClass}">
            <i data-lucide="${iconName}" class="${statusClass === 'active' ? 'spin' : ''}"></i>
            ${statusLabel}
          </span>
        </div>
        <div class="sub-step-body">
          <div class="sub-step-title">${escapeHtml(step.name)}</div>
          <div class="sub-step-desc">${escapeHtml(step.description)}</div>
          ${step.detail ? `<div class="sub-step-detail" title="${escapeHtml(step.detail)}">${escapeHtml(step.detail)}</div>` : ''}
        </div>
        <div class="sub-step-footer">
          <div class="sub-step-progress-bar-wrap">
            <div class="sub-step-progress-bar ${statusClass}" style="width: ${Math.max(progress > 0 ? 5 : 0, progress)}%"></div>
          </div>
          <span class="sub-step-pct">${progress}%</span>
        </div>
      </div>
    `;
  }).join('');
}

// 6. Update Telemetry HUD with Accurate Batch & Stage Info
function updateTelemetryHUD(state) {
  const montageIdEl = document.getElementById('renderMontageId');
  const kindTagEl = document.getElementById('renderKindTag');
  const clipCountEl = document.getElementById('renderClipCount');
  const batchValEl = document.getElementById('renderBatchVal');
  const batchInfoEl = document.getElementById('renderBatchInfo');
  const batchTargetEl = document.getElementById('renderBatchTarget');
  const overallBar = document.getElementById('renderOverallProgressBar');
  const overallText = document.getElementById('renderOverallPercentText');
  const fpsEl = document.getElementById('renderFpsVal');
  const speedEl = document.getElementById('renderSpeedVal');
  const timeEl = document.getElementById('renderTimeVal');
  const statusPillEl = document.getElementById('renderStatusPill');

  const telemetry = state.telemetry || {};
  const overallPercent = Math.round(state.overallProgressPercent || 0);
  const isActive = state.isActive;

  if (montageIdEl) montageIdEl.innerText = state.montageId ? `#${state.montageId.substring(0, 12)}...` : 'No Active Session';
  if (kindTagEl) {
    if (state.kindKey) {
      kindTagEl.style.display = 'inline-flex';
      kindTagEl.innerText = `${state.kindKey.toUpperCase()}${state.subKindKey ? ' / ' + state.subKindKey.toUpperCase() : ''}`;
    } else {
      kindTagEl.style.display = 'none';
    }
  }
  if (clipCountEl) clipCountEl.innerText = state.totalClips ? `${state.totalClips} Source Clips` : '0 Clips';

  // Accurate Batches Programmed & Current Pass
  if (batchValEl) {
    if (state.totalPasses > 0) {
      batchValEl.innerText = `${state.currentPass} / ${state.totalPasses}`;
    } else {
      batchValEl.innerText = '-- / --';
    }
  }

  if (batchInfoEl) {
    if (state.totalPasses > 0) {
      batchInfoEl.style.display = 'inline-flex';
      const clipsInBatch = state.batchClipsCount ? ` (${state.batchClipsCount} clips/batch)` : '';
      batchInfoEl.innerText = `${state.totalPasses} Batches Programmed${clipsInBatch}`;
    } else {
      batchInfoEl.style.display = 'none';
    }
  }

  if (batchTargetEl) {
    if (state.currentBatchTarget) {
      batchTargetEl.style.display = 'inline-flex';
      const fileName = state.currentBatchTarget.split('/').pop() || state.currentBatchTarget;
      batchTargetEl.innerText = `Target: ${fileName}`;
    } else {
      batchTargetEl.style.display = 'none';
    }
  }

  if (overallBar) overallBar.style.width = `${Math.max(overallPercent > 0 ? 4 : 0, overallPercent)}%`;
  if (overallText) overallText.innerText = `${overallPercent}%`;

  if (fpsEl) fpsEl.innerText = telemetry.fps ? `${Math.round(telemetry.fps)} FPS` : '-- FPS';
  if (speedEl) speedEl.innerText = telemetry.speed || '--x';
  if (timeEl) timeEl.innerText = telemetry.timeEncoded || '00:00:00';

  if (statusPillEl) {
    if (isActive) {
      statusPillEl.className = 'status-pill active';
      statusPillEl.innerHTML = `<span class="pulse-dot"></span> ENCODING (Stage 3.${state.currentStepIndex || 1})`;
    } else if (state.status === 'Completed' || state.status === 6) {
      statusPillEl.className = 'status-pill completed';
      statusPillEl.innerHTML = `<i data-lucide="check-circle-2" style="width: 12px; height: 12px;"></i> COMPLETED`;
    } else if (state.status === 'Failed' || state.status === 7) {
      statusPillEl.className = 'status-pill failed';
      statusPillEl.innerHTML = `<i data-lucide="alert-circle" style="width: 12px; height: 12px;"></i> FAILED`;
    } else {
      statusPillEl.className = 'status-pill idle';
      statusPillEl.innerHTML = `<span class="idle-dot"></span> STANDING BY`;
    }
  }
}

// 7. Update Live Terminal Logs
function updateTerminalLogs(logs = []) {
  const terminalBody = document.getElementById('renderTerminalBody');
  const lineCountEl = document.getElementById('terminalLineCount');
  if (!terminalBody) return;

  if (lineCountEl) lineCountEl.innerText = `${logs.length} events`;

  if (!logs || logs.length === 0) {
    terminalBody.innerHTML = `
      <div class="terminal-empty">
        <i data-lucide="terminal" style="width: 28px; height: 28px; opacity: 0.4;"></i>
        <div>Waiting for Montage Worker to begin compiling packages.</div>
        <div style="font-size: 0.76rem; margin-top: 0.25rem; color: var(--text-muted);">
          When a completed package is present in <code>bag_montage.json</code>, active rendering steps, batch metrics, and FFmpeg logs stream here live.
        </div>
      </div>
    `;
    return;
  }

  terminalBody.innerHTML = logs.map(log => {
    const timeStr = log.timestamp ? new Date(log.timestamp).toLocaleTimeString() : '';
    const level = (log.level || 'info').toLowerCase();

    let levelBadge = 'INFO';
    let levelClass = 'info';

    if (level === 'step') {
      levelBadge = 'STEP';
      levelClass = 'step';
    } else if (level === 'ffmpeg') {
      levelBadge = 'FFMPEG';
      levelClass = 'ffmpeg';
    } else if (level === 'shorts') {
      levelBadge = 'SHORTS';
      levelClass = 'shorts';
    } else if (level === 'thumb') {
      levelBadge = 'THUMB';
      levelClass = 'thumb';
    } else if (level === 'ai') {
      levelBadge = 'AI';
      levelClass = 'ai';
    } else if (level === 'warning' || level === 'warn') {
      levelBadge = 'WARN';
      levelClass = 'warn';
    } else if (level === 'error') {
      levelBadge = 'ERROR';
      levelClass = 'error';
    } else if (level === 'success') {
      levelBadge = 'DONE';
      levelClass = 'success';
    }

    return `
      <div class="terminal-line ${levelClass}">
        <span class="terminal-time">[${timeStr}]</span>
        <span class="terminal-tag ${levelClass}">${levelBadge}</span>
        <span class="terminal-msg">${escapeHtml(log.message)}</span>
      </div>
    `;
  }).join('');

  if (autoScroll) {
    terminalBody.scrollTop = terminalBody.scrollHeight;
  }
}

// 8. Event Handlers & Control Actions
function setupEventListeners() {
  const autoScrollBtn = document.getElementById('btnToggleAutoScroll');
  if (autoScrollBtn) {
    autoScrollBtn.addEventListener('click', () => {
      autoScroll = !autoScroll;
      autoScrollBtn.classList.toggle('active', autoScroll);
      autoScrollBtn.title = autoScroll ? 'Auto-scroll enabled' : 'Auto-scroll disabled';
    });
  }

  const clearBtn = document.getElementById('btnClearTerminal');
  if (clearBtn) {
    clearBtn.addEventListener('click', () => {
      const terminalBody = document.getElementById('renderTerminalBody');
      if (terminalBody) {
        terminalBody.innerHTML = '<div class="terminal-empty">Logs cleared locally.</div>';
      }
    });
  }

  const toggleSubPipelineBtn = document.getElementById('btnToggleSubPipeline');
  if (toggleSubPipelineBtn) {
    toggleSubPipelineBtn.addEventListener('click', toggleRenderInspector);
  }
}

export function toggleRenderInspector() {
  const panel = document.getElementById('montageRenderInspectorPanel');
  const btn = document.getElementById('btnToggleSubPipeline');
  if (!panel) return;

  isSubPipelineExpanded = !isSubPipelineExpanded;
  if (isSubPipelineExpanded) {
    panel.style.display = 'block';
    if (btn) btn.innerHTML = '<i data-lucide="chevron-up"></i> Hide Render Inspector';
  } else {
    panel.style.display = 'none';
    if (btn) btn.innerHTML = '<i data-lucide="chevron-down"></i> Show Render Inspector';
  }

  if (window.lucide) lucide.createIcons();
}

// Trigger Montage Worker via Backend (only runs if package has Completed: true)
export async function triggerMontage() {
  const btn = document.getElementById('btnTriggerMontage');
  if (btn) btn.disabled = true;

  try {
    showToast('Checking package readiness...', 'info');
    const res = await fetch(getApiUrl('/api/dashboard/trigger-montage'), { method: 'POST' });
    const data = await res.json();
    if (res.ok && data.success) {
      showToast(data.message || 'Render process launched for completed package!', 'success');
      const panel = document.getElementById('montageRenderInspectorPanel');
      if (panel) panel.style.display = 'block';
      isSubPipelineExpanded = true;
    } else {
      showToast(data.message || 'Cannot render: No package with Completed: true found.', 'warning');
    }
  } catch (err) {
    console.error('Trigger montage error:', err);
    showToast('Error communicating with server', 'error');
  } finally {
    if (btn) btn.disabled = false;
  }
}

// Window scope bindings
window.triggerMontage = triggerMontage;
window.toggleRenderInspector = toggleRenderInspector;

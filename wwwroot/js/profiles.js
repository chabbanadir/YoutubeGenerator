// Profiles Orchestrator Module (Kinds & SubKinds)

async function fetchProfilesData() {
  try {
    if (typeof fetchResourcesList === 'function') {
      await fetchResourcesList();
    }

    const response = await fetch('/api/dashboard/profiles');
    if (!response.ok) {
      throw new Error(`Server returned HTTP ${response.status}`);
    }
    const data = await response.json();
    window.state = window.state || {};
    window.state.profilesData = data;
    
    // Update header profile count
    const totalCount = (data.kinds ? data.kinds.length : 0) + (data.subkinds ? data.subkinds.length : 0);
    const countEl = document.getElementById('profilesCount');
    if (countEl) countEl.textContent = totalCount;

    // Render Kinds & SubKinds using dedicated modules
    if (typeof renderKindsGrid === 'function') {
      renderKindsGrid(data.kinds || []);
    }
    if (typeof renderSubkindsGrid === 'function') {
      renderSubkindsGrid(data.subkinds || []);
    }

  } catch (error) {
    console.error("Failed to fetch profiles data:", error);
    showToast("Failed to load profiles data.", "error");
  }
}

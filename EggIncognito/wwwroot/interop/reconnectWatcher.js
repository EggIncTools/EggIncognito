(function () {
  const VERSION_URL = '/api/app/version';
  const POLL_MS = 2000;
  const MAX_POLLS = 150;
  const DEAD = /components-(reconnect-(failed|rejected)|resume-failed)/;
  let loadedVersion = null;
  let polling = false;
  let polls = 0;
  let modal = null;

  async function fetchVersion() {
    try {
      const r = await fetch(VERSION_URL, { cache: 'no-store' });
      if (!r.ok) return null;
      const j = await r.json();
      return j && typeof j.version === 'string' ? j.version : null;
    } catch {
      return null;
    }
  }

  async function captureLoadedVersion() {
    loadedVersion = await fetchVersion();
  }

  function circuitDead() {
    return !!modal && DEAD.test(modal.className || '');
  }

  function startPolling() {
    if (polling) return;
    polling = true;
    polls = 0;
    tick();
  }

  function stopPolling() {
    polling = false;
  }

  async function tick() {
    if (!polling) return;
    polls += 1;
    const current = await fetchVersion();
    if (current) {
      if (loadedVersion && current !== loadedVersion) {
        location.reload();
        return;
      }
      if (circuitDead()) {
        location.reload();
        return;
      }
    }
    if (polls >= MAX_POLLS) {
      stopPolling();
      return;
    }
    setTimeout(tick, POLL_MS);
  }

  function observeDialog() {
    modal = document.getElementById('components-reconnect-modal');
    if (!modal) {
      setTimeout(observeDialog, 200);
      return;
    }
    const obs = new MutationObserver(() => {
      const cls = modal.className || '';
      if (/components-reconnect-(show|failed|rejected)|components-resume-failed/.test(cls) || modal.open) startPolling();
      else stopPolling();
    });
    obs.observe(modal, { attributes: true, attributeFilter: ['class', 'open'] });
  }

  captureLoadedVersion();
  observeDialog();
})();

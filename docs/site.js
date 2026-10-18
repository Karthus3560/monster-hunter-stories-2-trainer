'use strict';
const screenshot = document.getElementById('app-shot');
document.querySelectorAll('[data-theme]').forEach(button => {
  button.addEventListener('click', () => {
    const night = button.dataset.theme === 'night';
    screenshot.src = night ? 'screenshots/battle-night.png' : 'screenshots/battle-light.png';
    screenshot.alt = night ? 'Kinship Studio Moonlit hunt theme with battle controls and reward sliders' : 'Kinship Studio Stories light theme with battle controls and reward sliders';
    document.querySelectorAll('[data-theme]').forEach(other => other.setAttribute('aria-pressed', String(other === button)));
  });
});

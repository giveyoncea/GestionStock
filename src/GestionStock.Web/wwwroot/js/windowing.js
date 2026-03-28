const windowStates = new Map();

function ensureState(windowId) {
  if (!windowStates.has(windowId)) {
    windowStates.set(windowId, {
      initialized: false,
      dragging: false,
      dragOffsetX: 0,
      dragOffsetY: 0,
      previousRect: null
    });
  }

  return windowStates.get(windowId);
}

function getFloatingRect(element) {
  const rect = element.getBoundingClientRect();
  return {
    left: rect.left,
    top: rect.top,
    width: rect.width,
    height: rect.height
  };
}

function applyRect(element, rect) {
  element.style.left = `${rect.left}px`;
  element.style.top = `${rect.top}px`;
  element.style.width = `${rect.width}px`;
  element.style.height = `${rect.height}px`;
}

export function initializeFloatingWindow(windowId, headerId) {
  const element = document.getElementById(windowId);
  const header = document.getElementById(headerId);

  if (!element || !header) {
    return;
  }

  const state = ensureState(windowId);
  if (state.initialized) {
    return;
  }

  const viewportWidth = window.innerWidth;
  const viewportHeight = window.innerHeight;
  const rect = element.getBoundingClientRect();

  const left = Math.max(220, (viewportWidth - rect.width) / 2);
  const top = Math.max(90, (viewportHeight - rect.height) / 2);

  element.style.left = `${left}px`;
  element.style.top = `${top}px`;

  const onPointerMove = (event) => {
    if (!state.dragging || element.classList.contains("workspace-window--maximized")) {
      return;
    }

    const nextLeft = Math.max(220, event.clientX - state.dragOffsetX);
    const nextTop = Math.max(80, event.clientY - state.dragOffsetY);

    element.style.left = `${nextLeft}px`;
    element.style.top = `${nextTop}px`;
  };

  const stopDragging = () => {
    state.dragging = false;
    document.body.style.userSelect = "";
  };

  header.addEventListener("pointerdown", (event) => {
    if (event.target instanceof HTMLElement && event.target.closest("button")) {
      return;
    }

    if (element.classList.contains("workspace-window--maximized")) {
      return;
    }

    const bounds = element.getBoundingClientRect();
    state.dragging = true;
    state.dragOffsetX = event.clientX - bounds.left;
    state.dragOffsetY = event.clientY - bounds.top;
    document.body.style.userSelect = "none";
  });

  window.addEventListener("pointermove", onPointerMove);
  window.addEventListener("pointerup", stopDragging);
  window.addEventListener("pointercancel", stopDragging);

  state.initialized = true;
}

export function setWindowMaximized(windowId, maximized) {
  const element = document.getElementById(windowId);
  if (!element) {
    return;
  }

  const state = ensureState(windowId);

  if (maximized) {
    if (!state.previousRect) {
      state.previousRect = getFloatingRect(element);
    }
  } else if (state.previousRect) {
    applyRect(element, state.previousRect);
    state.previousRect = null;
  }
}

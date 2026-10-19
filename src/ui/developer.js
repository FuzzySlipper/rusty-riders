/**
 * The Engine developer shell, docked at the left beside play: two inspection views of what the hands hold (each hand's
 * grip from the side, C# inspection targets `main-grip` and `off-grip`), the first-person workbench (the tunables panel
 * over C# HeldWorkbench), the scene tree and the console. Offered only where the host runs its debug service
 * (`rusty dev --live-debug`). Closed, it holds no panel and sends nothing; however it closes (its own Close, the key),
 * the motion viewer returns the hands to what they hold and `onClose` returns input to play.
 *
 * Provenance: rusty-fptester `src/ui/developer.js` (the maintained reference; from rusty-hotel's, on the Engine shell's
 * `onClose`), with hotel's grip views and its motion viewer reset on close.
 */

/** Whether the Engine's debug service runs, asked through the Engine's own client (a streamed page or a window alike). */
export async function developerAvailable(loadPanel = () => import('@rusty-engine/live-debug')) {
  try {
    const { createLiveDebugHttpTransport } = await loadPanel();
    return (await createLiveDebugHttpTransport().catalog()).available === true;
  } catch {
    return false;
  }
}

export function createDeveloperShell(host, { viewport = null, onClose = () => {}, loadPanel = () => import('@rusty-engine/live-debug') } = {}) {
  let current = null;
  const close = () => {
    const previous = current;
    current = null;
    if (!previous) return;
    previous.shell?.close();
    void previous.transport?.execute('riders.dev.motion.off').catch(() => {});
    previous.error?.remove();
    onClose();
  };
  return {
    get isOpen() { return current !== null; },
    async open() {
      if (current) return;
      const session = { shell: null, transport: null, error: null };
      current = session;
      try {
        const { mountDeveloperShell, createLiveDebugHttpTransport } = await loadPanel();
        if (current !== session) return;
        session.transport = createLiveDebugHttpTransport();
        session.shell = mountDeveloperShell(host, {
          side: 'left',
          presentation: 'dock',
          transport: session.transport,
          onClose: () => { if (current === session) close(); },
          panels: [
            ...(viewport ? [
              { kind: 'view', title: 'Main grip', options: { viewport, id: 'main', subject: 'target main-grip', framings: ['side', 'front', 'top', 'close'] } },
              { kind: 'view', title: 'Off grip', options: { viewport, id: 'off', subject: 'target off-grip', framings: ['side', 'front', 'top', 'close'] } }
            ] : []),
            { kind: 'tunables', title: 'Workbench' },
            { kind: 'scene' },
            { kind: 'console' }
          ]
        });
        await session.shell.open();
        if (current !== session) session.shell.close();
      } catch (error) {
        if (current !== session) return;
        session.error = host.ownerDocument.createElement('p');
        session.error.className = 'developer-error';
        session.error.setAttribute('role', 'alert');
        session.error.textContent = `Developer shell unavailable: ${error instanceof Error ? error.message : 'could not load the Engine panel'}`;
        host.append(session.error);
      }
    },
    close,
    dispose: close
  };
}

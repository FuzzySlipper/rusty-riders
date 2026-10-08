/** Menu request lifetime only; the Engine port is the sole lifecycle state owner. */
export function createPauseFlow(lifecycle, changed) {
  let pending = null;
  let error = '';
  let disposed = false;
  const snapshot = () => ({ state: lifecycle?.state(), pending, error });
  const notify = () => { if (!disposed) changed(snapshot()); };
  const unsubscribe = lifecycle?.subscribe(() => { error = ''; notify(); });

  const request = async target => {
    if (disposed || pending) return false;
    if (!lifecycle) { error = 'Pause is unavailable in this host.'; notify(); return false; }
    if (lifecycle.state() === target) return true;
    pending = target;
    error = '';
    notify();
    try {
      const result = await (target === 'paused' ? lifecycle.pause() : lifecycle.resume());
      if (disposed) return false;
      // A newer external transition may have superseded an accepted request.
      if (!result.accepted || lifecycle.state() !== target) {
        error = result.diagnostic || 'The game state changed. Please try again.';
        return false;
      }
      return true;
    } catch (failure) {
      if (!disposed) error = `Could not ${target === 'paused' ? 'pause' : 'resume'} the game. ${failure.message}`;
      return false;
    } finally {
      pending = null;
      notify();
    }
  };
  return { snapshot, pause: () => request('paused'), resume: () => request('running'),
    dispose() { disposed = true; unsubscribe?.(); } };
}

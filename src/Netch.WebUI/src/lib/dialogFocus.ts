// Keep keyboard navigation inside a dialog and return to its trigger on close.
export function dialogFocus(node: HTMLElement) {
  const previous = document.activeElement instanceof HTMLElement ? document.activeElement : null;
  node.tabIndex = -1;
  node.focus();
  function onKeydown(event: KeyboardEvent) {
    if (event.key !== 'Tab' || event.defaultPrevented) return;
    if (event.target instanceof Element && event.target.closest('[role="dialog"], [role="alertdialog"]') !== node) return;
    const controls = Array.from(node.querySelectorAll<HTMLElement>('button, a[href], input, select, textarea, summary, [tabindex="0"]'))
      .filter(element => !element.matches(':disabled') && !element.closest('[inert]') && element.getClientRects().length > 0);
    if (!controls.length) { event.preventDefault(); node.focus(); return; }
    const first = controls[0], last = controls[controls.length - 1];
    if (event.shiftKey && (document.activeElement === first || document.activeElement === node)) {
      event.preventDefault(); last.focus();
    } else if (!event.shiftKey && (document.activeElement === last || document.activeElement === node)) {
      event.preventDefault(); first.focus();
    }
  }
  node.addEventListener('keydown', onKeydown);
  return { destroy() { node.removeEventListener('keydown', onKeydown); queueMicrotask(() => { if (previous?.isConnected && !previous.closest('[inert]')) previous.focus(); }); } };
}

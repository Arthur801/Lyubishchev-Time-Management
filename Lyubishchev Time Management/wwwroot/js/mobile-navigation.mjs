export const isMoreSection = (section) => section === 'category' || section === 'tag';

export function createMoreSheetController({ trigger, dialog, close }) {
  const sync = () => trigger.setAttribute('aria-expanded', String(dialog.open));
  const closeSheet = () => {
    if (dialog.open) dialog.close();
  };
  const open = () => {
    if (!dialog.open) dialog.showModal();
    sync();
    close.focus();
  };

  trigger.addEventListener('click', open);
  close.addEventListener('click', closeSheet);
  dialog.addEventListener('click', (event) => {
    if (event.target === dialog) closeSheet();
  });
  // Covers every close path (close button, backdrop click, native Escape handling) in one place
  // so aria-expanded sync and focus restoration to the trigger never have to be duplicated.
  dialog.addEventListener('close', () => {
    sync();
    trigger.focus();
  });

  sync();
  return { open, close: closeSheet };
}

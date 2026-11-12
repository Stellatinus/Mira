import { useEffect } from 'react';
import type { RefObject } from 'react';

/** Chiude un menu aperto quando si clicca fuori o si preme Esc (`viaEscape` dice quale dei due). */
export function useDismiss(open: boolean, root: RefObject<HTMLElement | null>, onClose: (viaEscape: boolean) => void) {
  useEffect(() => {
    if (!open) return;
    const onPointer = (event: MouseEvent) => {
      if (!root.current?.contains(event.target as Node)) onClose(false);
    };
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose(true);
    };
    document.addEventListener('mousedown', onPointer);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', onPointer);
      document.removeEventListener('keydown', onKey);
    };
  }, [open, root, onClose]);
}

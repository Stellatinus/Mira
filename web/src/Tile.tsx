import { useEffect, useState } from 'react';
import { ItemIcon } from './icons';
import type { DashboardItem } from './types';

interface Props {
  item: Pick<DashboardItem, 'type' | 'logoUrl'>;
  /** Dimensione dell'icona di ripiego. */
  iconSize: number;
  big?: boolean;
}

/**
 * L'immagine in cima alle card e al pannello. Un repository mostra il suo logo (anteprima sociale o
 * avatar del proprietario) direttamente, in un quadrato 1:1 con gli angoli stondati; se manca o non si
 * carica, lo stesso quadrato con l'icona. Le issue e le PR hanno il foglio con l'angolo piegato.
 */
export function Tile({ item, iconSize, big = false }: Props) {
  const [failed, setFailed] = useState(false);
  useEffect(() => setFailed(false), [item.logoUrl]);

  if (item.type !== 'repo') {
    return (
      <div className={big ? 'tile big' : 'tile'}>
        <ItemIcon type={item.type} size={iconSize} />
      </div>
    );
  }

  return (
    <div className={big ? 'logo big' : 'logo'}>
      {item.logoUrl && !failed ? (
        <img
          src={item.logoUrl}
          alt=""
          loading="lazy"
          referrerPolicy="no-referrer"
          onError={() => setFailed(true)}
        />
      ) : (
        <ItemIcon type="repo" size={iconSize} />
      )}
    </div>
  );
}

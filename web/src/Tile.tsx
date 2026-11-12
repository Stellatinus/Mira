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
 * Il foglio con l'angolo piegato che sta in cima alle card e al pannello. Per un repository mostra il
 * suo logo (anteprima sociale o avatar del proprietario); se manca o non si carica, l'icona del tipo.
 */
export function Tile({ item, iconSize, big = false }: Props) {
  const [failed, setFailed] = useState(false);
  useEffect(() => setFailed(false), [item.logoUrl]);

  const logo = item.type === 'repo' && !failed ? item.logoUrl : null;

  return (
    <div className={big ? 'tile big' : 'tile'}>
      {logo ? (
        <img
          className="tile-img"
          src={logo}
          alt=""
          loading="lazy"
          referrerPolicy="no-referrer"
          onError={() => setFailed(true)}
        />
      ) : (
        <ItemIcon type={item.type} size={iconSize} />
      )}
    </div>
  );
}

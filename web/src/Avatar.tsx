import type { AccountStatus } from './types';

interface Props {
  account?: Pick<AccountStatus, 'login' | 'avatarUrl'>;
  size?: number;
}

/** Avatar quadrato dall'angolo arrotondato; senza immagine mostra l'iniziale del login. */
export function Avatar({ account, size = 20 }: Props) {
  const style = { width: size, height: size, fontSize: Math.round(size * 0.5) };
  if (account?.avatarUrl) {
    return <img className="avatar" src={account.avatarUrl} alt="" width={size} height={size} style={style} />;
  }
  return (
    <span className="avatar placeholder" style={style} aria-hidden>
      {account?.login.slice(0, 1).toUpperCase()}
    </span>
  );
}
